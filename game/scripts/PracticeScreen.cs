using System;
using System.Collections.Generic;
using System.Linq;
using Catan.AI;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Placement practice: the whole opening in snake order on a random board or one from a seed. The bots place at their
/// turns; you place your settlements and roads at yours. After each of your placements (or all at the end, a setting), the
/// coach rates it the way the bots judge positions: your pick's rank and rating, the best three with pips, resources, port
/// and reasons (scarce resources, open spots within reach, blocking risk), and medals on the board. Retry this board, a new
/// board, or play the game out from the finished opening. The rating logic is <see cref="PlacementCoach"/> and
/// <see cref="OpeningPractice"/> (UI-free, reused by later coach modes).
/// </summary>
public partial class PracticeScreen : Control
{
    private static readonly Rect2 BoardRect = new(20, 20, 1120, 860);
    private static readonly Rect2 PanelRect = new(1160, 20, 420, 860);
    private static readonly Color[] Medals = { new(0.98f, 0.78f, 0.18f), new(0.78f, 0.8f, 0.84f), new(0.8f, 0.52f, 0.25f) };
    private const double BotPauseSeconds = 0.35;

    /// <summary>One of your placements and how the coach rated it.</summary>
    private sealed record Pick(bool IsRoad, int Chosen, IReadOnlyList<SpotRating> Spots, IReadOnlyList<RoadRating> Roads, int Settlement)
    {
        public int Rank => IsRoad ? Roads.First(r => r.Edge == Chosen).Rank : Spots.First(s => s.Vertex == Chosen).Rank;
        public double Rating => IsRoad ? Roads.First(r => r.Edge == Chosen).Rating : Spots.First(s => s.Vertex == Chosen).Rating;
        public int Count => IsRoad ? Roads.Count : Spots.Count;
    }

    private PlacementCoach _coach = null!;

    /// <summary>This opening's score is in your practice history (once per opening, however often the summary is redrawn).</summary>
    private bool _recorded;
    private BotWeights _weights = null!;
    private BoardView _board = null!;
    private Label _situation = null!, _status = null!, _result = null!, _details = null!;
    private CheckBox _showAll = null!, _feedbackEach = null!;
    private Button _continue = null!, _playOut = null!;

    private GameSetup _setup = null!;
    private ulong _boardSeed;
    private OpeningPractice _practice = null!;
    private string[] _names = Array.Empty<string>();
    private readonly List<Pick> _picks = new();
    private Pick? _shown;          // the pick whose feedback is on screen
    private bool _waiting;         // feedback is showing: the opening waits for Continue
    private double _botTimer;

    public override void _Input(InputEvent @event)
    {
        if (GameSession.HandleFullscreenKey(@event))
            GetViewport().SetInputAsHandled();
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // let clicks through to the board (the panel still stops the ones over it)
        AddChild(new SeaView());

        string weightsPath = ProjectSettings.GlobalizePath("res://bots/best.json");
        _weights = System.IO.File.Exists(weightsPath) ? BotWeights.Load(weightsPath) : new BotWeights();
        _coach = new PlacementCoach(_weights);

        _board = new BoardView { HoverTargetsOnly = true };
        _board.Setup(BoardRect);
        _board.Clicked += OnClicked;
        _board.Overlay = DrawMarkers;
        AddChild(_board);

        var side = Ui.Panel(null, PanelRect, out var panel);
        AddChild(side);
        // Fill any window: the board takes the extra room, the panel stays on the right at full height.
        ScreenLayout.Watch(this, extra =>
        {
            _board.Setup(new Rect2(BoardRect.Position, BoardRect.Size + extra));
            side.Position = PanelRect.Position + new Vector2(extra.X, 0);
            side.Size = PanelRect.Size + new Vector2(0, extra.Y);
        });
        panel.AddChild(Ui.Label("Placement practice", 28));
        _situation = Wrapped(15, Ui.MutedText);
        panel.AddChild(_situation);
        _status = Wrapped(18, Ui.Text);
        panel.AddChild(_status);
        panel.AddChild(new HSeparator());

        // The feedback scrolls if it is long (five spots with reasons).
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var feedback = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(feedback);
        _result = Wrapped(19, Ui.Text);
        feedback.AddChild(_result);
        _details = Wrapped(14, Ui.MutedText);
        feedback.AddChild(_details);

        _continue = Button("Continue", Continue);
        _continue.Visible = false;
        panel.AddChild(_continue);
        _showAll = Check("Show every spot's rating", false);
        _showAll.Toggled += _ => _board.Redraw();
        panel.AddChild(_showAll);
        _feedbackEach = Check("Rate each placement as I make it", GameSession.Options.PracticeFeedbackEach);
        _feedbackEach.Toggled += on => GameSession.SaveSettings(GameSession.Options with { PracticeFeedbackEach = on });
        panel.AddChild(_feedbackEach);

        var buttons = new GridContainer { Columns = 2 };
        buttons.AddThemeConstantOverride("h_separation", 10);
        buttons.AddThemeConstantOverride("v_separation", 8);
        buttons.AddChild(Button("Retry this board", () => Start(newBoard: false)));
        buttons.AddChild(Button("New board", () => Start(newBoard: true)));
        _playOut = Button("Play it out", PlayOut);
        _playOut.TooltipText = "Play the rest of the game from this opening";
        buttons.AddChild(_playOut);
        buttons.AddChild(Button("Menu", () => GetTree().ChangeSceneToFile("res://scenes/Menu.tscn")));
        panel.AddChild(buttons);

        Start(newBoard: true);
        DevShots.Run(this);
    }

    private static Label Wrapped(int size, Color color)
    {
        var label = Ui.Label("", size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(PanelRect.Size.X - 40, 0);
        return label;
    }

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(180, 44), FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += onClick;
        return button;
    }

    private static CheckBox Check(string text, bool on)
    {
        var box = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
        box.AddThemeFontSizeOverride("font_size", 15);
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_disabled_color" })
            box.AddThemeColorOverride(name, name == "font_disabled_color" ? Ui.MutedText : Ui.Text);
        return box;
    }

    /// <summary>A new opening: a new board (random, or the seed picked under Play), or the same board and seats again.</summary>
    private void Start(bool newBoard)
    {
        if (newBoard || _setup is null)
        {
            ulong random() => (ulong)System.Random.Shared.NextInt64();
            ulong dev = ulong.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SEED"), out ulong d) ? d : 0;
            _boardSeed = _setup is null && dev > 0 ? dev : GameSession.Options.BoardSeed ?? random() % 1_000_000;
            _setup = GameSetup.Create(GameSetup.SeedFor(GameSession.Options.PreferredColor, random));
        }
        _names = _setup.Colors.Select((c, seat) => seat == _setup.HumanSeat ? "you" : c.ToString()).ToArray();
        _practice = new OpeningPractice(_weights, BoardGenerator.Balanced(new Rng(_boardSeed)), GameSession.Options.ToSettings(),
            _setup.HumanSeat, _setup.BotSeed);
        _picks.Clear();
        _recorded = false;
        _shown = null;
        _waiting = false;
        _continue.Visible = false;
        _playOut.Disabled = true;
        _result.Text = "";
        _details.Text = "";
        string place = (_setup.HumanSeat + 1) switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => "4th" };
        _situation.Text = $"You are {_setup.HumanColor}, {place} in turn order. Board seed {_boardSeed}.";
        Refresh();
    }

    public override void _Process(double delta)
    {
        // Developer screenshots: CATAN_PRACTICE_PICK=N makes your placements for you (the Nth best choice), and
        // CATAN_PRACTICE_CONTINUE=1 also presses Continue after each rating.
        if (_waiting && System.Environment.GetEnvironmentVariable("CATAN_PRACTICE_CONTINUE") == "1")
            Continue();
        if (_practice is { YourTurn: true } && !_waiting && int.TryParse(System.Environment.GetEnvironmentVariable("CATAN_PRACTICE_PICK"), out int nth))
        {
            bool road = _practice.State.Phase == Phase.SetupRoad;
            int choice = road ? _coach.RateRoads(_practice.State, _setup.HumanSeat).ElementAtOrDefault(nth - 1)?.Edge ?? -1
                : _coach.Rate(_practice.State, _setup.HumanSeat).ElementAtOrDefault(nth - 1)?.Vertex ?? -1;
            if (choice >= 0)
                OnClicked(road ? BoardHit.Edge(choice) : BoardHit.Vertex(choice));
            return;
        }
        if (_practice is null || _waiting || _practice.Done || _practice.YourTurn)
            return;
        _botTimer += delta;
        if (_botTimer < BotPauseSeconds)
            return;
        _botTimer = 0;
        _practice.BotStep();
        Refresh();
    }

    /// <summary>The board and the status line; on your turn, the spots or roads you may pick glow.</summary>
    private void Refresh()
    {
        _board.Show(PlayerView.From(_practice.State, _setup.HumanSeat), _setup.Colors);
        if (_practice.Done)
        {
            _board.SetTargets(Array.Empty<BoardHit>());
            _status.Text = "Opening done.";
            _playOut.Disabled = false;
            if (!_waiting)
                ShowSummary();
        }
        else if (_practice.YourTurn && !_waiting)
        {
            bool road = _practice.State.Phase == Phase.SetupRoad;
            _board.SetTargets(_practice.Legal().Select(a => road ? BoardHit.Edge(a.Target) : BoardHit.Vertex(a.Target)));
            _status.Text = road ? "Your turn: place the road next to your new settlement."
                : _practice.Round == 1 ? "Your turn: place your first settlement."
                : "Your turn: place your second settlement (it gives you a card from each tile around it).";
        }
        else
        {
            _board.SetTargets(Array.Empty<BoardHit>());
            if (!_practice.Done && !_practice.YourTurn)
                _status.Text = $"{_setup.Colors[Rules.ActingSeat(_practice.State)]} is placing…";
        }
        _board.Redraw();
    }

    private void OnClicked(BoardHit hit)
    {
        if (_waiting || !_practice.YourTurn)
            return;
        var move = _practice.Legal().FirstOrDefault(a => (a.Type == ActionType.BuildRoad ? BoardHit.Edge(a.Target) : BoardHit.Vertex(a.Target)) == hit);
        if (move.Type is not (ActionType.BuildSettlement or ActionType.BuildRoad) || move.Seat != _setup.HumanSeat)
            return;

        // Rate before placing: the choices as they were.
        bool road = move.Type == ActionType.BuildRoad;
        int settlement = road ? _picks.LastOrDefault(p => !p.IsRoad)?.Chosen ?? -1 : move.Target;
        var pick = road
            ? new Pick(true, move.Target, Array.Empty<SpotRating>(), _coach.RateRoads(_practice.State, _setup.HumanSeat, _names), settlement)
            : new Pick(false, move.Target, _coach.Rate(_practice.State, _setup.HumanSeat, _names), Array.Empty<RoadRating>(), settlement);
        _picks.Add(pick);
        _practice.Place(move);

        if (_feedbackEach.ButtonPressed)
        {
            ShowFeedback(pick);
            _waiting = true;
            _continue.Visible = true;
        }
        Refresh();
    }

    private void Continue()
    {
        _waiting = false;
        _continue.Visible = false;
        _shown = null;
        _result.Text = "";
        _details.Text = "";
        Refresh();
    }

    private static string Grade(double rating, int rank) =>
        rank == 1 ? "Perfect! The bots' top choice."
        : rating >= 90 ? "Excellent."
        : rating >= 75 ? "Good."
        : rating >= 50 ? "OK, but there were better choices."
        : "Weak: several choices were much better.";

    private void ShowFeedback(Pick pick)
    {
        _shown = pick;
        var lines = new List<string>();
        if (pick.IsRoad)
        {
            var mine = pick.Roads.First(r => r.Edge == pick.Chosen);
            _result.Text = $"{Grade(mine.Rating, mine.Rank)}\nYour road: #{mine.Rank} of {pick.Roads.Count}, rating {mine.Rating:F0}";
            lines.Add("Yours:");
            lines.AddRange(mine.Facts.Select(f => $"   • {f}"));
            foreach (var r in pick.Roads.Take(3).Where(r => r.Edge != mine.Edge))
            {
                lines.Add("");
                lines.Add($"#{r.Rank} (rating {r.Rating:F0}):");
                lines.AddRange(r.Facts.Select(f => $"   • {f}"));
            }
        }
        else
        {
            var mine = pick.Spots.First(s => s.Vertex == pick.Chosen);
            _result.Text = $"{Grade(mine.Rating, mine.Rank)}\nYour spot: #{mine.Rank} of {pick.Spots.Count}, rating {mine.Rating:F0}";
            lines.Add($"Yours: {Describe(mine)}");
            lines.Add("");
            foreach (var s in pick.Spots.Take(3).Where(s => s.Vertex != mine.Vertex)) // the top three (yours is already above)
                lines.Add($"#{s.Rank}: {Describe(s)}");
        }
        _details.Text = string.Join("\n", lines);
        _board.Redraw();
    }

    /// <summary>The end of the opening: every one of your placements with its rank and rating, and an average.</summary>
    private void ShowSummary()
    {
        if (_picks.Count == 0)
            return;
        _shown = null;
        double average = _picks.Average(p => p.Rating);
        if (!_recorded)
        {
            _recorded = true;
            GameSession.RecordScore(new PracticeResult(DateTime.Now, PracticeKind.Placement, average, _picks.Count, _picks.Count(p => p.Rank == 1)));
        }
        _result.Text = $"Your opening: average rating {average:F0}";
        if (GameSession.History.Describe(PracticeKind.Placement) is { } history)
            _result.Text += $"\nYour placement practice: {history}";
        var lines = new List<string>();
        int settlement = 0;
        foreach (var p in _picks)
        {
            string what = p.IsRoad ? $"   road" : $"{(++settlement == 1 ? "1st" : "2nd")} settlement";
            lines.Add($"{what}: #{p.Rank} of {p.Count}, rating {p.Rating:F0}");
        }
        if (!_feedbackEach.ButtonPressed)
        {
            // Rated at the end: the details for each placement, in order.
            foreach (var p in _picks.Where(p => !p.IsRoad))
            {
                var best = p.Spots[0];
                lines.Add("");
                lines.Add($"Best spot when you placed there: {Describe(best)}");
            }
        }
        lines.Add("");
        lines.Add("Your picks are marked on the board. Play it out, retry this board, or deal a new one.");
        _details.Text = string.Join("\n", lines);
        _board.Redraw();
    }

    private void PlayOut()
    {
        if (!_practice.Done)
            return;
        var players = Enumerable.Range(0, GameConstants.PlayerCount).Select(seat => seat == _setup.HumanSeat ? "You" : "SearchBot").ToArray();
        GameSession.Resume = _practice.ToRecord(_setup.Seed, players);
        GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
    }

    private static string Describe(SpotRating r)
    {
        var parts = Enumerable.Range(0, GameConstants.ResourceCount).Where(i => r.ResourcePips[i] > 0)
            .OrderByDescending(i => r.ResourcePips[i]).Select(i => $"{GameText.Resource(i)} {r.ResourcePips[i]}");
        string harbor = r.Harbor is { } h ? h == HarborType.Generic ? ", 3:1 port" : $", 2:1 {GameText.Resource((int)h)} port" : "";
        string starting = r.StartingCards.Total > 0 ? $"\n   starts you with {GameText.Cards(r.StartingCards)}" : "";
        string why = r.Reasons.Count > 0 ? $"\n   why: {string.Join(", ", r.Reasons)}" : "";
        string facts = r.Facts is { Count: > 0 } f ? $"\n   {string.Join("; ", f)}" : "";
        return $"{r.Pips} pips ({string.Join(", ", parts)}){harbor}, rating {r.Rating:F0}{starting}{why}{facts}";
    }

    // ---- Board markers ----

    private Vector2 EdgePoint(int edge) =>
        (_board.VertexPoint(Topology.EdgeVertices[edge, 0]) + _board.VertexPoint(Topology.EdgeVertices[edge, 1])) / 2 - _board.Position;

    private Vector2 SpotPoint(int vertex) => _board.VertexPoint(vertex) - _board.Position;

    /// <summary>
    /// Feedback on screen: medals on the best three (spots or roads), your pick's rank, and optionally every spot's rating.
    /// At the end of the opening: each of your placements with its rank.
    /// </summary>
    private void DrawMarkers(CanvasItem c)
    {
        float size = _board.HexSize;
        if (_shown is { } pick)
        {
            Vector2 At(int id) => pick.IsRoad ? EdgePoint(id) : SpotPoint(id);
            if (!pick.IsRoad && _showAll.ButtonPressed)
                foreach (var r in pick.Spots.Skip(3))
                    if (r.Vertex != pick.Chosen)
                        Pill(c, At(r.Vertex), $"{r.Rating:F0}", RatingColor(r.Rating), size * 0.16f);
            var top = pick.IsRoad ? pick.Roads.Take(3).Select(r => r.Edge).ToList() : pick.Spots.Take(3).Select(s => s.Vertex).ToList();
            for (int i = top.Count - 1; i >= 0; i--)
            {
                c.DrawArc(At(top[i]), size * (pick.IsRoad ? 0.2f : 0.26f), 0, Mathf.Tau, 32, Medals[i], 5, true);
                Pill(c, At(top[i]) + new Vector2(0, -size * 0.42f), $"#{i + 1}", Medals[i], size * 0.2f);
            }
            if (pick.Rank > 3)
            {
                c.DrawArc(At(pick.Chosen), size * 0.24f, 0, Mathf.Tau, 32, RatingColor(pick.Rating), 5, true);
                Pill(c, At(pick.Chosen) + new Vector2(0, -size * 0.42f), $"#{pick.Rank}", RatingColor(pick.Rating), size * 0.2f);
            }
        }
        else if (_practice is { Done: true } && !_waiting)
            foreach (var p in _picks)
            {
                var at = p.IsRoad ? EdgePoint(p.Chosen) : SpotPoint(p.Chosen);
                Pill(c, at + new Vector2(0, -size * 0.38f), $"#{p.Rank}", p.Rank <= 3 ? Medals[p.Rank - 1] : RatingColor(p.Rating), size * 0.18f);
            }
    }

    private static Color RatingColor(double rating) =>
        new Color(0.85f, 0.2f, 0.2f).Lerp(new Color(0.2f, 0.7f, 0.3f), (float)Math.Clamp(rating / 100, 0, 1));

    private static void Pill(CanvasItem c, Vector2 at, string text, Color fill, float height)
    {
        var font = ThemeDB.FallbackFont;
        int fontSize = (int)(height * 0.8f);
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X + height * 0.8f;
        var rect = new Rect2(at - new Vector2(width / 2, height / 2), new Vector2(width, height));
        FlatIcons.Rounded(c, rect, fill, height / 2, shadow: true);
        var ink = fill.Luminance > 0.6f ? Ui.Text : Godot.Colors.White;
        c.DrawString(font, new Vector2(rect.Position.X, at.Y + fontSize * 0.36f), text, HorizontalAlignment.Center, width, fontSize, ink);
    }
}
