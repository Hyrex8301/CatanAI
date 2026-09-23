using Catan.Core;

namespace Catan.UI;

/// <summary>What one piece of a log line shows.</summary>
public enum LogPartKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>A player's name in their color (<see cref="LogPart.Value"/> = seat).</summary>
    Name,

    /// <summary>A resource card (<see cref="LogPart.Value"/> = resource).</summary>
    Card,

    /// <summary>A face-down resource card (a hidden steal).</summary>
    CardBack,

    /// <summary>A development card face (<see cref="LogPart.Value"/> = type).</summary>
    DevCard,

    /// <summary>A face-down development card (someone else's purchase).</summary>
    DevBack,

    /// <summary>A die face (<see cref="LogPart.Value"/> = 1-6).</summary>
    Die,

    /// <summary>A board piece in the acting player's color (<see cref="LogPart.Value"/> = PieceType).</summary>
    Piece,

    /// <summary>The robber.</summary>
    Robber,
}

public readonly record struct LogPart(LogPartKind Kind, int Value = 0, string Text = "")
{
    public static LogPart Of(string text) => new(LogPartKind.Text, 0, text);
}

/// <summary>One log line: who it's about (their icon leads the line; -1 for none) and its parts.</summary>
/// <param name="Divider">A turn ended: draw a divider instead of a line.</param>
public sealed record LogLine(int Seat, IReadOnlyList<LogPart> Parts, bool Divider = false)
{
    /// <summary>The line as plain text (tooltips, tests).</summary>
    public string PlainText(Func<int, string> name) => string.Concat(Parts.Select(p => p.Kind switch
    {
        LogPartKind.Text => p.Text,
        LogPartKind.Name => name(p.Value),
        LogPartKind.Card => $"[{GameText.Resource(p.Value)}]",
        LogPartKind.CardBack => "[card]",
        LogPartKind.DevCard => $"[{GameText.DevCard((DevCardType)p.Value)}]",
        LogPartKind.DevBack => "[dev card]",
        LogPartKind.Die => $"[{p.Value}]",
        LogPartKind.Piece => $"[{((PieceType)p.Value).ToString().ToLowerInvariant()}]",
        _ => "[robber]",
    }));
}

/// <summary>
/// Colonist-style log lines from redacted events: colored names with card, dice and piece icons in place of words
/// ("Red rolled [3][5]", "You got [wool][wool]"). Works on the viewer's own redacted log, so hidden details stay hidden.
/// </summary>
public static class LogText
{
    public static LogLine Line(GameEvent e) => e switch
    {
        DiceRolled d => new(d.Seat, new[] { Name(d.Seat), LogPart.Of(" rolled "), Die(d.D1), Die(d.D2) }),
        ResourcesProduced p => new(p.Seat, Join(Name(p.Seat), " got ", Cards(p.Gained))),
        Built b => new(b.Seat, new[] { Name(b.Seat), LogPart.Of($" built a {b.Piece.ToString().ToLowerInvariant()} "), new LogPart(LogPartKind.Piece, (int)b.Piece) }),
        DevCardBought d => new(d.Seat, new[] { Name(d.Seat), LogPart.Of(" bought "), d.Type is { } t ? new LogPart(LogPartKind.DevCard, (int)t) : new LogPart(LogPartKind.DevBack) }),
        DevCardPlayed d => new(d.Seat, new[] { Name(d.Seat), LogPart.Of(" played "), new LogPart(LogPartKind.DevCard, (int)d.Type), LogPart.Of(" " + GameText.DevCard(d.Type)) }),
        MonopolyTaken m => new(m.Seat, Join(Name(m.Seat), " took ", Cards(ResourceSet.Of((Resource)m.Resource, m.Count)), " from ", Name(m.Victim))),
        Discarded d => new(d.Seat, Join(Name(d.Seat), " discarded ", Cards(d.Cards))),
        RobberMoved r => new(r.Seat, new[] { Name(r.Seat), LogPart.Of(" moved the robber "), new LogPart(LogPartKind.Robber) }),
        CardStolen c => new(c.Thief, new[]
        {
            Name(c.Thief), LogPart.Of(" stole "), c.Resource >= 0 ? new LogPart(LogPartKind.Card, c.Resource) : new LogPart(LogPartKind.CardBack),
            LogPart.Of(" from "), Name(c.Victim),
        }),
        BankTraded b => new(b.Seat, Join(Name(b.Seat), " gave the bank ", Cards(b.Gave), " and got ", Cards(b.Got))),
        TradeOffered o => new(o.Seat, Join(Name(o.Seat), " offered ", Cards(o.Give), " for ", Cards(o.Get))),
        TradeEdited o => new(o.Seat, Join(Name(o.Seat), " changed an offer to ", Cards(o.Give), " for ", Cards(o.Get))),
        TradeCountered c => new(c.Seat, Join(Name(c.Seat), " countered: gives ", Cards(c.Give), " for ", Cards(c.Get))),
        TradeReplied r => new(r.Seat, new[] { Name(r.Seat), LogPart.Of(r.Accepted ? " accepted the offer" : " declined the offer") }),
        TradeRejected r => new(r.Seat, new[] { Name(r.Seat), LogPart.Of(" turned down "), Name(r.Partner) }),
        TradeDone t => new(t.Seat, Join(Name(t.Seat), " gave ", Cards(t.Gave), " and got ", Cards(t.Got), " from ", Name(t.Partner))),
        TradeCancelled c => new(c.Seat, new[] { Name(c.Seat), LogPart.Of(" withdrew an offer") }),
        AwardChanged a => a.To >= 0
            ? new(a.To, new[] { Name(a.To), LogPart.Of(a.Award == Award.LongestRoad ? " took Longest Road" : " took Largest Army") })
            : new(-1, new[] { LogPart.Of(a.Award == Award.LongestRoad ? "Longest Road is set aside" : "Largest Army is set aside") }),
        TurnEnded t => new(t.Seat, Array.Empty<LogPart>(), Divider: true),
        GameEnded g => g.Winner >= 0 ? new(g.Winner, new[] { Name(g.Winner), LogPart.Of(" won the game!") }) : new(-1, new[] { LogPart.Of("The game ended in a draw") }),
        _ => new(-1, new[] { LogPart.Of(e.ToString()) }),
    };

    private static LogPart Name(int seat) => new(LogPartKind.Name, seat);

    private static LogPart Die(int face) => new(LogPartKind.Die, face);

    /// <summary>One card icon per card, resources in order ("nothing" for an empty set).</summary>
    private static IEnumerable<LogPart> Cards(ResourceSet cards)
    {
        if (cards.Total == 0)
            yield return LogPart.Of("nothing");
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            for (int k = 0; k < cards[r]; k++)
                yield return new LogPart(LogPartKind.Card, r);
    }

    private static IReadOnlyList<LogPart> Join(params object[] pieces)
    {
        var parts = new List<LogPart>();
        foreach (var piece in pieces)
            switch (piece)
            {
                case LogPart p: parts.Add(p); break;
                case string s: parts.Add(LogPart.Of(s)); break;
                case IEnumerable<LogPart> many: parts.AddRange(many); break;
            }
        return parts;
    }
}
