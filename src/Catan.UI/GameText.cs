using Catan.Core;

namespace Catan.UI;

/// <summary>
/// Plain-English text for events (the log) and actions (buttons and tooltips), from one seat's point of view.
/// The human seat is "You"; other seats are named by color. Works on redacted events: hidden details stay hidden.
/// </summary>
public sealed class GameText
{
    private static readonly string[] ResourceNames = { "brick", "lumber", "wool", "grain", "ore" };
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly int _viewer;

    public GameText(IReadOnlyList<SeatColor> colors, int viewer)
    {
        _colors = colors;
        _viewer = viewer;
    }

    public string Seat(int seat) => seat == _viewer ? "You" : seat >= 0 && seat < _colors.Count ? _colors[seat].ToString() : "nobody";

    public static string Resource(int r) => r is >= 0 and < 5 ? ResourceNames[r] : "a card";

    public static string Cards(ResourceSet cards)
    {
        var parts = new List<string>();
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (cards[r] != 0)
                parts.Add($"{cards[r]} {ResourceNames[r]}");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    public static string DevCard(DevCardType type) => type switch
    {
        DevCardType.VictoryPoint => "Victory Point",
        DevCardType.RoadBuilding => "Road Building",
        DevCardType.YearOfPlenty => "Year of Plenty",
        _ => type.ToString(),
    };

    public string Describe(GameEvent e) => e switch
    {
        DiceRolled d => $"{Seat(d.Seat)} rolled {d.Total} ({d.D1} + {d.D2})",
        ResourcesProduced p => $"{Seat(p.Seat)} got {Cards(p.Gained)}",
        Built b => $"{Seat(b.Seat)} built a {b.Piece.ToString().ToLowerInvariant()}",
        DevCardBought d => $"{Seat(d.Seat)} bought a development card" + (d.Type is { } t ? $" ({DevCard(t)})" : ""),
        DevCardPlayed d => $"{Seat(d.Seat)} played {DevCard(d.Type)}",
        MonopolyTaken m => $"{Seat(m.Seat)} took {m.Count} {Resource(m.Resource)} from {Seat(m.Victim)}",
        Discarded d => $"{Seat(d.Seat)} discarded {Cards(d.Cards)}",
        RobberMoved r => $"{Seat(r.Seat)} moved the robber",
        CardStolen c => $"{Seat(c.Thief)} stole {(c.Resource >= 0 ? "1 " + Resource(c.Resource) : "a card")} from {Seat(c.Victim)}",
        BankTraded b => $"{Seat(b.Seat)} traded {Cards(b.Gave)} with the bank for {Cards(b.Got)}",
        TradeOffered o => $"{Seat(o.Seat)} offered {Cards(o.Give)} for {Cards(o.Get)}",
        TradeEdited o => $"{Seat(o.Seat)} changed an offer to {Cards(o.Give)} for {Cards(o.Get)}",
        TradeCountered c => $"{Seat(c.Seat)} countered: {Cards(c.Give)} for {Cards(c.Get)}",
        TradeReplied r => $"{Seat(r.Seat)} {(r.Accepted ? "accepted" : "declined")} an offer",
        TradeRejected r => $"{Seat(r.Seat)} turned down {Seat(r.Partner)}",
        TradeDone t => $"{Seat(t.Seat)} traded {Cards(t.Gave)} to {Seat(t.Partner)} for {Cards(t.Got)}",
        TradeCancelled c => $"{Seat(c.Seat)} withdrew an offer",
        AwardChanged a => $"{(a.Award == Award.LongestRoad ? "Longest Road" : "Largest Army")}: " +
                          (a.To >= 0 ? $"{Seat(a.To)} now {(a.To == _viewer ? "hold" : "holds")} it" : "set aside"),
        TurnEnded t => $"{Seat(t.Seat)} ended the turn",
        GameEnded g => g.Winner < 0 ? "The game ended in a draw" : $"{Seat(g.Winner)} won the game!",
        _ => e.ToString(),
    };

    /// <summary>Button text for an action.</summary>
    public string Describe(GameAction a) => a.Type switch
    {
        ActionType.BuildRoad => "Build road",
        ActionType.BuildSettlement => "Build settlement",
        ActionType.BuildCity => "Build city",
        ActionType.RollDice => "Roll dice",
        ActionType.Discard => $"Discard {Cards(a.Give)}",
        ActionType.MoveRobber => a.Target2 >= 0 ? $"Rob {Seat(a.Target2)}" : "Move robber (no one to rob)",
        ActionType.BuyDevCard => "Buy development card",
        ActionType.PlayKnight => "Play Knight",
        ActionType.PlayRoadBuilding => "Play Road Building",
        ActionType.PlayYearOfPlenty => $"Year of Plenty: {Cards(a.Get)}",
        ActionType.PlayMonopoly => $"Monopoly: {Resource(a.Target)}",
        ActionType.BankTrade => $"Bank: {Cards(a.Give)} → {Cards(a.Get)}",
        ActionType.OfferTrade => $"Offer {Cards(a.Give)} for {Cards(a.Get)}",
        ActionType.EditOffer => $"Change offer to {Cards(a.Give)} for {Cards(a.Get)}",
        ActionType.CounterOffer => $"Counter: {Cards(a.Give)} for {Cards(a.Get)}",
        ActionType.AcceptOffer => a.Seat == _viewer ? "Accept" : $"{Seat(a.Seat)} accepts",
        ActionType.DeclineOffer => a.Target2 >= 0 ? $"Turn down {Seat(a.Target2)}" : "Decline",
        ActionType.ConfirmTrade => $"Trade with {Seat(a.Target2)}",
        ActionType.CancelOffer => "Withdraw offer",
        ActionType.EndTurn => "End turn",
        _ => a.Type.ToString(),
    };
}
