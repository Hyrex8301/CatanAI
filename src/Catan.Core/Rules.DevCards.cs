namespace Catan.Core;

public static partial class Rules
{
    public const int RoadBuildingRoads = 2;

    // ---- Legal lists (PreRoll and Main) ----

    private static void DevPlayActions(GameState s, int seat, List<GameAction> buffer)
    {
        if (s.DevPlayedThisTurn)
            return;

        if (PlayableCount(s, seat, DevCardType.Knight) > 0)
            buffer.Add(new GameAction(ActionType.PlayKnight, seat));

        if (PlayableCount(s, seat, DevCardType.RoadBuilding) > 0 && CanPlaceAnyRoad(s, seat))
            buffer.Add(new GameAction(ActionType.PlayRoadBuilding, seat));

        if (PlayableCount(s, seat, DevCardType.YearOfPlenty) > 0)
            for (int a = 0; a < R; a++)
                for (int b = a; b < R; b++)
                {
                    var get = ResourceSet.Of((Resource)a) + ResourceSet.Of((Resource)b);
                    if (get.FitsIn(s.Bank))
                        buffer.Add(new GameAction(ActionType.PlayYearOfPlenty, seat, Get: get));
                }

        if (PlayableCount(s, seat, DevCardType.Monopoly) > 0)
            for (int r = 0; r < R; r++)
                buffer.Add(new GameAction(ActionType.PlayMonopoly, seat, r));
    }

    // ---- Legality ----

    private static bool IsDevPlay(ActionType type) =>
        type is ActionType.PlayKnight or ActionType.PlayRoadBuilding or ActionType.PlayYearOfPlenty or ActionType.PlayMonopoly;

    private static bool IsLegalDevPlay(GameState s, GameAction a, out string reason)
    {
        var type = a.Type switch
        {
            ActionType.PlayKnight => DevCardType.Knight,
            ActionType.PlayRoadBuilding => DevCardType.RoadBuilding,
            ActionType.PlayYearOfPlenty => DevCardType.YearOfPlenty,
            _ => DevCardType.Monopoly,
        };
        if (s.DevPlayedThisTurn)
            return Fail("You've already played a development card this turn.", out reason);
        if (PlayableCount(s, a.Seat, type) == 0)
            return s.DevHand[a.Seat * DevTypes + (int)type] > 0
                ? Fail($"You can't play a {type} on the turn you bought it.", out reason)
                : Fail($"You don't have a {type} card.", out reason);

        switch (a.Type)
        {
            case ActionType.PlayRoadBuilding:
                if (s.RoadsLeft[a.Seat] == 0)
                    return Fail("You have no roads left to place.", out reason);
                if (!CanPlaceAnyRoad(s, a.Seat))
                    return Fail("There's nowhere you can place a road.", out reason);
                break;

            case ActionType.PlayYearOfPlenty:
                for (int r = 0; r < R; r++)
                    if (a.Get[r] < 0)
                        return Fail("Year of Plenty can't take negative counts.", out reason);
                if (a.Get.Total != 2)
                    return Fail("Year of Plenty takes exactly 2 resource cards.", out reason);
                if (!a.Get.FitsIn(s.Bank))
                    return Fail("The bank doesn't have those cards.", out reason);
                break;

            case ActionType.PlayMonopoly:
                if (a.Target is < 0 or >= R)
                    return Fail("Name a resource for Monopoly.", out reason);
                break;
        }
        return Pass(out reason);
    }

    /// <summary>Cards of this type the seat can play now: its hand minus any the current player bought this turn.</summary>
    private static int PlayableCount(GameState s, int seat, DevCardType type)
    {
        int held = s.DevHand[seat * DevTypes + (int)type];
        return seat == s.CurrentPlayer ? held - s.DevBoughtThisTurn[(int)type] : held;
    }

    private static bool CanPlaceAnyRoad(GameState s, int seat)
    {
        if (s.RoadsLeft[seat] == 0)
            return false;
        for (int e = 0; e < Topology.EdgeCount; e++)
            if (CanPlaceRoad(s, seat, e, out _))
                return true;
        return false;
    }

    // ---- Apply ----

    private static void UseDevCard(GameState s, int seat, DevCardType type, List<GameEvent>? events)
    {
        s.DevHand[seat * DevTypes + (int)type]--;
        s.DevPlayed[(int)type]++;
        s.DevPlayedThisTurn = true;
        events?.Add(new DevCardPlayed(seat, type));
    }

    /// <summary>Knight: move the robber and steal (no discards). Play continues in MoveRobber.</summary>
    private static void ApplyPlayKnight(GameState s, GameAction a, List<GameEvent>? events)
    {
        UseDevCard(s, a.Seat, DevCardType.Knight, events);
        s.KnightsPlayed[a.Seat]++;
        UpdateLargestArmy(s, a.Seat, events);
        s.Phase = Phase.MoveRobber;
    }

    /// <summary>Road Building: up to 2 free roads, fewer if pieces run out.</summary>
    private static void ApplyPlayRoadBuilding(GameState s, GameAction a, List<GameEvent>? events)
    {
        UseDevCard(s, a.Seat, DevCardType.RoadBuilding, events);
        s.FreeRoads = Math.Min(RoadBuildingRoads, s.RoadsLeft[a.Seat]);
        s.Phase = Phase.RoadBuilding;
    }

    private static void ApplyPlayYearOfPlenty(GameState s, GameAction a, List<GameEvent>? events)
    {
        UseDevCard(s, a.Seat, DevCardType.YearOfPlenty, events);
        GiveFromBank(s, a.Seat, a.Get);
        events?.Add(new ResourcesProduced(a.Seat, a.Get));
    }

    /// <summary>Monopoly: every other player hands over all of the named resource.</summary>
    private static void ApplyPlayMonopoly(GameState s, GameAction a, List<GameEvent>? events)
    {
        UseDevCard(s, a.Seat, DevCardType.Monopoly, events);
        int r = a.Target;
        for (int victim = 0; victim < GameConstants.PlayerCount; victim++)
        {
            if (victim == a.Seat)
                continue;
            int count = s.Hand[victim * R + r];
            s.Hand[victim * R + r] = 0;
            s.Hand[a.Seat * R + r] += count;
            events?.Add(new MonopolyTaken(a.Seat, victim, r, count));
        }
    }

    // ---- Road Building phase ----

    private static void RoadBuildingActions(GameState s, int seat, List<GameAction> buffer)
    {
        for (int e = 0; e < Topology.EdgeCount; e++)
            if (CanPlaceRoad(s, seat, e, out _))
                buffer.Add(new GameAction(ActionType.BuildRoad, seat, e));
    }

    private static bool IsLegalRoadBuilding(GameState s, GameAction a, out string reason)
    {
        if (a.Type != ActionType.BuildRoad)
            return Fail("Place your free roads from Road Building first.", out reason);
        return CanPlaceRoad(s, a.Seat, a.Target, out reason);
    }

    private static void ApplyFreeRoad(GameState s, GameAction a, List<GameEvent>? events)
    {
        PlaceRoad(s, a.Seat, a.Target, events);
        UpdateLongestRoad(s, events);
        s.FreeRoads--;
        if (s.FreeRoads == 0 || !CanPlaceAnyRoad(s, a.Seat))
        {
            s.FreeRoads = 0;
            s.Phase = s.HasRolled ? Phase.Main : Phase.PreRoll;
        }
    }
}
