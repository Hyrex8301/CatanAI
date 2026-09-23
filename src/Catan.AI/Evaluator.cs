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

    private static readonly int FVp = I("vp"), FProd = I("prod_brick"), FDiversity = I("diversity"), FHarbor2 = I("harbor_2to1"),
        FHarbor3 = I("harbor_3to1"), FHand = I("hand_total"), FOver7 = I("hand_over7"), FCanRoad = I("can_road"),
        FCanSettlement = I("can_settlement"), FCanCity = I("can_city"), FCanDev = I("can_dev"), FSpots = I("settle_spots"),
        FBestSpot = I("best_spot_pips"), FRoad = I("road_length"), FRoadGap = I("longest_road_gap"), FKnights = I("knights"),
        FArmyGap = I("army_gap"), FDev = I("dev_cards"), FBlocked = I("robber_blocked"), FOppMax = I("opp_max"), FOppMean = I("opp_mean");

    private static int I(string name) => BotWeights.IndexOf(name);

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
    public void Values(GameState s, Span<double> value)
    {
        Span<double> features = stackalloc double[Seats * BotWeights.Names.Count];
        Features(s, features);
        int n = BotWeights.Names.Count;
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
        int n = BotWeights.Names.Count;
        f.Clear();

        // Production in pips (x/36 is the expected cards per roll), and pips the robber blocks.
        Span<double> prod = stackalloc double[Seats * R];
        Span<double> blocked = stackalloc double[Seats];
        for (int h = 0; h < Topology.HexCount; h++)
        {
            int r = s.Board.ResourceAt(h);
            if (r < 0)
                continue;
            int pips = s.Board.PipsAt(h);
            for (int c = 0; c < 6; c++)
            {
                int v = Topology.HexVertices[h, c];
                int owner = s.VertexOwner[v];
                if (owner < 0)
                    continue;
                if (h == s.RobberHex)
                    blocked[owner] += pips * s.VertexLevel[v];
                else
                    prod[owner * R + r] += pips * s.VertexLevel[v];
            }
        }

        // Harbors owned.
        Span<bool> generic = stackalloc bool[Seats];
        Span<bool> twoToOne = stackalloc bool[Seats * R];
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            int owner = s.VertexOwner[v], spot = Topology.VertexHarbor[v];
            if (owner < 0 || spot < 0)
                continue;
            var type = s.Board.HarborTypeAt(spot);
            if (type == HarborType.Generic)
                generic[owner] = true;
            else
                twoToOne[owner * R + (int)type] = true;
        }

        // Where each seat could settle next (distance rule and one of its roads; cost ignored).
        Span<int> spots = stackalloc int[Seats];
        Span<int> bestSpot = stackalloc int[Seats];
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            if (s.VertexOwner[v] >= 0 || NextToBuilding(s, v))
                continue;
            int pips = 0;
            for (int i = 0; i < 3; i++)
            {
                int h = Topology.VertexHexes[v, i];
                if (h >= 0)
                    pips += s.Board.PipsAt(h);
            }
            for (int i = 0; i < 3; i++)
            {
                int e = Topology.VertexEdges[v, i];
                if (e < 0 || s.EdgeOwner[e] < 0)
                    continue;
                int owner = s.EdgeOwner[e];
                if (i > 0 && SeenOwner(s, v, i, owner))
                    continue; // count each seat once per vertex
                spots[owner]++;
                bestSpot[owner] = Math.Max(bestSpot[owner], pips);
            }
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
            row[FHarbor2] = harbor2;
            row[FHarbor3] = generic[seat] ? 1 : 0;
            row[FHand] = handSize;
            row[FOver7] = Math.Max(0, handSize - Rules.DiscardLimit);
            row[FCanRoad] = s.RoadsLeft[seat] > 0 && Costs.Road.FitsIn(hand) ? 1 : 0;
            row[FCanSettlement] = s.SettlementsLeft[seat] > 0 && Costs.Settlement.FitsIn(hand) ? 1 : 0;
            row[FCanCity] = s.CitiesLeft[seat] > 0 && Costs.City.FitsIn(hand) ? 1 : 0;
            row[FCanDev] = Costs.DevCard.FitsIn(hand) ? 1 : 0;
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

    private static bool NextToBuilding(GameState s, int v)
    {
        for (int i = 0; i < 3; i++)
        {
            int nb = Topology.VertexNeighbors[v, i];
            if (nb >= 0 && s.VertexOwner[nb] >= 0)
                return true;
        }
        return false;
    }

    private static bool SeenOwner(GameState s, int v, int upTo, int owner)
    {
        for (int j = 0; j < upTo; j++)
        {
            int e = Topology.VertexEdges[v, j];
            if (e >= 0 && s.EdgeOwner[e] == owner)
                return true;
        }
        return false;
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
