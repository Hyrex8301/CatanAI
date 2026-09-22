namespace Catan.Core;

/// <summary>Build costs and per-player piece supply for the base game.</summary>
public static class Costs
{
    public static readonly ResourceSet Road = new(Brick: 1, Lumber: 1, Wool: 0, Grain: 0, Ore: 0);
    public static readonly ResourceSet Settlement = new(Brick: 1, Lumber: 1, Wool: 1, Grain: 1, Ore: 0);
    public static readonly ResourceSet City = new(Brick: 0, Lumber: 0, Wool: 0, Grain: 2, Ore: 3);
    public static readonly ResourceSet DevCard = new(Brick: 0, Lumber: 0, Wool: 1, Grain: 1, Ore: 1);

    public const int RoadsPerPlayer = 15;
    public const int SettlementsPerPlayer = 5;
    public const int CitiesPerPlayer = 4;

    public const int BankPerResource = 19;
}
