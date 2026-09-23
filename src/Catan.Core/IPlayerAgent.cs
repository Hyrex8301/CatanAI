namespace Catan.Core;

/// <summary>
/// One interface for every seat: bots, and later the Godot human seat. Bots finish instantly with Task.FromResult; the
/// human agent finishes when the player clicks.
/// </summary>
public interface IPlayerAgent
{
    string Name { get; }

    /// <summary>
    /// The game is waiting on this seat. <paramref name="legal"/> lists every enumerable legal action; it is empty during
    /// Discard, and never contains trade offers, edits or counters. For those the agent builds the action itself and the
    /// runner checks it with IsLegal.
    /// </summary>
    Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct);

    /// <summary>
    /// Optional: this seat may answer open trades (accept, decline, counter, or withdraw its counter) but nobody is waiting on it.
    /// Return null to do nothing for now; the runner asks again after the game moves on. <paramref name="legal"/> lists the
    /// enumerable answers (counters are built by the agent).
    /// </summary>
    Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        Task.FromResult<GameAction?>(null);
}
