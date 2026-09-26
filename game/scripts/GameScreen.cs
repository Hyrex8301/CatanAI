using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catan.AI;
using Catan.AI.Talk;
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
    private static readonly Rect2 LogRect = new(1258, 4, 338, 200);
    private static readonly Rect2 ChatRect = new(1258, 208, 338, 174);
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
    private ChatPanel _chat = null!;
    private TableTalk _talk = null!;
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
    private readonly HelpPanel _help = new();
    private readonly CardPicker _discard = new();
    private readonly Queue<GameAction> _queued = new();

    /// <summary>Each bot seat's agent (thinking on a worker thread); null for your seat.</summary>
    private readonly BackgroundAgent?[] _bots = new BackgroundAgent?[GameConstants.PlayerCount];

    // Layers pinned to the window's edges (design coordinates inside; moved by Relayout when the window is bigger).
    private Control _rightLayer = null!, _youLayer = null!, _handLayer = null!, _barLayer = null!, _centerLayer = null!;
    private Vector2 _extra;

    private PlayerView? _view;
    private BuildMode _mode;
    private bool _tradeOpen, _shownOnce, _countsPending;
    private int _loggedEvents;
    private readonly TurnClock _clock = new();

    /// <summary>Your time ran out and the game made a move for you; cleared when any move is applied.</summary>
    private bool _timeUpHandled;

    /// <summary>Position practice: each play of your turn, graded in the background; the review shows when you end the turn.</summary>
    private TurnReview _review = null!;
    private readonly List<(GameAction Move, Task<DecisionGrade> Grade)> _plays = new();
    private int _ungraded;
    private bool _practiceOver;
    private bool _paused;

    /// <summary>Game review: every decision you made (what you saw, what you could do, what you did), graded after the game.</summary>
    private readonly List<Decision> _decisions = new();
    private GameReviewPanel _reviewPanel = null!;
    private Task<GameReview>? _gameReview;

    /// <summary>A move the game made for you (time ran out): not yours to be graded on.</summary>
    private bool _autoMove;

    /// <summary>Position practice, until you end the practice turn (no clock, no time-outs until then).</summary>
    private bool Deciding => _start?.Kind == ScenarioKind.Decision && !_practiceOver;

    /// <summary>Your open offers that every opponent declined, and when that was first seen (they close a few seconds later).</summary>
    private readonly Dictionary<(int Slot, TradeOffer Offer), DateTime> _declinedOffers = new();

    private const double DeclinedOfferCloseSeconds = 3;

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
        AddChild(new SeaView());

        _options = GameSession.Options;
        if (ulong.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SEED"), out ulong devSeed))
            _options = _options with { Seed = devSeed };
        _ = int.TryParse(System.Environment.GetEnvironmentVariable("CATAN_AUTOPLAY"), out _autoplayActions);
        var resume = GameSession.Resume;
        GameSession.Resume = null;
        _start = GameSession.StartPosition;
        GameSession.StartPosition = null;
        if (_start is null && System.Environment.GetEnvironmentVariable("CATAN_START_POSITION") is { Length: > 0 } devPosition)
            _start = Catan.Core.Position.FromJson(System.IO.File.ReadAllText(devPosition)); // developer screenshots
        _setup = GameSetup.Create(resume?.Seed ?? _options.Seed ?? NewSeed());
        _text = new GameText(_setup.Colors, _setup.HumanSeat);
        _human = new HumanAgent("You", TimeSpan.FromSeconds(_options.ResponseWindowSeconds));
        _human.PromptChanged += OnPromptChanged;
        _talk = CreateTalk(resume);
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
            _setup = GameSetup.Create(_options.Seed ?? NewSeed());
            _text = new GameText(_setup.Colors, _setup.HumanSeat);
            _talk = CreateTalk(null);
            _runner = CreateRunner(null);
        }
        _runner.ActionApplied += OnActionApplied;

        _board = new BoardView { HoverTargetsOnly = true };
        _board.Setup(BoardRect);
        _board.Clicked += OnBoardClicked;
        AddChild(_board);

        AddTopLeftButtons();

        _rightLayer = ScreenLayout.Layer(this);   // log, chat, bank, opponents, offers
        _youLayer = ScreenLayout.Layer(this);     // your panel (bottom right)
        _barLayer = ScreenLayout.Layer(this);     // buttons, status, timer (bottom, next to your panel)
        _handLayer = ScreenLayout.Layer(this);    // your hand and the panels that grow out of it (bottom left)
        _centerLayer = ScreenLayout.Layer(this);  // popups over the middle of the board

        // Right column: log, bank, the opponents in the order they play after you, then you.
        _log = new LogPanel(_rightLayer, LogRect, _setup.Colors, _setup.HumanSeat, _text);
        _log.AddNote(resume is null && _start?.Kind == ScenarioKind.Decision
            ? $"Position practice. You are {_setup.HumanColor}."
            : resume is null && _start is not null
            ? $"Playing from a saved position{(_start.Title is { } title ? $": {title}" : "")}. You are {_setup.HumanColor}."
            : resume is null
            ? $"Board seed {BoardSeed} (type it in Play → Normal game to get this board again). You are {_setup.HumanColor}, seat {_setup.HumanSeat + 1} in turn order."
            : $"Continuing a saved game (seed {_setup.Seed}). You are {_setup.HumanColor}.");
        if (resume is null && _start?.Description is { Length: > 0 } about)
            _log.AddNote(about);
        if (loadError is not null)
            _log.AddNote($"That save couldn't be loaded ({loadError}). Started a new game instead.", Ui.Bad);
        _chat = new ChatPanel(_rightLayer, ChatRect, _setup.Colors, _setup.HumanSeat);
        foreach (var line in _talk.Lines)
            _chat.Add(line);
        _talk.LineAdded += _chat.Add;
        _chat.Sent += OnChatSent;
        _bank = new BankView { Position = BankRect.Position, Size = BankRect.Size };
        _rightLayer.AddChild(_bank);
        int row = 0;
        foreach (int seat in HudModel.Opponents(_setup.HumanSeat))
        {
            _playerCards[seat] = new PlayerCard { Position = new Vector2(LogRect.Position.X, RowTop + row++ * (RowHeight + RowGap)), Size = new Vector2(LogRect.Size.X, RowHeight) };
            _rightLayer.AddChild(_playerCards[seat]);
        }
        _playerCards[_setup.HumanSeat] = new PlayerCard { Position = YouRect.Position, Size = YouRect.Size, Big = true };
        _youLayer.AddChild(_playerCards[_setup.HumanSeat]);

        // Bottom: hand, buttons, status; dice beside whoever rolled.
        _hand = new HandBar(_handLayer, HandRect);
        _hand.ResourceClicked += OnHandResourceClicked;
        _bar = new ActionBar(_barLayer, ButtonsAt, StatusRect, TimerRect, _setup.HumanColor);
        _bar.Clicked += OnBarClicked;
        _dice = new DiceView { Visible = false };
        _dice.Clicked += () => OnBarClicked(BarItem.Roll);
        AddChild(_dice);

        // Trading.
        _proposal = new TradeProposal(_handLayer, ProposalRect, BankButtonRect, PeopleButtonRect, _setup.HumanColor, _setup.HumanSeat, _text, Submit, SubmitAll, WhyNot);
        _proposal.Sent += () => { _tradeOpen = false; Refresh(); };
        _popups = new TradePopups(_rightLayer, PopupsTopRight, _setup.Colors, _setup.HumanSeat, _text, Submit, WhyNot,
            edit: (slot, o) => { _proposal.StartEdit(slot, o); _tradeOpen = true; Refresh(); },
            counter: (slot, o) => { _proposal.StartCounter(slot, o); Refresh(); });
        _popups.DealNote = DealNote;

        // Sevens and dev cards: discard from your hand, pick who to rob, play a card from your hand.
        _discardUi = new DiscardPanel(_handLayer, DiscardRect, _discard,
            () => Submit(new GameAction(ActionType.Discard, _setup.HumanSeat, Give: _discard.Cards)));
        // Picking a card moves it out of the hand bar (a full Refresh here would loop: it resets the picker's limits).
        _discard.Changed += () => { if (_view is not null) _hand.Update(HudModel.Hand(_view, _discard.Cards)); };
        _victims = new VictimPopup(_centerLayer, VictimCenter, _setup.Colors, _text, () => { _choices = null; Refresh(); });
        _devPopup = new DevCardPopup(_handLayer, DevRect, _setup.HumanSeat, Submit);
        _review = new TurnReview(_centerLayer, VictimCenter, NewPosition, () => { _review.Close(); _paused = false; }, Leave);
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
        _reviewPanel = new GameReviewPanel();
        _reviewPanel.Back += () => _gameOver.Visible = true;
        _reviewPanel.NewGame += () => { _quit.Cancel(); GameSession.Resume = null; GetTree().ReloadCurrentScene(); };
        _reviewPanel.MainMenu += Leave;
        _gameOver.ReviewGame += ReviewGame;
        AddChild(_reviewPanel);
        AddChild(_help);

        ScreenLayout.Watch(this, Relayout);
        Refresh();
        CallDeferred(MethodName.StartGame);
        DevShots.Run(this);
    }

    /// <summary>
    /// Fits the window: the right column and your panel stay on the right edge, the hand and buttons on the bottom, popups
    /// over the board's middle, and the board takes all the room in between (never stretched: the scale is fixed).
    /// </summary>
    private void Relayout(Vector2 extra)
    {
        _extra = extra;
        _rightLayer.Position = new Vector2(extra.X, 0);
        _youLayer.Position = extra;
        _barLayer.Position = extra;
        _handLayer.Position = new Vector2(0, extra.Y);
        _centerLayer.Position = extra / 2;
        _hand.SetWidth(HandRect.Size.X + extra.X); // up to the buttons, which stay beside your panel
        _board.Setup(new Rect2(BoardRect.Position, BoardRect.Size + extra));
        _animator.Setup(_board, Where, new Vector2(BoardRect.GetCenter().X + extra.X / 2, 14));
        _gameOver.Relayout(extra);
        if (_view is { } v)
            ShowDice(v, _human.Prompt?.Legal, animate: false);
    }

    /// <summary>Settings (Save / Main menu) and fullscreen, top left.</summary>
    private void AddTopLeftButtons()
    {
        var menu = new PopupMenu();
        menu.AddItem("Save game", 0);
        menu.AddItem("Main menu", 1);
        menu.AddItem("Game results", 2);
        menu.SetItemDisabled(2, true);
        menu.AddItem("How to play", 3);
        _menu = menu;
        menu.IdPressed += id =>
        {
            if (id == 0)
                SaveGame();
            else if (id == 1)
                Leave();
            else if (id == 2)
                ShowResults();
            else if (id == 3)
                _help.Open();
        };
        AddChild(menu);
        var gear = new ActionTile { Position = new Vector2(8, 8), Size = new Vector2(44, 44), DrawIcon = (c, at, s, ink) => Gear(c, at, s) };
        gear.Set(true, "Settings");
        gear.Clicked += () => menu.Popup(new Rect2I(8, 56, 160, 0));
        AddChild(gear);
        var full = new ActionTile { Position = new Vector2(8, 58), Size = new Vector2(44, 44), DrawIcon = (c, at, s, ink) => FullscreenIcon(c, at, s) };
        full.Set(true, "Fullscreen");
        full.Clicked += GameSession.ToggleFullscreen;
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
        if (_start is not null)
            return GameRunner.FromPosition(_start, agents, new RngChance(_setup.ChanceSeed));
        var state = new GameState(BoardGenerator.Balanced(new Rng(BoardSeed)), _options.ToSettings());
        return new GameRunner(state, agents, new RngChance(_setup.ChanceSeed));
    }

    /// <summary>
    /// A bot seat: SearchBot (the strongest bot) with the bundled weights and calibration, thinking on all but two threads
    /// for CATAN_THINK_MS per decision (default 500: in Sim matches 2,000 playouts played as well as 16,000, and 0.5 s buys
    /// several thousand; 50 ms in dev autoplay). CATAN_BOT=smart plays SmartBot instead.
    /// </summary>
    private IPlayerAgent CreateBot(int seat, int turnOffset)
    {
        ulong seed = _setup.BotSeed + (ulong)seat + (ulong)turnOffset * 7919;
        if (System.Environment.GetEnvironmentVariable("CATAN_BOT") == "smart")
            return new SmartBot(BotWeightsFile(), SmartBotSettings.Play, seed) { Table = _talk };
        int ms = int.TryParse(System.Environment.GetEnvironmentVariable("CATAN_THINK_MS"), out int t) && t > 0 ? t : _autoplayActions > 0 && !ForceAnimate ? 50 : 500;
        var calibration = WinModel.Load(ProjectSettings.GlobalizePath("res://bots/calibration.json"));
        return new SearchBot(BotWeightsFile(), calibration, new SearchSettings { ThinkMs = ms }, seed) { Table = _talk };
    }

    private IPlayerAgent HumanSeatAgent() => _autoplayActions <= 0 ? _human
        : new AutoplayAgent(new SmartBot(BotWeightsFile(), SmartBotSettings.Training, _setup.BotSeed + 101), _human, () => Autoplaying)
        {
            Decided = d => { lock (_decisions) _decisions.Add(d); },
        };

    private bool Autoplaying => _runner is not null && _runner.Actions.Count < _autoplayActions;

    /// <summary>Developer screenshots: CATAN_ANIMATE=1 keeps animations and bot pauses on while autoplaying.</summary>
    private static readonly bool ForceAnimate = System.Environment.GetEnvironmentVariable("CATAN_ANIMATE") == "1";

    private bool Fast => Autoplaying && !ForceAnimate;

    /// <summary>The trained weights shipped with the game (game/bots/best.json), or the hand-set defaults if there are none yet.</summary>
    internal static BotWeights BotWeightsFile()
    {
        string path = ProjectSettings.GlobalizePath("res://bots/best.json");
        return System.IO.File.Exists(path) ? BotWeights.Load(path) : new BotWeights();
    }

    /// <summary>The board: the one picked in the mode setup, or the game's own random one.</summary>
    private ulong BoardSeed => _options.BoardSeed ?? _setup.BoardSeed % 1_000_000; // short enough to type back in

    /// <summary>
    /// A new game's seed: random, giving you your chosen colour if you picked one, and the position's seat when playing from
    /// a saved position.
    /// </summary>
    private ulong NewSeed() => GameSetup.SeedFor(_options.PreferredColor, _start?.Seat, () => (ulong)System.Random.Shared.NextInt64());

    /// <summary>The saved position this game starts from (Play → Position practice), or null.</summary>
    private Catan.Core.Position? _start;

    /// <summary>The table talk for this game: new, or the one saved with the game being continued.</summary>
    private TableTalk CreateTalk(GameRecord? resume)
    {
        var names = _setup.Colors.Select(c => c.ToString().ToLowerInvariant()).ToArray();
        if (resume?.TableTalk is { } json)
        {
            try
            {
                return TableTalk.FromJson(names, json);
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException)
            {
                GD.PushWarning($"Saved table talk couldn't be read: {ex.Message}");
            }
        }
        return new TableTalk(names);
    }

    /// <summary>The promises riding on the offer in <paramref name="slot"/>, in words for its card ("Deal: Orange won't block you").</summary>
    private string? DealNote(int slot)
    {
        if (_talk.DealOn(slot) is not { } d)
            return null;
        string Who(int seat) => seat < 0 ? "whoever takes it" : _text.Seat(seat);
        string Terms(IReadOnlyList<PromiseTerm> terms) => string.Join(" or ", terms.Select(t => t.Kind switch
        {
            PromiseKind.NoBlock => "block",
            PromiseKind.NoSteal => "steal from",
            _ => $"take the {Spots.Name(_runner.State.Board, t.Vertex)} spot from",
        }).Distinct());
        var parts = new List<string>();
        if (d.CurrentPromises.Count > 0)
            parts.Add($"{Who(d.Current)} won't {Terms(d.CurrentPromises)} {Who(d.Partner).ToLowerInvariant()}");
        if (d.PartnerPromises.Count > 0)
            parts.Add($"{Who(d.Partner)} won't {Terms(d.PartnerPromises)} {Who(d.Current).ToLowerInvariant()}");
        return parts.Count == 0 ? null : $"Deal: {string.Join("; ", parts)}";
    }

    /// <summary>The game so far, with its table talk, for saving.</summary>
    private GameRecord Record()
    {
        var record = _runner.ToRecord(_setup.Seed);
        record.TableTalk = _talk.ToJson();
        return record;
    }

    /// <summary>
    /// You said something in the chat. It's addressed to the current player when it's someone else's turn (they're the one
    /// who can trade), and to anyone on your own turn, unless you name a colour.
    /// </summary>
    private void OnChatSent(string text)
    {
        var s = _runner.State;
        int target = s.CurrentPlayer == _setup.HumanSeat ? -1 : s.CurrentPlayer;
        _talk.Hear(_setup.HumanSeat, text, s, target);
    }

    private void StartGame() => RunGame();

    /// <summary>The game loop. Bots answer instantly; after their moves we pause so you can follow along.</summary>
    private async void RunGame()
    {
        try
        {
            while (!_runner.IsOver && !_quit.IsCancellationRequested)
            {
                // Position practice: the game waits while your turn's review is up (Play it out lets it go on).
                while (_paused && !_quit.IsCancellationRequested)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_quit.IsCancellationRequested)
                    break;
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
        _gameOver.CanReview = _decisions.Count > 0;
        _gameOver.Show(GameOverSummary.From(_runner.State, _runner.Log.For(_setup.HumanSeat)), _setup.Colors, _setup.HumanSeat);
        if (System.Environment.GetEnvironmentVariable("CATAN_SHOW_REVIEW") == "1")
            ReviewGame(); // developer screenshots
    }

    private void OnActionApplied(GameAction action, IReadOnlyList<GameEvent> events)
    {
        _talk.OnAction(_runner.State, action);
        _clock.OnAction(action, _runner.State.CurrentPlayer, DateTime.UtcNow);
        _timeUpHandled = false;
        // Autosave at the end of each of your turns, and when the game ends.
        if (!Autoplaying && events.Any(e => e is TurnEnded t && t.Seat == _setup.HumanSeat || e is GameEnded))
            GameSession.Store.Autosave(Record());
        Refresh();
    }

    public override void _Process(double delta)
    {
        _chat.Tick();
        if (Deciding || _paused)
            _bar.SetTimer("");
        else if (_human.Prompt is { IsOptional: true, Deadline: { } deadline })
        {
            _popups.Tick(deadline, _human.ResponseWindow);
            _bar.SetTimer(Clock(Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds)));
        }
        else if (!_runner.IsOver)
        {
            _bar.SetTimer(Clock(_clock.Remaining(DateTime.UtcNow)));
        }
        else
            _bar.SetTimer(""); // the game is over: no clock
        OnTimeUp();
        CloseDeclinedOffers();
        // "Orange is thinking…" appears while a bot works on a decision (it thinks on another thread).
        if (_human.Prompt is null && _view is { } v && !_runner.IsOver)
        {
            var (status, help) = StatusText(v, null);
            _bar.SetStatus(status, help);
        }
    }

    private static string Clock(double seconds)
    {
        int whole = (int)Math.Ceiling(seconds); // a countdown shows 00:05 for its whole first second
        return $"{whole / 60:00}:{whole % 60:00}";
    }

    /// <summary>
    /// When your time runs out: the game rolls for you, or ends your turn; anything else it must have first (moving the robber,
    /// a discard, free roads, a setup placement) is picked the way a bot would.
    /// </summary>
    private void OnTimeUp()
    {
        if (Deciding)
            return;
        if (_timeUpHandled || Autoplaying || _runner.IsOver || _view is not { } v || v.CurrentPlayer != _setup.HumanSeat
            || _human.Prompt is not { IsOptional: false } prompt || !_clock.Expired(DateTime.UtcNow))
            return;
        _timeUpHandled = true;
        var legal = prompt.Legal;
        GameAction? move = legal.FirstOrDefault(a => a.Type == ActionType.RollDice) is { Type: ActionType.RollDice } roll ? roll
            : v.Phase == Phase.Main && legal.FirstOrDefault(a => a.Type == ActionType.EndTurn) is { Type: ActionType.EndTurn } end ? end
            : null;
        move ??= new SmartBot(BotWeightsFile(), SmartBotSettings.Training, _setup.BotSeed + 202).DecideAsync(prompt.View, legal, default).Result;
        CloseProposal();
        _discard.Clear();
        _queued.Clear();
        _pending = null;
        _bar.SetStatus("Time's up", v.Phase == Phase.PreRoll ? "Rolled for you." : "Your turn was finished for you.");
        _autoMove = true;
        Submit(move.Value);
        _autoMove = false;
    }

    /// <summary>Your offers that every opponent declined close by themselves after a few seconds.</summary>
    private void CloseDeclinedOffers()
    {
        if (_runner.IsOver || _view is not { } v || v.Phase != Phase.Main || v.CurrentPlayer != _setup.HumanSeat
            || _human.Prompt is not { IsOptional: false })
        {
            _declinedOffers.Clear();
            return;
        }
        var now = DateTime.UtcNow;
        for (int slot = 0; slot < v.Offers.Length; slot++)
        {
            var offer = v.Offers[slot];
            bool allDeclined = offer.IsActive && !offer.IsCounter && offer.From == _setup.HumanSeat
                && Enumerable.Range(0, GameConstants.PlayerCount).All(seat => seat == offer.From || offer.ResponseOf(seat) == TradeOffer.Declined);
            if (!allDeclined)
                continue;
            if (!_declinedOffers.TryGetValue((slot, offer), out var since))
                _declinedOffers[(slot, offer)] = now;
            else if ((now - since).TotalSeconds >= DeclinedOfferCloseSeconds)
            {
                _declinedOffers.Remove((slot, offer));
                Submit(new GameAction(ActionType.CancelOffer, _setup.HumanSeat, slot));
                return; // one per frame: the view refreshes after each move
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (GameSession.HandleFullscreenKey(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // Space rolls the dice, or ends your turn. Handled before buttons see it (a focused button would take Space too).
        if (@event is InputEventKey { Keycode: Key.Space, Pressed: true, Echo: false } && !_chat.Typing && _view is { } v && _human.Prompt is { IsOptional: false } prompt)
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
        string path = GameSession.Store.Save(Record(), DateTime.Now);
        _log.AddNote($"Saved as {System.IO.Path.GetFileNameWithoutExtension(path)}.");
    }

    private void ClearPending()
    {
        _pending = null;
        _board.SetPending(BoardHit.None);
    }

    private void OnPromptChanged()
    {
        // Developer screenshots: CATAN_PRACTICE_AUTO=1 plays the practice turn with a bot's moves (through Submit, so they're graded).
        if (Deciding && System.Environment.GetEnvironmentVariable("CATAN_PRACTICE_AUTO") == "1" && _human.Prompt is { IsOptional: false })
            CallDeferred(MethodName.PracticeAutoMove);
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
        _clock.Update(view, DateTime.UtcNow);
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
            if (animate && GameSounds.For(seen[_loggedEvents], _setup.HumanSeat, view.CurrentPlayer) is { } sound)
                Sounds.Play(sound);
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
        float y = row.GlobalPosition.Y + row.Size.Y / 2 - _dice.Size.Y / 2;
        _dice.Position = new Vector2(LogRect.Position.X + _extra.X - _dice.Size.X - 4, Math.Min(y, StatusRect.Position.Y + _extra.Y - _dice.Size.Y - 4));
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
        SpotKind.Seat when spot.Id == _setup.HumanSeat => _handLayer.Position + HandRect.Position + new Vector2(150, HandRect.Size.Y / 2),
        SpotKind.Seat => _playerCards[spot.Id].GlobalPosition + new Vector2(60, _playerCards[spot.Id].Size.Y / 2 + 6),
        _ => _rightLayer.Position + BankRect.Position + new Vector2(BankRect.Size.X / 2, BankRect.Size.Y / 2),
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
        // Typing in the chat: Shift+click puts a corner's numbers into the message. A plain click still plays (a click that
        // does nothing in the game puts them in too).
        bool typing = _chat.Typing;
        bool spot = typing && hit.Kind == HitKind.Vertex;
        if (spot && Input.IsKeyPressed(Key.Shift))
        {
            _chat.Insert(Spots.Name(_runner.State.Board, hit.Id));
            return;
        }
        if (_human.Prompt is not { IsOptional: false } prompt || _view is null)
        {
            if (spot)
                _chat.Insert(Spots.Name(_runner.State.Board, hit.Id));
            return;
        }
        var matches = ActionBarModel.BoardActions(_mode, _view, prompt.Legal).Where(a => TargetOf(a) == hit).ToList();
        bool plays = matches.Count > 0 || (_mode == BuildMode.None && ActionBarModel.QuickBuilds(_view, prompt.Legal).Any(a => TargetOf(a) == hit));
        if (spot && !plays)
        {
            _chat.Insert(Spots.Name(_runner.State.Board, hit.Id));
            return;
        }
        if (typing)
            _chat.Leave();
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

    /// <summary>
    /// Position practice: a play of your turn. Moves from the legal list are graded in the background from the view you had
    /// just before (every move you could have made, ranked); trade offers aren't in that list, so they're only counted, and
    /// neither is trade bookkeeping (withdraw, confirm). Forced moves aren't counted at all. Ending the turn ends the practice
    /// and shows the review.
    /// </summary>
    private void RecordPracticePlay(HumanPrompt prompt, GameAction action)
    {
        var decision = new Decision(prompt.View, prompt.Legal, action);
        if (prompt.Legal.Count == 1)
        {
            // A forced move (nothing else to do) isn't a decision: not graded, not counted.
        }
        else if (GameReviewer.IsGradable(decision))
        {
            var coach = new DecisionCoach(BotWeightsFile());
            var names = Enumerable.Range(0, GameConstants.PlayerCount).Select(_text.Seat).ToArray();
            var (view, legal) = (prompt.View, prompt.Legal.ToList());
            _plays.Add((action, Task.Run(() => coach.Grade(view, legal, names))));
        }
        else
            _ungraded++;
        if (action.Type == ActionType.EndTurn)
        {
            _practiceOver = true;
            _paused = true;
            ShowTurnReview();
        }
    }

    private async void ShowTurnReview()
    {
        CloseProposal();
        _review.ShowWaiting();
        await Task.WhenAll(_plays.Select(p => p.Grade));
        if (!IsInsideTree())
            return;
        var graded = _plays.Select(p => (p.Grade.Result, p.Move)).Where(p => p.Result.Moves.Any(m => m.Move == p.Move))
            .Select(p => new GradedPlay(p.Result.Of(p.Move), p.Result.Moves[0], p.Result.Moves.Count)).ToList();
        if (graded.Count > 0)
            GameSession.RecordScore(new PracticeResult(DateTime.Now, PracticeKind.Position, graded.Average(p => p.Yours.Rating), graded.Count,
                graded.Count(p => p.Yours.Rank == 1)));
        _review.Show(graded, _ungraded, GameSession.History.Describe(PracticeKind.Position));
    }

    /// <summary>
    /// Review my game (results screen): the bots grade every decision you made, on all threads (the game is over, so they're
    /// free). Graded once; the score goes into your practice history.
    /// </summary>
    private async void ReviewGame()
    {
        _gameOver.Visible = false;
        if (_gameReview is null)
        {
            var weights = BotWeightsFile();
            var names = Enumerable.Range(0, GameConstants.PlayerCount).Select(_text.Seat).ToArray();
            var decisions = _decisions.ToList();
            _gameReview = Task.Run(() => GameReviewer.Review(weights, decisions, names));
            _reviewPanel.ShowWaiting();
            var review = await _gameReview;
            if (!IsInsideTree())
                return;
            if (review.Plays.Count > 0)
                GameSession.RecordScore(new PracticeResult(DateTime.Now, PracticeKind.GameReview, review.Score, review.Plays.Count, review.BestPicks));
        }
        _reviewPanel.Show(await _gameReview, GameSession.History.Describe(PracticeKind.GameReview));
    }

    private void NewPosition()
    {
        _quit.Cancel();
        GameSession.Resume = null;
        GameSession.StartPosition = GameModes.DealPosition();
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Developer screenshots (CATAN_PRACTICE_AUTO=1): the practice turn played by a bot through <see cref="Submit"/>.</summary>
    private void PracticeAutoMove()
    {
        if (!Deciding || _human.Prompt is not { IsOptional: false } prompt || prompt.View.CurrentPlayer != _setup.HumanSeat)
            return;
        var bot = new SmartBot(BotWeightsFile(), SmartBotSettings.Play, _setup.BotSeed + 303 + (ulong)_plays.Count);
        var move = bot.DecideAsync(prompt.View, prompt.Legal, default).Result;
        if (move.Type == ActionType.OfferTrade)
            move = prompt.Legal.First(a => a.Type == ActionType.EndTurn); // keep the dev turn short: no waiting on answers
        Submit(move);
    }

    /// <summary>Checks the move with the engine first (the runner would reject an illegal one and stop the game).</summary>
    private bool Submit(GameAction action)
    {
        if (!Rules.IsLegal(_runner.State, action, out string reason))
        {
            _bar.SetStatus("Can't do that", reason, error: true);
            return false;
        }
        if (!_autoMove && _human.Prompt is { } asked && asked.Legal.Count > 1 && asked.Legal.Contains(action))
            _decisions.Add(new Decision(asked.View, asked.Legal.ToList(), action));
        if (Deciding && _human.Prompt is { IsOptional: false } prompt && prompt.View.CurrentPlayer == _setup.HumanSeat)
            RecordPracticePlay(prompt, action);
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

        /// <summary>The bot's decisions for your seat (developer screenshots of the game review).</summary>
        public Action<Decision>? Decided { get; init; }

        public async Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
        {
            if (!_useBot())
                return await _human.DecideAsync(view, legal, ct);
            var move = await _bot.DecideAsync(view, legal, ct);
            if (legal.Count > 1 && legal.Contains(move))
                Decided?.Invoke(new Decision(view, legal.ToList(), move));
            return move;
        }

        public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
            (_useBot() ? _bot : _human).RespondAsync(view, legal, ct);
    }
}
