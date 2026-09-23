using Catan.Core;

namespace Catan.AI;

/// <summary>
/// Finds engine bugs by playing huge numbers of weird but legal games. Uses only its view and the legal list.
/// Weighted mode: if any build is legal it builds 80% of the time, otherwise it picks uniformly among the other actions
/// (EndTurn included). 5% of Main decisions are a random trade offer. Discards and robber victims are random.
/// Offers that reach it: accepts at random if it can pay, sometimes counters. PureRandom drops the weights.
/// </summary>
public sealed class RandomBot : IPlayerAgent
{
    private readonly Rng _rng;
    private readonly bool _pureRandom;
    private readonly List<GameAction> _builds = new(), _others = new();

    public RandomBot(ulong seed, bool pureRandom = false)
    {
        _rng = new Rng(seed);
        _pureRandom = pureRandom;
    }

    public string Name => _pureRandom ? "RandomBot(pure)" : "RandomBot";

    public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        Task.FromResult(Decide(view, legal));

    public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        Task.FromResult(Respond(view, legal));

    private GameAction Decide(PlayerView view, IReadOnlyList<GameAction> legal)
    {
        if (view.Phase == Phase.Discard)
            return Rules.RandomDiscard(view, _rng);
        if (view.Phase == Phase.Main && _rng.NextInt(20) == 0 && Rules.RandomTradeOffer(view, _rng) is { } offer)
            return offer;
        if (_pureRandom)
            return legal[_rng.NextInt(legal.Count)];

        _builds.Clear();
        _others.Clear();
        foreach (var a in legal)
            (a.Type is ActionType.BuildRoad or ActionType.BuildSettlement or ActionType.BuildCity ? _builds : _others).Add(a);

        if (_builds.Count > 0 && (_others.Count == 0 || _rng.NextInt(10) < 8))
            return _builds[_rng.NextInt(_builds.Count)];
        return _others[_rng.NextInt(_others.Count)];
    }

    /// <summary>Answers an open trade most of the time: a counter 1 time in 10, otherwise a random listed answer.</summary>
    private GameAction? Respond(PlayerView view, IReadOnlyList<GameAction> legal)
    {
        if (_rng.NextInt(4) == 0)
            return null; // not now; asked again once the game moves on
        if (_rng.NextInt(10) == 0 && Rules.RandomCounterOffer(view, _rng) is { } counter)
            return counter;
        return legal.Count > 0 ? legal[_rng.NextInt(legal.Count)] : null;
    }
}
