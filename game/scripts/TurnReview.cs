using System;
using System.Collections.Generic;
using System.Linq;
using Catan.AI;
using Godot;

/// <summary>One of your plays in Position practice: your move's grade and the bots' pick at that moment.</summary>
public sealed record GradedPlay(MoveRating Yours, MoveRating Best, int Choices);

/// <summary>
/// Position practice, after you end your turn: every play you made, ranked by the bots among the moves you had at that
/// moment (with the bots' pick and why when yours wasn't it), a score for the turn, then New position / Play it out / Menu.
/// </summary>
public sealed class TurnReview
{
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _body;
    private readonly Vector2 _center;
    private readonly Action _newPosition, _playOut, _menu;

    public TurnReview(Control parent, Vector2 center, Action newPosition, Action playOut, Action menu)
    {
        _center = center;
        (_newPosition, _playOut, _menu) = (newPosition, playOut, menu);
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(600, 0) };
        _panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 10, margin: 18));
        parent.AddChild(_panel);
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(_body);
        _panel.Resized += () => _panel.Position = _center - _panel.Size / 2;
    }

    public void Close() => _panel.Visible = false;

    /// <summary>While the bots are still grading your last plays.</summary>
    public void ShowWaiting()
    {
        Clear();
        _body.AddChild(Ui.Label("Ranking your plays…", 20));
        _body.AddChild(Ui.Label("The bots play the position out many times for every move you could have made.", 14, Ui.MutedText));
        Open();
    }

    public void Show(IReadOnlyList<GradedPlay> plays, int ungraded)
    {
        Clear();
        double score = plays.Count == 0 ? 0 : plays.Average(p => p.Yours.Rating);
        int picks = plays.Count(p => p.Yours.Rank == 1);
        _body.AddChild(Ui.Label($"Your turn: {score:0} / 100", 22, Tone(score)));
        _body.AddChild(Ui.Label(plays.Count == 0 ? "No graded plays." : $"{picks} of {plays.Count} plays were the bots' pick.", 15));
        _body.AddChild(new HSeparator());

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        int n = 0;
        foreach (var play in plays)
        {
            var mine = play.Yours;
            rows.AddChild(Wrapped($"{++n}. {mine.Description}   #{mine.Rank} of {play.Choices} ({mine.Rating:0})", 16, Tone(mine.Rating)));
            if (mine.Rank == 1)
                rows.AddChild(Wrapped($"      The bots' pick too: {string.Join("; ", mine.Reasons.Take(2))}", 14, Ui.MutedText));
            else
                rows.AddChild(Wrapped($"      Bots' pick: {play.Best.Description}: {string.Join("; ", play.Best.Reasons.Take(2))}", 14, Ui.MutedText));
        }
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(580, Math.Min(440, 58 * Math.Max(1, plays.Count))),
        };
        rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(rows);
        _body.AddChild(scroll);
        if (ungraded > 0)
            _body.AddChild(Ui.Label(ungraded == 1 ? "1 trade offer isn't graded." : $"{ungraded} trade offers aren't graded.", 14, Ui.MutedText));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.AddChild(Button("New position", _newPosition));
        buttons.AddChild(Button("Play it out", _playOut));
        buttons.AddChild(Button("Menu", _menu));
        _body.AddChild(buttons);
        Open();
    }

    private static Color Tone(double rating) => rating >= 90 ? Ui.Good : rating >= 60 ? Ui.Text : Ui.Bad;

    private static Label Wrapped(string text, int size, Color color)
    {
        var label = Ui.Label(text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(560, 0);
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

    private void Open()
    {
        _panel.Visible = true;
        _panel.ResetSize();
        _panel.Position = _center - _panel.Size / 2;
    }
}
