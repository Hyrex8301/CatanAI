namespace Catan.Core;

/// <summary>
/// What one seat may see, copied out of the game state. Nothing in a view refers back to the state or to hidden data:
/// arrays are fresh copies, Board and Settings are immutable and public, and events are redacted for this seat.
/// Hidden from a seat: other seats' hands and dev cards (only their sizes), the dev deck's composition (only its size),
/// the resource in others' steals and the type of others' dev card purchases.
/// </summary>
public sealed class PlayerView
{
    private const int Seats = GameConstants.PlayerCount;
    private const int R = GameConstants.ResourceCount;
    private const int D = GameConstants.DevCardTypeCount;

    public readonly int Seat;
    public readonly Board Board;
    public readonly GameSettings Settings;

    // Board and pieces (public)
    public readonly sbyte[] VertexOwner;
    public readonly byte[] VertexLevel;
    public readonly sbyte[] EdgeOwner;
    public readonly int RobberHex;
    public readonly int[] RoadsLeft, SettlementsLeft, CitiesLeft;

    // Turn (public)
    public readonly Phase Phase;
    public readonly int CurrentPlayer, ActingSeat, TurnNumber, SetupStep, LastRoll, FreeRoads, OffersThisTurn, Winner;
    public readonly bool HasRolled, DevPlayedThisTurn;
    public readonly int[] DiscardOwed;
    public readonly TradeOffer[] Offers;

    // Scores (public)
    public readonly int[] PublicVP, RoadLength, KnightsPlayed;
    public readonly int LongestRoadOwner, LargestArmyOwner;

    // Cards: public counts
    public readonly int[] Bank;
    public readonly int DevDeckSize;
    public readonly int[] DevPlayed;           // by type, all seats (plays are public)
    public readonly int[] HandSizes;           // per seat
    public readonly int[] DevCardCounts;       // per seat

    // Cards: this seat's own
    public readonly int[] Hand;                // 5 resources
    public readonly int[] DevHand;             // 5 dev types, including cards bought this turn
    public readonly int[] DevBoughtThisTurn;   // this seat's, when it is the current player; zeros otherwise
    public readonly int TotalVP;               // public VP plus this seat's hidden VP cards

    /// <summary>This seat's redacted event log.</summary>
    public readonly IReadOnlyList<GameEvent> Events;

    private PlayerView(GameState s, int seat, IReadOnlyList<GameEvent> events)
    {
        Seat = seat;
        Board = s.Board;
        Settings = s.Settings;

        VertexOwner = (sbyte[])s.VertexOwner.Clone();
        VertexLevel = (byte[])s.VertexLevel.Clone();
        EdgeOwner = (sbyte[])s.EdgeOwner.Clone();
        RobberHex = s.RobberHex;
        RoadsLeft = (int[])s.RoadsLeft.Clone();
        SettlementsLeft = (int[])s.SettlementsLeft.Clone();
        CitiesLeft = (int[])s.CitiesLeft.Clone();

        Phase = s.Phase;
        CurrentPlayer = s.CurrentPlayer;
        ActingSeat = Rules.ActingSeat(s);
        TurnNumber = s.TurnNumber;
        SetupStep = s.SetupStep;
        LastRoll = s.LastRoll;
        FreeRoads = s.FreeRoads;
        OffersThisTurn = s.OffersThisTurn;
        Winner = s.Winner;
        HasRolled = s.HasRolled;
        DevPlayedThisTurn = s.DevPlayedThisTurn;
        DiscardOwed = (int[])s.DiscardOwed.Clone();
        Offers = (TradeOffer[])s.Offers.Clone();

        PublicVP = (int[])s.PublicVP.Clone();
        RoadLength = (int[])s.RoadLength.Clone();
        KnightsPlayed = (int[])s.KnightsPlayed.Clone();
        LongestRoadOwner = s.LongestRoadOwner;
        LargestArmyOwner = s.LargestArmyOwner;

        Bank = (int[])s.Bank.Clone();
        DevDeckSize = s.DevDeck.Sum();
        DevPlayed = (int[])s.DevPlayed.Clone();
        HandSizes = new int[Seats];
        DevCardCounts = new int[Seats];
        for (int other = 0; other < Seats; other++)
        {
            HandSizes[other] = s.HandSize(other);
            for (int t = 0; t < D; t++)
                DevCardCounts[other] += s.DevHand[other * D + t];
        }

        Hand = s.Hand.AsSpan(seat * R, R).ToArray();
        DevHand = s.DevHand.AsSpan(seat * D, D).ToArray();
        DevBoughtThisTurn = seat == s.CurrentPlayer ? (int[])s.DevBoughtThisTurn.Clone() : new int[D];
        TotalVP = s.TotalVP(seat);

        Events = events;
    }

    /// <summary>The view for <paramref name="seat"/>, with its redacted log from <paramref name="log"/>.</summary>
    public static PlayerView From(GameState state, int seat, EventLog log) => new(state, seat, log.For(seat));

    /// <summary>The view for <paramref name="seat"/> from an unredacted list of events (redacted here).</summary>
    public static PlayerView From(GameState state, int seat, IReadOnlyList<GameEvent>? events = null) =>
        new(state, seat, events is null ? Array.Empty<GameEvent>() : events.Select(e => e.RedactFor(seat)).ToArray());

    public ResourceSet HandSet => ResourceSet.From(Hand);
}
