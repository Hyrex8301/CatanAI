using Catan.Core;

namespace Catan.UI;

/// <summary>The buttons on the action bar, left to right (the dice sit between City and End Turn).</summary>
public enum BarItem { Trade, BuyDev, Road, Settlement, City, Roll, EndTurn }

/// <summary>What a board click means during your Main phase. Other phases (setup, robber, Road Building) pick their own targets.</summary>
public enum BuildMode { None, Road, Settlement, City }

/// <summary>One action bar button: whether it can be used now, and a tooltip (name, cost, pieces left, and why not).</summary>
public sealed record BarState(BarItem Item, bool Enabled, string Tooltip, int? Left = null);

/// <summary>A roll to show on the dice: who rolled and what.</summary>
public sealed record RollShown(int Seat, int D1, int D2)
{
    public int Total => D1 + D2;
}

/// <summary>
/// The action bar and build modes, from your view and the legal list (null when the game isn't waiting on you). A button is
/// enabled exactly when the engine lists a matching action (Trade: when it's your Main phase); otherwise the tooltip says why.
/// </summary>
public static class ActionBarModel
{
    public static BarState State(BarItem item, PlayerView v, IReadOnlyList<GameAction>? legal)
    {
        int you = v.Seat;
        switch (item)
        {
            case BarItem.Trade:
            {
                bool ok = legal is not null && v.Phase == Phase.Main && v.CurrentPlayer == you;
                return new BarState(item, ok, Join("Trade with players or the bank", ok ? null : Blocked(v, legal) ?? "Nothing to trade right now"));
            }
            case BarItem.BuyDev:
            {
                bool ok = Has(legal, ActionType.BuyDevCard);
                string? why = ok ? null : Blocked(v, legal) ?? (v.DevDeckSize == 0 ? "No development cards left" : Missing(Costs.DevCard, v));
                return new BarState(item, ok, Join($"Development card: {GameText.Cards(Costs.DevCard)} ({v.DevDeckSize} left in the deck)", why), v.DevDeckSize);
            }
            case BarItem.Road:
                return Build(item, v, legal, ActionType.BuildRoad, "Road", Costs.Road, v.RoadsLeft[you], "roads");
            case BarItem.Settlement:
                return Build(item, v, legal, ActionType.BuildSettlement, "Settlement", Costs.Settlement, v.SettlementsLeft[you], "settlements");
            case BarItem.City:
                return Build(item, v, legal, ActionType.BuildCity, "City", Costs.City, v.CitiesLeft[you], "cities");
            case BarItem.Roll:
            {
                bool ok = Has(legal, ActionType.RollDice);
                return new BarState(item, ok, ok ? "Click to roll (Space)" : Roll(v) is { } r ? $"Last roll: {r.Total}" : "Nobody has rolled yet");
            }
            default:
            {
                bool ok = Has(legal, ActionType.EndTurn);
                return new BarState(item, ok, Join("End your turn (Space)", ok ? null : Blocked(v, legal) ?? "You can't end your turn yet"));
            }
        }
    }

    /// <summary>
    /// The board spots to highlight. In setup, robber and Road Building phases: every legal board action. In your Main phase:
    /// only the chosen build mode's (none until you pick Road, Settlement or City).
    /// </summary>
    public static IEnumerable<GameAction> BoardActions(BuildMode mode, PlayerView v, IReadOnlyList<GameAction> legal)
    {
        var wanted = v.Phase == Phase.Main
            ? mode switch
            {
                BuildMode.Road => ActionType.BuildRoad,
                BuildMode.Settlement => ActionType.BuildSettlement,
                BuildMode.City => ActionType.BuildCity,
                _ => (ActionType?)null,
            }
            : null;
        foreach (var a in legal)
        {
            bool board = a.Type is ActionType.BuildRoad or ActionType.BuildSettlement or ActionType.BuildCity or ActionType.MoveRobber;
            if (board && (v.Phase != Phase.Main || a.Type == wanted))
                yield return a;
        }
    }

    /// <summary>The build mode a bar button selects, if any.</summary>
    public static BuildMode ModeOf(BarItem item) => item switch
    {
        BarItem.Road => BuildMode.Road,
        BarItem.Settlement => BuildMode.Settlement,
        BarItem.City => BuildMode.City,
        _ => BuildMode.None,
    };

    /// <summary>The most recent roll in your event log, or null before the first roll.</summary>
    public static RollShown? Roll(PlayerView v)
    {
        for (int i = v.Events.Count - 1; i >= 0; i--)
            if (v.Events[i] is DiceRolled d)
                return new RollShown(d.Seat, d.D1, d.D2);
        return null;
    }

    private static BarState Build(BarItem item, PlayerView v, IReadOnlyList<GameAction>? legal, ActionType type, string name, ResourceSet cost, int left, string plural)
    {
        bool ok = v.Phase == Phase.Main && Has(legal, type);
        string? why = null;
        if (!ok)
            why = Blocked(v, legal)
                  ?? (left == 0 ? $"No {plural} left" : null)
                  ?? Missing(cost, v)
                  ?? (type == ActionType.BuildCity ? "No settlement you can upgrade" : "No place to build one");
        return new BarState(item, ok, Join($"{name}: {GameText.Cards(cost)} ({left} left)", why), left);
    }

    /// <summary>Why nothing on the bar works right now (not your move, before the roll, a discard, ...), or null in your Main phase.</summary>
    private static string? Blocked(PlayerView v, IReadOnlyList<GameAction>? legal)
    {
        if (legal is null || v.CurrentPlayer != v.Seat && v.Phase != Phase.Discard)
            return v.Phase == Phase.GameOver ? "The game is over" : "Not your turn";
        return v.Phase switch
        {
            Phase.Main => null,
            Phase.PreRoll => "Roll the dice first",
            Phase.SetupSettlement or Phase.SetupRoad => "Place your starting pieces first",
            Phase.Discard => "Discard first",
            Phase.MoveRobber => "Move the robber first",
            Phase.RoadBuilding => "Place your free roads first",
            _ => "Not now",
        };
    }

    /// <summary>"You need 1 more brick and 2 more ore", or null if the hand covers the cost.</summary>
    private static string? Missing(ResourceSet cost, PlayerView v)
    {
        var parts = new List<string>();
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (cost[r] > v.Hand[r])
                parts.Add($"{cost[r] - v.Hand[r]} more {GameText.Resource(r)}");
        return parts.Count == 0 ? null : "You need " + string.Join(" and ", parts);
    }

    private static bool Has(IReadOnlyList<GameAction>? legal, ActionType type)
    {
        if (legal is null)
            return false;
        foreach (var a in legal)
            if (a.Type == type)
                return true;
        return false;
    }

    private static string Join(string title, string? why) => why is null ? title : $"{title}\n{why}";
}
