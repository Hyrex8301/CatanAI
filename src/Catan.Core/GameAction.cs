namespace Catan.Core;

/// <summary>
/// One decision by one seat. Target: vertex, edge, hex, resource, partner seat, or seat bitmask. Target2: robber victim or -1.
/// Record struct, so actions compare by value.
/// </summary>
public readonly record struct GameAction(ActionType Type, int Seat, int Target = -1, int Target2 = -1,
                                         ResourceSet Give = default, ResourceSet Get = default);

public enum PieceType : byte { Road, Settlement, City }

/// <summary>Everything that happens in a game is reported as events. Redaction for other seats arrives in step 13.</summary>
public abstract record GameEvent;

public sealed record Built(int Seat, PieceType Piece, int Target) : GameEvent;

public sealed record DiceRolled(int Seat, int D1, int D2) : GameEvent
{
    public int Total => D1 + D2;
}

/// <summary>Cards a seat received from the bank (setup payout or a roll).</summary>
public sealed record ResourcesProduced(int Seat, ResourceSet Gained) : GameEvent;

/// <summary>Hidden from other seats in step 13: they see Type as unknown.</summary>
public sealed record DevCardBought(int Seat, DevCardType Type) : GameEvent;

public enum Award : byte { LongestRoad, LargestArmy }

/// <summary>An award moved. From or To is -1 when nobody held it / nobody holds it now.</summary>
public sealed record AwardChanged(Award Award, int From, int To) : GameEvent;

public sealed record TurnEnded(int Seat) : GameEvent;

/// <summary>Winner is -1 for a draw at the turn cap.</summary>
public sealed record GameEnded(int Winner) : GameEvent;
