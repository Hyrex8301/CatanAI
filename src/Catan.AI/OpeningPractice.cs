using Catan.Core;

namespace Catan.AI;

/// <summary>
/// The whole opening, for placement practice: settlement and road for everyone in snake order (1-2-3-4, then 4-3-2-1).
/// Bots place at their turns (SmartBot with the given weights: deterministic, so retrying a board with the same choices
/// replays it exactly); you place at yours with <see cref="Place"/>. Keeps every move, so the finished opening can be
/// continued as a normal game (<see cref="ToRecord"/>: the opening has no dice, so the record has no random outcomes).
/// No UI: the practice screen and a future coach mode drive it.
/// </summary>
public sealed class OpeningPractice
{
    private readonly BotWeights _weights;
    private readonly ulong _botSeed;
    private readonly List<GameAction> _actions = new();
    private readonly List<GameAction> _legal = new();
    private readonly RngChance _chance = new(1); // the opening never draws on chance

    public OpeningPractice(BotWeights weights, Board board, GameSettings settings, int seat, ulong botSeed)
    {
        _weights = weights;
        _botSeed = botSeed;
        State = new GameState(board, settings);
        Seat = seat;
    }

    public GameState State { get; }

    /// <summary>Your seat.</summary>
    public int Seat { get; }

    public IReadOnlyList<GameAction> Actions => _actions;

    /// <summary>True once everyone has placed both settlements and roads.</summary>
    public bool Done => State.Phase is not (Phase.SetupSettlement or Phase.SetupRoad);

    public bool YourTurn => !Done && Rules.ActingSeat(State) == Seat;

    /// <summary>Which of your settlements comes next (1 or 2), counting the one being placed.</summary>
    public int Round => Enumerable.Range(0, Topology.VertexCount).Count(v => State.VertexOwner[v] == Seat) + (State.Phase == Phase.SetupSettlement ? 1 : 0);

    /// <summary>The moves you may make now (settlement spots, or roads next to your new settlement).</summary>
    public IReadOnlyList<GameAction> Legal()
    {
        if (!YourTurn)
            return Array.Empty<GameAction>();
        Rules.GetLegalActions(State, Seat, _legal);
        return _legal.ToList();
    }

    /// <summary>One bot placement (when it isn't your turn and the opening isn't over): the move it made, or null.</summary>
    public GameAction? BotStep()
    {
        if (Done || YourTurn)
            return null;
        int acting = Rules.ActingSeat(State);
        var bot = new SmartBot(_weights, SmartBotSettings.Play, _botSeed * 31 + (ulong)acting * 7 + (ulong)_actions.Count);
        Rules.GetLegalActions(State, acting, _legal);
        var move = bot.DecideAsync(PlayerView.From(State, acting), _legal, default).GetAwaiter().GetResult();
        Apply(move);
        return move;
    }

    /// <summary>Your placement. Throws if it isn't legal (the UI only offers legal spots).</summary>
    public void Place(GameAction move)
    {
        if (!YourTurn || move.Seat != Seat || !Rules.IsLegal(State, move, out string reason))
            throw new InvalidOperationException($"Not a legal placement now: {(YourTurn ? "illegal move" : "not your turn")}.");
        Apply(move);
    }

    private void Apply(GameAction move)
    {
        Rules.Apply(State, move, _chance);
        _actions.Add(move);
    }

    /// <summary>The opening so far as a game record, to continue as a normal game (seed: the game setup's master seed).</summary>
    public GameRecord ToRecord(ulong seed, IReadOnlyList<string> players) => new()
    {
        Seed = seed,
        Settings = State.Settings,
        Board = State.Board.ToLayout(),
        Players = players.ToArray(),
        Actions = _actions.ToArray(),
        Chance = Array.Empty<ChanceOutcome>(),
        FinalHash = GameRecord.HashText(State.ComputeHash()),
    };
}
