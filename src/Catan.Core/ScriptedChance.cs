namespace Catan.Core;

/// <summary>
/// Tests: queue exact outcomes (force a 7, pick the stolen card, fix the dev draw). Each call takes the next
/// scripted outcome of its kind; when a queue is empty it defers to the fallback, or throws if there is none.
/// Scripted steals and draws must be possible, so a bad script fails loudly instead of corrupting the game.
/// </summary>
public sealed class ScriptedChance : IChance
{
    private readonly Queue<(int D1, int D2)> _dice = new();
    private readonly Queue<int> _steals = new();
    private readonly Queue<int> _draws = new();
    private readonly IChance? _fallback;

    public ScriptedChance(IChance? fallback = null) => _fallback = fallback;

    public bool IsExhausted => _dice.Count == 0 && _steals.Count == 0 && _draws.Count == 0;

    public ScriptedChance Dice(int d1, int d2)
    {
        if (d1 is < 1 or > 6 || d2 is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(d1), $"Dice ({d1}, {d2}) must each be 1-6.");
        _dice.Enqueue((d1, d2));
        return this;
    }

    /// <summary>Queue a roll by its total (2-12), e.g. Roll(7) queues (1, 6).</summary>
    public ScriptedChance Roll(int total)
    {
        if (total is < 2 or > 12)
            throw new ArgumentOutOfRangeException(nameof(total), total, "Roll total must be 2-12.");
        int d1 = Math.Max(1, total - 6);
        return Dice(d1, total - d1);
    }

    public ScriptedChance Steal(Resource resource)
    {
        _steals.Enqueue((int)resource);
        return this;
    }

    public ScriptedChance Draw(DevCardType type)
    {
        _draws.Enqueue((int)type);
        return this;
    }

    public (int D1, int D2) RollDice() =>
        _dice.Count > 0 ? _dice.Dequeue() : Fallback(nameof(RollDice)).RollDice();

    public int PickStolenCard(ReadOnlySpan<int> victimHand)
    {
        if (_steals.Count == 0)
            return Fallback(nameof(PickStolenCard)).PickStolenCard(victimHand);
        int r = _steals.Dequeue();
        if (victimHand[r] <= 0)
            throw new InvalidOperationException($"Scripted steal of {(Resource)r}, but the victim has none.");
        return r;
    }

    public int DrawDevCard(ReadOnlySpan<int> deckCounts)
    {
        if (_draws.Count == 0)
            return Fallback(nameof(DrawDevCard)).DrawDevCard(deckCounts);
        int t = _draws.Dequeue();
        if (deckCounts[t] <= 0)
            throw new InvalidOperationException($"Scripted draw of {(DevCardType)t}, but none are left in the deck.");
        return t;
    }

    private IChance Fallback(string call) =>
        _fallback ?? throw new InvalidOperationException($"ScriptedChance.{call} has nothing scripted and no fallback.");
}
