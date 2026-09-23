namespace Catan.Core;

/// <summary>
/// Runs a game between agents. Each step:
/// 1. If opponents may answer open trades (Main), ask all of them at once, each with a view built from the same state
///    snapshot, so nobody sees another's answer first. Apply their answers in turn order after the current player.
///    An optional seat that passes (null) is asked again only after the game has moved on.
/// 2. Find the acting seat, build its view and legal list, await its decision, check IsLegal, apply, log events, record.
/// Actions are recorded in the order they were applied, which is all a replay needs.
/// </summary>
public sealed class GameRunner
{
    private readonly IReadOnlyList<IPlayerAgent> _agents;
    private readonly IChance _chance;
    private readonly bool _validate;
    private readonly List<GameAction> _legal = new();
    private readonly List<GameEvent> _events = new();
    private readonly List<GameAction> _actions = new();
    private int _optionalAskedAt = -1;

    /// <param name="validate">Run <see cref="StateValidator"/> after every action and throw on a violation.</param>
    public GameRunner(GameState state, IReadOnlyList<IPlayerAgent> agents, IChance chance, bool validate = false)
    {
        if (agents.Count != GameConstants.PlayerCount)
            throw new ArgumentException($"A game needs {GameConstants.PlayerCount} agents.", nameof(agents));
        State = state;
        _agents = agents;
        _chance = chance;
        _validate = validate;
    }

    public GameState State { get; }
    public EventLog Log { get; } = new();

    /// <summary>Every applied action, in order.</summary>
    public IReadOnlyList<GameAction> Actions => _actions;

    public bool IsOver => State.Phase == Phase.GameOver;

    /// <summary>Plays until the game ends.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!IsOver)
            await StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Optional trade answers (if any are due), then one decision by the acting seat. Returns false once the game is over.</summary>
    public async Task<bool> StepAsync(CancellationToken ct = default)
    {
        if (IsOver)
            return false;
        await AskOptionalSeatsAsync(ct).ConfigureAwait(false);
        if (IsOver)
            return false;

        int seat = Rules.ActingSeat(State);
        Rules.GetLegalActions(State, seat, _legal);
        var view = PlayerView.From(State, seat, Log);
        var action = await _agents[seat].DecideAsync(view, _legal.ToArray(), ct).ConfigureAwait(false);
        if (action.Seat != seat)
            throw new InvalidOperationException($"{_agents[seat].Name} (seat {seat}) returned an action for seat {action.Seat}.");
        Apply(action, _agents[seat]);
        return !IsOver;
    }

    private async Task AskOptionalSeatsAsync(CancellationToken ct)
    {
        if (_optionalAskedAt == _actions.Count)
            return;
        _optionalAskedAt = _actions.Count;

        var canAct = new bool[GameConstants.PlayerCount];
        if (Rules.OptionalSeats(State, canAct) == 0)
            return;

        // Build every view and legal list from the same snapshot before asking anyone.
        var asks = new List<(int Seat, Task<GameAction?> Answer)>();
        for (int i = 1; i < GameConstants.PlayerCount; i++)
        {
            int seat = (State.CurrentPlayer + i) % GameConstants.PlayerCount;
            if (!canAct[seat])
                continue;
            var legal = new List<GameAction>();
            Rules.GetLegalActions(State, seat, legal);
            var view = PlayerView.From(State, seat, Log);
            asks.Add((seat, _agents[seat].RespondAsync(view, legal, ct)));
        }
        await Task.WhenAll(asks.Select(a => a.Answer)).ConfigureAwait(false);

        foreach (var (seat, answer) in asks)
        {
            if (answer.Result is not { } action)
                continue;
            if (action.Seat != seat)
                throw new InvalidOperationException($"{_agents[seat].Name} (seat {seat}) answered for seat {action.Seat}.");
            Apply(action, _agents[seat]);
            if (IsOver)
                return;
        }
        _optionalAskedAt = _actions.Count;
    }

    private void Apply(GameAction action, IPlayerAgent agent)
    {
        if (!Rules.IsLegal(State, action, out string reason))
            throw new InvalidOperationException($"{agent.Name} (seat {action.Seat}) chose an illegal {action.Type}: {reason}");

        _events.Clear();
        Rules.Apply(State, action, _chance, _events);
        _actions.Add(action);
        Log.AddRange(_events);

        if (_validate)
        {
            var errors = StateValidator.Check(State);
            if (errors.Count > 0)
                throw new StateViolationException(_actions.Count, action, errors);
        }
    }
}

/// <summary>The state failed validation after an action: a rules bug. Carries the action number for replaying.</summary>
public sealed class StateViolationException : Exception
{
    public StateViolationException(int actionNumber, GameAction action, IReadOnlyList<string> violations)
        : base($"State invalid after action {actionNumber} ({action}):\n" + string.Join("\n", violations))
    {
        ActionNumber = actionNumber;
        Action = action;
        Violations = violations;
    }

    public int ActionNumber { get; }
    public GameAction Action { get; }
    public IReadOnlyList<string> Violations { get; }
}
