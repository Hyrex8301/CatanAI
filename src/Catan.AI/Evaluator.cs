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

    private static readonly int FDeadRoads = I("dead_roads"), FLeaderThreat = I("leader_threat"), FLastHelp = I("last_help");

    private static readonly int FKnightBlocked = I("knight_blocked"), FVpLead = I("vp_lead"), FRival = I("rival");

    private static readonly int FStuck = I("settles_stuck"), FStuckCityCombo = I("stuck_city_combo"), FStuckCityMissing = I("stuck_city_missing");

    private static readonly int FRobberMagnet = I("robber_magnet"), FPublicLead = I("public_lead");

    /// <summary>Points at which the leader threat starts to count (it is full one point from winning).</summary>
    private const int ThreatStartVp = 5;

    private static int I(string name) => BotWeights.IndexOf(name);

    private static readonly int FeatureCount = BotWeights.Names.Count;

    // Topology tables flattened to [vertex * 3 + i]: two-dimensional arrays are slow to index in this hot loop.
    private static readonly int[] VertexHexes = Flatten(Topology.VertexHexes), VertexEdges = Flatten(Topology.VertexEdges),
        VertexNeighbors = Flatten(Topology.VertexNeighbors),
        EdgeVertices = Flatten(Topology.EdgeVertices);

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
        double score = value[me] - Weights[FOppMax] * best - Weights[FOppMean] * sum / (Seats - 1);

        // Leader threat: from 5 points on, the opponent with the most points counts extra against us (fully at one point
        // from winning), and the others count less, so slowing the leader and helping whoever is behind both pay.
        int leader = -1, leaderVp = 0;
        for (int seat = 0; seat < Seats; seat++)
        {
            int vp = s.TotalVP(seat);
            if (seat != me && (leader < 0 || vp > leaderVp || (vp == leaderVp && value[seat] > value[leader])))
                (leader, leaderVp) = (seat, vp);
        }
        double t = Math.Clamp((leaderVp - ThreatStartVp) / (double)Math.Max(1, s.Settings.VpToWin - 1 - ThreatStartVp), 0, 1);
        if (t > 0)
            score += -Weights[FLeaderThreat] * t * value[leader] + Weights[FOppMean] * Weights[FLastHelp] * t * (sum - value[leader]) / (Seats - 1);

        // Rivals: opponents going for the same award as us (both into dev cards and knights, or both building long roads)
        // count extra, so blocking and robbing them pays more.
        double rivals = 0;
        for (int seat = 0; seat < Seats; seat++)
            if (seat != me)
                rivals += Rivalry(s, me, seat) * value[seat];
        if (rivals != 0)
            score -= Weights[FRival] * rivals / (Seats - 1);
        return score;
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
        Span<int> onHex = stackalloc int[Seats * Topology.HexCount]; // pips x level per seat per hex (robber_magnet)
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
                    onHex[owner * Topology.HexCount + h] += board.PipsAt(h) * level;
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

        // Dead roads: road ends (no building or other road of ours there) with no free spot at the end or one road further.
        Span<int> dead = stackalloc int[Seats];
        for (int e = 0; e < Topology.EdgeCount; e++)
        {
            int owner = edgeOwner[e];
            if (owner < 0)
                continue;
            for (int end = 0; end < 2; end++)
            {
                int v = EdgeVertices[e * 2 + end];
                if (IsRoadEnd(s, v, e, owner) && !LeadsSomewhere(s, v, e))
                    dead[owner]++;
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
            row[FDeadRoads] = dead[seat];
            // Holding a knight while the robber sits on one of our tiles (playing it would free the tile).
            row[FKnightBlocked] = blocked[seat] > 0 && s.DevHand[seat * D + (int)DevCardType.Knight] > 0 ? 1 : 0;
            // How far ahead of everyone we are (a big lead makes us the target).
            int otherVp = 0;
            for (int other = 0; other < Seats; other++)
                if (other != seat)
                    otherVp = Math.Max(otherVp, s.TotalVP(other));
            row[FVpLead] = Math.Clamp(s.TotalVP(seat) - otherVp, 0, 5);
            // Out of settlements: every one is on the board, so growing takes a city first.
            bool stuck = s.SettlementsLeft[seat] == 0 && s.CitiesLeft[seat] > 0;
            row[FStuck] = stuck ? 1 : 0;
            row[FStuckCityCombo] = stuck ? row[FCityCombo] : 0;
            row[FStuckCityMissing] = stuck ? row[FCityMissing] : 0;
            // The obvious robber target: the most production one robber placement could shut off (not where it stands now),
            // and the lead in points everyone can see.
            int magnet = 0;
            for (int h = 0; h < Topology.HexCount; h++)
                if (h != robber)
                    magnet = Math.Max(magnet, onHex[seat * Topology.HexCount + h]);
            row[FRobberMagnet] = magnet / 36.0;
            int otherPublic = 0;
            for (int other = 0; other < Seats; other++)
                if (other != seat)
                    otherPublic = Math.Max(otherPublic, s.PublicVP[other]);
            row[FPublicLead] = Math.Clamp(s.PublicVP[seat] - otherPublic, 0, 5);
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

    /// <summary>How many awards two seats are both going for: Largest Army (both have 2+ knights played or dev cards held)
    /// and Longest Road (both have a road of 4+).</summary>
    private static int Rivalry(GameState s, int a, int b)
    {
        int rivalry = 0;
        if (ArmyInterest(s, a) >= 2 && ArmyInterest(s, b) >= 2)
            rivalry++;
        if (s.RoadLength[a] >= 4 && s.RoadLength[b] >= 4)
            rivalry++;
        return rivalry;
    }

    private static int ArmyInterest(GameState s, int seat)
    {
        int cards = s.KnightsPlayed[seat];
        for (int t = 0; t < D; t++)
            cards += s.DevHand[seat * D + t];
        return cards;
    }

    /// <summary>True if <paramref name="v"/> is where <paramref name="seat"/>'s road <paramref name="edge"/> stops: no building or other road of theirs.</summary>
    private static bool IsRoadEnd(GameState s, int v, int edge, int seat)
    {
        if (s.VertexOwner[v] == seat)
            return false;
        for (int i = 0; i < 3; i++)
        {
            int e = VertexEdges[v * 3 + i];
            if (e >= 0 && e != edge && s.EdgeOwner[e] == seat)
                return false;
        }
        return true;
    }

    /// <summary>A road end at <paramref name="v"/> still leads somewhere: a free spot there, or one more free road away.</summary>
    private static bool LeadsSomewhere(GameState s, int v, int edge)
    {
        if (s.VertexOwner[v] >= 0)
            return false; // someone else's building blocks the way
        if (IsFreeSpot(s, v))
            return true;
        for (int i = 0; i < 3; i++)
        {
            int e = VertexEdges[v * 3 + i], u = VertexNeighbors[v * 3 + i];
            if (e >= 0 && e != edge && s.EdgeOwner[e] < 0 && u >= 0 && IsFreeSpot(s, u))
                return true;
        }
        return false;
    }

    /// <summary>Empty, and no building next to it (the distance rule).</summary>
    private static bool IsFreeSpot(GameState s, int v)
    {
        if (s.VertexOwner[v] >= 0)
            return false;
        for (int i = 0; i < 3; i++)
        {
            int nb = VertexNeighbors[v * 3 + i];
            if (nb >= 0 && s.VertexOwner[nb] >= 0)
                return false;
        }
        return true;
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
