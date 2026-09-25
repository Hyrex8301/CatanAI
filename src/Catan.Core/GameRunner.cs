namespace Catan.Core;

/// <summary>
/// Runs a game between agents. Each step:
/// 1. If opponents may answer open trades (Main), ask all of them at once, each with a view built from the same state
///    snapshot, so nobody sees another's answer first. Apply their answers in turn order after the current player.
///    An optional seat that passes (null) is asked again only after the game has moved on.
/// 2. Find the acting seat, build its view and legal list, await its decision, check IsLegal, apply, log events, record.
/// Actions are recorded in the order they were applied, which is all a replay needs.
/// Awaits deliberately continue on the caller's context (no ConfigureAwait(false)): in Godot every action is then applied
/// on the main thread, the same thread that draws the state.
/// </summary>
public sealed class GameRunner
{
    private readonly IReadOnlyList<IPlayerAgent> _agents;
    private readonly RecordingChance _chance;
    private readonly bool _validate;
    private readonly bool _startedFresh;
    private readonly List<GameAction> _legal = new();
    private readonly List<GameEvent> _events = new();
    private readonly List<GameAction> _actions = new();
    private int _optionalAskedAt = -1;

    /// <param name="validate">Run <see cref="StateValidator"/> after every action and throw on a violation.</param>
    public GameRunner(GameState state, IReadOnlyList<IPlayerAgent> agents, IChance chance, bool validate = false)
        : this(state, agents, new RecordingChance(chance), validate,
               state.ComputeHash() == new GameState(state.Board, state.Settings).ComputeHash())
    {
    }

    private GameRunner(GameState state, IReadOnlyList<IPlayerAgent> agents, RecordingChance chance, bool validate, bool startedFresh)
    {
        if (agents.Count != GameConstants.PlayerCount)
            throw new ArgumentException($"A game needs {GameConstants.PlayerCount} agents.", nameof(agents));
        State = state;
        _agents = agents;
        _chance = chance;
        _validate = validate;
        _startedFresh = startedFresh;
    }

    /// <summary>The saved position this game started from (null for a game started from a new board).</summary>
    public Position? Start { get; private set; }

    /// <summary>
    /// A game that starts from a saved position: the log opens with <see cref="PositionStarted"/> (hand sizes, bank, and each
    /// seat's own hand) so bots can track hands from there, and <see cref="ToRecord"/> stores the position, so the game saves
    /// and replays like any other. Throws <see cref="ArgumentException"/> for a damaged position.
    /// </summary>
    public static GameRunner FromPosition(Position start, IReadOnlyList<IPlayerAgent> agents, IChance chance, bool validate = false)
    {
        var runner = new GameRunner(start.ToState(), agents, new RecordingChance(chance), validate, startedFresh: false) { Start = start };
        runner.Log.Add(PositionStarted.Of(runner.State, start.HandsKnown));
        return runner;
    }

    /// <summary>
    /// Continues a saved game: replays the record (with its recorded outcomes) and returns a runner positioned right after it,
    /// with the same actions, log and outcome history, so <see cref="ToRecord"/> later covers the whole game.
    /// New random outcomes come from <paramref name="continueWith"/>. Throws <see cref="ReplayException"/> if the record
    /// doesn't replay cleanly to its final hash.
    /// </summary>
    public static GameRunner Resume(GameRecord record, IReadOnlyList<IPlayerAgent> agents, IChance continueWith, bool validate = false)
    {
        if (record.FormatVersion != GameRecord.CurrentFormatVersion)
            throw new ReplayException($"Unsupported record format {record.FormatVersion}.");
        GameState state;
        try
        {
            state = record.Start?.ToState() ?? new GameState(Board.FromLayout(record.Board), record.Settings);
        }
        catch (ArgumentException ex)
        {
            throw new ReplayException($"The saved starting position can't be used: {ex.Message}");
        }
        var runner = new GameRunner(state, agents, new RecordingChance(continueWith, record.Chance), validate, startedFresh: record.Start is null)
        {
            Start = record.Start,
        };
        if (record.Start is not null)
            runner.Log.Add(PositionStarted.Of(state, record.Start.HandsKnown));

        var replay = new ReplayChance(record.Chance);
        for (int i = 0; i < record.Actions.Count; i++)
        {
            var action = record.Actions[i];
            if (!Rules.IsLegal(state, action, out string reason))
                throw new ReplayException($"Action {i} ({action.Type} by seat {action.Seat}) is illegal: {reason}");
            runner._events.Clear();
            Rules.Apply(state, action, replay, runner._events);
            runner._actions.Add(action);
            runner.Log.AddRange(runner._events);
        }
        if (replay.Remaining != 0)
            throw new ReplayException($"The record has {replay.Remaining} unused random outcomes.");
        if (record.FinalHash is { } hash && GameRecord.HashText(state.ComputeHash()) != hash)
            throw new ReplayException($"Final hash {GameRecord.HashText(state.ComputeHash())} differs from the recorded {hash}.");
        return runner;
    }

    /// <summary>
    /// The game so far as a record that replays to the current state. Only for games started from a new game
    /// (a record stores the board and settings, not an arbitrary starting position).
    /// </summary>
    public GameRecord ToRecord(ulong? seed = null)
    {
        if (!_startedFresh && Start is null)
            throw new InvalidOperationException("Only games started from a new GameState or a saved position can be recorded.");
        return new GameRecord
        {
            Seed = seed,
            Settings = State.Settings,
            Board = State.Board.ToLayout(),
            Players = _agents.Select(a => a.Name).ToArray(),
            Actions = _actions.ToArray(),
            Chance = _chance.Log.ToArray(),
            FinalHash = GameRecord.HashText(State.ComputeHash()),
            Start = Start,
        };
    }

    public GameState State { get; }
    public EventLog Log { get; } = new();

    /// <summary>Every applied action, in order.</summary>
    public IReadOnlyList<GameAction> Actions => _actions;

    public bool IsOver => State.Phase == Phase.GameOver;

    /// <summary>Raised after each action is applied (and validated), with the events it produced.</summary>
    public event Action<GameAction, IReadOnlyList<GameEvent>>? ActionApplied;

    /// <summary>Plays until the game ends.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!IsOver)
            await StepAsync(ct);
    }

    /// <summary>Optional trade answers (if any are due), then one decision by the acting seat. Returns false once the game is over.</summary>
    public async Task<bool> StepAsync(CancellationToken ct = default)
    {
        if (IsOver)
            return false;
        await AskOptionalSeatsAsync(ct);
        if (IsOver)
            return false;

        int seat = Rules.ActingSeat(State);
        Rules.GetLegalActions(State, seat, _legal);
        var view = PlayerView.From(State, seat, Log);
        var action = await _agents[seat].DecideAsync(view, _legal.ToArray(), ct);
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
        await Task.WhenAll(asks.Select(a => a.Answer));

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
        ActionApplied?.Invoke(action, _events);
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
