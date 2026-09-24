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
/// The playable game screen (1600×900), laid out like colonist.io: the board on the sea; the log, bank and player rows down
/// the right; your hand bottom left and the square action buttons bottom right under a status box. Trade offers pop up in
/// the board's top-right corner; proposing a trade opens a panel that grows out of your hand. The dice sit beside the row
/// of the player who rolled. You play a random seat through a <see cref="HumanAgent"/>, SmartBots the others, with a pause
/// after each bot move. Everything drawn comes from your seat's PlayerView. Esc closes a panel or cancels a choice.
/// </summary>
public partial class GameScreen : Control
{
    // Layout (1600×900).
    private static readonly Rect2 BoardRect = new(140, 18, 1010, 744);
    private static readonly Rect2 LogRect = new(1258, 4, 338, 378);
    private static readonly Rect2 BankRect = new(1258, 388, 338, 70);
    private const float RowTop = 464, RowHeight = 86, RowGap = 4;
    private static readonly Rect2 YouRect = new(1258, 734, 338, 162);
    private static readonly Rect2 HandRect = new(4, 802, 740, 94);
    private static readonly Vector2 ButtonsAt = new(750, 808);
    private static readonly Rect2 StatusRect = new(916, 758, 250, 44);
    private static readonly Rect2 TimerRect = new(1170, 758, 80, 44);
    private static readonly Rect2 ProposalRect = new(4, 554, 740, 242);
    private static readonly Rect2 BankButtonRect = new(750, 636, 80, 80);
    private static readonly Rect2 PeopleButtonRect = new(750, 720, 80, 80);
    private static readonly Vector2 PopupsTopRight = new(1250, 8);
    private static readonly Rect2 DevRect = new(4, 560, 540, 236);
    private static readonly Rect2 DiscardRect = new(4, 648, 740, 148);
    private static readonly Vector2 VictimCenter = new(645, 390);

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
    private LogPanel _log = null!;
    private ActionBar _bar = null!;
    private DiceView _dice = null!;
    private DevCardPopup _devPopup = null!;
    private VictimPopup _victims = null!;
    private TradeProposal _proposal = null!;
    private TradePopups _popups = null!;
    private DiscardPanel _discardUi = null!;
    private Animator _animator = null!;
    private AnimationCues _cues = null!;
    private GameOverScreen _gameOver = null!;
    private PopupMenu _menu = null!;
    private readonly CardPicker _discard = new();
    private readonly Queue<GameAction> _queued = new();

    /// <summary>Each bot seat's agent (thinking on a worker thread); null for your seat.</summary>
    private readonly BackgroundAgent?[] _bots = new BackgroundAgent?[GameConstants.PlayerCount];

    private PlayerView? _view;
    private BuildMode _mode;
    private bool _tradeOpen, _shownOnce, _countsPending;
    private int _loggedEvents;
    private (int Player, int Turn) _turnKey = (-1, -1);
    private DateTime _turnStarted = DateTime.UtcNow;

    /// <summary>When a board click matches several legal actions (e.g. two players to rob), they're offered as buttons.</summary>
    private List<GameAction>? _choices;

    /// <summary>Click-to-build: the build clicked once (its shadow shows on the board); clicking the same spot again builds it.</summary>
    private GameAction? _pending;

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

        AddTopLeftButtons();

        // Right column: log, bank, the opponents in the order they play after you, then you.
        _log = new LogPanel(this, LogRect, _setup.Colors, _setup.HumanSeat, _text);
        _log.AddNote(resume is null
            ? $"Game seed {_setup.Seed}. You are {_setup.HumanColor}, seat {_setup.HumanSeat + 1} in turn order."
            : $"Continuing a saved game (seed {_setup.Seed}). You are {_setup.HumanColor}.");
        if (loadError is not null)
            _log.AddNote($"That save couldn't be loaded ({loadError}). Started a new game instead.", Ui.Bad);
        _bank = new BankView { Position = BankRect.Position, Size = BankRect.Size };
        AddChild(_bank);
        int row = 0;
        foreach (int seat in HudModel.Opponents(_setup.HumanSeat))
        {
            _playerCards[seat] = new PlayerCard { Position = new Vector2(LogRect.Position.X, RowTop + row++ * (RowHeight + RowGap)), Size = new Vector2(LogRect.Size.X, RowHeight) };
            AddChild(_playerCards[seat]);
        }
        _playerCards[_setup.HumanSeat] = new PlayerCard { Position = YouRect.Position, Size = YouRect.Size, Big = true };
        AddChild(_playerCards[_setup.HumanSeat]);

        // Bottom: hand, buttons, status; dice beside whoever rolled.
        _hand = new HandBar(this, HandRect);
        _hand.ResourceClicked += OnHandResourceClicked;
        _bar = new ActionBar(this, ButtonsAt, StatusRect, TimerRect, _setup.HumanColor);
        _bar.Clicked += OnBarClicked;
        _dice = new DiceView { Visible = false };
        _dice.Clicked += () => OnBarClicked(BarItem.Roll);
        AddChild(_dice);

        // Trading.
        _proposal = new TradeProposal(this, ProposalRect, BankButtonRect, PeopleButtonRect, _setup.HumanColor, _setup.HumanSeat, _text, Submit, SubmitAll, WhyNot);
        _proposal.Sent += () => { _tradeOpen = false; Refresh(); };
        _popups = new TradePopups(this, PopupsTopRight, _setup.Colors, _setup.HumanSeat, _text, Submit, WhyNot,
            edit: (slot, o) => { _proposal.StartEdit(slot, o); _tradeOpen = true; Refresh(); },
            counter: (slot, o) => { _proposal.StartCounter(slot, o); Refresh(); });

        // Sevens and dev cards: discard from your hand, pick who to rob, play a card from your hand.
        _discardUi = new DiscardPanel(this, DiscardRect, _discard,
            () => Submit(new GameAction(ActionType.Discard, _setup.HumanSeat, Give: _discard.Cards)));
        // Picking a card moves it out of the hand bar (a full Refresh here would loop: it resets the picker's limits).
        _discard.Changed += () => { if (_view is not null) _hand.Update(HudModel.Hand(_view, _discard.Cards)); };
        _victims = new VictimPopup(this, VictimCenter, _setup.Colors, _text, () => { _choices = null; Refresh(); });
        _devPopup = new DevCardPopup(this, DevRect, _setup.HumanSeat, Submit);
        _devPopup.Closed += () => { _devPopup.Close(); Refresh(); };
        _hand.DevClicked += type => { CloseProposal(); _devPopup.Open(type); Refresh(); };

        // Animations play over everything else.
        _cues = new AnimationCues(_text, _setup.HumanSeat);
        _animator = new Animator();
        AddChild(_animator);
        _animator.Setup(_board, Where, new Vector2(BoardRect.GetCenter().X, 14));
        _animator.Finished += () => { if (_countsPending && _view is not null) ShowCounts(_view); };

        _gameOver = new GameOverScreen();
        _gameOver.NewGame += () => { _quit.Cancel(); GameSession.Resume = null; GetTree().ReloadCurrentScene(); };
        _gameOver.MainMenu += Leave;
        AddChild(_gameOver);

        Refresh();
        CallDeferred(MethodName.StartGame);
        DevShots.Run(this);
    }

    /// <summary>Settings (Save / Main menu) and fullscreen, top left.</summary>
    private void AddTopLeftButtons()
    {
        var menu = new PopupMenu();
        menu.AddItem("Save game", 0);
        menu.AddItem("Main menu", 1);
        menu.AddItem("Game results", 2);
        menu.SetItemDisabled(2, true);
        _menu = menu;
        menu.IdPressed += id =>
        {
            if (id == 0)
                SaveGame();
            else if (id == 1)
                Leave();
            else
                ShowResults();
        };
        AddChild(menu);
        var gear = new ActionTile { Position = new Vector2(8, 8), Size = new Vector2(44, 44), DrawIcon = (c, at, s, ink) => Gear(c, at, s) };
        gear.Set(true, "Settings");
        gear.Clicked += () => menu.Popup(new Rect2I(8, 56, 160, 0));
        AddChild(gear);
        var full = new ActionTile { Position = new Vector2(8, 58), Size = new Vector2(44, 44), DrawIcon = (c, at, s, ink) => FullscreenIcon(c, at, s) };
        full.Set(true, "Fullscreen");
        full.Clicked += () => DisplayServer.WindowSetMode(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen
            ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
        AddChild(full);
    }

    /// <summary>A new game from the setup, or a saved game continued with dice seeded from the save point (no rerolling by reloading).</summary>
    private GameRunner CreateRunner(GameRecord? resume)
    {
        int turnOffset = resume?.Actions.Count ?? 0;
        var agents = new IPlayerAgent[GameConstants.PlayerCount];
        for (int seat = 0; seat < agents.Length; seat++)
            agents[seat] = seat == _setup.HumanSeat ? HumanSeatAgent() : _bots[seat] = new BackgroundAgent(CreateBot(seat, turnOffset));

        if (resume is not null)
            return GameRunner.Resume(resume, agents, new RngChance(_setup.ChanceSeed ^ (ulong)resume.Actions.Count * 0x9E3779B97F4A7C15UL));
        var state = new GameState(BoardGenerator.Balanced(new Rng(_setup.BoardSeed)), _options.ToSettings());
        return new GameRunner(state, agents, new RngChance(_setup.ChanceSeed));
    }

    /// <summary>
    /// A bot seat: SmartBot with the bundled weights. For trying the search bot before it's the default, CATAN_BOT=search
    /// (optionally CATAN_THINK_MS) plays SearchBot with the bundled calibration on all but two threads.
    /// </summary>
    private IPlayerAgent CreateBot(int seat, int turnOffset)
    {
        ulong seed = _setup.BotSeed + (ulong)seat + (ulong)turnOffset * 7919;
        if (System.Environment.GetEnvironmentVariable("CATAN_BOT") == "search")
        {
            int ms = int.TryParse(System.Environment.GetEnvironmentVariable("CATAN_THINK_MS"), out int t) ? t : 1000;
            var calibration = WinModel.Load(ProjectSettings.GlobalizePath("res://bots/calibration.json"));
            return new SearchBot(BotWeightsFile(), calibration, new SearchSettings { ThinkMs = ms }, seed);
        }
        return new SmartBot(BotWeightsFile(), SmartBotSettings.Play, seed);
    }

    private IPlayerAgent HumanSeatAgent() => _autoplayActions <= 0 ? _human
        : new AutoplayAgent(new SmartBot(BotWeightsFile(), SmartBotSettings.Training, _setup.BotSeed + 101), _human, () => Autoplaying);

    private bool Autoplaying => _runner is not null && _runner.Actions.Count < _autoplayActions;

    /// <summary>Developer screenshots: CATAN_ANIMATE=1 keeps animations and bot pauses on while autoplaying.</summary>
    private static readonly bool ForceAnimate = System.Environment.GetEnvironmentVariable("CATAN_ANIMATE") == "1";

    private bool Fast => Autoplaying && !ForceAnimate;

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
                var stepClock = System.Diagnostics.Stopwatch.StartNew();
                await _runner.StepAsync(_quit.Token);
                if (!humanActing && _runner.Actions.Count > before && !_runner.IsOver && _options.BotDelaySeconds > 0 && !Fast)
                {
                    // The pause tops up quick decisions only: a bot that thought for a while has already made you wait.
                    double pause = _options.BotDelaySeconds - stepClock.Elapsed.TotalSeconds;
                    if (pause > 0.02)
                        await ToSignal(GetTree().CreateTimer(pause), SceneTreeTimer.SignalName.Timeout);
                    // Let flying cards land before the next bot move.
                    while (_animator.Busy && !_quit.IsCancellationRequested)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return; // leaving the screen
        }
        catch (Exception ex)
        {
            _log.AddNote($"Error: {ex.Message}", Ui.Bad);
            GD.PushError(ex.ToString());
        }
        Refresh();
        if (_runner.IsOver && !_quit.IsCancellationRequested)
        {
            // Let the last move's animations and toasts play, then show the results.
            await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
            ShowResults();
        }
    }

    private void ShowResults()
    {
        if (!_runner.IsOver || _quit.IsCancellationRequested)
            return;
        _menu.SetItemDisabled(2, false);
        _gameOver.Show(GameOverSummary.From(_runner.State, _runner.Log.For(_setup.HumanSeat)), _setup.Colors, _setup.HumanSeat);
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
        if (_human.Prompt is { IsOptional: true, Deadline: { } deadline })
        {
            _popups.Tick(deadline, _human.ResponseWindow);
            _bar.SetTimer(Clock(Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds)));
        }
        else if (!_runner.IsOver)
            _bar.SetTimer(Clock((DateTime.UtcNow - _turnStarted).TotalSeconds));
        // "Orange is thinking…" appears while a bot works on a decision (it thinks on another thread).
        if (_human.Prompt is null && _view is { } v && !_runner.IsOver)
        {
            var (status, help) = StatusText(v, null);
            _bar.SetStatus(status, help);
        }
    }

    private static string Clock(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

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
        if (_pending is not null)
            ClearPending();
        else if (_choices is not null)
            _choices = null;
        else if (_devPopup.Visible)
            _devPopup.Close();
        else if (_mode != BuildMode.None)
            _mode = BuildMode.None;
        else
            CloseProposal();
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
        _log.AddNote($"Saved as {System.IO.Path.GetFileNameWithoutExtension(path)}.");
    }

    private void ClearPending()
    {
        _pending = null;
        _board.SetPending(BoardHit.None);
    }

    private void OnPromptChanged()
    {
        _choices = null;
        ClearPending();
        _mode = BuildMode.None;
        _discard.Clear();
        Refresh();
        if (_queued.Count > 0 && _human.Prompt is { IsOptional: false })
            CallDeferred(MethodName.SubmitQueued);
    }

    private void CloseProposal()
    {
        _tradeOpen = false;
        _proposal.Reset();
    }

    // ---- Drawing everything from your view ----

    private void Refresh()
    {
        if (!IsInsideTree())
            return;
        var view = PlayerView.From(_runner.State, _setup.HumanSeat, _runner.Log);
        _view = view;
        if ((view.CurrentPlayer, view.TurnNumber) != _turnKey)
        {
            _turnKey = (view.CurrentPlayer, view.TurnNumber);
            _turnStarted = DateTime.UtcNow;
        }
        _board.Show(view, _setup.Colors);
        bool discarding = _human.Prompt is { IsOptional: false } && view.Phase == Phase.Discard;
        if (discarding)
            _discard.SetLimits(view.Hand, view.DiscardOwed[_setup.HumanSeat]);

        var seen = _runner.Log.For(_setup.HumanSeat);
        bool newRoll = false;
        bool animate = _shownOnce && !Fast;
        var cues = new List<Cue>();
        for (; _loggedEvents < seen.Count; _loggedEvents++)
        {
            newRoll |= seen[_loggedEvents] is DiceRolled;
            _log.Add(seen[_loggedEvents]);
            cues.AddRange(_cues.For(seen[_loggedEvents], view)); // always, so the cue builder keeps track of rolls and settlements
        }

        var prompt = _human.Prompt;
        var legal = prompt is { IsOptional: false } ? prompt.Legal : null;
        if (legal is null || view.Phase != Phase.Main)
            _mode = BuildMode.None;
        bool answering = prompt is { IsOptional: true };
        if (view.CurrentPlayer != _setup.HumanSeat || view.Phase != Phase.Main) // not a passing moment with no prompt (right after a submit)
            _tradeOpen = false;
        _proposal.Update(view, prompt);
        _proposal.Visible = _tradeOpen || (_proposal.IsCounter && answering);
        _popups.Update(view, prompt);
        _bar.Update(view, legal, _mode, _proposal.Visible);
        ShowDice(view, legal, newRoll && _shownOnce);
        if (animate)
            _animator.Play(cues);
        // Hands, player rows and the bank change when the flying cards land, not before.
        if (_animator.Busy)
            _countsPending = true;
        else
            ShowCounts(view);
        _shownOnce = true;
        _board.SetTargets(legal is null ? Array.Empty<BoardHit>()
            : ActionBarModel.BoardActions(_mode, view, legal).Select(TargetOf).Distinct());
        var quick = legal is null || _mode != BuildMode.None ? new List<GameAction>() : ActionBarModel.QuickBuilds(view, legal).ToList();
        if (_pending is { } p && !quick.Contains(p))
            ClearPending();
        _board.SetQuickTargets(quick.Select(a => (TargetOf(a), ActionBarModel.PieceOf(a))), Ui.SeatColor(_setup.HumanColor));

        _discardUi.Show(discarding, view.DiscardOwed[_setup.HumanSeat]);
        _victims.Show(legal is null ? null : _choices, view, Submit);
        _devPopup.Update(view, legal);
        var (status, help) = StatusText(view, prompt);
        _bar.SetStatus(status, help);
    }

    /// <summary>The dice sit beside the row of the player who rolled (or yours, glowing, when it's your roll).</summary>
    private void ShowDice(PlayerView v, IReadOnlyList<GameAction>? legal, bool animate)
    {
        var rollState = ActionBarModel.State(BarItem.Roll, v, legal);
        var roll = ActionBarModel.Roll(v);
        int seat = rollState.Enabled ? _setup.HumanSeat : roll?.Seat ?? -1;
        _dice.Visible = seat >= 0;
        if (seat < 0)
            return;
        var row = _playerCards[seat];
        float y = row.Position.Y + row.Size.Y / 2 - _dice.Size.Y / 2;
        _dice.Position = new Vector2(LogRect.Position.X - _dice.Size.X - 4, Math.Min(y, StatusRect.Position.Y - _dice.Size.Y - 4));
        _dice.Set(rollState.Enabled, rollState.Tooltip);
        _dice.Show(rollState.Enabled ? null : roll, roll is null ? "" : $"{_text.Seat(roll.Seat)} rolled {roll.Total}", animate);
    }

    /// <summary>Your hand, the player rows and the bank (held back while cards are still flying).</summary>
    private void ShowCounts(PlayerView view)
    {
        _countsPending = false;
        _bank.Show(view);
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            _playerCards[seat].Show(HudModel.Seat(view, seat, _setup.Colors));
        bool discarding = _human.Prompt is { IsOptional: false } && view.Phase == Phase.Discard;
        _hand.Update(HudModel.Hand(view, discarding ? _discard.Cards : default));
    }

    /// <summary>Screen points for flying cards: a hex's center, a bot's avatar, your hand, or the bank row.</summary>
    private Vector2 Where(Spot spot) => spot.Kind switch
    {
        SpotKind.Hex => _board.HexCenter(spot.Id),
        SpotKind.Seat when spot.Id == _setup.HumanSeat => HandRect.Position + new Vector2(150, HandRect.Size.Y / 2),
        SpotKind.Seat => _playerCards[spot.Id].Position + new Vector2(60, _playerCards[spot.Id].Size.Y / 2 + 6),
        _ => BankRect.Position + new Vector2(BankRect.Size.X / 2, BankRect.Size.Y / 2),
    };

    private bool CanTrade(PlayerView v, HumanPrompt? prompt) =>
        prompt is { IsOptional: false } && v.Phase == Phase.Main && v.CurrentPlayer == _setup.HumanSeat;

    /// <summary>The short status ("Your Turn") and a longer hint for its tooltip.</summary>
    private (string, string) StatusText(PlayerView v, HumanPrompt? prompt)
    {
        if (_runner.IsOver)
            return (v.Winner == _setup.HumanSeat ? "You won!" : v.Winner >= 0 ? $"{_text.Seat(v.Winner)} won" : "Draw", "The game is over.");
        if (prompt is null)
            return (v.ActingSeat == _setup.HumanSeat ? "Your Turn" : v.ActingSeat >= 0 && _bots[v.ActingSeat]?.Thinking == true ? $"{_text.Seat(v.ActingSeat)} is thinking…" : v.ActingSeat >= 0 ? $"{_text.Seat(v.ActingSeat)}'s Turn" : "", "Waiting for the bots.");
        if (prompt.IsOptional)
            return ("Answer Trade", $"{_text.Seat(v.CurrentPlayer)} wants to trade: accept, decline or counter in the offer card (top right).");
        if (_choices is not null)
            return ("Choose Who to Rob", "Click a player in the popup, or Esc to pick a different hex.");
        return v.Phase switch
        {
            Phase.SetupSettlement => ("Place Settlement", "Click a highlighted corner."),
            Phase.SetupRoad => ("Place Road", "Click a highlighted edge next to your new settlement."),
            Phase.PreRoll => ("Roll the Dice", "Click the dice (or press Space). You may play a development card first."),
            Phase.Main => _mode switch
            {
                BuildMode.Road => ("Place Road", "Click a highlighted edge (Esc to cancel)."),
                BuildMode.Settlement => ("Place Settlement", "Click a highlighted corner (Esc to cancel)."),
                BuildMode.City => ("Place City", "Click one of your settlements (Esc to cancel)."),
                _ when _pending is { } p => ($"Click Again: {ActionBarModel.PieceOf(p)}", $"Click the same spot again to build your {ActionBarModel.PieceOf(p).ToString().ToLowerInvariant()}, or Esc to cancel."),
                _ => ("Your Turn", "Build (click a spot on the board, or a build button), trade, play a card, or end your turn (Space)."),
            },
            Phase.Discard => ($"Discard {v.DiscardOwed[_setup.HumanSeat]} Cards", "A 7 was rolled and you hold more than 7 cards: click cards in your hand to pick them."),
            Phase.MoveRobber => ("Move the Robber", "Click a highlighted hex."),
            Phase.RoadBuilding => ("Place Free Road", "Road Building: click a highlighted edge."),
            _ => ("", ""),
        };
    }

    // ---- Your moves ----

    private void OnBarClicked(BarItem item)
    {
        if (_human.Prompt is not { IsOptional: false } prompt)
            return;
        switch (item)
        {
            case BarItem.Trade:
                ClearPending();
                if (_tradeOpen)
                    CloseProposal();
                else
                    _tradeOpen = true;
                _mode = BuildMode.None;
                Refresh();
                break;
            case BarItem.Road or BarItem.Settlement or BarItem.City:
                ClearPending();
                var mode = ActionBarModel.ModeOf(item);
                _mode = _mode == mode ? BuildMode.None : mode;
                CloseProposal();
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

    /// <summary>A resource card in your hand: picks it for a discard, or gives it in the trade you're proposing (opening the panel).</summary>
    private void OnHandResourceClicked(int resource)
    {
        if (_view is not { } v)
            return;
        var prompt = _human.Prompt;
        if (prompt is { IsOptional: false } && v.Phase == Phase.Discard)
            _discard.Add(resource);
        else if (CanTrade(v, prompt))
        {
            _tradeOpen = true;
            _proposal.AddGive(resource);
            Refresh();
        }
        else if (prompt is { IsOptional: true } && _proposal.IsCounter)
            _proposal.AddGive(resource);
    }

    private void OnBoardClicked(BoardHit hit)
    {
        if (_human.Prompt is not { IsOptional: false } prompt || _view is null)
            return;
        var matches = ActionBarModel.BoardActions(_mode, _view, prompt.Legal).Where(a => TargetOf(a) == hit).ToList();
        if (matches.Count == 1)
            Submit(matches[0]);
        else if (matches.Count > 1)
        {
            _choices = matches;
            Refresh();
        }
        else if (_mode == BuildMode.None && ActionBarModel.QuickBuilds(_view, prompt.Legal).Where(a => TargetOf(a) == hit).ToList() is [var quick])
        {
            // Click-to-build: the first click shows a shadow of the piece, a second click on the same spot builds it.
            if (_pending == quick)
                Submit(quick);
            else
            {
                _pending = quick;
                _board.SetPending(hit);
                CloseProposal();
                Refresh();
            }
        }
        else if (_pending is not null)
        {
            ClearPending();
            Refresh();
        }
    }

    /// <summary>The engine's reason a move is illegal right now, or null if it's fine (the trade panel checks as you build).</summary>
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

    /// <summary>Checks the move with the engine first (the runner would reject an illegal one and stop the game).</summary>
    private bool Submit(GameAction action)
    {
        if (!Rules.IsLegal(_runner.State, action, out string reason))
        {
            _bar.SetStatus("Can't do that", reason, error: true);
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

    // ---- Icons for the top-left buttons ----

    private static void Gear(CanvasItem c, Vector2 at, float s)
    {
        var ink = Ui.ButtonInk;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            c.DrawLine(at + dir * s * 0.28f, at + dir * s * 0.46f, ink, s * 0.16f);
        }
        c.DrawCircle(at, s * 0.33f, ink);
        c.DrawCircle(at, s * 0.14f, Ui.ButtonBlue);
    }

    private static void FullscreenIcon(CanvasItem c, Vector2 at, float s)
    {
        var ink = Ui.ButtonInk;
        float r = s * 0.4f, l = s * 0.18f;
        foreach (var (x, y) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var corner = at + new Vector2(x * r, y * r);
            c.DrawLine(corner, corner - new Vector2(x * l, 0), ink, 3);
            c.DrawLine(corner, corner - new Vector2(0, y * l), ink, 3);
        }
    }

    // ---- Developer screenshots (see DevShots) ----


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
