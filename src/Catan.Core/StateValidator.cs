namespace Catan.Core;

/// <summary>
/// Recounts a state from scratch and reports every inconsistency: resources, dev cards, pieces, placement,
/// VP and road lengths. Tests and the Sim's --validate call it; an empty list means the state is consistent.
/// </summary>
public static class StateValidator
{
    private const int Seats = GameConstants.PlayerCount;
    private const int R = GameConstants.ResourceCount;
    private const int D = GameConstants.DevCardTypeCount;

    public static List<string> Check(GameState s)
    {
        var errors = new List<string>();
        CheckNonNegative(s, errors);
        CheckResources(s, errors);
        CheckDevCards(s, errors);
        CheckPieces(s, errors);
        CheckPlacement(s, errors);
        CheckScoring(s, errors);
        CheckTurn(s, errors);
        return errors;
    }

    private static void CheckNonNegative(GameState s, List<string> errors)
    {
        void Scan(string name, int[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] < 0)
                    errors.Add($"{name}[{i}] is negative ({values[i]}).");
        }

        Scan(nameof(s.Hand), s.Hand);
        Scan(nameof(s.Bank), s.Bank);
        Scan(nameof(s.DevHand), s.DevHand);
        Scan(nameof(s.DevBoughtThisTurn), s.DevBoughtThisTurn);
        Scan(nameof(s.DevDeck), s.DevDeck);
        Scan(nameof(s.DevPlayed), s.DevPlayed);
        Scan(nameof(s.KnightsPlayed), s.KnightsPlayed);
        Scan(nameof(s.RoadsLeft), s.RoadsLeft);
        Scan(nameof(s.SettlementsLeft), s.SettlementsLeft);
        Scan(nameof(s.CitiesLeft), s.CitiesLeft);
        Scan(nameof(s.DiscardOwed), s.DiscardOwed);
    }

    private static void CheckResources(GameState s, List<string> errors)
    {
        for (int r = 0; r < R; r++)
        {
            int total = s.Bank[r];
            for (int seat = 0; seat < Seats; seat++)
                total += s.Hand[seat * R + r];
            if (total != Costs.BankPerResource)
                errors.Add($"{(Resource)r}: bank + hands = {total}, expected {Costs.BankPerResource}.");
        }
    }

    private static void CheckDevCards(GameState s, List<string> errors)
    {
        for (int t = 0; t < D; t++)
        {
            int total = s.DevDeck[t] + s.DevPlayed[t];
            for (int seat = 0; seat < Seats; seat++)
                total += s.DevHand[seat * D + t];
            if (total != StandardPieces.DevDeck[t])
                errors.Add($"{(DevCardType)t}: deck + hands + played = {total}, expected {StandardPieces.DevDeck[t]}.");

            if (s.CurrentPlayer is >= 0 and < Seats && s.DevBoughtThisTurn[t] > s.DevHand[s.CurrentPlayer * D + t])
                errors.Add($"{(DevCardType)t}: {s.DevBoughtThisTurn[t]} bought this turn but seat {s.CurrentPlayer} holds {s.DevHand[s.CurrentPlayer * D + t]}.");
        }

        if (s.DevPlayed[(int)DevCardType.VictoryPoint] != 0)
            errors.Add("VictoryPoint cards are never played, but DevPlayed counts some.");
        int knights = s.KnightsPlayed.Sum();
        if (s.DevPlayed[(int)DevCardType.Knight] != knights)
            errors.Add($"DevPlayed[Knight] = {s.DevPlayed[(int)DevCardType.Knight]} but KnightsPlayed sums to {knights}.");
    }

    private static void CheckPieces(GameState s, List<string> errors)
    {
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            int owner = s.VertexOwner[v], level = s.VertexLevel[v];
            if (owner is < -1 or >= Seats)
                errors.Add($"Vertex {v} has invalid owner {owner}.");
            if (level > 2 || (owner < 0) != (level == 0))
                errors.Add($"Vertex {v} has owner {owner} but level {level}.");
        }
        for (int e = 0; e < Topology.EdgeCount; e++)
            if (s.EdgeOwner[e] is < -1 or >= Seats)
                errors.Add($"Edge {e} has invalid owner {s.EdgeOwner[e]}.");

        for (int seat = 0; seat < Seats; seat++)
        {
            int settlements = 0, cities = 0, roads = 0;
            for (int v = 0; v < Topology.VertexCount; v++)
                if (s.VertexOwner[v] == seat)
                {
                    if (s.VertexLevel[v] == 1) settlements++;
                    else if (s.VertexLevel[v] == 2) cities++;
                }
            for (int e = 0; e < Topology.EdgeCount; e++)
                if (s.EdgeOwner[e] == seat)
                    roads++;

            if (settlements + s.SettlementsLeft[seat] != Costs.SettlementsPerPlayer)
                errors.Add($"Seat {seat}: {settlements} settlements on board + {s.SettlementsLeft[seat]} left != {Costs.SettlementsPerPlayer}.");
            if (cities + s.CitiesLeft[seat] != Costs.CitiesPerPlayer)
                errors.Add($"Seat {seat}: {cities} cities on board + {s.CitiesLeft[seat]} left != {Costs.CitiesPerPlayer}.");
            if (roads + s.RoadsLeft[seat] != Costs.RoadsPerPlayer)
                errors.Add($"Seat {seat}: {roads} roads on board + {s.RoadsLeft[seat]} left != {Costs.RoadsPerPlayer}.");
        }
    }

    private static void CheckPlacement(GameState s, List<string> errors)
    {
        // Distance rule: no two buildings on neighboring vertices.
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            if (s.VertexOwner[v] < 0)
                continue;
            for (int i = 0; i < 3; i++)
            {
                int n = Topology.VertexNeighbors[v, i];
                if (n > v && s.VertexOwner[n] >= 0)
                    errors.Add($"Buildings on neighboring vertices {v} and {n} break the distance rule.");
            }
        }

        // Every road is connected, through its owner's roads, to one of its owner's buildings.
        Span<bool> reached = stackalloc bool[Topology.EdgeCount];
        Span<int> queue = stackalloc int[Topology.EdgeCount];
        for (int seat = 0; seat < Seats; seat++)
        {
            reached.Clear();
            int head = 0, tail = 0;
            for (int e = 0; e < Topology.EdgeCount; e++)
                if (s.EdgeOwner[e] == seat &&
                    (s.VertexOwner[Topology.EdgeVertices[e, 0]] == seat || s.VertexOwner[Topology.EdgeVertices[e, 1]] == seat))
                {
                    reached[e] = true;
                    queue[tail++] = e;
                }
            while (head < tail)
            {
                int e = queue[head++];
                for (int slot = 0; slot < 4; slot++)
                {
                    int f = Topology.EdgeNeighbors[e, slot];
                    if (f >= 0 && !reached[f] && s.EdgeOwner[f] == seat)
                    {
                        reached[f] = true;
                        queue[tail++] = f;
                    }
                }
            }
            for (int e = 0; e < Topology.EdgeCount; e++)
                if (s.EdgeOwner[e] == seat && !reached[e])
                    errors.Add($"Seat {seat}'s road on edge {e} isn't connected to any of its buildings.");
        }

        if (s.RobberHex is < 0 or >= Topology.HexCount)
            errors.Add($"Robber is on invalid hex {s.RobberHex}.");
    }

    private static void CheckScoring(GameState s, List<string> errors)
    {
        for (int seat = 0; seat < Seats; seat++)
        {
            int length = LongestRoad.Compute(s, seat);
            if (s.RoadLength[seat] != length)
                errors.Add($"Seat {seat}: cached road length {s.RoadLength[seat]}, recount {length}.");
        }

        // Longest Road: the holder has 5+ and is at least tied for longest. With no holder, nobody is longest alone with 5+.
        int lr = s.LongestRoadOwner;
        if (lr is < -1 or >= Seats)
            errors.Add($"Invalid LongestRoadOwner {lr}.");
        else if (lr >= 0)
        {
            if (s.RoadLength[lr] < 5)
                errors.Add($"Seat {lr} holds Longest Road with length {s.RoadLength[lr]} < 5.");
            if (s.RoadLength.Max() > s.RoadLength[lr])
                errors.Add($"Seat {lr} holds Longest Road but another seat's road is longer.");
        }
        else if (UniqueMax(s.RoadLength, 5) >= 0)
            errors.Add($"Nobody holds Longest Road, but seat {UniqueMax(s.RoadLength, 5)} is longest alone with 5+.");

        // Largest Army: the holder has 3+ Knights and at least as many as anyone. It is never set aside.
        int la = s.LargestArmyOwner;
        if (la is < -1 or >= Seats)
            errors.Add($"Invalid LargestArmyOwner {la}.");
        else if (la >= 0)
        {
            if (s.KnightsPlayed[la] < 3)
                errors.Add($"Seat {la} holds Largest Army with {s.KnightsPlayed[la]} Knights < 3.");
            if (s.KnightsPlayed.Max() > s.KnightsPlayed[la])
                errors.Add($"Seat {la} holds Largest Army but another seat has played more Knights.");
        }
        else if (s.KnightsPlayed.Max() >= 3)
            errors.Add("Nobody holds Largest Army, but a seat has played 3+ Knights.");

        for (int seat = 0; seat < Seats; seat++)
        {
            int vp = (lr == seat ? 2 : 0) + (la == seat ? 2 : 0);
            for (int v = 0; v < Topology.VertexCount; v++)
                if (s.VertexOwner[v] == seat)
                    vp += s.VertexLevel[v];
            if (s.PublicVP[seat] != vp)
                errors.Add($"Seat {seat}: cached public VP {s.PublicVP[seat]}, recount {vp}.");
        }
    }

    private static void CheckTurn(GameState s, List<string> errors)
    {
        if (s.CurrentPlayer is < 0 or >= Seats)
            errors.Add($"Invalid CurrentPlayer {s.CurrentPlayer}.");
        if (s.Winner is < -1 or >= Seats)
            errors.Add($"Invalid Winner {s.Winner}.");
        else if (s.Winner >= 0)
        {
            if (s.Phase != Phase.GameOver)
                errors.Add($"Seat {s.Winner} won but the phase is {s.Phase}.");
            if (s.TotalVP(s.Winner) < s.Settings.VpToWin)
                errors.Add($"Seat {s.Winner} won with only {s.TotalVP(s.Winner)} VP.");
        }
        if (s.Phase != Phase.GameOver && s.CurrentPlayer is >= 0 and < Seats && s.TotalVP(s.CurrentPlayer) >= s.Settings.VpToWin)
            errors.Add($"Seat {s.CurrentPlayer} has {s.TotalVP(s.CurrentPlayer)} VP on its own turn but the game isn't over.");
        if (!Enum.IsDefined(s.Phase))
            errors.Add($"Invalid Phase {(int)s.Phase}.");

        if (s.Phase == Phase.RoadBuilding && s.FreeRoads <= 0)
            errors.Add("Phase is RoadBuilding but no free roads are left.");
        if (s.Phase != Phase.RoadBuilding && s.FreeRoads != 0)
            errors.Add($"FreeRoads is {s.FreeRoads} outside the RoadBuilding phase.");

        int owed = s.DiscardOwed.Sum();
        if (s.Phase == Phase.Discard && owed == 0)
            errors.Add("Phase is Discard but nobody owes a discard.");
        if (s.Phase != Phase.Discard && owed > 0)
            errors.Add($"Discards are owed outside the Discard phase ({s.Phase}).");
        for (int seat = 0; seat < Seats; seat++)
            if (s.DiscardOwed[seat] > s.HandSize(seat))
                errors.Add($"Seat {seat} owes {s.DiscardOwed[seat]} discards but holds {s.HandSize(seat)} cards.");
    }

    /// <summary>The seat with the strictly highest value, if that value is at least <paramref name="minimum"/>; else -1.</summary>
    internal static int UniqueMax(int[] values, int minimum)
    {
        int best = -1, bestValue = int.MinValue;
        bool tied = false;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > bestValue)
            {
                best = i;
                bestValue = values[i];
                tied = false;
            }
            else if (values[i] == bestValue)
                tied = true;
        }
        return !tied && bestValue >= minimum ? best : -1;
    }
}
