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

public enum ChanceKind : byte { Dice, Steal, Draw }

/// <summary>One random outcome, as stored in a game record. Dice use D1 and D2; Steal (resource) and Draw (dev type) use Value.</summary>
public readonly record struct ChanceOutcome(ChanceKind Kind, int D1 = 0, int D2 = 0, int Value = 0);

/// <summary>Wraps any IChance and logs every outcome, for the game record.</summary>
public sealed class RecordingChance : IChance
{
    private readonly IChance _inner;
    private readonly List<ChanceOutcome> _log = new();

    public RecordingChance(IChance inner) => _inner = inner;

    public IReadOnlyList<ChanceOutcome> Log => _log;

    public (int D1, int D2) RollDice()
    {
        var (d1, d2) = _inner.RollDice();
        _log.Add(new ChanceOutcome(ChanceKind.Dice, d1, d2));
        return (d1, d2);
    }

    public int PickStolenCard(ReadOnlySpan<int> victimHand)
    {
        int r = _inner.PickStolenCard(victimHand);
        _log.Add(new ChanceOutcome(ChanceKind.Steal, Value: r));
        return r;
    }

    public int DrawDevCard(ReadOnlySpan<int> deckCounts)
    {
        int t = _inner.DrawDevCard(deckCounts);
        _log.Add(new ChanceOutcome(ChanceKind.Draw, Value: t));
        return t;
    }
}

/// <summary>
/// Replays recorded outcomes. Each kind is read from its own queue in recorded order, so old records keep working even if
/// the engine later asks for dice, steals and draws in a different interleaving. Impossible outcomes throw.
/// </summary>
public sealed class ReplayChance : IChance
{
    private readonly Queue<ChanceOutcome> _dice = new(), _steals = new(), _draws = new();

    public ReplayChance(IEnumerable<ChanceOutcome> outcomes)
    {
        foreach (var o in outcomes)
            (o.Kind switch { ChanceKind.Dice => _dice, ChanceKind.Steal => _steals, _ => _draws }).Enqueue(o);
    }

    /// <summary>Recorded outcomes not used yet.</summary>
    public int Remaining => _dice.Count + _steals.Count + _draws.Count;

    public (int D1, int D2) RollDice()
    {
        var o = Next(_dice, "dice roll");
        if (o.D1 is < 1 or > 6 || o.D2 is < 1 or > 6)
            throw new ReplayException($"Recorded dice ({o.D1}, {o.D2}) are invalid.");
        return (o.D1, o.D2);
    }

    public int PickStolenCard(ReadOnlySpan<int> victimHand)
    {
        int r = Next(_steals, "steal").Value;
        if (r < 0 || r >= victimHand.Length || victimHand[r] <= 0)
            throw new ReplayException($"Recorded steal of resource {r}, but the victim has none.");
        return r;
    }

    public int DrawDevCard(ReadOnlySpan<int> deckCounts)
    {
        int t = Next(_draws, "dev card draw").Value;
        if (t < 0 || t >= deckCounts.Length || deckCounts[t] <= 0)
            throw new ReplayException($"Recorded draw of dev type {t}, but none are left in the deck.");
        return t;
    }

    private static ChanceOutcome Next(Queue<ChanceOutcome> queue, string what) =>
        queue.Count > 0 ? queue.Dequeue() : throw new ReplayException($"The record has no more {what} outcomes.");
}

public sealed class ReplayException : Exception
{
    public ReplayException(string message) : base(message) { }
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
