using Catan.Core;

namespace Catan.AI;

/// <summary>One settlement spot as the coach sees it.</summary>
/// <param name="Rating">0-100 within this position: 100 for the best legal spot, 0 for the worst.</param>
/// <param name="Rank">1 for the best spot.</param>
/// <param name="Pips">Total dice pips of the tiles around the spot (how often it produces).</param>
/// <param name="ResourcePips">Pips per resource.</param>
/// <param name="Harbor">The harbor at the spot, or null.</param>
public sealed record SpotRating(int Vertex, double Score, double Rating, int Rank, int Pips, int[] ResourcePips, HarborType? Harbor);

/// <summary>
/// Practice for opening placements: deals a board with the players before you already placed (by SmartBot), then rates every
/// legal spot for your first settlement the way the bots judge it (their evaluation of the settlement plus its best road).
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
    /// A new practice position from <paramref name="seed"/>: a balanced board, your seat (1st to 4th in turn order), and the
    /// settlements and roads of the players before you, placed by SmartBots.
    /// </summary>
    public GameState Deal(ulong seed, out int seat)
    {
        var rng = new Rng(seed, stream: 3);
        var s = new GameState(BoardGenerator.Balanced(new Rng(seed)));
        seat = rng.NextInt(GameConstants.PlayerCount);
        var chance = new RngChance(seed);
        var legal = new List<GameAction>();
        while (Rules.ActingSeat(s) != seat)
        {
            int acting = Rules.ActingSeat(s);
            var bot = new SmartBot(_weights, SmartBotSettings.Play, seed * 31 + (ulong)acting);
            Rules.GetLegalActions(s, acting, legal);
            var move = bot.DecideAsync(PlayerView.From(s, acting), legal, default).GetAwaiter().GetResult();
            Rules.Apply(s, move, chance);
        }
        return s;
    }

    /// <summary>Every legal settlement spot for <paramref name="seat"/>, best first.</summary>
    public IReadOnlyList<SpotRating> Rate(GameState s, int seat)
    {
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, seat, legal);
        legal.RemoveAll(a => a.Type != ActionType.BuildSettlement);
        // Every spot gets a full score (the planner's beam only looks closely at its top few): the settlement plus its best
        // road, judged by the bots' evaluation.
        var scores = new double[legal.Count];
        var roads = new List<GameAction>();
        var chance = new RngChance(1);
        for (int i = 0; i < legal.Count; i++)
        {
            var placed = s.Clone();
            Rules.Apply(placed, legal[i], chance);
            Rules.GetLegalActions(placed, seat, roads);
            scores[i] = double.MinValue;
            foreach (var road in roads)
            {
                var after = placed.Clone();
                Rules.Apply(after, road, chance);
                scores[i] = Math.Max(scores[i], _eval.Score(after, seat));
            }
            if (roads.Count == 0)
                scores[i] = _eval.Score(placed, seat);
        }

        double best = scores.DefaultIfEmpty(0).Max(), worst = scores.DefaultIfEmpty(0).Min();
        var order = Enumerable.Range(0, legal.Count).OrderByDescending(i => scores[i]).ThenBy(i => legal[i].Target).ToList();
        return order.Select((i, rank) =>
        {
            int vertex = legal[i].Target;
            var (pips, perResource) = Production(s.Board, vertex);
            int spot = Topology.VertexHarbor[vertex];
            double rating = best > worst ? 100 * (scores[i] - worst) / (best - worst) : 100;
            return new SpotRating(vertex, scores[i], rating, rank + 1, pips, perResource, spot >= 0 ? s.Board.HarborTypeAt(spot) : null);
        }).ToList();
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
