using System;
using System.Collections.Generic;
using System.Linq;
using Catan.AI;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Placement practice: a new board with the players before you already placed; click where you'd put your first
/// settlement, then see how the bots rate every spot: your pick's rank and rating, the top three marked gold, silver and
/// bronze with their pips and resources, and optionally every spot's rating. Next board deals another.
/// </summary>
public partial class PracticeScreen : Control
{
    private static readonly Rect2 BoardRect = new(20, 20, 1120, 860);
    private static readonly Rect2 PanelRect = new(1160, 20, 420, 860);
    private static readonly SeatColor[] Colors = { SeatColor.Red, SeatColor.Blue, SeatColor.White, SeatColor.Orange };
    private static readonly Color[] Medals = { new(0.98f, 0.78f, 0.18f), new(0.78f, 0.8f, 0.84f), new(0.8f, 0.52f, 0.25f) };

    private PlacementCoach _coach = null!;
    private BoardView _board = null!;
    private Label _title = null!, _situation = null!, _result = null!, _details = null!;
    private CheckBox _showAll = null!;

    private GameState _state = null!;
    private int _seat;
    private IReadOnlyList<SpotRating> _ratings = Array.Empty<SpotRating>();
    private int _picked = -1;
    private int _boards, _perfect;
    private double _ratingTotal;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // let clicks through to the board (the panel still stops the ones over it)
        var background = new ColorRect { Color = Ui.Sea, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        string weightsPath = ProjectSettings.GlobalizePath("res://bots/best.json");
        _coach = new PlacementCoach(System.IO.File.Exists(weightsPath) ? BotWeights.Load(weightsPath) : new BotWeights());

        _board = new BoardView { HoverTargetsOnly = true };
        _board.Setup(BoardRect);
        _board.Clicked += OnClicked;
        _board.Overlay = DrawMarkers;
        AddChild(_board);

        AddChild(Ui.Panel(null, PanelRect, out var panel));
        _title = Ui.Label("Placement practice", 28);
        panel.AddChild(_title);
        _situation = Wrapped(16, Ui.Text);
        panel.AddChild(_situation);
        panel.AddChild(new HSeparator());
        _result = Wrapped(20, Ui.Text);
        panel.AddChild(_result);
        _details = Wrapped(15, Ui.MutedText);
        panel.AddChild(_details);
        panel.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _showAll = new CheckBox { Text = "Show every spot's rating", Disabled = true };
        _showAll.AddThemeFontSizeOverride("font_size", 16);
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_disabled_color" })
            _showAll.AddThemeColorOverride(name, name == "font_disabled_color" ? Ui.MutedText : Ui.Text);
        _showAll.Toggled += _ => _board.Redraw();
        panel.AddChild(_showAll);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Button("Next board", Deal));
        buttons.AddChild(Button("Menu", () => GetTree().ChangeSceneToFile("res://scenes/Menu.tscn")));
        panel.AddChild(buttons);

        Deal();
        DevShots.Run(this);
    }


    private static Label Wrapped(int size, Color color)
    {
        var label = Ui.Label("", size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(PanelRect.Size.X - 30, 0);
        return label;
    }

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(180, 48), FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += onClick;
        return button;
    }

    private void Deal()
    {
        ulong seed = ulong.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SEED"), out ulong dev) && _boards == 0 && _picked < 0
            ? dev : (ulong)System.Random.Shared.NextInt64();
        _state = _coach.Deal(seed, out _seat);
        _ratings = _coach.Rate(_state, _seat);
        _picked = -1;
        _showAll.ButtonPressed = false;
        _showAll.Disabled = true;
        _board.Show(PlayerView.From(_state, _seat), Colors);
        _board.SetTargets(_ratings.Select(r => BoardHit.Vertex(r.Vertex)));

        string place = (_seat + 1) switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => "4th" };
        string before = _seat == 0 ? "Nobody has placed yet." : $"{string.Join(" and ", Colors.Take(_seat))} placed before you.";
        _situation.Text = $"You are {Colors[_seat]}, placing {place}. {before}\n\nClick the corner where you'd put your first settlement.";
        _result.Text = "";
        _details.Text = _boards > 0 ? Score() : "";
        _board.Redraw();
    }

    private void OnClicked(BoardHit hit)
    {
        if (_picked >= 0 || hit.Kind != HitKind.Vertex || _ratings.All(r => r.Vertex != hit.Id))
            return;
        _picked = hit.Id;
        var mine = _ratings.First(r => r.Vertex == _picked);
        var shown = _state.Clone();
        Rules.Apply(shown, new GameAction(ActionType.BuildSettlement, _seat, _picked), new RngChance(1));
        _board.Show(PlayerView.From(shown, _seat), Colors);
        _board.SetTargets(Array.Empty<BoardHit>());
        _showAll.Disabled = false;

        _boards++;
        _ratingTotal += mine.Rating;
        if (mine.Rank == 1)
            _perfect++;
        string grade = mine.Rank == 1 ? "Perfect! That's the bots' top spot."
            : mine.Rating >= 90 ? "Excellent pick."
            : mine.Rating >= 75 ? "Good pick."
            : mine.Rating >= 50 ? "OK, but there were better spots."
            : "Weak: several spots were much better.";
        _result.Text = $"{grade}\nYour spot: #{mine.Rank} of {_ratings.Count}, rating {mine.Rating:F0}";
        var lines = new List<string> { $"Yours: {Describe(mine)}", "" };
        for (int i = 0; i < Math.Min(3, _ratings.Count); i++)
            lines.Add($"{(i == 0 ? "Best" : i == 1 ? "2nd" : "3rd")}: {Describe(_ratings[i])}");
        lines.Add("");
        lines.Add(Score());
        _details.Text = string.Join("\n", lines);
        _board.Redraw();
    }

    private string Score() => $"This session: {_boards} board{(_boards == 1 ? "" : "s")}, {_perfect} top pick{(_perfect == 1 ? "" : "s")}, average rating {(_boards == 0 ? 0 : _ratingTotal / _boards):F0}";

    private static string Describe(SpotRating r)
    {
        var parts = Enumerable.Range(0, GameConstants.ResourceCount).Where(i => r.ResourcePips[i] > 0)
            .OrderByDescending(i => r.ResourcePips[i]).Select(i => $"{GameText.Resource(i)} {r.ResourcePips[i]}");
        string harbor = r.Harbor is { } h ? h == HarborType.Generic ? ", 3:1 harbor" : $", 2:1 {GameText.Resource((int)h)} harbor" : "";
        return $"{r.Pips} pips ({string.Join(", ", parts)}){harbor}, rating {r.Rating:F0}";
    }

    /// <summary>After a pick: medals on the top three, your pick's rank, and optionally every spot's rating.</summary>
    private void DrawMarkers(CanvasItem c)
    {
        if (_picked < 0)
            return;
        float size = _board.HexSize;
        if (_showAll.ButtonPressed)
            foreach (var r in _ratings.Skip(3))
                if (r.Vertex != _picked)
                    Pill(c, _board.VertexPoint(r.Vertex) - _board.Position, $"{r.Rating:F0}", RatingColor(r.Rating), size * 0.16f);
        for (int i = Math.Min(3, _ratings.Count) - 1; i >= 0; i--)
        {
            var at = _board.VertexPoint(_ratings[i].Vertex) - _board.Position;
            c.DrawArc(at, size * 0.26f, 0, Mathf.Tau, 32, Medals[i], 5, true);
            Pill(c, at + new Vector2(0, -size * 0.42f), $"#{i + 1}", Medals[i], size * 0.2f);
        }
        var mine = _ratings.First(r => r.Vertex == _picked);
        if (mine.Rank > 3)
        {
            var at = _board.VertexPoint(_picked) - _board.Position;
            c.DrawArc(at, size * 0.26f, 0, Mathf.Tau, 32, RatingColor(mine.Rating), 5, true);
            Pill(c, at + new Vector2(0, -size * 0.42f), $"#{mine.Rank}", RatingColor(mine.Rating), size * 0.2f);
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
