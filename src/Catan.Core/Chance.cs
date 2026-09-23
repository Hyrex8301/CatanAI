namespace Catan.Core;

/// <summary>Every random outcome the rules need. The rules never touch an RNG directly.</summary>
public interface IChance
{
    (int D1, int D2) RollDice();

    /// <summary>Resource index, weighted by the victim's counts. The victim holds at least one card.</summary>
    int PickStolenCard(ReadOnlySpan<int> victimHand);

    /// <summary>Dev card type, weighted by what's left. The deck is not empty.</summary>
    int DrawDevCard(ReadOnlySpan<int> deckCounts);
}

/// <summary>Live games and simulations: outcomes come from a seeded <see cref="Rng"/>.</summary>
public sealed class RngChance : IChance
{
    private readonly Rng _rng;

    public RngChance(Rng rng) => _rng = rng;

    public RngChance(ulong seed) : this(new Rng(seed)) { }

    public (int D1, int D2) RollDice() => (_rng.NextInt(6) + 1, _rng.NextInt(6) + 1);

    public int PickStolenCard(ReadOnlySpan<int> victimHand) => WeightedPick(victimHand);

    public int DrawDevCard(ReadOnlySpan<int> deckCounts) => WeightedPick(deckCounts);

    private int WeightedPick(ReadOnlySpan<int> counts)
    {
        int total = 0;
        foreach (int c in counts)
            total += c;
        if (total <= 0)
            throw new InvalidOperationException("Cannot pick from an empty set.");

        int r = _rng.NextInt(total);
        for (int i = 0; i < counts.Length; i++)
        {
            r -= counts[i];
            if (r < 0)
                return i;
        }
        throw new InvalidOperationException("Unreachable: weighted pick ran past the end.");
    }
}
