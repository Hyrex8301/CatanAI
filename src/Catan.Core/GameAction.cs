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
