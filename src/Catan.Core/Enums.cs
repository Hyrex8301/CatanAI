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
    TradeReply,
    TradeConfirm,
    GameOver,
}

public enum ActionType : byte
{
    BuildRoad, BuildSettlement, BuildCity,          // free during setup and Road Building
    RollDice, Discard, MoveRobber,
    BuyDevCard, PlayKnight, PlayRoadBuilding, PlayYearOfPlenty, PlayMonopoly,
    BankTrade, OfferTrade, AcceptOffer, DeclineOffer, ConfirmTrade, CancelOffer,
    EndTurn,
}

public static class GameConstants
{
    public const int ResourceCount = 5;
    public const int DevCardTypeCount = 5;
}
