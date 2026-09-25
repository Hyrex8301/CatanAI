namespace Catan.Core;

/// <summary>
/// One decision by one seat. Target: vertex, edge, hex, resource, partner seat, or seat bitmask. Target2: robber victim or -1.
/// Record struct, so actions compare by value.
/// </summary>
public readonly record struct GameAction(ActionType Type, int Seat, int Target = -1, int Target2 = -1,
                                         ResourceSet Give = default, ResourceSet Get = default);

public enum PieceType : byte { Road, Settlement, City }

/// <summary>
/// Everything that happens in a game is reported as events. Almost all are public; <see cref="RedactFor"/> hides the two
/// details a seat may not see: the resource in someone else's <see cref="CardStolen"/> and the type in someone else's
/// <see cref="DevCardBought"/>.
/// </summary>
public abstract record GameEvent
{
    /// <summary>This event as <paramref name="viewer"/> may see it. Public events return themselves (no allocation).</summary>
    public virtual GameEvent RedactFor(int viewer) => this;
}

public sealed record Built(int Seat, PieceType Piece, int Target) : GameEvent;

public sealed record DiceRolled(int Seat, int D1, int D2) : GameEvent
{
    public int Total => D1 + D2;
}

/// <summary>Cards a seat received from the bank (setup payout or a roll).</summary>
public sealed record ResourcesProduced(int Seat, ResourceSet Gained) : GameEvent;

/// <summary>Discards go face up.</summary>
public sealed record Discarded(int Seat, ResourceSet Cards) : GameEvent;

public sealed record RobberMoved(int Seat, int Hex) : GameEvent;

/// <summary>
/// Resource is a resource index, or -1 for seats other than thief and victim: the only resource information the game hides,
/// so the M3 hand tracker is built around it.
/// </summary>
public sealed record CardStolen(int Thief, int Victim, int Resource) : GameEvent
{
    public override GameEvent RedactFor(int viewer) =>
        viewer == Thief || viewer == Victim || Resource < 0 ? this : this with { Resource = -1 };
}

/// <summary>Type is null for seats other than the buyer.</summary>
public sealed record DevCardBought(int Seat, DevCardType? Type) : GameEvent
{
    public override GameEvent RedactFor(int viewer) => viewer == Seat || Type is null ? this : this with { Type = null };
}

public sealed record DevCardPlayed(int Seat, DevCardType Type) : GameEvent;

/// <summary>One per opponent when Monopoly is played, including opponents who had none.</summary>
public sealed record MonopolyTaken(int Seat, int Victim, int Resource, int Count) : GameEvent;

public sealed record BankTraded(int Seat, ResourceSet Gave, ResourceSet Got) : GameEvent;

/// <summary>
/// The first event of a game that starts from a saved position (it has no history): every hand's size, the bank, and the
/// hands themselves, each seat seeing only its own unless <see cref="HandsKnown"/> (a scenario that shows every hand).
/// Bots' hand trackers start from this.
/// </summary>
public sealed record PositionStarted(int[] HandSizes, int[] Bank, ResourceSet[] Hands, bool HandsKnown, bool InSetup) : GameEvent
{
    public override GameEvent RedactFor(int viewer) =>
        HandsKnown ? this : this with { Hands = Hands.Select((hand, seat) => seat == viewer ? hand : default).ToArray() };

    public static PositionStarted Of(GameState s, bool handsKnown) => new(
        Enumerable.Range(0, GameConstants.PlayerCount).Select(s.HandSize).ToArray(),
        (int[])s.Bank.Clone(),
        Enumerable.Range(0, GameConstants.PlayerCount).Select(seat => ResourceSet.From(s.HandOf(seat))).ToArray(),
        handsKnown,
        s.Phase is Phase.SetupSettlement or Phase.SetupRoad);
}

/// <summary>The current player opens an offer in <see cref="Slot"/> to every opponent: it gives Give, wants Get.</summary>
public sealed record TradeOffered(int Seat, int Slot, ResourceSet Give, ResourceSet Get) : GameEvent;

/// <summary>The current player changed an open offer's terms; everyone's responses to it reset.</summary>
public sealed record TradeEdited(int Seat, int Slot, ResourceSet Give, ResourceSet Get) : GameEvent;

/// <summary>An opponent countered the offer in ParentSlot with its own proposal in Slot (it gives Give, wants Get).</summary>
public sealed record TradeCountered(int Seat, int Slot, int ParentSlot, ResourceSet Give, ResourceSet Get) : GameEvent;

/// <summary>An opponent accepted or declined the offer in Slot.</summary>
public sealed record TradeReplied(int Seat, int Slot, bool Accepted) : GameEvent;

/// <summary>The current player turned down Partner's acceptance of (or counter in) Slot.</summary>
public sealed record TradeRejected(int Seat, int Slot, int Partner) : GameEvent;

/// <summary>The current player (Seat) gave <see cref="Gave"/> to Partner and got <see cref="Got"/> back.</summary>
public sealed record TradeDone(int Seat, int Partner, ResourceSet Gave, ResourceSet Got) : GameEvent;

/// <summary>An offer or counter in Slot was withdrawn by its maker.</summary>
public sealed record TradeCancelled(int Seat, int Slot) : GameEvent;

public enum Award : byte { LongestRoad, LargestArmy }

/// <summary>An award moved. From or To is -1 when nobody held it / nobody holds it now.</summary>
public sealed record AwardChanged(Award Award, int From, int To) : GameEvent;

public sealed record TurnEnded(int Seat) : GameEvent;

/// <summary>Winner is -1 for a draw at the turn cap.</summary>
public sealed record GameEnded(int Winner) : GameEvent;
