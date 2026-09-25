using Catan.Core;

namespace Catan.AI;

/// <summary>
/// Scores a position for one seat: a weighted sum of features per seat (<see cref="BotWeights"/>), then the seat's own value
/// minus a share of the strongest opponent's and of the average opponent's. A win or loss dominates everything.
/// Allocation-free; called for every candidate move, so it is kept simple and fast.
/// </summary>
public sealed class Evaluator
{
    public const double WinScore = 100_000;

    private const int Seats = GameConstants.PlayerCount;
    private const int R = GameConstants.ResourceCount;
    private const int D = GameConstants.DevCardTypeCount;

    private static readonly int FCityCombo = I("city_combo"), FRoadCombo = I("road_combo"), FDevCombo = I("dev_combo");

    private static readonly int FVp = I("vp"), FProd = I("prod_brick"), FDiversity = I("diversity"), FHarbor2 = I("harbor_2to1"),
        FHarbor3 = I("harbor_3to1"), FHand = I("hand_total"), FOver7 = I("hand_over7"), FCanRoad = I("can_road"),
        FCanSettlement = I("can_settlement"), FCanCity = I("can_city"), FCanDev = I("can_dev"), FSpots = I("settle_spots"),
        FBestSpot = I("best_spot_pips"), FRoad = I("road_length"), FRoadGap = I("longest_road_gap"), FKnights = I("knights"),
        FArmyGap = I("army_gap"), FDev = I("dev_cards"), FBlocked = I("robber_blocked"), FOppMax = I("opp_max"), FOppMean = I("opp_mean");

    private static readonly int FCityMissing = I("city_missing"), FSettlementMissing = I("settlement_missing"), FDevMissing = I("dev_missing");

    private static int I(string name) => BotWeights.IndexOf(name);

    private static readonly int FeatureCount = BotWeights.Names.Count;

    // Topology tables flattened to [vertex * 3 + i]: two-dimensional arrays are slow to index in this hot loop.
    private static readonly int[] VertexHexes = Flatten(Topology.VertexHexes), VertexEdges = Flatten(Topology.VertexEdges),
        VertexNeighbors = Flatten(Topology.VertexNeighbors);

    private static int[] Flatten(int[,] table) => table.Cast<int>().ToArray();

    public Evaluator(BotWeights weights) => Weights = weights;

    public BotWeights Weights { get; }

    /// <summary>How good <paramref name="s"/> is for <paramref name="me"/> (higher is better).</summary>
    public double Score(GameState s, int me)
    {
        if (s.Winner >= 0)
            return s.Winner == me ? WinScore : -WinScore;

        Span<double> value = stackalloc double[Seats];
        Values(s, value);
        double best = double.MinValue, sum = 0;
        for (int seat = 0; seat < Seats; seat++)
        {
            if (seat == me)
                continue;
            best = Math.Max(best, value[seat]);
            sum += value[seat];
        }
        return value[me] - Weights[FOppMax] * best - Weights[FOppMean] * sum / (Seats - 1);
    }

    /// <summary>Every seat's weighted feature total.</summary>
    [System.Runtime.CompilerServices.SkipLocalsInit]
    public void Values(GameState s, Span<double> value)
    {
        Span<double> features = stackalloc double[Seats * FeatureCount];
        Features(s, features);
        int n = FeatureCount;
        for (int seat = 0; seat < Seats; seat++)
        {
            double v = 0;
            for (int f = 0; f < n; f++)
                v += Weights[f] * features[seat * n + f];
            value[seat] = v;
        }
    }

    /// <summary>
    /// Raw features for every seat, laid out [seat * featureCount + feature] in <see cref="BotWeights.Names"/> order.
    /// The opponent-share entries (opp_max, opp_mean) are left 0: they're applied in <see cref="Score"/>.
    /// </summary>
    public static void Features(GameState s, Span<double> f)
    {
        int n = FeatureCount;
        f.Clear();
        var board = s.Board;
        var owners = s.VertexOwner;
        var edgeOwner = s.EdgeOwner;
        int robber = s.RobberHex;

        // One pass over the vertices. Buildings: production in pips (x/36 is the expected cards per roll), pips the robber
        // blocks, harbors owned. Empty spots: where each seat could settle next (distance rule and one of its roads; cost
        // ignored). All sums are whole numbers, so the order doesn't change the result.
        Span<int> prod = stackalloc int[Seats * R];
        Span<int> blocked = stackalloc int[Seats];
        Span<bool> generic = stackalloc bool[Seats];
        Span<bool> twoToOne = stackalloc bool[Seats * R];
        Span<int> spots = stackalloc int[Seats];
        Span<int> bestSpot = stackalloc int[Seats];
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            int owner = owners[v];
            if (owner >= 0)
            {
                int level = s.VertexLevel[v];
                for (int i = 0; i < 3; i++)
                {
                    int h = VertexHexes[v * 3 + i];
                    if (h < 0)
                        continue;
                    int r = board.ResourceAt(h);
                    if (r < 0)
                        continue;
                    if (h == robber)
                        blocked[owner] += board.PipsAt(h) * level;
                    else
                        prod[owner * R + r] += board.PipsAt(h) * level;
                }
                int spot = Topology.VertexHarbor[v];
                if (spot >= 0)
                {
                    var type = board.HarborTypeAt(spot);
                    if (type == HarborType.Generic)
                        generic[owner] = true;
                    else
                        twoToOne[owner * R + (int)type] = true;
                }
                continue;
            }

            int e0 = VertexEdges[v * 3], e1 = VertexEdges[v * 3 + 1], e2 = VertexEdges[v * 3 + 2];
            int o0 = e0 >= 0 ? edgeOwner[e0] : -1, o1 = e1 >= 0 ? edgeOwner[e1] : -1, o2 = e2 >= 0 ? edgeOwner[e2] : -1;
            if (o0 < 0 && o1 < 0 && o2 < 0)
                continue; // nobody's road reaches it
            int n0 = VertexNeighbors[v * 3], n1 = VertexNeighbors[v * 3 + 1], n2 = VertexNeighbors[v * 3 + 2];
            if ((n0 >= 0 && owners[n0] >= 0) || (n1 >= 0 && owners[n1] >= 0) || (n2 >= 0 && owners[n2] >= 0))
                continue;
            int pips = 0;
            for (int i = 0; i < 3; i++)
            {
                int h = VertexHexes[v * 3 + i];
                if (h >= 0)
                    pips += board.PipsAt(h);
            }
            // Count each seat once per vertex.
            if (o0 >= 0)
                AddSpot(spots, bestSpot, o0, pips);
            if (o1 >= 0 && o1 != o0)
                AddSpot(spots, bestSpot, o1, pips);
            if (o2 >= 0 && o2 != o0 && o2 != o1)
                AddSpot(spots, bestSpot, o2, pips);
        }

        int maxRoad = 0, maxKnights = 0;
        for (int seat = 0; seat < Seats; seat++)
        {
            maxRoad = Math.Max(maxRoad, s.RoadLength[seat]);
            maxKnights = Math.Max(maxKnights, s.KnightsPlayed[seat]);
        }

        for (int seat = 0; seat < Seats; seat++)
        {
            var row = f.Slice(seat * n, n);
            var hand = s.HandOf(seat);
            int handSize = s.HandSize(seat);

            row[FVp] = s.TotalVP(seat);
            int diversity = 0;
            double harbor2 = 0;
            for (int r = 0; r < R; r++)
            {
                double p = prod[seat * R + r];
                row[FProd + r] = p / 36.0;
                if (p > 0)
                    diversity++;
                if (twoToOne[seat * R + r])
                    harbor2 += p / 36.0;
            }
            row[FDiversity] = diversity;
            // Resources that only pay off together: ore + grain make cities, brick + lumber roads and settlements,
            // wool + grain + ore dev cards. The weaker of each set limits how often you can build.
            double brick = prod[seat * R + 0], lumber = prod[seat * R + 1], wool = prod[seat * R + 2], grain = prod[seat * R + 3], ore = prod[seat * R + 4];
            row[FCityCombo] = Math.Min(ore, grain) / 36.0;
            row[FRoadCombo] = Math.Min(brick, lumber) / 36.0;
            row[FDevCombo] = Math.Min(wool, Math.Min(grain, ore)) / 36.0;
            row[FHarbor2] = harbor2;
            row[FHarbor3] = generic[seat] ? 1 : 0;
            row[FHand] = handSize;
            row[FOver7] = Math.Max(0, handSize - Rules.DiscardLimit);
            row[FCanRoad] = s.RoadsLeft[seat] > 0 && Costs.Road.FitsIn(hand) ? 1 : 0;
            row[FCanSettlement] = s.SettlementsLeft[seat] > 0 && Costs.Settlement.FitsIn(hand) ? 1 : 0;
            row[FCanCity] = s.CitiesLeft[seat] > 0 && Costs.City.FitsIn(hand) ? 1 : 0;
            row[FCanDev] = Costs.DevCard.FitsIn(hand) ? 1 : 0;
            row[FCityMissing] = s.CitiesLeft[seat] > 0 && s.SettlementsLeft[seat] < Costs.SettlementsPerPlayer ? Missing(Costs.City, hand) : Costs.City.Total;
            row[FSettlementMissing] = s.SettlementsLeft[seat] > 0 && spots[seat] > 0 ? Missing(Costs.Settlement, hand) : Costs.Settlement.Total;
            row[FDevMissing] = DevDeckSize(s) > 0 ? Missing(Costs.DevCard, hand) : Costs.DevCard.Total;
            row[FSpots] = Math.Min(spots[seat], 6);
            row[FBestSpot] = bestSpot[seat];
            row[FRoad] = s.RoadLength[seat];
            row[FRoadGap] = Math.Clamp(s.RoadLength[seat] - OtherMax(s.RoadLength, seat), -5, 5);
            row[FKnights] = s.KnightsPlayed[seat];
            row[FArmyGap] = Math.Clamp(s.KnightsPlayed[seat] - OtherMax(s.KnightsPlayed, seat), -3, 3);
            int dev = 0;
            for (int t = 0; t < D; t++)
                if (t != (int)DevCardType.VictoryPoint)
                    dev += s.DevHand[seat * D + t];
            row[FDev] = dev;
            row[FBlocked] = blocked[seat] / 36.0;
        }
    }

    private static int Missing(ResourceSet cost, ReadOnlySpan<int> hand)
    {
        int missing = 0;
        for (int r = 0; r < R; r++)
            missing += Math.Max(0, cost[r] - hand[r]);
        return missing;
    }

    private static int DevDeckSize(GameState s)
    {
        int total = 0;
        for (int t = 0; t < D; t++)
            total += s.DevDeck[t];
        return total;
    }

    private static void AddSpot(Span<int> spots, Span<int> bestSpot, int seat, int pips)
    {
        spots[seat]++;
        bestSpot[seat] = Math.Max(bestSpot[seat], pips);
    }

    private static int OtherMax(int[] values, int seat)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++)
            if (i != seat)
                best = Math.Max(best, values[i]);
        return best;
    }
}
