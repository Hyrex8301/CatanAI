using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>One chat line: who said it (-1 = the table itself, e.g. announcing a broken promise), what, and a note under it
/// (what the reader understood).</summary>
public sealed record ChatLine(int Seat, string Text, int Turn, string? Note = null);

/// <summary>
/// Promises riding on the current player's trade offer: once the offer (<see cref="Give"/> / <see cref="Get"/>, from the
/// current player's side; a side with no cards named matches any) is done with <see cref="Partner"/> (-1 = whoever), the
/// current player owes <see cref="CurrentPromises"/> to the partner and the partner owes <see cref="PartnerPromises"/>.
/// </summary>
public sealed record DealTerms(int Current, int Partner, IReadOnlyList<PromiseTerm> CurrentPromises, IReadOnlyList<PromiseTerm> PartnerPromises,
    ResourceSet Give, ResourceSet Get, int? Length);

/// <summary>A deal said in the chat and not yet settled: the reading, when, and whether the other side answered.</summary>
public sealed record ChatProposal(int Id, Reading Reading, int Turn, bool Answered = false);

/// <summary>
/// Everything said at the table in one game: the chat lines, deals offered in the chat, promises riding on trade offers,
/// and the promises made (<see cref="Deals"/>). Shared by every bot (it's all public) and the game screen. Call
/// <see cref="OnAction"/> after each applied move: it attaches announced promises to the matching trade offer, records them
/// when that trade is done, and announces broken ones. Deals are only made while nobody has <see cref="DealsUntilVp"/>
/// points. Safe to share between threads.
/// </summary>
public sealed class TableTalk
{
    /// <summary>No new deals once anyone has this many (public) points.</summary>
    public const int DealsUntilVp = 5;

    private readonly List<ChatLine> _lines = new();
    private readonly List<ChatProposal> _proposals = new();
    private readonly Dictionary<int, DealTerms> _attached = new();
    private DealTerms? _nextOffer;
    private readonly object _lock = new();

    public TableTalk(IReadOnlyList<string> names, DealBook? deals = null)
    {
        Names = names;
        Deals = deals ?? new DealBook();
    }

    /// <summary>Each seat's colour name as players write it ("red").</summary>
    public IReadOnlyList<string> Names { get; }

    public DealBook Deals { get; }

    /// <summary>Raised after a line is added (on the thread that added it).</summary>
    public event Action<ChatLine>? LineAdded;

    public IReadOnlyList<ChatLine> Lines
    {
        get { lock (_lock) return _lines.ToList(); }
    }

    /// <summary>True while deals may still be made: nobody has <see cref="DealsUntilVp"/> public points yet.</summary>
    public static bool DealsOpen(PlayerView view) => view.PublicVP.Max() < DealsUntilVp;

    public void Say(int seat, string text, int turn, string? note = null)
    {
        var line = new ChatLine(seat, text, turn, note);
        lock (_lock)
            _lines.Add(line);
        LineAdded?.Invoke(line);
    }

    /// <summary>
    /// A chat line typed by a person: read it (<see cref="PhraseReader"/>), post it with what it understood, and act on it:
    /// a proposal is opened (and, from the current player, rides on their next matching trade offer); a yes or no answers
    /// the latest open proposal made to them; a yes to a promise-only swap records it at once.
    /// </summary>
    public Reading Hear(int seat, string text, GameState s, int defaultTarget)
    {
        var reading = PhraseReader.Read(text, seat, Names, defaultTarget, s.Board, v => Spots.IsOpen(s, v));
        string? note = reading.Kind == TalkKind.Chatter && reading.Problem is null ? null : reading.Describe(Names, seat, s.Board);
        Say(seat, text, s.TurnNumber, note);
        switch (reading.Kind)
        {
            case TalkKind.Proposal:
                Propose(reading, s.TurnNumber, s.CurrentPlayer);
                break;
            case TalkKind.Accept or TalkKind.Refuse:
                Answer(seat, reading.Kind == TalkKind.Accept, s.TurnNumber, s.CurrentPlayer);
                break;
        }
        return reading;
    }

    /// <summary>Opens a proposal. From the current player, its promises ride on their next trade offer with the same cards.</summary>
    public ChatProposal Propose(Reading reading, int turn, int currentPlayer)
    {
        ChatProposal p;
        lock (_lock)
        {
            p = new ChatProposal(_proposals.Count, reading, turn);
            _proposals.Add(p);
        }
        if (reading.Speaker == currentPlayer && (reading.SpeakerGives.Total > 0 || reading.SpeakerGets.Total > 0))
            AttachToNextOffer(TermsFor(reading, currentPlayer));
        return p;
    }

    /// <summary>Proposals made this turn to <paramref name="seat"/> (or to anyone) that nobody has answered yet.</summary>
    public List<ChatProposal> OpenProposalsFor(int seat, int turn)
    {
        lock (_lock)
            return _proposals.Where(p => !p.Answered && p.Turn == turn && p.Reading.Speaker != seat
                                         && (p.Reading.Target == seat || p.Reading.Target < 0)).ToList();
    }

    /// <summary>
    /// <paramref name="seat"/> says yes or no to the latest open proposal made to them. A yes to a promise-only proposal
    /// records the promises now; a yes from the current player to one with cards rides on their next matching offer.
    /// </summary>
    public ChatProposal? Answer(int seat, bool yes, int turn, int currentPlayer)
    {
        ChatProposal? p;
        lock (_lock)
        {
            p = _proposals.LastOrDefault(q => !q.Answered && q.Turn == turn && q.Reading.Speaker != seat
                                              && (q.Reading.Target == seat || q.Reading.Target < 0));
            if (p is null)
                return null;
            _proposals[p.Id] = p = p with { Answered = true };
        }
        var r = p.Reading;
        if (!yes)
            return p;
        if (r.SpeakerGives.Total == 0 && r.SpeakerGets.Total == 0)
            Record(r.Speaker, seat, r.SpeakerPromises, r.TargetPromises, r.Length, turn);
        else if (seat == currentPlayer)
            AttachToNextOffer(TermsFor(r, seat) with { Partner = r.Speaker });
        return p;
    }

    /// <summary>The current player's next offer with these cards carries <paramref name="terms"/>.</summary>
    public void AttachToNextOffer(DealTerms terms)
    {
        lock (_lock)
            _nextOffer = terms;
    }

    /// <summary>The promises riding on the offer in <paramref name="slot"/>, if any.</summary>
    public DealTerms? DealOn(int slot)
    {
        lock (_lock)
            return _attached.TryGetValue(slot, out var d) ? d : null;
    }

    /// <summary>
    /// After a move is applied: attaches waiting promises to a new matching offer, drops them from edited or cancelled
    /// offers, records them when the trade is confirmed with the right partner, and records and announces broken promises.
    /// </summary>
    public List<BrokenPromise> OnAction(GameState s, GameAction action)
    {
        DealTerms? done = null;
        lock (_lock)
            switch (action.Type)
            {
                case ActionType.OfferTrade:
                    if (_nextOffer is { } next && next.Current == action.Seat && Matches(next, action))
                    {
                        int slot = FindOffer(s, action);
                        if (slot >= 0)
                            _attached[slot] = next with { Give = action.Give, Get = action.Get };
                        _nextOffer = null;
                    }
                    break;
                case ActionType.EditOffer or ActionType.CancelOffer:
                    _attached.Remove(action.Target);
                    break;
                case ActionType.ConfirmTrade:
                    if (_attached.TryGetValue(action.Target, out var d) && (d.Partner < 0 || d.Partner == action.Target2))
                    {
                        done = d;
                        _attached.Remove(action.Target);
                    }
                    break;
                case ActionType.EndTurn:
                    _attached.Clear();
                    _nextOffer = null;
                    break;
            }
        if (done is not null)
            Record(done.Current, action.Target2, done.CurrentPromises, done.PartnerPromises, done.Length, s.TurnNumber);

        var broken = Deals.Record(s, action);
        foreach (var b in broken)
        {
            string what = b.Promise.Term.Kind switch
            {
                PromiseKind.NoBlock => "not to block them",
                PromiseKind.NoSteal => "not to steal from them",
                _ => $"not to take the {Spots.Name(s.Board, b.Promise.Term.Vertex)} spot",
            };
            Say(-1, $"{Capital(Names[b.Promise.From])} broke their promise to {Names[b.Promise.To]} ({what}).", s.TurnNumber);
        }
        return broken;
    }

    private void Record(int a, int b, IReadOnlyList<PromiseTerm> aPromises, IReadOnlyList<PromiseTerm> bPromises, int? length, int turn)
    {
        foreach (var t in aPromises)
            Deals.Add(a, b, t, turn, length);
        foreach (var t in bPromises)
            Deals.Add(b, a, t, turn, length);
        var parts = new[] { Promised(a, b, aPromises), Promised(b, a, bPromises) }.Where(p => p.Length > 0);
        if (aPromises.Count + bPromises.Count > 0)
            Say(-1, $"Deal: {string.Join("; ", parts)}.", turn);
    }

    private string Promised(int from, int to, IReadOnlyList<PromiseTerm> terms) =>
        terms.Count == 0 ? "" : $"{Names[from]} won't {string.Join(" or ", terms.Select(t => t.Kind switch { PromiseKind.NoBlock => "block", PromiseKind.NoSteal => "steal from", _ => "settle near" }).Distinct())} {Names[to]}";

    /// <summary>A proposal's promises from the current player's side (who promises what once their trade is done).</summary>
    private static DealTerms TermsFor(Reading r, int current) =>
        r.Speaker == current
            ? new DealTerms(current, r.Target, r.SpeakerPromises, r.TargetPromises, r.SpeakerGives, r.SpeakerGets, r.Length)
            : new DealTerms(current, r.Speaker, r.TargetPromises, r.SpeakerPromises, r.SpeakerGets, r.SpeakerGives, r.Length);

    /// <summary>An offer carries the terms if it has the cards named (a side nobody named can be any card).</summary>
    private static bool Matches(DealTerms terms, GameAction offer) =>
        (terms.Give.Total == 0 || terms.Give == offer.Give) && (terms.Get.Total == 0 || terms.Get == offer.Get);

    private static int FindOffer(GameState s, GameAction offer)
    {
        for (int slot = 0; slot < GameConstants.MaxOpenOffers; slot++)
        {
            var o = s.Offers[slot];
            if (o.IsActive && !o.IsCounter && o.From == offer.Seat && o.Give == offer.Give && o.Get == offer.Get && o.Responses == 0)
                return slot;
        }
        return -1;
    }

    /// <summary>The chat and the promises, for saving with the game.</summary>
    public string ToJson()
    {
        List<ChatLine> lines;
        lock (_lock)
            lines = _lines.ToList();
        return System.Text.Json.JsonSerializer.Serialize(new Saved(lines, Deals.ToJson()));
    }

    /// <summary>Table talk saved by <see cref="ToJson"/>; proposals and offers in flight aren't kept (they end with the turn).</summary>
    public static TableTalk FromJson(IReadOnlyList<string> names, string json)
    {
        var saved = System.Text.Json.JsonSerializer.Deserialize<Saved>(json) ?? throw new ArgumentException("Table talk JSON is empty.");
        var talk = new TableTalk(names, DealBook.FromJson(saved.Deals));
        talk._lines.AddRange(saved.Lines);
        return talk;
    }

    private sealed record Saved(List<ChatLine> Lines, string Deals);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
