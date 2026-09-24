using Catan.Core;

namespace Catan.AI;

/// <summary>
/// Chance with a separate random stream for dice, steals and dev card draws. Two copies with the same seed roll the same
/// dice in the same order even when one of them steals or draws more cards, which keeps paired search branches comparable.
/// </summary>
public sealed class StreamChance : IChance
{
    private readonly RngChance _dice, _steals, _draws;

    public StreamChance(ulong seed)
    {
        _dice = new RngChance(new Rng(seed, stream: 11));
        _steals = new RngChance(new Rng(seed, stream: 13));
        _draws = new RngChance(new Rng(seed, stream: 17));
    }

    public (int D1, int D2) RollDice() => _dice.RollDice();

    public int PickStolenCard(ReadOnlySpan<int> victimHand) => _steals.PickStolenCard(victimHand);

    public int DrawDevCard(ReadOnlySpan<int> deckCounts) => _draws.DrawDevCard(deckCounts);
}
