using Catan.Core;

namespace Catan.AI;

/// <summary>One settlement spot as the coach sees it.</summary>
/// <param name="Rating">0-100 within this position: 100 for the best legal spot, 0 for the worst.</param>
/// <param name="Rank">1 for the best spot.</param>
/// <param name="Pips">Total dice pips of the tiles around the spot (how often it produces).</param>
/// <param name="ResourcePips">Pips per resource.</param>
/// <param name="Harbor">The harbor at the spot, or null.</param>
/// <param name="StartingCards">Second settlement only: the cards it gives you straight away (one per resource tile).</param>
/// <param name="Reasons">What makes the spot good, in plain words, biggest first (up to three).</param>
/// <param name="Facts">Plain facts about the spot: scarce resources it produces, open spots within reach after its best
/// road, and whether an opponent is as close to them (blocking risk).</param>
public sealed record SpotRating(int Vertex, double Score, double Rating, int Rank, int Pips, int[] ResourcePips, HarborType? Harbor,
    ResourceSet StartingCards, IReadOnlyList<string> Reasons, IReadOnlyList<string>? Facts = null);

/// <summary>One opening road as the coach sees it: which way it points and what that does in the race for spots.</summary>
/// <param name="TowardPips">The best open spot the road puts within one more road (0 if none).</param>
/// <param name="Facts">Plain words: the spot it points at, spots it wins or ties from an opponent, or that it leads nowhere.</param>
/// <param name="Target">The open spot the road heads for, or -1.</param>
public sealed record RoadRating(int Edge, double Score, double Rating, int Rank, int TowardPips, IReadOnlyList<string> Facts, int Target = -1);

/// <summary>
/// Practice for opening placements. Deals a board where the players before you have placed (by SmartBot), for your first
/// settlement (round 1) or your second (round 2: the whole first round is played, your own first settlement placed by the
/// bot, and the players after you in turn order place again first, since round 2 runs in reverse). Then rates every legal spot
/// the way the bots judge it: their evaluation of the position after the settlement and its best road, which already weighs
/// how the spot fits with what you have. The reasons compare the bots' features before and after, weighted. Setup has no
/// hidden cards, so ratings are exact and repeatable.
/// </summary>
public sealed class PlacementCoach
{
    private readonly BotWeights _weights;
    private readonly Evaluator _eval;

    public PlacementCoach(BotWeights weights)
    {
        _weights = weights;
        _eval = new Evaluator(weights);
    }

    /// <summary>
    /// A practice position from <paramref name="seed"/> for settlement <paramref name="round"/> (1 or 2): a balanced board,
    /// your seat (1st to 4th in turn order), and everything placed before your turn, by SmartBots.
    /// </summary>
    public GameState Deal(ulong seed, out int seat, int round = 1)
    {
        var rng = new Rng(seed, stream: 3);
        var s = new GameState(BoardGenerator.Balanced(new Rng(seed)));
        int me = rng.NextInt(GameConstants.PlayerCount);
        seat = me;
        var chance = new RngChance(seed);
        var legal = new List<GameAction>();
        bool YourTurn() => Rules.ActingSeat(s) == me && s.Phase == Phase.SetupSettlement && Settlements(s, me) == round - 1;
        while (!YourTurn())
        {
            int acting = Rules.ActingSeat(s);
            var bot = new SmartBot(_weights, SmartBotSettings.Play, seed * 31 + (ulong)acting);
            Rules.GetLegalActions(s, acting, legal);
            var move = bot.DecideAsync(PlayerView.From(s, acting), legal, default).GetAwaiter().GetResult();
            Rules.Apply(s, move, chance);
        }
        return s;
    }

    private static int Settlements(GameState s, int seat) => Enumerable.Range(0, Topology.VertexCount).Count(v => s.VertexOwner[v] == seat);

    /// <summary>Every legal settlement spot for <paramref name="seat"/>, best first. <paramref name="names"/> names the seats in facts.</summary>
    public IReadOnlyList<SpotRating> Rate(GameState s, int seat, IReadOnlyList<string>? names = null)
    {
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, seat, legal);
        legal.RemoveAll(a => a.Type != ActionType.BuildSettlement);
        bool second = Settlements(s, seat) == 1;

        // Every spot gets a full score (the planner's beam only looks closely at its top few): the settlement plus its best
        // road, judged by the bots' evaluation.
        var scores = new double[legal.Count];
        var bestStates = new GameState[legal.Count];
        var roads = new List<GameAction>();
        var chance = new RngChance(1);
        for (int i = 0; i < legal.Count; i++)
        {
            var placed = s.Clone();
            Rules.Apply(placed, legal[i], chance);
            Rules.GetLegalActions(placed, seat, roads);
            scores[i] = _eval.Score(placed, seat);
            bestStates[i] = placed;
            bool first = true;
            foreach (var road in roads)
            {
                var after = placed.Clone();
                Rules.Apply(after, road, chance);
                double score = _eval.Score(after, seat);
                if (first || score > scores[i])
                    (scores[i], bestStates[i], first) = (score, after, false);
            }
        }

        var before = Features(s, seat);
        double best = scores.DefaultIfEmpty(0).Max(), worst = scores.DefaultIfEmpty(0).Min();
        var order = Enumerable.Range(0, legal.Count).OrderByDescending(i => scores[i]).ThenBy(i => legal[i].Target).ToList();
        return order.Select((i, rank) =>
        {
            int vertex = legal[i].Target;
            var (pips, perResource) = Production(s.Board, vertex);
            int spot = Topology.VertexHarbor[vertex];
            double rating = best > worst ? 100 * (scores[i] - worst) / (best - worst) : 100;
            var starting = second ? StartingCards(s.Board, vertex) : default;
            return new SpotRating(vertex, scores[i], rating, rank + 1, pips, perResource, spot >= 0 ? s.Board.HarborTypeAt(spot) : null,
                starting, Reasons(before, Features(bestStates[i], seat)), SpotFacts(bestStates[i], seat, perResource, names));
        }).ToList();
    }

    /// <summary>
    /// Every legal road for <paramref name="seat"/> (after a setup settlement, or any road), best first. Ranked by the bots'
    /// evaluation of the position after it; roads it scores the same are ranked by the best open spot each one reaches,
    /// valued as the position with your settlement there (so resource fit and ports count, not just pips). The facts say
    /// where the road heads, how that spot fits what you produce, who else is racing for it, whether there's a backup, and
    /// (below #1) what the best road does better.
    /// </summary>
    public IReadOnlyList<RoadRating> RateRoads(GameState s, int seat, IReadOnlyList<string>? names = null)
    {
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, seat, legal);
        legal.RemoveAll(a => a.Type != ActionType.BuildRoad);
        var chance = new RngChance(1);
        var before = Race(s);
        var produced = ProducedPips(s, seat);

        var options = legal.Select(road =>
        {
            var after = s.Clone();
            Rules.Apply(after, road, chance);
            double score = _eval.Score(after, seat);
            // The open spots this road wins the race for (within one more road; on a tie, whoever plays sooner), best first.
            // In the opening, the best open spots will probably go to the settlements others still have to place.
            var taken = OpeningTaken(after, seat);
            var won = Race(after).Where(p => Evaluator.RaceWinner(after, p.Distance) == seat).ToList();
            var reach = won.Where(p => !taken.Contains(p.Vertex))
                .Select(p => (Spot: p, Value: SettledValue(after, seat, p.Vertex)))
                .OrderByDescending(p => p.Value).ToList();
            // The best spot it gets to first that the rest of the opening will probably take.
            int? lost = won.Where(p => taken.Contains(p.Vertex)).OrderByDescending(p => p.Pips).Select(p => (int?)p.Pips).FirstOrDefault();
            return (Road: road, Score: score, Reach: reach, TargetValue: reach.Count > 0 ? reach[0].Value : double.NaN, Lost: lost,
                Others: OthersLeft(after, seat));
        }).OrderByDescending(o => Math.Round(o.Score, 6)).ThenByDescending(o => double.IsNaN(o.TargetValue) ? double.MinValue : o.TargetValue)
          .ThenBy(o => o.Road.Target).ToList();

        // Rating: the evaluation, and between roads it scores alike, how good the best spot within reach is (a road that
        // reaches nothing counts as reaching a little less than the worst spot any road reaches).
        double floor = options.Where(o => !double.IsNaN(o.TargetValue)).Select(o => o.TargetValue).DefaultIfEmpty(0).Min() - 1;
        var keys = options.Select(o => o.Score + 0.05 * (double.IsNaN(o.TargetValue) ? floor : o.TargetValue)).ToList();
        double best = keys.DefaultIfEmpty(0).Max(), worst = keys.DefaultIfEmpty(0).Min();
        var top = options.FirstOrDefault();
        return options.Select((o, rank) =>
        {
            var facts = new List<string>();
            int toward = 0, target = -1;
            if (o.Lost is { } lostPips && (o.Reach.Count == 0 || lostPips > o.Reach[0].Spot.Pips))
                facts.Add($"the {lostPips}-pip spot it points at will probably be taken in the opening ({o.Others} settlement{(o.Others == 1 ? "" : "s")} still to be placed by others)");
            if (o.Reach.Count > 0)
            {
                var spot = o.Reach[0].Spot;
                target = spot.Vertex;
                toward = spot.Pips;
                facts.Add($"heads for a {SpotWords(s.Board, spot.Vertex)}");
                facts.AddRange(Fit(s.Board, spot.Vertex, produced));
                facts.Add(RaceWords(before, spot, seat, names));
                int backups = o.Reach.Count - 1;
                facts.Add(backups == 0 ? "no backup spot within reach if it's taken"
                    : $"{backups} other open spot{(backups == 1 ? "" : "s")} within reach as a backup");
            }
            else
                facts.Add("leads nowhere useful: no open spot within reach that you'd get first");
            if (rank > 0 && top.Reach is { Count: > 0 })
                facts.Add(Better(s.Board, top.Reach[0].Spot, o.Reach.Count > 0 ? o.Reach[0].Spot : null, produced));
            double rating = best > worst ? 100 * (keys[rank] - worst) / (best - worst) : 100;
            return new RoadRating(o.Road.Target, o.Score, rating, rank + 1, toward, facts, target);
        }).ToList();
    }

    /// <summary>The position with <paramref name="seat"/> settled at <paramref name="vertex"/>, scored by the bots (roads ignored).</summary>
    private double SettledValue(GameState s, int seat, int vertex)
    {
        var settled = s.Clone();
        settled.VertexOwner[vertex] = (sbyte)seat;
        settled.VertexLevel[vertex] = 1;
        settled.PublicVP[seat]++;
        if (settled.SettlementsLeft[seat] > 0)
            settled.SettlementsLeft[seat]--;
        return _eval.Score(settled, seat);
    }

    /// <summary>Pips per resource from a seat's buildings (cities count double).</summary>
    private static int[] ProducedPips(GameState s, int seat)
    {
        var pips = new int[GameConstants.ResourceCount];
        for (int v = 0; v < Topology.VertexCount; v++)
            if (s.VertexOwner[v] == seat)
            {
                var (_, perResource) = Production(s.Board, v);
                for (int r = 0; r < pips.Length; r++)
                    pips[r] += perResource[r] * s.VertexLevel[v];
            }
        return pips;
    }

    /// <summary>"12-pip spot (wheat 5, ore 4, sheep 3) with a 3:1 port".</summary>
    private static string SpotWords(Board board, int vertex)
    {
        var (total, perResource) = Production(board, vertex);
        var parts = Enumerable.Range(0, GameConstants.ResourceCount).Where(r => perResource[r] > 0)
            .OrderByDescending(r => perResource[r]).Select(r => $"{ResourceNames.Of(r)} {perResource[r]}");
        int spot = Topology.VertexHarbor[vertex];
        string port = spot < 0 ? "" : board.HarborTypeAt(spot) == HarborType.Generic ? " with a 3:1 port"
            : $" with a 2:1 {ResourceNames.Of((int)board.HarborTypeAt(spot))} port";
        return $"{total}-pip spot ({string.Join(", ", parts)}){port}";
    }

    /// <summary>How a spot fits what you produce: new resources, or more of what you already get the most of.</summary>
    private static IEnumerable<string> Fit(Board board, int vertex, int[] produced)
    {
        var (_, perResource) = Production(board, vertex);
        var fresh = Enumerable.Range(0, GameConstants.ResourceCount).Where(r => perResource[r] > 0 && produced[r] == 0).ToList();
        if (fresh.Count > 0)
            yield return $"adds {string.Join(" and ", fresh.Select(ResourceNames.Of))}, which you don't produce yet";
        int most = produced.Max();
        var stacked = Enumerable.Range(0, GameConstants.ResourceCount).Where(r => perResource[r] > 0 && produced[r] > 0 && produced[r] == most).ToList();
        if (fresh.Count == 0 && stacked.Count > 0)
            yield return $"more {string.Join(" and ", stacked.Select(ResourceNames.Of))}, which you already get the most of";
    }

    /// <summary>Who else is racing for the spot the road heads for.</summary>
    private static string RaceWords(List<(int Vertex, int Pips, int[] Distance)> before, (int Vertex, int Pips, int[] Distance) spot, int seat,
        IReadOnlyList<string>? names)
    {
        var rivals = Enumerable.Range(0, GameConstants.PlayerCount).Where(o => o != seat && spot.Distance[o] <= 2).ToList();
        var tied = rivals.Where(o => spot.Distance[o] == spot.Distance[seat]).ToList();
        if (tied.Count > 0)
            return $"{string.Join(" and ", tied.Select(o => Name(names, o)))} {(tied.Count == 1 ? "is" : "are")} just as close, but you play first";
        var was = before.FirstOrDefault(b => b.Vertex == spot.Vertex);
        if (was.Distance is not null)
        {
            var overtaken = Enumerable.Range(0, GameConstants.PlayerCount)
                .Where(o => o != seat && was.Distance[o] <= 2 && was.Distance[o] <= was.Distance[seat]).ToList();
            if (overtaken.Count > 0)
                return $"gets you there before {string.Join(" and ", overtaken.Select(o => Name(names, o)))}";
        }
        return rivals.Count == 0 ? "no one else is near it"
            : $"{string.Join(" and ", rivals.Select(o => Name(names, o)))} could get there too, but you're closer";
    }

    /// <summary>For a road below #1: what the best road's spot has that this one's doesn't.</summary>
    private static string Better(Board board, (int Vertex, int Pips, int[] Distance) bestSpot, (int Vertex, int Pips, int[] Distance)? mine, int[] produced)
    {
        if (mine is not { } m)
            return $"#1 heads for a {SpotWords(board, bestSpot.Vertex)}";
        if (m.Vertex == bestSpot.Vertex)
            return "#1 heads for the same spot, and the bots like the position after it a little more";
        if (bestSpot.Pips > m.Pips)
            return $"#1 heads for a {bestSpot.Pips}-pip spot, {bestSpot.Pips - m.Pips} more pips";
        var (_, bestRes) = Production(board, bestSpot.Vertex);
        var (_, mineRes) = Production(board, m.Vertex);
        var lacking = Enumerable.Range(0, GameConstants.ResourceCount).Where(r => bestRes[r] > 0 && produced[r] == 0 && mineRes[r] == 0).ToList();
        if (lacking.Count > 0)
            return $"#1's spot adds {string.Join(" and ", lacking.Select(ResourceNames.Of))}, which you lack";
        if (Topology.VertexHarbor[bestSpot.Vertex] >= 0 && Topology.VertexHarbor[m.Vertex] < 0)
            return "#1's spot has a port";
        return "#1's spot fits what you produce better";
    }

    /// <summary>
    /// In the opening: the open spots the other players' remaining settlements will probably take (the best ones, as many as
    /// they have left to place). Empty outside the opening.
    /// </summary>
    private static HashSet<int> OpeningTaken(GameState s, int seat)
    {
        var rank = new int[Topology.VertexCount];
        var left = new int[GameConstants.PlayerCount];
        var taken = new HashSet<int>();
        if (!Evaluator.OpeningRanks(s, rank, left))
            return taken;
        int others = left.Sum() - left[seat];
        for (int v = 0; v < Topology.VertexCount; v++)
            if (rank[v] < others)
                taken.Add(v);
        return taken;
    }

    /// <summary>How many opening settlements the other players still have to place (0 outside the opening).</summary>
    private static int OthersLeft(GameState s, int seat)
    {
        var rank = new int[Topology.VertexCount];
        var left = new int[GameConstants.PlayerCount];
        return Evaluator.OpeningRanks(s, rank, left) ? left.Sum() - left[seat] : 0;
    }

    /// <summary>Every open spot with its pips and how many roads each seat is from it.</summary>
    private static List<(int Vertex, int Pips, int[] Distance)> Race(GameState s)
    {
        var spots = new List<(int, int, int[])>();
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            if (!Evaluator.IsOpenSpot(s, v))
                continue;
            var distance = new int[GameConstants.PlayerCount];
            Evaluator.SpotDistances(s, v, distance);
            spots.Add((v, Production(s.Board, v).Total, distance));
        }
        return spots;
    }

    private static int Closest(int[] distance, int seat) =>
        Enumerable.Range(0, GameConstants.PlayerCount).Where(o => o != seat).OrderBy(o => distance[o]).First();

    private static string Name(IReadOnlyList<string>? names, int seat) => names is not null && seat < names.Count ? names[seat] : $"seat {seat + 1}";

    /// <summary>
    /// Plain facts about a settlement spot, from the position after it and its best road: scarce resources it produces,
    /// open spots within reach, and opponents as close to them (who could block).
    /// </summary>
    private static List<string> SpotFacts(GameState after, int seat, int[] spotPips, IReadOnlyList<string>? names)
    {
        var facts = new List<string>();
        // Scarcity: the resources with the fewest pips on the whole board.
        var boardPips = new int[GameConstants.ResourceCount];
        for (int h = 0; h < Topology.HexCount; h++)
            if (after.Board.ResourceAt(h) is var r and >= 0)
                boardPips[r] += after.Board.PipsAt(h);
        int scarcest = boardPips.Min();
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (spotPips[r] > 0 && boardPips[r] <= scarcest + 1)
                facts.Add($"{ResourceNames.Of(r)} is scarce on this board ({boardPips[r]} pips in all)");

        // Expansion and blocking risk.
        var taken = OpeningTaken(after, seat);
        var mine = Race(after).Where(p => Evaluator.RaceWinner(after, p.Distance) == seat && !taken.Contains(p.Vertex)).ToList();
        if (mine.Count == 0)
            facts.Add("no open spot within one more road");
        else
            facts.Add($"{mine.Count} open spot{(mine.Count == 1 ? "" : "s")} within one more road (best {mine.Max(p => p.Pips)} pips)");
        var contested = mine.Where(p => Enumerable.Range(0, GameConstants.PlayerCount).Any(o => o != seat && p.Distance[o] == p.Distance[seat]))
            .OrderByDescending(p => p.Pips).FirstOrDefault();
        if (contested.Distance is not null)
            facts.Add($"{Name(names, Closest(contested.Distance, seat))} is as close to your {contested.Pips}-pip spot: it could be blocked");
        return facts;
    }

    /// <summary>The seat's raw evaluation features.</summary>
    private static double[] Features(GameState s, int seat)
    {
        int n = BotWeights.Names.Count;
        var all = new double[GameConstants.PlayerCount * n];
        Evaluator.Features(s, all);
        return all.Skip(seat * n).Take(n).ToArray();
    }

    /// <summary>The features that gained the most weighted value, in plain words (up to three).</summary>
    private IReadOnlyList<string> Reasons(double[] before, double[] after)
    {
        var gains = new Dictionary<string, double>();
        for (int f = 0; f < before.Length; f++)
        {
            string? phrase = Phrase(BotWeights.Names[f]);
            double gain = _weights[BotWeights.Names[f]] * (after[f] - before[f]);
            if (phrase is not null && gain > 0.5)
                gains[phrase] = gains.GetValueOrDefault(phrase) + gain;
        }
        return gains.OrderByDescending(g => g.Value).Take(3).Select(g => g.Key).ToList();
    }

    private static string? Phrase(string feature) => feature switch
    {
        "prod_brick" or "prod_lumber" or "prod_wool" or "prod_grain" or "prod_ore" =>
            $"{ResourceNames.Of(Array.IndexOf(new[] { "prod_brick", "prod_lumber", "prod_wool", "prod_grain", "prod_ore" }, feature))} production",
        "diversity" => "resource variety",
        "city_combo" => "wheat + ore for cities",
        "road_combo" => "wood + brick for roads",
        "dev_combo" => "sheep + wheat + ore for dev cards",
        "harbor_2to1" => "a 2:1 harbor",
        "harbor_3to1" => "a 3:1 harbor",
        "hand_total" or "can_road" or "can_settlement" or "can_city" or "can_dev" => "useful starting cards",
        "settle_spots" => "room to expand",
        "best_spot_pips" => "a strong spot to expand to",
        _ => null,
    };

    /// <summary>The cards a second settlement gives straight away: one per resource tile around it.</summary>
    public static ResourceSet StartingCards(Board board, int vertex)
    {
        var cards = new int[GameConstants.ResourceCount];
        for (int h = 0; h < Topology.HexCount; h++)
            for (int c = 0; c < 6; c++)
                if (Topology.HexVertices[h, c] == vertex && board.ResourceAt(h) >= 0)
                    cards[board.ResourceAt(h)]++;
        return ResourceSet.From(cards);
    }

    /// <summary>Total pips around a spot, and pips per resource.</summary>
    public static (int Total, int[] PerResource) Production(Board board, int vertex)
    {
        var perResource = new int[GameConstants.ResourceCount];
        int total = 0;
        for (int h = 0; h < Topology.HexCount; h++)
        {
            bool touches = false;
            for (int c = 0; c < 6; c++)
                touches |= Topology.HexVertices[h, c] == vertex;
            int r = board.ResourceAt(h);
            if (!touches || r < 0)
                continue;
            perResource[r] += board.PipsAt(h);
            total += board.PipsAt(h);
        }
        return (total, perResource);
    }
}
