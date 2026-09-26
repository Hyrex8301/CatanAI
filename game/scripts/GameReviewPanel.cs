using System;
using System.Linq;
using Catan.AI;
using Catan.UI;
using Godot;

/// <summary>
/// Game review, from the results screen: a score for your whole game, how many plays were as good as the bots' pick, what
/// your plays cost in victory points, and the plays that cost the most (with the bots' pick and why). Back returns to the
/// results.
/// </summary>
public partial class GameReviewPanel : Control
{
    /// <summary>Most costly plays listed.</summary>
    public const int ShownMistakes = 8;

    private readonly VBoxContainer _body = new();

    public event Action? Back, NewGame, MainMenu;

    public GameReviewPanel()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 14, margin: 22));
        center.AddChild(panel);
        _body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(_body);
    }

    public void ShowWaiting()
    {
        Clear();
        _body.AddChild(Ui.Label("Reviewing your game…", 22));
        _body.AddChild(Ui.Label("The bots are grading every decision you made, from what you could see at the time.", 15, Ui.MutedText));
        Visible = true;
    }

    public void Show(GameReview review, string? history)
    {
        Clear();
        _body.AddChild(Ui.Label($"Game review: {review.Score:0} / 100", 24, Tone(review.Score)));
        _body.AddChild(Wrapped(review.Plays.Count == 0 ? "No decisions to grade in this game."
            : $"{review.BestPicks} of {review.Plays.Count} plays were the bots' pick or as good. Your plays cost about {review.TotalCostVp:0.0} points in all, by the bots' reckoning.", 16, Ui.Text));
        if (history is not null)
            _body.AddChild(Wrapped($"Your game reviews: {history}", 14, Ui.MutedText));
        _body.AddChild(new HSeparator());

        var mistakes = review.Mistakes.Take(ShownMistakes).ToList();
        _body.AddChild(Ui.Label(mistakes.Count == 0 ? "No costly plays. Well played!" : "The plays that cost you most", 18));
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 4);
        foreach (var p in mistakes)
        {
            rows.AddChild(Wrapped($"Round {p.Round}: {p.Yours.Description}   about {p.CostVp:0.0} {(Math.Round(p.CostVp, 1) == 1 ? "point" : "points")} worse", 16,
                p.CostVp >= 1 ? Ui.Bad : Ui.Text));
            rows.AddChild(Wrapped($"      Bots' pick: {p.Best.Description}: {string.Join("; ", p.Best.Reasons.Take(2))}", 14, Ui.MutedText));
        }
        if (mistakes.Count > 0)
        {
            var scroll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                CustomMinimumSize = new Vector2(740, Math.Min(460, 56 * mistakes.Count)),
            };
            scroll.AddChild(rows);
            _body.AddChild(scroll);
        }

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.AddChild(Button("Back to results", () => { Visible = false; Back?.Invoke(); }));
        buttons.AddChild(Button("New game", () => NewGame?.Invoke()));
        buttons.AddChild(Button("Main menu", () => MainMenu?.Invoke()));
        _body.AddChild(buttons);
        Visible = true;
    }

    private static Color Tone(double rating) => rating >= 90 ? Ui.Good : rating >= 60 ? Ui.Text : Ui.Bad;

    private static Label Wrapped(string text, int size, Color color)
    {
        var label = Ui.Label(text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(720, 0);
        return label;
    }

    private static Button Button(string text, Action pressed)
    {
        var b = Ui.Button(text, 17);
        b.Pressed += pressed;
        return b;
    }

    private void Clear()
    {
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }
}
