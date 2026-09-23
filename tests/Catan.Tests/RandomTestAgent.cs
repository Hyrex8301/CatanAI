using Catan.Core;

namespace Catan.Tests;

/// <summary>
/// Plays random legal moves from its view only: random discards, 5% new offers in Main, and answers open trades
/// half the time (a quarter of those as counters). Step 15's RandomBot is the real one; this keeps the tests independent of it.
/// </summary>
public sealed class RandomTestAgent : IPlayerAgent
{
    private readonly Rng _rng;

    public RandomTestAgent(ulong seed) => _rng = new Rng(seed);

    public string Name => "RandomTestAgent";

    public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
    {
        if (view.Phase == Phase.Discard)
            return Task.FromResult(Rules.RandomDiscard(view, _rng));
        if (view.Phase == Phase.Main && _rng.NextInt(20) == 0 && Rules.RandomTradeOffer(view, _rng) is { } offer)
            return Task.FromResult(offer);
        return Task.FromResult(legal[_rng.NextInt(legal.Count)]);
    }

    public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
    {
        if (_rng.NextInt(2) == 0)
            return Task.FromResult<GameAction?>(null);
        if (_rng.NextInt(4) == 0 && Rules.RandomCounterOffer(view, _rng) is { } counter)
            return Task.FromResult<GameAction?>(counter);
        return Task.FromResult<GameAction?>(legal[_rng.NextInt(legal.Count)]);
    }

    /// <summary>A new game on a balanced board from <paramref name="seed"/>, four random agents, seeded dice.</summary>
    public static GameRunner Game(ulong seed, bool validate = false, GameSettings? settings = null)
    {
        var state = new GameState(BoardGenerator.Balanced(new Rng(seed)), settings);
        var agents = Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomTestAgent(seed * 10 + (ulong)i)).ToArray();
        return new GameRunner(state, agents, new RngChance(seed + 1000), validate);
    }
}
