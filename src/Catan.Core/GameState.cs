namespace Catan.Core;

public sealed record GameSettings
{
    public static readonly GameSettings Default = new();

    public int VpToWin { get; init; } = 10;
    public bool FriendlyRobber { get; init; }

    /// <summary>At this many turns the game ends as a draw, so random games always terminate. Real games can set it high.</summary>
    public int MaxTurns { get; init; } = 500;

    public int MaxOffersPerTurn { get; init; } = 3;
}

/// <summary>An open player-trade offer from the current player: they give <see cref="Give"/> and receive <see cref="Get"/>.</summary>
/// <param name="ToSeats">Bitmask of seats the offer is made to.</param>
public readonly record struct TradeOffer(bool IsActive, ResourceSet Give, ResourceSet Get, int ToSeats);

/// <summary>
/// Plain game data in small arrays, no rules logic. Rules live in the static Rules class; randomness comes through IChance.
/// Per-seat arrays are indexed [seat * 5 + resource] or [seat * 5 + devType].
/// </summary>
public sealed class GameState
{
    private const int Seats = GameConstants.PlayerCount;
    private const int R = GameConstants.ResourceCount;
    private const int D = GameConstants.DevCardTypeCount;

    public readonly Board Board;             // shared, never copied
    public readonly GameSettings Settings;

    // Pieces
    public readonly sbyte[] VertexOwner = new sbyte[Topology.VertexCount]; // -1 empty, else seat 0-3
    public readonly byte[] VertexLevel = new byte[Topology.VertexCount];   // 0 none, 1 settlement, 2 city
    public readonly sbyte[] EdgeOwner = new sbyte[Topology.EdgeCount];     // -1 empty
    public int RobberHex;
    public readonly int[] RoadsLeft = new int[Seats], SettlementsLeft = new int[Seats], CitiesLeft = new int[Seats];

    // Cards (resource order Brick, Lumber, Wool, Grain, Ore)
    public readonly int[] Hand = new int[Seats * R];
    public readonly int[] Bank = new int[R];
    public readonly int[] DevHand = new int[Seats * D];          // includes cards bought this turn
    public readonly int[] DevBoughtThisTurn = new int[D];        // current player's, not playable until next turn
    public readonly int[] DevDeck = new int[D];
    public readonly int[] DevPlayed = new int[D];                // all seats, by type; VP cards are never played
    public readonly int[] KnightsPlayed = new int[Seats];

    // Cached scoring (StateValidator recomputes these from scratch)
    public int LongestRoadOwner = -1, LargestArmyOwner = -1;
    public readonly int[] RoadLength = new int[Seats];
    public readonly int[] PublicVP = new int[Seats];             // everything except hidden VP cards

    // Turn state
    public Phase Phase;
    public int CurrentPlayer, TurnNumber, SetupStep, LastRoll, FreeRoads, OffersThisTurn;
    public bool HasRolled, DevPlayedThisTurn;
    public readonly int[] DiscardOwed = new int[Seats];
    public TradeOffer Offer;
    public readonly sbyte[] OfferReply = new sbyte[Seats];       // -1 waiting, 0 declined, 1 accepted
    public int Winner = -1;

    /// <summary>A new game at the start of setup: empty board, full bank and deck, robber on the desert.</summary>
    public GameState(Board board, GameSettings? settings = null)
    {
        Board = board;
        Settings = settings ?? GameSettings.Default;
        Array.Fill(VertexOwner, (sbyte)-1);
        Array.Fill(EdgeOwner, (sbyte)-1);
        RobberHex = board.DesertHex;
        Array.Fill(RoadsLeft, Costs.RoadsPerPlayer);
        Array.Fill(SettlementsLeft, Costs.SettlementsPerPlayer);
        Array.Fill(CitiesLeft, Costs.CitiesPerPlayer);
        Array.Fill(Bank, Costs.BankPerResource);
        StandardPieces.DevDeck.CopyTo(DevDeck, 0);
        Array.Fill(OfferReply, (sbyte)-1);
        Phase = Phase.SetupSettlement;
    }

    public int TotalVP(int seat) => PublicVP[seat] + DevHand[seat * D + (int)DevCardType.VictoryPoint];

    public ReadOnlySpan<int> HandOf(int seat) => Hand.AsSpan(seat * R, R);

    public int HandSize(int seat)
    {
        int total = 0;
        for (int r = 0; r < R; r++)
            total += Hand[seat * R + r];
        return total;
    }

    public GameState Clone()
    {
        var copy = new GameState(Board, Settings);
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Copies every dynamic field into this instance without allocating, so search can reuse a pool of states.</summary>
    public void CopyFrom(GameState other)
    {
        if (!ReferenceEquals(Board, other.Board) || !Equals(Settings, other.Settings))
            throw new ArgumentException("CopyFrom needs a state from the same game (same Board and Settings).", nameof(other));

        Array.Copy(other.VertexOwner, VertexOwner, VertexOwner.Length);
        Array.Copy(other.VertexLevel, VertexLevel, VertexLevel.Length);
        Array.Copy(other.EdgeOwner, EdgeOwner, EdgeOwner.Length);
        RobberHex = other.RobberHex;
        Array.Copy(other.RoadsLeft, RoadsLeft, Seats);
        Array.Copy(other.SettlementsLeft, SettlementsLeft, Seats);
        Array.Copy(other.CitiesLeft, CitiesLeft, Seats);

        Array.Copy(other.Hand, Hand, Hand.Length);
        Array.Copy(other.Bank, Bank, R);
        Array.Copy(other.DevHand, DevHand, DevHand.Length);
        Array.Copy(other.DevBoughtThisTurn, DevBoughtThisTurn, D);
        Array.Copy(other.DevDeck, DevDeck, D);
        Array.Copy(other.DevPlayed, DevPlayed, D);
        Array.Copy(other.KnightsPlayed, KnightsPlayed, Seats);

        LongestRoadOwner = other.LongestRoadOwner;
        LargestArmyOwner = other.LargestArmyOwner;
        Array.Copy(other.RoadLength, RoadLength, Seats);
        Array.Copy(other.PublicVP, PublicVP, Seats);

        Phase = other.Phase;
        CurrentPlayer = other.CurrentPlayer;
        TurnNumber = other.TurnNumber;
        SetupStep = other.SetupStep;
        LastRoll = other.LastRoll;
        FreeRoads = other.FreeRoads;
        OffersThisTurn = other.OffersThisTurn;
        HasRolled = other.HasRolled;
        DevPlayedThisTurn = other.DevPlayedThisTurn;
        Array.Copy(other.DiscardOwed, DiscardOwed, Seats);
        Offer = other.Offer;
        Array.Copy(other.OfferReply, OfferReply, Seats);
        Winner = other.Winner;
    }

    /// <summary>FNV-1a (64-bit) over every dynamic field in a fixed order, 4 little-endian bytes per value. Board and Settings are excluded.</summary>
    public ulong ComputeHash()
    {
        ulong h = FnvOffset;
        Mix(ref h, VertexOwner);
        Mix(ref h, VertexLevel);
        Mix(ref h, EdgeOwner);
        Mix(ref h, RobberHex);
        Mix(ref h, RoadsLeft);
        Mix(ref h, SettlementsLeft);
        Mix(ref h, CitiesLeft);

        Mix(ref h, Hand);
        Mix(ref h, Bank);
        Mix(ref h, DevHand);
        Mix(ref h, DevBoughtThisTurn);
        Mix(ref h, DevDeck);
        Mix(ref h, DevPlayed);
        Mix(ref h, KnightsPlayed);

        Mix(ref h, LongestRoadOwner);
        Mix(ref h, LargestArmyOwner);
        Mix(ref h, RoadLength);
        Mix(ref h, PublicVP);

        Mix(ref h, (int)Phase);
        Mix(ref h, CurrentPlayer);
        Mix(ref h, TurnNumber);
        Mix(ref h, SetupStep);
        Mix(ref h, LastRoll);
        Mix(ref h, FreeRoads);
        Mix(ref h, OffersThisTurn);
        Mix(ref h, HasRolled ? 1 : 0);
        Mix(ref h, DevPlayedThisTurn ? 1 : 0);
        Mix(ref h, DiscardOwed);
        Mix(ref h, Offer.IsActive ? 1 : 0);
        for (int r = 0; r < R; r++)
            Mix(ref h, Offer.Give[r]);
        for (int r = 0; r < R; r++)
            Mix(ref h, Offer.Get[r]);
        Mix(ref h, Offer.ToSeats);
        Mix(ref h, OfferReply);
        Mix(ref h, Winner);
        return h;
    }

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static void Mix(ref ulong h, int value)
    {
        for (int i = 0; i < 4; i++)
        {
            h ^= (byte)(value >> (8 * i));
            h *= FnvPrime;
        }
    }

    private static void Mix(ref ulong h, int[] values)
    {
        foreach (int v in values)
            Mix(ref h, v);
    }

    private static void Mix(ref ulong h, sbyte[] values)
    {
        foreach (sbyte v in values)
            Mix(ref h, v);
    }

    private static void Mix(ref ulong h, byte[] values)
    {
        foreach (byte v in values)
            Mix(ref h, v);
    }
}
