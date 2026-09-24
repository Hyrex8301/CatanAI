using Catan.Core;

namespace Catan.UI;

/// <summary>
/// Runs a bot's thinking on a worker thread, so the game screen keeps drawing and animating while a slow bot (a search bot
/// thinks for a second or more) decides. The bot only ever gets its own PlayerView copy, so thinking elsewhere is safe;
/// the game loop awaits the result and carries on on its own thread. <see cref="Thinking"/> says whether it's busy.
/// </summary>
public sealed class BackgroundAgent : IPlayerAgent
{
    private readonly IPlayerAgent _inner;
    private int _busy;

    public BackgroundAgent(IPlayerAgent inner) => _inner = inner;

    public string Name => _inner.Name;

    /// <summary>The wrapped bot.</summary>
    public IPlayerAgent Inner => _inner;

    /// <summary>True while a decision is being worked out.</summary>
    public bool Thinking => Volatile.Read(ref _busy) > 0;

    public async Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
    {
        Interlocked.Increment(ref _busy);
        try
        {
            return await Task.Run(() => _inner.DecideAsync(view, legal, ct), ct);
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
        }
    }

    public async Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
    {
        Interlocked.Increment(ref _busy);
        try
        {
            return await Task.Run(() => _inner.RespondAsync(view, legal, ct), ct);
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
        }
    }
}
