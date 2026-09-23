using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Catan.AI;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The playable game screen (1600×900). Runs the game loop: you in a random seat through a <see cref="HumanAgent"/>,
/// RandomBots in the others, with a pause after each bot move. Everything drawn comes from your seat's PlayerView.
/// Board moves: click a highlighted spot. Other moves: the buttons under the board. Esc returns to the menu.
/// </summary>
public partial class GameScreen : Control
{
    // Layout (1600×900): players on the left, trades and log on the right, your hand and buttons under the board.
    public static readonly Rect2 PlayersRect = new(12, 12, 290, 876);
    public static readonly Rect2 TradesRect = new(1238, 12, 350, 420);
    public static readonly Rect2 LogRect = new(1238, 444, 350, 444);
    public static readonly Rect2 HandRect = new(314, 718, 912, 170);
    public static readonly Rect2 BoardRect = new(314, 12, 912, 694);

    private GameOptions _options = null!;
    private GameSetup _setup = null!;
    private GameRunner _runner = null!;
    private HumanAgent _human = null!;
    private GameText _text = null!;
    private readonly CancellationTokenSource _quit = new();
    private readonly Rng _discardRng = new(1);

    private BoardView _board = null!;
    private PlayersPanel _players = null!;
    private LogPanel _log = null!;
    private ActionPanel _actions = null!;
    private int _loggedEvents;

    /// <summary>When a board click matches several legal actions (e.g. two players to rob), they're offered as buttons.</summary>
    private List<GameAction>? _choices;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // let clicks reach the board (panels still stop the ones over them)
        var background = new ColorRect { Color = Ui.Sea, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        _options = GameSession.Options;
        _setup = GameSetup.Create(_options.Seed ?? (ulong)System.Random.Shared.NextInt64());
        _text = new GameText(_setup.Colors, _setup.HumanSeat);
        _human = new HumanAgent("You", TimeSpan.FromSeconds(_options.ResponseWindowSeconds));
        _human.PromptChanged += OnPromptChanged;

        var agents = new IPlayerAgent[GameConstants.PlayerCount];
        for (int seat = 0; seat < agents.Length; seat++)
            agents[seat] = seat == _setup.HumanSeat ? _human : new RandomBot(_setup.BotSeed + (ulong)seat);
        var state = new GameState(BoardGenerator.Balanced(new Rng(_setup.BoardSeed)), _options.ToSettings());
        _runner = new GameRunner(state, agents, new RngChance(_setup.ChanceSeed));
        _runner.ActionApplied += (_, _) => Refresh();

        _board = new BoardView { HoverTargetsOnly = true };
        _board.Setup(BoardRect);
        _board.Clicked += OnBoardClicked;
        AddChild(_board);

        AddChild(Ui.Panel("Players", PlayersRect, out var players));
        _players = new PlayersPanel(players, _setup);

        AddChild(Ui.Panel("Trades", TradesRect, out var trades));
        trades.AddChild(Ui.Label("Trade offers get their own panel in step 9.\nFor now, answer offers with the buttons below the board.", 14, Ui.MutedText));

        AddChild(Ui.Panel("Log", LogRect, out var log));
        _log = new LogPanel(log, new Vector2(320, 380));
        _log.AddMuted($"Game seed {_setup.Seed}. You are {_setup.HumanColor}, seat {_setup.HumanSeat + 1} in turn order.");

        AddChild(Ui.Panel("Your turn", HandRect, out var hand));
        _actions = new ActionPanel(hand);

        Refresh();
        CallDeferred(MethodName.StartGame);
    }

    private void StartGame() => RunGame();

    /// <summary>The game loop. Bots answer instantly; after their moves we pause so you can follow along.</summary>
    private async void RunGame()
    {
        try
        {
            while (!_runner.IsOver && !_quit.IsCancellationRequested)
            {
                bool humanActing = Rules.ActingSeat(_runner.State) == _setup.HumanSeat;
                int before = _runner.Actions.Count;
                await _runner.StepAsync(_quit.Token);
                if (!humanActing && _runner.Actions.Count > before && !_runner.IsOver && _options.BotDelaySeconds > 0)
                    await ToSignal(GetTree().CreateTimer(_options.BotDelaySeconds), SceneTreeTimer.SignalName.Timeout);
            }
        }
        catch (OperationCanceledException)
        {
            return; // leaving the screen
        }
        catch (Exception ex)
        {
            _log.Add($"[color=#b00000]Error: {ex.Message}[/color]");
            GD.PushError(ex.ToString());
        }
        Refresh();
    }

    public override void _Process(double delta)
    {
        // Keep the countdown on an optional answer ticking.
        if (_human.Prompt is { IsOptional: true, Deadline: { } deadline })
            _actions.SetPrompt(OptionalPromptText(_human.Prompt, deadline));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            Leave();
    }

    public override void _ExitTree() => _quit.Cancel();

    private void Leave()
    {
        _quit.Cancel();
        GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
    }

    private void OnPromptChanged()
    {
        _choices = null;
        Refresh();
    }

    // ---- Drawing everything from your view ----

    private void Refresh()
    {
        if (!IsInsideTree())
            return;
        var state = _runner.State;
        var view = PlayerView.From(state, _setup.HumanSeat, _runner.Log);
        _board.Show(view, _setup.Colors);
        _players.Update(view);

        var seen = _runner.Log.For(_setup.HumanSeat);
        for (; _loggedEvents < seen.Count; _loggedEvents++)
            if (seen[_loggedEvents] is not TurnEnded)
                _log.Add(_text.Describe(seen[_loggedEvents]));

        var prompt = _human.Prompt;
        _board.SetTargets(prompt is { IsOptional: false } ? prompt.Legal.Select(TargetOf).Where(t => t.Kind != HitKind.None).Distinct() : Array.Empty<BoardHit>());
        _actions.SetHand(HandText(view));
        _actions.SetPrompt(PromptText(view, prompt));
        _actions.SetButtons(Buttons(view, prompt));
    }

    private string PromptText(PlayerView v, HumanPrompt? prompt)
    {
        if (_runner.IsOver)
            return v.Winner == _setup.HumanSeat ? "You won!" : v.Winner >= 0 ? $"{_text.Seat(v.Winner)} won the game." : "The game ended in a draw.";
        if (prompt is null)
            return v.ActingSeat >= 0 ? $"{_text.Seat(v.ActingSeat)} is playing…" : "";
        if (prompt.IsOptional)
            return OptionalPromptText(prompt, prompt.Deadline ?? DateTime.UtcNow);
        if (_choices is not null)
            return "Choose:";
        return v.Phase switch
        {
            Phase.SetupSettlement => "Place a settlement: click a highlighted corner.",
            Phase.SetupRoad => "Place a road next to it: click a highlighted edge.",
            Phase.PreRoll => "Your turn: roll the dice (or play a development card first).",
            Phase.Main => "Build (click a highlighted spot), trade, play a card, or end your turn.",
            Phase.Discard => $"A 7 was rolled and you hold more than 7 cards: discard {v.DiscardOwed[_setup.HumanSeat]}.",
            Phase.MoveRobber => "Move the robber: click a highlighted hex.",
            Phase.RoadBuilding => "Road Building: place a free road.",
            _ => "",
        };
    }

    private string OptionalPromptText(HumanPrompt prompt, DateTime deadline)
    {
        int seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
        return $"{_text.Seat(prompt.View.CurrentPlayer)} is trading. Answer below ({seconds} s), or Skip.";
    }

    private static string HandText(PlayerView v)
    {
        var dev = new List<string>();
        for (int t = 0; t < GameConstants.DevCardTypeCount; t++)
            if (v.DevHand[t] > 0)
            {
                int fresh = v.DevBoughtThisTurn[t];
                dev.Add($"{v.DevHand[t]} {GameText.DevCard((DevCardType)t)}" + (fresh > 0 ? $" ({fresh} new)" : ""));
            }
        return $"Your cards: {GameText.Cards(v.HandSet)}" + (dev.Count > 0 ? $"    Development cards: {string.Join(", ", dev)}" : "");
    }

    // ---- Your moves ----

    private IEnumerable<(string, string?, Action)> Buttons(PlayerView v, HumanPrompt? prompt)
    {
        if (prompt is null || _runner.IsOver)
            yield break;

        if (_choices is not null)
        {
            foreach (var choice in _choices)
                yield return (_text.Describe(choice), null, () => Submit(choice));
            yield return ("Cancel", null, () => { _choices = null; Refresh(); });
            yield break;
        }

        if (!prompt.IsOptional && v.Phase == Phase.Discard)
        {
            yield return ($"Discard {v.DiscardOwed[_setup.HumanSeat]} at random", "A proper discard picker comes in step 6.",
                () => Submit(Rules.RandomDiscard(v, _discardRng)));
            yield break;
        }

        foreach (var action in prompt.Legal)
            if (TargetOf(action).Kind == HitKind.None)
                yield return (ButtonText(v, action), null, () => Submit(action));

        if (prompt.IsOptional)
            yield return ("Skip", "Don't answer now.", () => _human.Skip());
    }

    /// <summary>Trade answers name the offer they answer.</summary>
    private string ButtonText(PlayerView v, GameAction a)
    {
        if (a.Type is ActionType.AcceptOffer or ActionType.DeclineOffer && a.Seat == _setup.HumanSeat && a.Target >= 0 && a.Target2 < 0)
        {
            var offer = v.Offers[a.Target];
            string what = offer.IsCounter
                ? $"{_text.Seat(offer.From)}'s counter ({GameText.Cards(offer.Give)} for your {GameText.Cards(offer.Get)})"
                : $"{_text.Seat(offer.From)}: their {GameText.Cards(offer.Give)} for your {GameText.Cards(offer.Get)}";
            return $"{(a.Type == ActionType.AcceptOffer ? "Accept" : "Decline")} {what}";
        }
        return _text.Describe(a);
    }

    private void OnBoardClicked(BoardHit hit)
    {
        if (_human.Prompt is not { IsOptional: false } prompt)
            return;
        var matches = prompt.Legal.Where(a => TargetOf(a) == hit).ToList();
        if (matches.Count == 1)
            Submit(matches[0]);
        else if (matches.Count > 1)
        {
            _choices = matches;
            Refresh();
        }
    }

    /// <summary>Checks the move with the engine first; the runner would reject an illegal one and stop the game.</summary>
    private void Submit(GameAction action)
    {
        if (!Rules.IsLegal(_runner.State, action, out string reason))
        {
            _actions.SetPrompt($"Can't do that: {reason}");
            return;
        }
        _choices = null;
        _human.Submit(action);
    }

    private static BoardHit TargetOf(GameAction a) => a.Type switch
    {
        ActionType.BuildRoad => BoardHit.Edge(a.Target),
        ActionType.BuildSettlement or ActionType.BuildCity => BoardHit.Vertex(a.Target),
        ActionType.MoveRobber => BoardHit.Hex(a.Target),
        _ => BoardHit.None,
    };
}
