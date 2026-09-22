namespace Catan.Core;

/// <summary>Five resource counts in the fixed order Brick, Lumber, Wool, Grain, Ore. Value equality via record struct.</summary>
public readonly record struct ResourceSet(int Brick, int Lumber, int Wool, int Grain, int Ore)
{
    public int this[Resource r] => this[(int)r];

    public int this[int index] => index switch
    {
        0 => Brick,
        1 => Lumber,
        2 => Wool,
        3 => Grain,
        4 => Ore,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public int Total => Brick + Lumber + Wool + Grain + Ore;

    /// <summary>True when this set can be paid from <paramref name="hand"/> (no count exceeds the hand's).</summary>
    public bool FitsIn(ResourceSet hand) =>
        Brick <= hand.Brick && Lumber <= hand.Lumber && Wool <= hand.Wool && Grain <= hand.Grain && Ore <= hand.Ore;

    /// <summary>Same as <see cref="FitsIn(ResourceSet)"/> for a hand stored as five ints, e.g. <c>state.Hand.AsSpan(seat * 5, 5)</c>.</summary>
    public bool FitsIn(ReadOnlySpan<int> hand) =>
        Brick <= hand[0] && Lumber <= hand[1] && Wool <= hand[2] && Grain <= hand[3] && Ore <= hand[4];

    public static ResourceSet Of(Resource r, int count = 1) => r switch
    {
        Resource.Brick => new(count, 0, 0, 0, 0),
        Resource.Lumber => new(0, count, 0, 0, 0),
        Resource.Wool => new(0, 0, count, 0, 0),
        Resource.Grain => new(0, 0, 0, count, 0),
        Resource.Ore => new(0, 0, 0, 0, count),
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    public static ResourceSet From(ReadOnlySpan<int> counts) => new(counts[0], counts[1], counts[2], counts[3], counts[4]);

    public static ResourceSet operator +(ResourceSet a, ResourceSet b) =>
        new(a.Brick + b.Brick, a.Lumber + b.Lumber, a.Wool + b.Wool, a.Grain + b.Grain, a.Ore + b.Ore);

    public static ResourceSet operator -(ResourceSet a, ResourceSet b) =>
        new(a.Brick - b.Brick, a.Lumber - b.Lumber, a.Wool - b.Wool, a.Grain - b.Grain, a.Ore - b.Ore);
}
