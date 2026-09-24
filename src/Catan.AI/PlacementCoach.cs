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
public sealed record SpotRating(int Vertex, double Score, double Rating, int Rank, int Pips, int[] ResourcePips, HarborType? Harbor,
    ResourceSet StartingCards, IReadOnlyList<string> Reasons);

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

    /// <summary>Every legal settlement spot for <paramref name="seat"/>, best first.</summary>
    public IReadOnlyList<SpotRating> Rate(GameState s, int seat)
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
                starting, Reasons(before, Features(bestStates[i], seat)));
        }).ToList();
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
