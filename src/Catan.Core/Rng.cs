namespace Catan.Core;

/// <summary>
/// PCG32 (XSH RR): 64-bit state, 32-bit output. Matches the reference pcg32_srandom_r / pcg32_random_r /
/// pcg32_boundedrand_r, so a seed gives the same sequence on every platform and runtime.
/// </summary>
public sealed class Rng
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong DefaultStream = 54;

    private ulong _state;
    private readonly ulong _inc;

    public Rng(ulong seed, ulong stream = DefaultStream)
    {
        _inc = (stream << 1) | 1;
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * Multiplier + _inc;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Uniform int in [0, bound). Rejects the low values that would bias the modulo.</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0)
            throw new ArgumentOutOfRangeException(nameof(bound), bound, "Bound must be positive.");
        uint b = (uint)bound;
        uint threshold = (uint)-b % b;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
                return (int)(r % b);
        }
    }

    /// <summary>Fisher-Yates shuffle in place.</summary>
    public void Shuffle<T>(Span<T> items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
