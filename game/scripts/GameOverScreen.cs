using System;
using System.Collections.Generic;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The end of the game: who won, each player's points by source (settlements, cities, Victory Point cards, Longest Road,
/// Largest Army, now revealed), a few stats, and a chart of how often each number was rolled. Buttons: View board (close),
/// New game, Main menu.
/// </summary>
public partial class GameOverScreen : Control
{
    private static readonly Vector2 PanelSize = new(980, 640);
    private static readonly BoardSkin Pieces = new FlatSkin();

    private GameOverSummary? _summary;
    private IReadOnlyList<SeatColor> _colors = Array.Empty<SeatColor>();
    private int _viewer;
    private Rect2 _panel;
    private Vector2 _extra;
    private HBoxContainer? _buttons;

    public event Action? NewGame, MainMenu;

    public GameOverScreen()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    public void Show(GameOverSummary summary, IReadOnlyList<SeatColor> colors, int viewer)
    {
        _summary = summary;
        _colors = colors;
        _viewer = viewer;
        foreach (var child in GetChildren())
            child.QueueFree();
        var buttons = _buttons = new HBoxContainer { Size = new Vector2(500, 50) };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        buttons.AddChild(Button("View board", () => Visible = false));
        buttons.AddChild(Button("New game", () => NewGame?.Invoke()));
        buttons.AddChild(Button("Main menu", () => MainMenu?.Invoke()));
        AddChild(buttons);
        Place();
        Visible = true;
        QueueRedraw();
    }

    /// <summary>Keeps the panel centred when the window changes size.</summary>
    public void Relayout(Vector2 extra)
    {
        _extra = extra;
        Place();
    }

    private void Place()
    {
        _panel = new Rect2((ScreenLayout.Design + _extra - PanelSize) / 2, PanelSize);
        if (_buttons is not null && IsInstanceValid(_buttons))
            _buttons.Position = _panel.Position + new Vector2(PanelSize.X - 520, PanelSize.Y - 70);
        QueueRedraw();
    }

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(150, 46), FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += onClick;
        return button;
    }

    public override void _Draw()
    {
        if (_summary is not { } s)
            return;
        DrawRect(new Rect2(Vector2.Zero, ScreenLayout.Design + _extra), new Color(0, 0, 0, 0.5f));
        DrawStyleBox(Ui.PanelStyle(Ui.Cream, radius: 14), _panel);
        var p = _panel.Position;

        string title = s.Winner == _viewer ? "You won!" : s.Winner >= 0 ? $"{_colors[s.Winner]} won" : "The game ended in a draw";
        Ui.DrawCentered(this, p + new Vector2(PanelSize.X / 2, 44), title, 36, s.Winner >= 0 ? Ui.SeatInk(_colors[s.Winner]) : Ui.Text, PanelSize.X);
        Ui.DrawCentered(this, p + new Vector2(PanelSize.X / 2, 82), $"after {s.Turns} turns", 15, Ui.MutedText, PanelSize.X);

        // Standings.
        float y = p.Y + 116;
        for (int i = 0; i < s.Standings.Count; i++, y += 92)
            Row(s.Standings[i], i + 1, new Rect2(p.X + 24, y, 600, 84));

        // Dice chart.
        Dice(s.Rolls, new Rect2(p.X + 650, p.Y + 120, 306, 360));
    }

    private void Row(FinalScore f, int rank, Rect2 r)
    {
        var fill = f.Seat == _summary!.Winner ? Ui.Highlight : new Color(1, 1, 1, 0.6f);
        var style = Ui.PanelStyle(fill, radius: 10);
        style.ShadowSize = 1;
        if (f.Seat == _summary.Winner)
        {
            style.BorderColor = Ui.Gold;
            style.SetBorderWidthAll(3);
        }
        DrawStyleBox(style, r);
        Ui.DrawCentered(this, r.Position + new Vector2(22, r.Size.Y / 2), rank.ToString(), 22, Ui.MutedText, 30);
        Ui.Avatar(this, r.Position + new Vector2(70, r.Size.Y / 2), 26, _colors[f.Seat], f.Seat == _viewer);
        Ui.DrawLeft(this, r.Position + new Vector2(108, 26), f.Seat == _viewer ? "You" : _colors[f.Seat].ToString(), 19, Ui.Text);
        Ui.DrawLeft(this, r.Position + new Vector2(108, 56), $"{f.Knights} knights · road {f.RoadLength} · {f.CardsFromRolls} cards from rolls", 13, Ui.MutedText);

        // Points by source: a little picture and a count for each.
        var color = Ui.SeatColor(_colors[f.Seat]);
        float x = r.Position.X + 380, mid = r.Position.Y + r.Size.Y / 2;
        x = Source(x, mid, f.Settlements, (c, at) => Pieces.Settlement(c, at, 90, color));
        x = Source(x, mid, f.Cities, (c, at) => Pieces.City(c, at, 70, color));
        x = Source(x, mid, f.VictoryCards, (c, at) => Icons.Skin.CardFace(c, new Rect2(at - new Vector2(10, 14), new Vector2(20, 28)), true, (int)DevCardType.VictoryPoint, false));
        if (f.LongestRoad)
            x = Source(x, mid, 2, (c, at) => Icons.Skin.Stat(c, at, 24, StatIcon.Road, Ui.Text), award: true);
        if (f.LargestArmy)
            Source(x, mid, 2, (c, at) => Icons.Skin.Stat(c, at, 24, StatIcon.Knight, Ui.Text), award: true);

        var vp = r.Position + new Vector2(r.Size.X - 40, r.Size.Y / 2);
        DrawCircle(vp, 26, Ui.Text);
        Ui.DrawCentered(this, vp, f.Total.ToString(), 24, Colors.White, 60);
    }

    /// <summary>One source of points: its picture with "×n" under it (skipped when zero).</summary>
    private float Source(float x, float mid, int count, Action<CanvasItem, Vector2> icon, bool award = false)
    {
        if (count == 0)
            return x;
        if (award)
            DrawCircle(new Vector2(x + 16, mid - 8), 16, Ui.Gold);
        icon(this, new Vector2(x + 16, mid - 8));
        Ui.DrawCentered(this, new Vector2(x + 16, mid + 22), award ? "+2" : $"×{count}", 13, Ui.Text, 40);
        return x + 40;
    }

    private void Dice(int[] rolls, Rect2 r)
    {
        var style = Ui.PanelStyle(new Color(1, 1, 1, 0.6f), radius: 10);
        style.ShadowSize = 1;
        DrawStyleBox(style, r);
        Ui.DrawCentered(this, r.Position + new Vector2(r.Size.X / 2, 22), "Dice rolls", 17, Ui.Text, r.Size.X);
        int max = 1;
        for (int n = 2; n <= 12; n++)
            max = Math.Max(max, rolls[n]);
        float barW = (r.Size.X - 30) / 11, bottom = r.End.Y - 34, height = r.Size.Y - 100;
        for (int n = 2; n <= 12; n++)
        {
            float h = height * rolls[n] / max;
            float x = r.Position.X + 15 + (n - 2) * barW;
            var bar = new Rect2(x + 3, bottom - h, barW - 6, h);
            FlatIcons.Rounded(this, bar, n == 7 ? Ui.Bad : n is 6 or 8 ? new Color(0.95f, 0.55f, 0.3f) : Ui.BadgeBlue, 3);
            Ui.DrawCentered(this, new Vector2(x + barW / 2, bottom - h - 10), rolls[n].ToString(), 12, Ui.Text, barW);
            Ui.DrawCentered(this, new Vector2(x + barW / 2, bottom + 14), n.ToString(), 13, Ui.MutedText, barW);
        }
    }
}
