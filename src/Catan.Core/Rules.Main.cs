namespace Catan.Core;

public static partial class Rules
{
    private const int DevTypes = GameConstants.DevCardTypeCount;

    private static void MainActions(GameState s, int seat, List<GameAction> buffer)
    {
        var hand = s.HandOf(seat);

        if (s.RoadsLeft[seat] > 0 && Costs.Road.FitsIn(hand))
            for (int e = 0; e < Topology.EdgeCount; e++)
                if (CanPlaceRoad(s, seat, e, out _))
                    buffer.Add(new GameAction(ActionType.BuildRoad, seat, e));

        if (s.SettlementsLeft[seat] > 0 && Costs.Settlement.FitsIn(hand))
            for (int v = 0; v < Topology.VertexCount; v++)
                if (CanPlaceSettlement(s, seat, v, out _))
                    buffer.Add(new GameAction(ActionType.BuildSettlement, seat, v));

        if (s.CitiesLeft[seat] > 0 && Costs.City.FitsIn(hand))
            for (int v = 0; v < Topology.VertexCount; v++)
                if (s.VertexOwner[v] == seat && s.VertexLevel[v] == 1)
                    buffer.Add(new GameAction(ActionType.BuildCity, seat, v));

        if (DevDeckSize(s) > 0 && Costs.DevCard.FitsIn(hand))
            buffer.Add(new GameAction(ActionType.BuyDevCard, seat));

        buffer.Add(new GameAction(ActionType.EndTurn, seat));
    }

    private static bool IsLegalMain(GameState s, GameAction a, out string reason)
    {
        int seat = a.Seat;
        switch (a.Type)
        {
            case ActionType.BuildRoad:
                if (s.RoadsLeft[seat] == 0)
                    return Fail("You have no roads left.", out reason);
                if (!Costs.Road.FitsIn(s.HandOf(seat)))
                    return Fail("A road costs 1 brick and 1 lumber.", out reason);
                return CanPlaceRoad(s, seat, a.Target, out reason);

            case ActionType.BuildSettlement:
                if (s.SettlementsLeft[seat] == 0)
                    return Fail("You have no settlements left.", out reason);
                if (!Costs.Settlement.FitsIn(s.HandOf(seat)))
                    return Fail("A settlement costs 1 brick, 1 lumber, 1 wool and 1 grain.", out reason);
                return CanPlaceSettlement(s, seat, a.Target, out reason);

            case ActionType.BuildCity:
                if (s.CitiesLeft[seat] == 0)
                    return Fail("You have no cities left.", out reason);
                if (!Costs.City.FitsIn(s.HandOf(seat)))
                    return Fail("A city costs 2 grain and 3 ore.", out reason);
                if (a.Target is < 0 or >= Topology.VertexCount)
                    return Fail($"Vertex {a.Target} doesn't exist.", out reason);
                if (s.VertexOwner[a.Target] != seat || s.VertexLevel[a.Target] != 1)
                    return Fail("A city must replace one of your settlements.", out reason);
                reason = "";
                return true;

            case ActionType.BuyDevCard:
                if (DevDeckSize(s) == 0)
                    return Fail("The development card deck is empty.", out reason);
                if (!Costs.DevCard.FitsIn(s.HandOf(seat)))
                    return Fail("A development card costs 1 wool, 1 grain and 1 ore.", out reason);
                reason = "";
                return true;

            case ActionType.EndTurn:
                reason = "";
                return true;

            case ActionType.RollDice:
                return Fail("You've already rolled this turn.", out reason);

            default:
                return Fail($"{a.Type} isn't implemented yet.", out reason);
        }
    }

    /// <summary>
    /// Empty edge that touches your building, or touches your road at a vertex with no opponent building
    /// (you can't build through an opponent's settlement).
    /// </summary>
    private static bool CanPlaceRoad(GameState s, int seat, int edge, out string reason)
    {
        if (!IsEmptyEdge(s, edge, out reason))
            return false;
        for (int end = 0; end < 2; end++)
        {
            int v = Topology.EdgeVertices[edge, end];
            int owner = s.VertexOwner[v];
            if (owner == seat)
                return true;
            if (owner >= 0)
                continue; // an opponent's building blocks connecting through this vertex
            for (int slot = end * 2; slot < end * 2 + 2; slot++)
            {
                int f = Topology.EdgeNeighbors[edge, slot];
                if (f >= 0 && s.EdgeOwner[f] == seat)
                    return true;
            }
        }
        return Fail("A road must connect to your building or road, and can't pass through an opponent's settlement.", out reason);
    }

    /// <summary>Distance rule, plus (outside setup) it must touch one of your roads.</summary>
    private static bool CanPlaceSettlement(GameState s, int seat, int vertex, out string reason)
    {
        if (!PassesDistanceRule(s, vertex, out reason))
            return false;
        for (int i = 0; i < 3; i++)
        {
            int e = Topology.VertexEdges[vertex, i];
            if (e >= 0 && s.EdgeOwner[e] == seat)
                return true;
        }
        return Fail("A settlement must touch one of your roads.", out reason);
    }

    private static void ApplyBuildRoad(GameState s, GameAction a, List<GameEvent>? events)
    {
        Pay(s, a.Seat, Costs.Road);
        PlaceRoad(s, a.Seat, a.Target, events);
        UpdateLongestRoad(s, events);
    }

    private static void ApplyBuildSettlement(GameState s, GameAction a, List<GameEvent>? events)
    {
        Pay(s, a.Seat, Costs.Settlement);
        PlaceSettlement(s, a.Seat, a.Target, events);
        // A new settlement can cut an opponent's road.
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            s.RoadLength[seat] = LongestRoad.Compute(s, seat);
        UpdateLongestRoad(s, events);
    }

    private static void ApplyBuildCity(GameState s, GameAction a, List<GameEvent>? events)
    {
        Pay(s, a.Seat, Costs.City);
        s.VertexLevel[a.Target] = 2;
        s.SettlementsLeft[a.Seat]++;
        s.CitiesLeft[a.Seat]--;
        s.PublicVP[a.Seat]++;
        events?.Add(new Built(a.Seat, PieceType.City, a.Target));
    }

    private static void ApplyBuyDevCard(GameState s, GameAction a, IChance chance, List<GameEvent>? events)
    {
        Pay(s, a.Seat, Costs.DevCard);
        int type = chance.DrawDevCard(s.DevDeck);
        s.DevDeck[type]--;
        s.DevHand[a.Seat * DevTypes + type]++;
        s.DevBoughtThisTurn[type]++;
        events?.Add(new DevCardBought(a.Seat, (DevCardType)type));
    }

    private static void ApplyEndTurn(GameState s, GameAction a, List<GameEvent>? events)
    {
        events?.Add(new TurnEnded(a.Seat));
        Array.Clear(s.DevBoughtThisTurn);
        s.DevPlayedThisTurn = false;
        s.HasRolled = false;
        s.OffersThisTurn = 0;
        s.CurrentPlayer = (s.CurrentPlayer + 1) % GameConstants.PlayerCount;
        s.TurnNumber++;
        s.Phase = Phase.PreRoll;

        if (s.TurnNumber > s.Settings.MaxTurns)
        {
            s.Phase = Phase.GameOver;
            events?.Add(new GameEnded(-1));
        }
    }

    private static int DevDeckSize(GameState s)
    {
        int total = 0;
        for (int t = 0; t < DevTypes; t++)
            total += s.DevDeck[t];
        return total;
    }

    /// <summary>Moves cards from a seat's hand to the bank.</summary>
    private static void Pay(GameState s, int seat, ResourceSet cost)
    {
        for (int r = 0; r < R; r++)
        {
            s.Hand[seat * R + r] -= cost[r];
            s.Bank[r] += cost[r];
        }
    }
}
