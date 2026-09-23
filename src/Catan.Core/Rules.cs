namespace Catan.Core;

/// <summary>
/// All game rules, as static functions over <see cref="GameState"/>. Split by area into partial files.
/// Apply assumes a legal action (search calls it directly); ApplyChecked validates first.
/// </summary>
public static partial class Rules
{
    private const int R = GameConstants.ResourceCount;

    /// <summary>The one seat that must act next, or -1 once the game is over.</summary>
    public static int ActingSeat(GameState s) => s.Phase switch
    {
        Phase.GameOver => -1,
        _ => s.CurrentPlayer,
    };

    /// <summary>Fills <paramref name="buffer"/> (cleared first) with every legal action. No allocations beyond list growth.</summary>
    public static void GetLegalActions(GameState s, List<GameAction> buffer)
    {
        buffer.Clear();
        int seat = ActingSeat(s);
        if (seat < 0)
            return;
        switch (s.Phase)
        {
            case Phase.SetupSettlement: SetupSettlementActions(s, seat, buffer); break;
            case Phase.SetupRoad: SetupRoadActions(s, seat, buffer); break;
            case Phase.PreRoll: PreRollActions(s, seat, buffer); break;
            case Phase.Main: MainActions(s, seat, buffer); break;
        }
    }

    public static bool IsLegal(GameState s, GameAction a, out string reason)
    {
        int seat = ActingSeat(s);
        if (seat < 0)
            return Fail("The game is over.", out reason);
        if (a.Seat != seat)
            return Fail($"It's seat {seat}'s turn to act, not seat {a.Seat}'s.", out reason);

        return s.Phase switch
        {
            Phase.SetupSettlement => IsLegalSetupSettlement(s, a, out reason),
            Phase.SetupRoad => IsLegalSetupRoad(s, a, out reason),
            Phase.PreRoll => IsLegalPreRoll(s, a, out reason),
            Phase.Main => IsLegalMain(s, a, out reason),
            _ => Fail($"{s.Phase} isn't implemented yet.", out reason),
        };
    }

    /// <summary>Applies a legal action. Behavior for an illegal action is undefined; use <see cref="ApplyChecked"/> when unsure.</summary>
    public static void Apply(GameState s, GameAction a, IChance chance, List<GameEvent>? events = null)
    {
        switch (a.Type)
        {
            case ActionType.BuildSettlement when s.Phase == Phase.SetupSettlement: ApplySetupSettlement(s, a, events); break;
            case ActionType.BuildRoad when s.Phase == Phase.SetupRoad: ApplySetupRoad(s, a, events); break;
            case ActionType.BuildSettlement: ApplyBuildSettlement(s, a, events); break;
            case ActionType.BuildRoad: ApplyBuildRoad(s, a, events); break;
            case ActionType.BuildCity: ApplyBuildCity(s, a, events); break;
            case ActionType.RollDice: ApplyRoll(s, a, chance, events); break;
            case ActionType.BuyDevCard: ApplyBuyDevCard(s, a, chance, events); break;
            case ActionType.EndTurn: ApplyEndTurn(s, a, events); break;
            default: throw new InvalidOperationException($"{a.Type} isn't implemented yet.");
        }
    }

    /// <summary>Validates, then applies. Throws <see cref="InvalidOperationException"/> with the reason if illegal.</summary>
    public static void ApplyChecked(GameState s, GameAction a, IChance chance, List<GameEvent>? events = null)
    {
        if (!IsLegal(s, a, out string reason))
            throw new InvalidOperationException($"Illegal {a.Type} by seat {a.Seat}: {reason}");
        Apply(s, a, chance, events);
    }

    // ---- Shared placement checks ----

    /// <summary>Empty vertex with no building on any neighboring vertex.</summary>
    private static bool PassesDistanceRule(GameState s, int vertex, out string reason)
    {
        if (vertex is < 0 or >= Topology.VertexCount)
            return Fail($"Vertex {vertex} doesn't exist.", out reason);
        if (s.VertexOwner[vertex] >= 0)
            return Fail($"Vertex {vertex} already has a building.", out reason);
        for (int i = 0; i < 3; i++)
        {
            int n = Topology.VertexNeighbors[vertex, i];
            if (n >= 0 && s.VertexOwner[n] >= 0)
                return Fail($"Vertex {vertex} is next to a building on vertex {n} (distance rule).", out reason);
        }
        reason = "";
        return true;
    }

    private static bool IsEmptyEdge(GameState s, int edge, out string reason)
    {
        if (edge is < 0 or >= Topology.EdgeCount)
            return Fail($"Edge {edge} doesn't exist.", out reason);
        if (s.EdgeOwner[edge] >= 0)
            return Fail($"Edge {edge} already has a road.", out reason);
        reason = "";
        return true;
    }

    // ---- Shared state updates ----

    private static void PlaceSettlement(GameState s, int seat, int vertex, List<GameEvent>? events)
    {
        s.VertexOwner[vertex] = (sbyte)seat;
        s.VertexLevel[vertex] = 1;
        s.SettlementsLeft[seat]--;
        s.PublicVP[seat]++;
        events?.Add(new Built(seat, PieceType.Settlement, vertex));
    }

    private static void PlaceRoad(GameState s, int seat, int edge, List<GameEvent>? events)
    {
        s.EdgeOwner[edge] = (sbyte)seat;
        s.RoadsLeft[seat]--;
        s.RoadLength[seat] = LongestRoad.Compute(s, seat);
        events?.Add(new Built(seat, PieceType.Road, edge));
    }

    /// <summary>Moves cards from the bank to a seat's hand.</summary>
    private static void GiveFromBank(GameState s, int seat, ResourceSet cards)
    {
        for (int r = 0; r < R; r++)
        {
            s.Bank[r] -= cards[r];
            s.Hand[seat * R + r] += cards[r];
        }
    }

    private static bool Fail(string why, out string reason)
    {
        reason = why;
        return false;
    }
}
