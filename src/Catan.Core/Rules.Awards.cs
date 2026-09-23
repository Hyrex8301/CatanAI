namespace Catan.Core;

public static partial class Rules
{
    public const int LongestRoadMinimum = 5;

    /// <summary>
    /// Reassigns Longest Road from the cached road lengths (call after they change).
    /// The holder keeps it while at least tied for longest with 5+. Otherwise it goes to the single longest seat with 5+,
    /// or is set aside when several tie or nobody has 5. Taking it from a holder needs strictly more.
    /// </summary>
    private static void UpdateLongestRoad(GameState s, List<GameEvent>? events)
    {
        int holder = s.LongestRoadOwner;
        int max = 0;
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            max = Math.Max(max, s.RoadLength[seat]);

        if (holder >= 0 && s.RoadLength[holder] >= LongestRoadMinimum && s.RoadLength[holder] >= max)
            return;

        int next = StateValidator.UniqueMax(s.RoadLength, LongestRoadMinimum);
        if (next == holder)
            return;

        if (holder >= 0)
            s.PublicVP[holder] -= 2;
        if (next >= 0)
            s.PublicVP[next] += 2;
        s.LongestRoadOwner = next;
        events?.Add(new AwardChanged(Award.LongestRoad, holder, next));
    }

    /// <summary>
    /// The current seat wins the moment its total VP (hidden VP cards included) reaches the target. Only the current seat:
    /// a seat that reaches the target on someone else's turn wins at the start of its own next turn.
    /// </summary>
    private static bool TryWin(GameState s, List<GameEvent>? events)
    {
        if (s.Phase == Phase.GameOver || s.TotalVP(s.CurrentPlayer) < s.Settings.VpToWin)
            return false;
        s.Winner = s.CurrentPlayer;
        s.Phase = Phase.GameOver;
        s.FreeRoads = 0; // a win mid-Road-Building (first free road takes Longest Road) forfeits the second road
        events?.Add(new GameEnded(s.Winner));
        return true;
    }

    public const int LargestArmyMinimum = 3;

    /// <summary>
    /// After <paramref name="seat"/> plays a Knight: the first to 3 Knights takes Largest Army; anyone else needs strictly more
    /// than the holder. It is never set aside.
    /// </summary>
    private static void UpdateLargestArmy(GameState s, int seat, List<GameEvent>? events)
    {
        int holder = s.LargestArmyOwner;
        if (seat == holder || s.KnightsPlayed[seat] < LargestArmyMinimum)
            return;
        if (holder >= 0 && s.KnightsPlayed[seat] <= s.KnightsPlayed[holder])
            return;

        if (holder >= 0)
            s.PublicVP[holder] -= 2;
        s.PublicVP[seat] += 2;
        s.LargestArmyOwner = seat;
        events?.Add(new AwardChanged(Award.LargestArmy, holder, seat));
    }
}
