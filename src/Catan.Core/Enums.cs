namespace Catan.Core;

/// <summary>Fixed resource order used by every int[5] array (hands, bank, costs).</summary>
public enum Resource : byte
{
    Brick = 0,
    Lumber = 1,
    Wool = 2,
    Grain = 3,
    Ore = 4,
}

/// <summary>Order matches DevHand / DevDeck indexing: Knight 14, VP 5, RoadBuilding 2, YearOfPlenty 2, Monopoly 2.</summary>
public enum DevCardType : byte
{
    Knight = 0,
    VictoryPoint = 1,
    RoadBuilding = 2,
    YearOfPlenty = 3,
    Monopoly = 4,
}

public enum Phase : byte
{
    SetupSettlement,
    SetupRoad,
    PreRoll,
    Discard,
    MoveRobber,
    Main,
    RoadBuilding,
    GameOver,
}

public enum ActionType : byte
{
    BuildRoad, BuildSettlement, BuildCity,          // free during setup and Road Building
    RollDice, Discard, MoveRobber,
    BuyDevCard, PlayKnight, PlayRoadBuilding, PlayYearOfPlenty, PlayMonopoly,
    // Player trades happen during Main. Offer / Edit / Confirm are the current player's; any opponent may Accept, Decline
    // or Counter an open offer at any time, in any order. Target is the offer's slot; Target2 a partner seat.
    BankTrade, OfferTrade, EditOffer, CounterOffer, AcceptOffer, DeclineOffer, ConfirmTrade, CancelOffer,
    EndTurn,
}

public static class GameConstants
{
    public const int PlayerCount = 4;
    public const int ResourceCount = 5;
    public const int DevCardTypeCount = 5;
    public const int DevDeckSize = 25;

    /// <summary>At most this many of the current player's offers are open at once.</summary>
    public const int MaxOpenOffers = 10;

    /// <summary>Trade slots: the current player's open offers plus one open counter per opponent.</summary>
    public const int OfferSlots = MaxOpenOffers + PlayerCount - 1;
}
