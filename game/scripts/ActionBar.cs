using System;
using System.Collections.Generic;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The colonist-style controls bottom right: a row of square buttons (Trade, Dev card, Road, Settlement, City, End Turn)
/// with a status box ("Your Turn", "Place Settlement", ...) and a timer box above them. Button states come from
/// <see cref="ActionBarModel"/>; clicks raise <see cref="Clicked"/> and the game screen decides what happens.
/// </summary>
public sealed class ActionBar
{
    private readonly Dictionary<BarItem, ActionTile> _tiles = new();
    private readonly Label _status, _timer;
    private readonly StatusAvatar _avatar;
    private bool _tradeOpen, _canEnd;

    public event Action<BarItem>? Clicked;

    public ActionBar(Control parent, Vector2 buttonsAt, Rect2 statusRect, Rect2 timerRect, SeatColor you)
    {
        float x = buttonsAt.X;
        foreach (var item in new[] { BarItem.Trade, BarItem.BuyDev, BarItem.Road, BarItem.Settlement, BarItem.City, BarItem.EndTurn })
        {
            var tile = new ActionTile { Position = new Vector2(x, buttonsAt.Y), Size = ActionTile.TileSize, DrawIcon = IconFor(item) };
            tile.Clicked += () => Clicked?.Invoke(item);
            parent.AddChild(tile);
            _tiles[item] = tile;
            x += ActionTile.TileSize.X + 4;
        }

        var status = new PanelContainer { Position = statusRect.Position, Size = statusRect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        status.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 6, margin: 4));
        var row = new HBoxContainer();
        _avatar = new StatusAvatar(you) { CustomMinimumSize = new Vector2(34, 34) };
        row.AddChild(_avatar);
        _status = Ui.Label("", 17);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.ClipText = true;
        row.AddChild(_status);
        status.AddChild(row);
        parent.AddChild(status);

        var timer = new PanelContainer { Position = timerRect.Position, Size = timerRect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        timer.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 6, margin: 4));
        _timer = Ui.Label("", 18);
        _timer.HorizontalAlignment = HorizontalAlignment.Center;
        _timer.VerticalAlignment = VerticalAlignment.Center;
        timer.AddChild(_timer);
        parent.AddChild(timer);
    }

    public void Update(PlayerView v, IReadOnlyList<GameAction>? legal, BuildMode mode, bool tradeOpen)
    {
        _tradeOpen = tradeOpen;
        foreach (var (item, tile) in _tiles)
        {
            var state = ActionBarModel.State(item, v, legal);
            bool selected = mode != BuildMode.None && ActionBarModel.ModeOf(item) == mode;
            if (item == BarItem.EndTurn)
                _canEnd = state.Enabled;
            string tip = item == BarItem.Trade && tradeOpen ? "Close the trade panel (Esc)" : state.Tooltip;
            tile.Set(state.Enabled, tip, selected, item is BarItem.Road or BarItem.Settlement or BarItem.City ? state.Left : null);
        }
    }

    /// <summary>The short status text ("Your Turn") and its longer help as a tooltip; red for a refused move.</summary>
    public void SetStatus(string text, string? help = null, bool error = false)
    {
        _status.Text = text;
        _status.TooltipText = help ?? "";
        _status.AddThemeColorOverride("font_color", error ? Ui.Bad : Ui.Text);
    }

    public void SetTimer(string text) => _timer.Text = text;

    private Action<CanvasItem, Vector2, float, Color> IconFor(BarItem item) => item switch
    {
        BarItem.Trade => (c, at, s, ink) => { if (_tradeOpen) Cross(c, at, s, ink); else TradeIcon(c, at, s, ink); },
        BarItem.BuyDev => (c, at, s, _) => Icons.Skin.CardBack(c, new Rect2(at - new Vector2(s * 0.34f, s * 0.46f), new Vector2(s * 0.68f, s * 0.92f)), true),
        BarItem.Road => (c, at, s, ink) => Piece(c, new[] { at + new Vector2(-s * 0.1f, -s * 0.45f), at + new Vector2(s * 0.1f, -s * 0.45f), at + new Vector2(s * 0.1f, s * 0.45f), at + new Vector2(-s * 0.1f, s * 0.45f) }, ink),
        BarItem.Settlement => (c, at, s, ink) => Piece(c, new[]
        {
            at + new Vector2(-s * 0.36f, s * 0.36f), at + new Vector2(-s * 0.36f, -s * 0.05f), at + new Vector2(0, -s * 0.4f),
            at + new Vector2(s * 0.36f, -s * 0.05f), at + new Vector2(s * 0.36f, s * 0.36f),
        }, ink),
        BarItem.City => (c, at, s, ink) => Piece(c, new[]
        {
            at + new Vector2(-s * 0.46f, s * 0.36f), at + new Vector2(-s * 0.46f, -s * 0.12f), at + new Vector2(-s * 0.22f, -s * 0.4f),
            at + new Vector2(0, -s * 0.12f), at + new Vector2(0, -s * 0.02f), at + new Vector2(s * 0.46f, -s * 0.02f), at + new Vector2(s * 0.46f, s * 0.36f),
        }, ink),
        _ => (c, at, s, ink) => { if (_canEnd) Skip(c, at, s, ink); else Hourglass(c, at, s, ink); },
    };

    private static void Piece(CanvasItem c, Vector2[] shape, Color ink)
    {
        c.DrawColoredPolygon(shape, ink.Lightened(0.25f));
        var closed = new Vector2[shape.Length + 1];
        shape.CopyTo(closed, 0);
        closed[^1] = shape[0];
        c.DrawPolyline(closed, ink.Darkened(0.2f), 2, true);
    }

    /// <summary>Two cards with arrows going round them: trading.</summary>
    private static void TradeIcon(CanvasItem c, Vector2 at, float s, Color ink)
    {
        FlatIcons.Rounded(c, new Rect2(at + new Vector2(-s * 0.42f, -s * 0.42f), new Vector2(s * 0.42f, s * 0.56f)), new Color(0.55f, 0.75f, 0.45f), 3);
        FlatIcons.Rounded(c, new Rect2(at + new Vector2(0, -s * 0.12f), new Vector2(s * 0.42f, s * 0.56f)), new Color(0.75f, 0.5f, 0.45f), 3);
        c.DrawArc(at, s * 0.42f, -2.6f, -1.0f, 12, ink, 3, true);
        c.DrawArc(at, s * 0.42f, 0.5f, 2.1f, 12, ink, 3, true);
        c.DrawColoredPolygon(new[] { at + new Vector2(s * 0.28f, -s * 0.42f), at + new Vector2(s * 0.12f, -s * 0.48f), at + new Vector2(s * 0.2f, -s * 0.28f) }, ink);
        c.DrawColoredPolygon(new[] { at + new Vector2(-s * 0.28f, s * 0.42f), at + new Vector2(-s * 0.12f, s * 0.48f), at + new Vector2(-s * 0.2f, s * 0.28f) }, ink);
    }

    private static void Cross(CanvasItem c, Vector2 at, float s, Color ink)
    {
        float r = s * 0.36f;
        c.DrawLine(at + new Vector2(-r, -r), at + new Vector2(r, r), Colors.White, s * 0.2f, true);
        c.DrawLine(at + new Vector2(r, -r), at + new Vector2(-r, r), Colors.White, s * 0.2f, true);
        c.DrawLine(at + new Vector2(-r, -r), at + new Vector2(r, r), ink, s * 0.12f, true);
        c.DrawLine(at + new Vector2(r, -r), at + new Vector2(-r, r), ink, s * 0.12f, true);
    }

    private static void Hourglass(CanvasItem c, Vector2 at, float s, Color ink)
    {
        float w = s * 0.3f, h = s * 0.42f;
        c.DrawRect(new Rect2(at + new Vector2(-w * 1.2f, -h - 4), new Vector2(w * 2.4f, 5)), ink);
        c.DrawRect(new Rect2(at + new Vector2(-w * 1.2f, h - 1), new Vector2(w * 2.4f, 5)), ink);
        var glass = new[] { at + new Vector2(-w, -h), at + new Vector2(w, -h), at + new Vector2(w * 0.15f, 0), at + new Vector2(w, h), at + new Vector2(-w, h), at + new Vector2(-w * 0.15f, 0) };
        c.DrawColoredPolygon(glass, Colors.White);
        c.DrawPolyline(new[] { glass[0], glass[1], glass[2], glass[3], glass[4], glass[5], glass[0] }, ink, 2.5f, true);
        c.DrawColoredPolygon(new[] { at + new Vector2(-w * 0.7f, h - 1), at + new Vector2(w * 0.7f, h - 1), at + new Vector2(0, h * 0.45f) }, ink.Lightened(0.3f));
    }

    /// <summary>"&gt;&gt;": end your turn.</summary>
    private static void Skip(CanvasItem c, Vector2 at, float s, Color ink)
    {
        float r = s * 0.34f;
        foreach (float dx in new[] { -r * 0.55f, r * 0.45f })
        {
            var tri = new[] { at + new Vector2(dx - r * 0.5f, -r), at + new Vector2(dx + r * 0.5f, 0), at + new Vector2(dx - r * 0.5f, r) };
            c.DrawColoredPolygon(tri, Colors.White);
            c.DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, ink, 2.5f, true);
        }
    }
}

/// <summary>Your avatar in the status box.</summary>
public partial class StatusAvatar : Control
{
    private readonly SeatColor _color;

    public StatusAvatar(SeatColor color) => _color = color;

    public StatusAvatar() : this(SeatColor.White)
    {
    }

    public override void _Draw() => Ui.Avatar(this, Size / 2, Size.X / 2, _color, true);
}
