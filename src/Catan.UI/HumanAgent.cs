using Catan.Core;

namespace Catan.UI;

/// <summary>What the human is being asked right now: a required decision, or an optional answer to open trades.</summary>
/// <param name="Deadline">For optional answers: when the response window closes (UTC).</param>
public sealed record HumanPrompt(PlayerView View, IReadOnlyList<GameAction> Legal, bool IsOptional, DateTime? Deadline);

/// <summary>
/// The human seat. The runner awaits <see cref="DecideAsync"/> / <see cref="RespondAsync"/>; the UI shows
/// <see cref="Prompt"/> and completes it with <see cref="Submit"/> (or <see cref="Skip"/> for optional answers).
/// An optional answer also ends by itself when the response window runs out, so bots are never stuck waiting.
/// The UI must only submit actions it has checked with Rules.IsLegal; the runner rejects anything else.
/// </summary>
public sealed class HumanAgent : IPlayerAgent
{
    private TaskCompletionSource<GameAction>? _decision;
    private TaskCompletionSource<GameAction?>? _response;

    public HumanAgent(string name, TimeSpan responseWindow)
    {
        Name = name;
        ResponseWindow = responseWindow;
    }

    public string Name { get; }
    public TimeSpan ResponseWindow { get; set; }

    /// <summary>The open question, or null when the game isn't waiting on the human.</summary>
    public HumanPrompt? Prompt { get; private set; }

    /// <summary>Raised whenever <see cref="Prompt"/> changes.</summary>
    public event Action? PromptChanged;

    public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GameAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        _decision = tcs;
        ct.Register(() => tcs.TrySetCanceled(ct));
        SetPrompt(new HumanPrompt(view, legal, false, null));
        return tcs.Task;
    }

    public async Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GameAction?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _response = tcs;
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(ResponseWindow);
        using var registration = window.Token.Register(() =>
        {
            if (ct.IsCancellationRequested)
                tcs.TrySetCanceled(ct);
            else
                tcs.TrySetResult(null); // the response window ran out
        });
        SetPrompt(new HumanPrompt(view, legal, true, DateTime.UtcNow + ResponseWindow));
        try
        {
            return await tcs.Task;
        }
        finally
        {
            if (ReferenceEquals(_response, tcs))
            {
                _response = null;
                SetPrompt(null);
            }
        }
    }

    /// <summary>Answers the open prompt. Returns false if nothing is being asked.</summary>
    public bool Submit(GameAction action)
    {
        if (_response is { } response)
        {
            _response = null;
            SetPrompt(null);
            return response.TrySetResult(action);
        }
        if (_decision is { } decision)
        {
            _decision = null;
            SetPrompt(null);
            return decision.TrySetResult(action);
        }
        return false;
    }

    /// <summary>Passes on an optional answer (the "Skip" button). Returns false if no optional answer is open.</summary>
    public bool Skip()
    {
        if (_response is not { } response)
            return false;
        _response = null;
        SetPrompt(null);
        return response.TrySetResult(null);
    }

    private void SetPrompt(HumanPrompt? prompt)
    {
        Prompt = prompt;
        PromptChanged?.Invoke();
    }
}
