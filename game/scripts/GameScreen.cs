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
/// Board moves: click a highlighted spot. Trades: the panel on the right. Other moves: the buttons under the board.
/// Saves: the Save button, plus an autosave at the end of each of your turns. Esc returns to the menu.
/// </summary>
public partial class GameScreen : Control
{
    // Layout (1600×900): players on the left, trades and log on the right, your hand and buttons under the board.
    public static readonly Rect2 PlayersRect = new(12, 12, 290, 876);
    public static readonly Rect2 TradesRect = new(1238, 12, 350, 520);
    public static readonly Rect2 LogRect = new(1238, 544, 350, 344);
    public static readonly Rect2 HandRect = new(314, 718, 912, 170);
    public static readonly Rect2 BoardRect = new(314, 12, 912, 694);

    private GameOptions _options = null!;
    private GameSetup _setup = null!;
    private GameRunner _runner = null!;
    private HumanAgent _human = null!;
    private GameText _text = null!;
    private readonly CancellationTokenSource _quit = new();

    private BoardView _board = null!;
    private PlayersPanel _players = null!;
    private LogPanel _log = null!;
    private ActionPanel _actions = null!;
    private TradePanel _trades = null!;
    private readonly CardPicker _discard = new();
    private CardPickerView _discardView = null!;
    private PlayerView? _view;
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
        var resume = GameSession.Resume;
        GameSession.Resume = null;
        _setup = GameSetup.Create(resume?.Seed ?? _options.Seed ?? (ulong)System.Random.Shared.NextInt64());
        _text = new GameText(_setup.Colors, _setup.HumanSeat);
        _human = new HumanAgent("You", TimeSpan.FromSeconds(_options.ResponseWindowSeconds));
        _human.PromptChanged += OnPromptChanged;
        string? loadError = null;
        try
        {
            _runner = CreateRunner(resume);
        }
        catch (ReplayException ex)
        {
            // A damaged or incompatible save: say so and start a new game instead.
            loadError = ex.Message;
            resume = null;
            _setup = GameSetup.Create(_options.Seed ?? (ulong)System.Random.Shared.NextInt64());
            _text = new GameText(_setup.Colors, _setup.HumanSeat);
            _runner = CreateRunner(null);
        }
        _runner.ActionApplied += OnActionApplied;

        _board = new BoardView { HoverTargetsOnly = true };
        _board.Setup(BoardRect);
        _board.Clicked += OnBoardClicked;
        AddChild(_board);

        AddChild(Ui.Panel("Players", PlayersRect, out var players));
        _players = new PlayersPanel(players, _setup);

        AddChild(Ui.Panel("Trades", TradesRect, out var trades));
        _trades = new TradePanel(trades, _text, _setup.HumanSeat, Submit, () => _human.Skip());

        AddChild(Ui.Panel("Log", LogRect, out var log));
        var gameButtons = new HBoxContainer();
        var save = new Button { Text = "Save game" };
        save.Pressed += SaveGame;
        var menu = new Button { Text = "Menu" };
        menu.Pressed += Leave;
        gameButtons.AddChild(save);
        gameButtons.AddChild(menu);
        log.AddChild(gameButtons);
        _log = new LogPanel(log, new Vector2(320, 230));
        _log.AddMuted(resume is null
            ? $"Game seed {_setup.Seed}. You are {_setup.HumanColor}, seat {_setup.HumanSeat + 1} in turn order."
            : $"Continuing a saved game (seed {_setup.Seed}). You are {_setup.HumanColor}.");
        if (loadError is not null)
            _log.Add($"[color=#b00000]That save couldn't be loaded ({loadError}). Started a new game instead.[/color]");

        AddChild(Ui.Panel("Your turn", HandRect, out var hand));
        _actions = new ActionPanel(hand);
        _discardView = new CardPickerView(_discard);
        // Only the buttons depend on the discard pick ("Discard 3 of 4"); a full Refresh here would loop (it resets limits).
        _discard.Changed += () =>
        {
            if (_view is not null)
                _actions.SetButtons(Buttons(_view, _human.Prompt));
        };

        Refresh();
        CallDeferred(MethodName.StartGame);
    }

    /// <summary>A new game from the setup, or a saved game continued with dice seeded from the save point (no rerolling by reloading).</summary>
    private GameRunner CreateRunner(GameRecord? resume)
    {
        int turnOffset = resume?.Actions.Count ?? 0;
        var agents = new IPlayerAgent[GameConstants.PlayerCount];
        for (int seat = 0; seat < agents.Length; seat++)
            agents[seat] = seat == _setup.HumanSeat ? _human : new RandomBot(_setup.BotSeed + (ulong)seat + (ulong)turnOffset * 7919);

        if (resume is not null)
            return GameRunner.Resume(resume, agents, new RngChance(_setup.ChanceSeed ^ (ulong)resume.Actions.Count * 0x9E3779B97F4A7C15UL));
        var state = new GameState(BoardGenerator.Balanced(new Rng(_setup.BoardSeed)), _options.ToSettings());
        return new GameRunner(state, agents, new RngChance(_setup.ChanceSeed));
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

    private void OnActionApplied(GameAction action, IReadOnlyList<GameEvent> events)
    {
        // Autosave at the end of each of your turns, and when the game ends.
        if (events.Any(e => e is TurnEnded t && t.Seat == _setup.HumanSeat || e is GameEnded))
            GameSession.Store.Autosave(_runner.ToRecord(_setup.Seed));
        Refresh();
    }

    public override void _Process(double delta)
    {
        // Keep the countdown on an optional answer ticking.
        if (_human.Prompt is { IsOptional: true, Deadline: { } deadline })
            _actions.SetPrompt(CountdownText(deadline));
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

    private void SaveGame()
    {
        string path = GameSession.Store.Save(_runner.ToRecord(_setup.Seed), DateTime.Now);
        _log.AddMuted($"Saved as {System.IO.Path.GetFileNameWithoutExtension(path)}.");
    }

    private void OnPromptChanged()
    {
        _choices = null;
        _discard.Clear();
        Refresh();
    }

    // ---- Drawing everything from your view ----

    private void Refresh()
    {
        if (!IsInsideTree())
            return;
        var state = _runner.State;
        var view = PlayerView.From(state, _setup.HumanSeat, _runner.Log);
        _view = view;
        _board.Show(view, _setup.Colors);
        _players.Update(view);

        var seen = _runner.Log.For(_setup.HumanSeat);
        for (; _loggedEvents < seen.Count; _loggedEvents++)
            if (seen[_loggedEvents] is not TurnEnded)
                _log.Add(_text.Describe(seen[_loggedEvents]));

        var prompt = _human.Prompt;
        _board.SetTargets(prompt is { IsOptional: false } ? prompt.Legal.Select(TargetOf).Where(t => t.Kind != HitKind.None).Distinct() : Array.Empty<BoardHit>());
        _trades.Update(view, prompt);

        bool discarding = prompt is { IsOptional: false } && view.Phase == Phase.Discard;
        if (discarding)
            _discard.SetLimits(view.Hand, view.DiscardOwed[_setup.HumanSeat]);
        _actions.SetExtra(discarding ? _discardView : null);
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
            return CountdownText(prompt.Deadline ?? DateTime.UtcNow);
        if (_choices is not null)
            return "Choose:";
        return v.Phase switch
        {
            Phase.SetupSettlement => "Place a settlement: click a highlighted corner.",
            Phase.SetupRoad => "Place a road next to it: click a highlighted edge.",
            Phase.PreRoll => "Your turn: roll the dice (or play a development card first).",
            Phase.Main => "Build (click a highlighted spot), trade (right panel), play a card, or end your turn.",
            Phase.Discard => $"A 7 was rolled and you hold more than 7 cards: choose {v.DiscardOwed[_setup.HumanSeat]} to discard.",
            Phase.MoveRobber => "Move the robber: click a highlighted hex.",
            Phase.RoadBuilding => "Road Building: place a free road.",
            _ => "",
        };
    }

    private string CountdownText(DateTime deadline)
    {
        int seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
        return $"{_text.Seat(_runner.State.CurrentPlayer)} is trading: answer in the Trades panel ({seconds} s).";
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

    private static bool IsPlayerTrade(ActionType type) => type is ActionType.OfferTrade or ActionType.EditOffer or ActionType.CounterOffer
        or ActionType.AcceptOffer or ActionType.DeclineOffer or ActionType.ConfirmTrade or ActionType.CancelOffer;

    private IEnumerable<(string, string?, Action)> Buttons(PlayerView v, HumanPrompt? prompt)
    {
        if (prompt is null || prompt.IsOptional || _runner.IsOver)
            yield break; // optional answers live in the Trades panel

        if (_choices is not null)
        {
            foreach (var choice in _choices)
                yield return (_text.Describe(choice), null, () => Submit(choice));
            yield return ("Cancel", null, () => { _choices = null; Refresh(); });
            yield break;
        }

        if (v.Phase == Phase.Discard)
        {
            int owed = v.DiscardOwed[_setup.HumanSeat];
            yield return ($"Discard {_discard.Total} of {owed}", "Pick the cards with − / + first.",
                () => Submit(new GameAction(ActionType.Discard, _setup.HumanSeat, Give: _discard.Cards)));
            yield break;
        }

        foreach (var action in prompt.Legal)
            if (TargetOf(action).Kind == HitKind.None && !IsPlayerTrade(action.Type))
                yield return (_text.Describe(action), null, () => Submit(action));
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

    /// <summary>Checks the move with the engine first (the runner would reject an illegal one and stop the game).</summary>
    private bool Submit(GameAction action)
    {
        if (!Rules.IsLegal(_runner.State, action, out string reason))
        {
            _actions.SetPrompt($"Can't do that: {reason}");
            return false;
        }
        _choices = null;
        return _human.Submit(action);
    }

    private static BoardHit TargetOf(GameAction a) => a.Type switch
    {
        ActionType.BuildRoad => BoardHit.Edge(a.Target),
        ActionType.BuildSettlement or ActionType.BuildCity => BoardHit.Vertex(a.Target),
        ActionType.MoveRobber => BoardHit.Hex(a.Target),
        _ => BoardHit.None,
    };
}
