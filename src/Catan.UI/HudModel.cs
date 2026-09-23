using Catan.Core;

namespace Catan.UI;

/// <summary>One stack in your hand bar: a resource or a dev card type, with how many you hold.</summary>
/// <param name="Fresh">Dev cards of this type bought this turn (can't be played until next turn).</param>
public sealed record CardStack(bool IsDev, int Type, int Count, int Fresh = 0)
{
    public string Name => IsDev ? GameText.DevCard((DevCardType)Type) : GameText.ResourceTitle(Type);
}

/// <summary>What one player card on the HUD shows, built only from the viewer's <see cref="PlayerView"/>.</summary>
/// <param name="Vp">Victory points as the viewer may see them: your total (with hidden VP cards), others' public VP.</param>
/// <param name="HiddenVp">Your own VP cards not yet shown to others (always 0 for other seats).</param>
public sealed record SeatSummary(
    int Seat, SeatColor Color, string Name, bool IsYou, bool IsActing, bool IsCurrent,
    int Vp, int HiddenVp, int Cards, int DevCards, int Knights, int RoadLength,
    bool LongestRoad, bool LargestArmy, int RoadsLeft, int SettlementsLeft, int CitiesLeft);

/// <summary>Turns a <see cref="PlayerView"/> into what the HUD draws: your hand's card stacks and the four player cards.</summary>
public static class HudModel
{
    /// <summary>Resources you hold in resource order, then dev cards in dev card order. Empty stacks are left out.</summary>
    public static IReadOnlyList<CardStack> Hand(PlayerView v) => Hand(v, default);

    /// <summary>Your hand with some cards set aside (picked for a discard), which leave the hand bar while picked.</summary>
    public static IReadOnlyList<CardStack> Hand(PlayerView v, ResourceSet setAside)
    {
        var stacks = new List<CardStack>();
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (v.Hand[r] - setAside[r] > 0)
                stacks.Add(new CardStack(false, r, v.Hand[r] - setAside[r]));
        for (int t = 0; t < GameConstants.DevCardTypeCount; t++)
            if (v.DevHand[t] > 0)
                stacks.Add(new CardStack(true, t, v.DevHand[t], v.DevBoughtThisTurn[t]));
        return stacks;
    }

    public static SeatSummary Seat(PlayerView v, int seat, IReadOnlyList<SeatColor> colors)
    {
        bool you = seat == v.Seat;
        int vp = you ? v.TotalVP : v.PublicVP[seat];
        return new SeatSummary(
            seat, colors[seat], you ? "You" : colors[seat].ToString(), you,
            IsActing: seat == v.ActingSeat, IsCurrent: seat == v.CurrentPlayer,
            Vp: vp, HiddenVp: you ? v.TotalVP - v.PublicVP[seat] : 0,
            Cards: v.HandSizes[seat], DevCards: v.DevCardCounts[seat], Knights: v.KnightsPlayed[seat], RoadLength: v.RoadLength[seat],
            LongestRoad: v.LongestRoadOwner == seat, LargestArmy: v.LargestArmyOwner == seat,
            RoadsLeft: v.RoadsLeft[seat], SettlementsLeft: v.SettlementsLeft[seat], CitiesLeft: v.CitiesLeft[seat]);
    }

    /// <summary>The other seats in turn order starting after you (the order they act after your turn).</summary>
    public static IEnumerable<int> Opponents(int viewer)
    {
        for (int i = 1; i < GameConstants.PlayerCount; i++)
            yield return (viewer + i) % GameConstants.PlayerCount;
    }
}
