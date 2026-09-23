namespace Catan.Core;

public static partial class Rules
{
    public const int SetupSteps = 8;

    /// <summary>Snake order 0, 1, 2, 3, 3, 2, 1, 0: the seat placing at setup step 0-7.</summary>
    public static int SetupSeat(int step) => step < 4 ? step : 7 - step;

    /// <summary>
    /// The settlement the current seat just placed in setup: its only settlement with none of its roads touching it.
    /// (A setup road can't end next to where the second settlement goes, because of the distance rule.)
    /// </summary>
    public static int SetupRoadAnchor(GameState s)
    {
        int seat = s.CurrentPlayer;
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            if (s.VertexOwner[v] != seat)
                continue;
            bool hasRoad = false;
            for (int i = 0; i < 3; i++)
            {
                int e = Topology.VertexEdges[v, i];
                if (e >= 0 && s.EdgeOwner[e] == seat)
                    hasRoad = true;
            }
            if (!hasRoad)
                return v;
        }
        return -1;
    }

    private static void SetupSettlementActions(GameState s, int seat, List<GameAction> buffer)
    {
        for (int v = 0; v < Topology.VertexCount; v++)
            if (PassesDistanceRule(s, v, out _))
                buffer.Add(new GameAction(ActionType.BuildSettlement, seat, v));
    }

    private static void SetupRoadActions(GameState s, int seat, List<GameAction> buffer)
    {
        int anchor = SetupRoadAnchor(s);
        for (int i = 0; i < 3; i++)
        {
            int e = Topology.VertexEdges[anchor, i];
            if (e >= 0 && s.EdgeOwner[e] < 0)
                buffer.Add(new GameAction(ActionType.BuildRoad, seat, e));
        }
    }

    private static bool IsLegalSetupSettlement(GameState s, GameAction a, out string reason)
    {
        if (a.Type != ActionType.BuildSettlement)
            return Fail("During setup, place a settlement first.", out reason);
        return PassesDistanceRule(s, a.Target, out reason);
    }

    private static bool IsLegalSetupRoad(GameState s, GameAction a, out string reason)
    {
        if (a.Type != ActionType.BuildRoad)
            return Fail("During setup, place a road next to the settlement you just placed.", out reason);
        if (!IsEmptyEdge(s, a.Target, out reason))
            return false;
        int anchor = SetupRoadAnchor(s);
        if (Topology.EdgeVertices[a.Target, 0] != anchor && Topology.EdgeVertices[a.Target, 1] != anchor)
            return Fail($"The setup road must touch the settlement just placed (vertex {anchor}).", out reason);
        return true;
    }

    private static void ApplySetupSettlement(GameState s, GameAction a, List<GameEvent>? events)
    {
        PlaceSettlement(s, a.Seat, a.Target, events);

        // The second settlement pays 1 card for each adjacent non-desert hex.
        if (s.SetupStep >= 4)
        {
            Span<int> gained = stackalloc int[R];
            for (int i = 0; i < 3; i++)
            {
                int h = Topology.VertexHexes[a.Target, i];
                if (h >= 0 && s.Board.ResourceAt(h) >= 0)
                    gained[s.Board.ResourceAt(h)]++;
            }
            var cards = ResourceSet.From(gained);
            if (cards.Total > 0)
            {
                GiveFromBank(s, a.Seat, cards);
                events?.Add(new ResourcesProduced(a.Seat, cards));
            }
        }

        s.Phase = Phase.SetupRoad;
    }

    private static void ApplySetupRoad(GameState s, GameAction a, List<GameEvent>? events)
    {
        PlaceRoad(s, a.Seat, a.Target, events);

        s.SetupStep++;
        if (s.SetupStep < SetupSteps)
        {
            s.CurrentPlayer = SetupSeat(s.SetupStep);
            s.Phase = Phase.SetupSettlement;
        }
        else
        {
            s.CurrentPlayer = 0;
            s.TurnNumber = 1;
            s.HasRolled = false;
            s.Phase = Phase.PreRoll;
        }
    }
}
