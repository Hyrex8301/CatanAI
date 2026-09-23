using Catan.Core;

namespace Catan.UI;

/// <summary>
/// Playing a development card from your hand: whether it can be played now (and why not), how many resources it asks you
/// to pick (Year of Plenty 2, Monopoly 1), and the engine action a pick turns into. Works from your view and legal list.
/// </summary>
public static class DevCardModel
{
    /// <summary>What each card does, for the play popup.</summary>
    public static string Description(DevCardType type) => type switch
    {
        DevCardType.Knight => "Move the robber and steal a card from a player next to it. Counts toward Largest Army.",
        DevCardType.RoadBuilding => "Place 2 roads for free.",
        DevCardType.YearOfPlenty => "Take any 2 resource cards from the bank.",
        DevCardType.Monopoly => "Name a resource: every other player gives you all of theirs.",
        _ => "Worth 1 victory point. It stays hidden and counts toward your score automatically.",
    };

    /// <summary>Resources the card asks you to pick before it can be played.</summary>
    public static int Picks(DevCardType type) => type switch
    {
        DevCardType.YearOfPlenty => 2,
        DevCardType.Monopoly => 1,
        _ => 0,
    };

    /// <summary>The play action for a card and its picks, or null when it isn't playable or the picks aren't complete.</summary>
    public static GameAction? Action(DevCardType type, int seat, ResourceSet picks) => type switch
    {
        DevCardType.Knight => new GameAction(ActionType.PlayKnight, seat),
        DevCardType.RoadBuilding => new GameAction(ActionType.PlayRoadBuilding, seat),
        DevCardType.YearOfPlenty when picks.Total == 2 => new GameAction(ActionType.PlayYearOfPlenty, seat, Get: picks),
        DevCardType.Monopoly when picks.Total == 1 => new GameAction(ActionType.PlayMonopoly, seat, FirstResource(picks)),
        _ => null,
    };

    /// <summary>Whether the card can be played now (the engine lists a play of it), and if not, why.</summary>
    public static (bool Playable, string Why) State(PlayerView v, IReadOnlyList<GameAction>? legal, DevCardType type)
    {
        var play = PlayType(type);
        if (play is { } t && legal is not null)
            foreach (var a in legal)
                if (a.Type == t)
                    return (true, "");
        if (type == DevCardType.VictoryPoint)
            return (false, "Victory Point cards aren't played: they count toward your score automatically.");
        int t2 = (int)type;
        if (v.DevHand[t2] == 0)
            return (false, $"You don't have a {GameText.DevCard(type)} card.");
        if (legal is null || v.CurrentPlayer != v.Seat)
            return (false, "You can only play development cards on your own turn.");
        if (v.Phase is not (Phase.PreRoll or Phase.Main))
            return (false, "Not now: finish the current step first.");
        if (v.DevPlayedThisTurn)
            return (false, "You've already played a development card this turn.");
        if (v.DevHand[t2] - v.DevBoughtThisTurn[t2] <= 0)
            return (false, "You can't play a card on the turn you bought it.");
        if (type == DevCardType.RoadBuilding)
            return (false, v.RoadsLeft[v.Seat] == 0 ? "You have no roads left to place." : "There's nowhere you can place a road.");
        return (false, "You can't play this card right now.");
    }

    private static ActionType? PlayType(DevCardType type) => type switch
    {
        DevCardType.Knight => ActionType.PlayKnight,
        DevCardType.RoadBuilding => ActionType.PlayRoadBuilding,
        DevCardType.YearOfPlenty => ActionType.PlayYearOfPlenty,
        DevCardType.Monopoly => ActionType.PlayMonopoly,
        _ => null,
    };

    private static int FirstResource(ResourceSet picks)
    {
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (picks[r] > 0)
                return r;
        return -1;
    }
}
