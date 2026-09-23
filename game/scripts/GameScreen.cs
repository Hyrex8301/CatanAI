using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catan.AI;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The playable game screen (1600×900), laid out like colonist.io. Runs the game loop: you in a random seat through a
/// <see cref="HumanAgent"/>, SmartBots in the others, with a pause after each bot move. Everything drawn comes from your
/// seat's PlayerView. Board moves: click a highlighted spot. Trades: the Trade button or a card in your hand opens the trade
/// window. Other moves: the buttons bottom right. Saves: the Save button, plus an autosave at the end of each of your turns.
/// Esc closes the trade window or cancels a choice.
/// </summary>
public partial class GameScreen : Control
{
    // Layout (1600×900): board top left with the bank in its corner, log and player cards down the right,
    // your hand and the action buttons along the bottom.
    public static readonly Rect2 BoardRect = new(10, 10, 1150, 648);
    public static readonly Rect2 BankRect = new(868, 18, 284, 76);
    public static readonly Rect2 TradesRect = new(10, 196, 580, 460); // the window shrinks to fit, bottom edge fixed
    public static readonly Rect2 LogRect = new(1172, 10, 418, 462);
    public static readonly Rect2 PlayersRect = new(1172, 482, 418, 408);
    public static readonly Rect2 HandRect = new(10, 712, 560, 178);
    public static readonly Rect2 ActionsRect = new(580, 712, 580, 178);
    public static readonly Rect2 ChoicesRect = new(966, 250, 190, 402);
    public static readonly Rect2 DiscardRect = new(22, 520, 360, 132);
    private const float PlayerCardHeight = 96;

    private GameOptions _options = null!;
    private GameSetup _setup = null!;
    private GameRunner _runner = null!;
    private HumanAgent _human = null!;
    private GameText _text = null!;
    private readonly CancellationTokenSource _quit = new();

    private BoardView _board = null!;
    private BankView _bank = null!;
    private readonly PlayerCard[] _playerCards = new PlayerCard[GameConstants.PlayerCount];
    private HandBar _hand = null!;
    private Label _prompt = null!;
    private LogPanel _log = null!;
    private ActionPanel _actions = null!;
    private ActionBar _bar = null!;
    private BuildMode _mode;
    private Control _discardPanel = null!;
    private bool _shownOnce;
    private TradeWindow _trades = null!;
    private readonly Queue<GameAction> _queued = new();
    private bool _tradeOpen;
    private readonly CardPicker _discard = new();
    private CardPickerView _discardView = null!;
    private PlayerView? _view;
    private int _loggedEvents;

    /// <summary>When a board click matches several legal actions (e.g. two players to rob), they're offered as buttons.</summary>
    private List<GameAction>? _choices;

    /// <summary>Developer screenshots: the human seat is played by a bot until this many actions have been applied.</summary>
    private int _autoplayActions;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // let clicks reach the board (panels still stop the ones over them)
        var background = new ColorRect { Color = Ui.Sea, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        _options = GameSession.Options;
        if (ulong.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SEED"), out ulong devSeed))
            _options = _options with { Seed = devSeed };
        _ = int.TryParse(System.Environment.GetEnvironmentVariable("CATAN_AUTOPLAY"), out _autoplayActions);
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

        _bank = new BankView { Position = BankRect.Position, Size = BankRect.Size };
        AddChild(_bank);

        // Opponents in the order they play after you, then you at the bottom.
        var order = HudModel.Opponents(_setup.HumanSeat).Append(_setup.HumanSeat).ToArray();
        float gap = (PlayersRect.Size.Y - order.Length * PlayerCardHeight) / (order.Length - 1);
        for (int i = 0; i < order.Length; i++)
        {
            var card = new PlayerCard
            {
                Position = PlayersRect.Position + new Vector2(0, i * (PlayerCardHeight + gap)),
                Size = new Vector2(PlayersRect.Size.X, PlayerCardHeight),
            };
            _playerCards[order[i]] = card;
            AddChild(card);
        }

        _hand = new HandBar(this, HandRect);
        _hand.ResourceClicked += OnHandResourceClicked;

        // What the game wants from you, in a banner over the bottom of the board.
        var banner = new CenterContainer
        {
            Position = new Vector2(BoardRect.Position.X, BoardRect.End.Y + 6), Size = new Vector2(BoardRect.Size.X, 44), MouseFilter = MouseFilterEnum.Ignore,
        };
        var pill = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        pill.AddThemeStyleboxOverride("panel", Ui.PanelStyle(new Color(0.1f, 0.12f, 0.16f, 0.85f), radius: 18, margin: 16));
        _prompt = Ui.Label("", 17, Colors.White);
        pill.AddChild(_prompt);
        banner.AddChild(pill);
        AddChild(banner);

        _trades = new TradeWindow(this, TradesRect, _text, _setup.HumanSeat, _setup.Colors, Submit, SubmitAll, WhyNot, () => _human.Skip(),
            () => { _tradeOpen = false; Refresh(); });

        AddChild(Ui.Panel(null, LogRect, out var log));
        var header = new HBoxContainer();
        var title = Ui.Label("Game log", 16, Ui.MutedText);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var save = new Button { Text = "Save" };
        save.Pressed += SaveGame;
        var menu = new Button { Text = "Menu" };
        menu.Pressed += Leave;
        header.AddChild(save);
        header.AddChild(menu);
        log.AddChild(header);
        _log = new LogPanel(log, new Vector2(390, 390));
        _log.AddMuted(resume is null
            ? $"Game seed {_setup.Seed}. You are {_setup.HumanColor}, seat {_setup.HumanSeat + 1} in turn order."
            : $"Continuing a saved game (seed {_setup.Seed}). You are {_setup.HumanColor}.");
        if (loadError is not null)
            _log.Add($"[color=#b00000]That save couldn't be loaded ({loadError}). Started a new game instead.[/color]");

        _bar = new ActionBar(this, ActionsRect);
        _bar.Clicked += OnBarClicked;
        _actions = new ActionPanel(this, ChoicesRect);
        _discardPanel = Ui.Panel("Discard: click cards in your hand, or use − / +", DiscardRect, out var discard);
        _discardPanel.Visible = false;
        AddChild(_discardPanel);
        _discardView = new CardPickerView(_discard);
        discard.AddChild(_discardView);
        // Only the buttons depend on the discard pick ("Discard 3 of 4"); a full Refresh here would loop (it resets limits).
        _discard.Changed += () =>
        {
            if (_view is not null)
                _actions.SetButtons(Buttons(_view, _human.Prompt));
        };

        Refresh();
        CallDeferred(MethodName.StartGame);
        DevScreenshot();
    }

    /// <summary>A new game from the setup, or a saved game continued with dice seeded from the save point (no rerolling by reloading).</summary>
    private GameRunner CreateRunner(GameRecord? resume)
    {
        int turnOffset = resume?.Actions.Count ?? 0;
        var agents = new IPlayerAgent[GameConstants.PlayerCount];
        for (int seat = 0; seat < agents.Length; seat++)
            agents[seat] = seat == _setup.HumanSeat ? HumanSeatAgent()
                : new SmartBot(BotWeightsFile(), SmartBotSettings.Play, _setup.BotSeed + (ulong)seat + (ulong)turnOffset * 7919);

        if (resume is not null)
            return GameRunner.Resume(resume, agents, new RngChance(_setup.ChanceSeed ^ (ulong)resume.Actions.Count * 0x9E3779B97F4A7C15UL));
        var state = new GameState(BoardGenerator.Balanced(new Rng(_setup.BoardSeed)), _options.ToSettings());
        return new GameRunner(state, agents, new RngChance(_setup.ChanceSeed));
    }

    private IPlayerAgent HumanSeatAgent() => _autoplayActions <= 0 ? _human
        : new AutoplayAgent(new SmartBot(BotWeightsFile(), SmartBotSettings.Training, _setup.BotSeed + 101), _human, () => Autoplaying);

    private bool Autoplaying => _runner is not null && _runner.Actions.Count < _autoplayActions;

    /// <summary>The trained weights shipped with the game (game/bots/best.json), or the hand-set defaults if there are none yet.</summary>
    private static BotWeights BotWeightsFile()
    {
        string path = ProjectSettings.GlobalizePath("res://bots/best.json");
        return System.IO.File.Exists(path) ? BotWeights.Load(path) : new BotWeights();
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
                if (!humanActing && _runner.Actions.Count > before && !_runner.IsOver && _options.BotDelaySeconds > 0 && !Autoplaying)
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
        if (!Autoplaying && events.Any(e => e is TurnEnded t && t.Seat == _setup.HumanSeat || e is GameEnded))
            GameSession.Store.Autosave(_runner.ToRecord(_setup.Seed));
        Refresh();
    }

    public override void _Process(double delta)
    {
        // Keep the countdown on an optional answer ticking.
        if (_human.Prompt is { IsOptional: true, Deadline: { } deadline })
        {
            _prompt.Text = CountdownText(deadline);
            _trades.Tick(deadline, _human.ResponseWindow);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Space rolls the dice, or ends your turn. Handled before buttons see it (a focused button would take Space too).
        if (@event is InputEventKey { Keycode: Key.Space, Pressed: true, Echo: false } && _view is { } v && _human.Prompt is { IsOptional: false } prompt)
        {
            var legal = prompt.Legal;
            if (ActionBarModel.State(BarItem.Roll, v, legal).Enabled)
                OnBarClicked(BarItem.Roll);
            else if (ActionBarModel.State(BarItem.EndTurn, v, legal).Enabled)
                OnBarClicked(BarItem.EndTurn);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;
        if (_choices is not null)
            _choices = null;
        else if (_mode != BuildMode.None)
            _mode = BuildMode.None;
        else
            _tradeOpen = false;
        Refresh();
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
        _mode = BuildMode.None;
        _discard.Clear();
        Refresh();
        if (_queued.Count > 0 && _human.Prompt is { IsOptional: false })
            CallDeferred(MethodName.SubmitQueued);
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
        _bank.Show(view);
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            _playerCards[seat].Show(HudModel.Seat(view, seat, _setup.Colors));
        _hand.Update(HudModel.Hand(view));

        var seen = _runner.Log.For(_setup.HumanSeat);
        bool newRoll = false;
        for (; _loggedEvents < seen.Count; _loggedEvents++)
        {
            newRoll |= seen[_loggedEvents] is DiceRolled;
            if (seen[_loggedEvents] is not TurnEnded)
                _log.Add(_text.Describe(seen[_loggedEvents]));
        }
        var roll = ActionBarModel.Roll(view);
        _bar.Dice.Show(roll, roll is null ? "No rolls yet" : $"{_text.Seat(roll.Seat)} rolled {roll.Total}",
            roll is null ? Ui.PanelBorder : Ui.SeatColor(_setup.Colors[roll.Seat]), animate: newRoll && _shownOnce);
        _shownOnce = true;

        var prompt = _human.Prompt;
        var legal = prompt is { IsOptional: false } ? prompt.Legal : null;
        if (legal is null || view.Phase != Phase.Main)
            _mode = BuildMode.None;
        _bar.Update(view, legal, _mode, Ui.SeatColor(_setup.HumanColor));
        _board.SetTargets(legal is null ? Array.Empty<BoardHit>()
            : ActionBarModel.BoardActions(_mode, view, legal).Select(TargetOf).Distinct());
        _trades.Update(view, prompt);
        // The trade window: open on request during your turn, and on its own when a bot's offer waits for your answer.
        if (!CanTrade(view, prompt))
            _tradeOpen = false;
        _trades.Visible = _tradeOpen || prompt is { IsOptional: true };

        bool discarding = prompt is { IsOptional: false } && view.Phase == Phase.Discard;
        if (discarding)
            _discard.SetLimits(view.Hand, view.DiscardOwed[_setup.HumanSeat]);
        _discardPanel.Visible = discarding;
        _prompt.Text = PromptText(view, prompt);
        _prompt.GetParent<Control>().Visible = _prompt.Text.Length > 0;
        _actions.SetButtons(Buttons(view, prompt));
    }

    private bool CanTrade(PlayerView v, HumanPrompt? prompt) =>
        prompt is { IsOptional: false } && v.Phase == Phase.Main && v.CurrentPlayer == _setup.HumanSeat;

    private string PromptText(PlayerView v, HumanPrompt? prompt)
    {
        if (_runner.IsOver)
            return v.Winner == _setup.HumanSeat ? "You won!" : v.Winner >= 0 ? $"{_text.Seat(v.Winner)} won the game." : "The game ended in a draw.";
        if (prompt is null)
            return v.ActingSeat >= 0 ? $"{_text.Seat(v.ActingSeat)} is playing…" : "";
        if (prompt.IsOptional)
            return CountdownText(prompt.Deadline ?? DateTime.UtcNow);
        if (_choices is not null)
            return "Choose one of the options on the right.";
        return v.Phase switch
        {
            Phase.SetupSettlement => "Place a settlement: click a highlighted corner.",
            Phase.SetupRoad => "Place a road next to it: click a highlighted edge.",
            Phase.PreRoll => "Your turn: click the dice to roll (or play a development card first).",
            Phase.Main => _mode switch
            {
                BuildMode.Road => "Click a highlighted edge to build a road (Esc to cancel).",
                BuildMode.Settlement => "Click a highlighted corner to build a settlement (Esc to cancel).",
                BuildMode.City => "Click one of your settlements to make it a city (Esc to cancel).",
                _ => "Build, trade, play a card, or end your turn.",
            },
            Phase.Discard => $"A 7 was rolled: discard {v.DiscardOwed[_setup.HumanSeat]} cards (click cards in your hand).",
            Phase.MoveRobber => "Move the robber: click a highlighted hex.",
            Phase.RoadBuilding => "Road Building: place a free road.",
            _ => "",
        };
    }

    private string CountdownText(DateTime deadline)
    {
        int seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
        return $"{_text.Seat(_runner.State.CurrentPlayer)} wants to trade: answer in the trade window ({seconds} s).";
    }

    // ---- Your moves ----

    private static bool IsPlayerTrade(ActionType type) => type is ActionType.OfferTrade or ActionType.EditOffer or ActionType.CounterOffer
        or ActionType.AcceptOffer or ActionType.DeclineOffer or ActionType.ConfirmTrade or ActionType.CancelOffer;

    private IEnumerable<(string, string?, Action)> Buttons(PlayerView v, HumanPrompt? prompt)
    {
        if (prompt is null || prompt.IsOptional || _runner.IsOver)
            yield break; // optional answers live in the trade window

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
            yield return ($"Discard {_discard.Total} of {owed}", "Click cards in your hand, or use − / +.",
                () => Submit(new GameAction(ActionType.Discard, _setup.HumanSeat, Give: _discard.Cards)));
            yield break;
        }

        // Moves without a proper control yet (dev card plays); the action bar and trade window have the rest.
        foreach (var action in prompt.Legal)
            if (TargetOf(action).Kind == HitKind.None && !IsPlayerTrade(action.Type)
                && action.Type is not (ActionType.RollDice or ActionType.EndTurn or ActionType.BuyDevCard or ActionType.BankTrade))
                yield return (_text.Describe(action), null, () => Submit(action));
    }

    private void OnBarClicked(BarItem item)
    {
        if (_human.Prompt is not { IsOptional: false } prompt)
            return;
        switch (item)
        {
            case BarItem.Trade:
                _tradeOpen = !_tradeOpen;
                _mode = BuildMode.None;
                Refresh();
                break;
            case BarItem.Road or BarItem.Settlement or BarItem.City:
                var mode = ActionBarModel.ModeOf(item);
                _mode = _mode == mode ? BuildMode.None : mode;
                _tradeOpen = false;
                Refresh();
                break;
            default:
                var type = item switch { BarItem.BuyDev => ActionType.BuyDevCard, BarItem.Roll => ActionType.RollDice, _ => ActionType.EndTurn };
                var action = prompt.Legal.FirstOrDefault(a => a.Type == type);
                if (action.Type == type)
                    Submit(action);
                break;
        }
    }

    /// <summary>A resource card in your hand: picks it for a discard, adds it to a counter you're building, or opens a trade offering it.</summary>
    private void OnHandResourceClicked(int resource)
    {
        if (_view is not { } v)
            return;
        var prompt = _human.Prompt;
        if (prompt is { IsOptional: false } && v.Phase == Phase.Discard)
        {
            _discard.Add(resource);
            return;
        }
        if (CanTrade(v, prompt) || prompt is { IsOptional: true })
        {
            _tradeOpen = CanTrade(v, prompt);
            _trades.OfferWith(resource);
            Refresh();
        }
    }

    private void OnBoardClicked(BoardHit hit)
    {
        if (_human.Prompt is not { IsOptional: false } prompt)
            return;
        if (_view is null)
            return;
        var matches = ActionBarModel.BoardActions(_mode, _view, prompt.Legal).Where(a => TargetOf(a) == hit).ToList();
        if (matches.Count == 1)
            Submit(matches[0]);
        else if (matches.Count > 1)
        {
            _choices = matches;
            Refresh();
        }
    }

    /// <summary>Checks the move with the engine first (the runner would reject an illegal one and stop the game).</summary>
    /// <summary>The engine's reason a move is illegal right now, or null if it's fine (the trade window checks as you build).</summary>
    private string? WhyNot(GameAction action) => Rules.IsLegal(_runner.State, action, out string reason) ? null : reason;

    /// <summary>Several moves in a row (a bank trade of several lots): the first now, the rest as the game asks again.</summary>
    private void SubmitAll(List<GameAction> actions)
    {
        _queued.Clear();
        for (int i = 1; i < actions.Count; i++)
            _queued.Enqueue(actions[i]);
        if (actions.Count > 0 && !Submit(actions[0]))
            _queued.Clear();
    }

    private void SubmitQueued()
    {
        if (_queued.Count > 0 && _human.Prompt is { IsOptional: false } && !Submit(_queued.Dequeue()))
            _queued.Clear();
    }

    private bool Submit(GameAction action)
    {
        if (!Rules.IsLegal(_runner.State, action, out string reason))
        {
            _prompt.Text = $"Can't do that: {reason}";
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

    // ---- Developer screenshots ----

    /// <summary>
    /// Set CATAN_SHOT to a .png path to save a screenshot after CATAN_SHOT_AFTER seconds (default 3) and quit. CATAN_SEED picks
    /// the game; CATAN_AUTOPLAY=N lets a bot play your seat, without pauses, for the first N actions (to reach mid-game).
    /// </summary>
    private async void DevScreenshot()
    {
        string? path = System.Environment.GetEnvironmentVariable("CATAN_SHOT");
        if (string.IsNullOrEmpty(path))
            return;
        double after = double.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SHOT_AFTER"), out double s) ? s : 3;
        await ToSignal(GetTree().CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    /// <summary>Plays the human seat with a bot while <see cref="_useBot"/> says so, then hands over to the human.</summary>
    private sealed class AutoplayAgent : IPlayerAgent
    {
        private readonly IPlayerAgent _bot, _human;
        private readonly Func<bool> _useBot;

        public AutoplayAgent(IPlayerAgent bot, IPlayerAgent human, Func<bool> useBot) => (_bot, _human, _useBot) = (bot, human, useBot);

        public string Name => _human.Name;

        public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
            (_useBot() ? _bot : _human).DecideAsync(view, legal, ct);

        public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
            (_useBot() ? _bot : _human).RespondAsync(view, legal, ct);
    }
}
