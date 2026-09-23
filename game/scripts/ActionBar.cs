using System;
using System.Collections.Generic;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The colonist-style bar bottom right: Trade and Buy Development Card, then Road / Settlement / City (each with its cost and
/// pieces left), the dice, and End Turn. Button states come from <see cref="ActionBarModel"/>; clicks raise
/// <see cref="Clicked"/> and the game screen decides what happens.
/// </summary>
public sealed class ActionBar
{
    private static readonly BoardSkin Pieces = new FlatSkin();
    private static readonly Color TileInk = new(0.25f, 0.28f, 0.33f);

    private readonly Dictionary<BarItem, ActionTile> _tiles = new();
    private readonly DiceView _dice;
    private Color _seatColor = Colors.White;

    public event Action<BarItem>? Clicked;

    public ActionBar(Control parent, Rect2 rect)
    {
        var panel = new Panel { Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle());
        parent.AddChild(panel);

        float x = 12, y = (rect.Size.Y - ActionTile.TileSize.Y) / 2 + 4;
        Tile(panel, BarItem.Trade, "Trade", null, ref x, y, (c, at, s) => TradeIcon(c, at, s));
        Tile(panel, BarItem.BuyDev, "Dev card", Costs.DevCard, ref x, y,
            (c, at, s) => Icons.Skin.CardBack(c, new Rect2(at - new Vector2(s * 0.34f, s * 0.46f), new Vector2(s * 0.68f, s * 0.92f)), true));
        x += 10;
        Tile(panel, BarItem.Road, "Road", Costs.Road, ref x, y,
            (c, at, s) => Pieces.Road(c, at + new Vector2(-s * 0.4f, s * 0.25f), at + new Vector2(s * 0.4f, -s * 0.25f), s * 2.2f, _seatColor));
        Tile(panel, BarItem.Settlement, "Settlement", Costs.Settlement, ref x, y, (c, at, s) => Pieces.Settlement(c, at, s * 3.2f, _seatColor));
        Tile(panel, BarItem.City, "City", Costs.City, ref x, y, (c, at, s) => Pieces.City(c, at, s * 2.4f, _seatColor));

        _dice = new DiceView { Position = new Vector2(x + 4, y - 6), Size = new Vector2(104, ActionTile.TileSize.Y) };
        _dice.Clicked += () => Clicked?.Invoke(BarItem.Roll);
        panel.AddChild(_dice);
        x += 116;

        var end = Tile(panel, BarItem.EndTurn, "End turn", null, ref x, y, (c, at, s) => EndIcon(c, at, s));
        end.Fill = new Color(0.82f, 0.95f, 0.84f);
    }

    public DiceView Dice => _dice;

    public void Update(PlayerView v, IReadOnlyList<GameAction>? legal, BuildMode mode, Color seatColor)
    {
        _seatColor = seatColor;
        foreach (var (item, tile) in _tiles)
        {
            var state = ActionBarModel.State(item, v, legal);
            bool selected = mode != BuildMode.None && ActionBarModel.ModeOf(item) == mode;
            tile.Set(state.Enabled, state.Tooltip, selected, state.Left);
        }
        var roll = ActionBarModel.State(BarItem.Roll, v, legal);
        _dice.Set(roll.Enabled, roll.Tooltip);
    }

    private ActionTile Tile(Control panel, BarItem item, string title, ResourceSet? cost, ref float x, float y, Action<CanvasItem, Vector2, float> icon)
    {
        var tile = new ActionTile { Title = title, Cost = cost, DrawIcon = icon, Position = new Vector2(x, y), Size = ActionTile.TileSize };
        tile.Clicked += () => Clicked?.Invoke(item);
        panel.AddChild(tile);
        _tiles[item] = tile;
        x += ActionTile.TileSize.X + 6;
        return tile;
    }

    /// <summary>Two arrows passing each other: give and get.</summary>
    private static void TradeIcon(CanvasItem c, Vector2 at, float s)
    {
        float w = s * 0.4f, h = s * 0.16f, head = s * 0.16f, thick = s * 0.09f;
        var top = at - new Vector2(0, h);
        c.DrawLine(top - new Vector2(w, 0), top + new Vector2(w - head * 0.5f, 0), TileInk, thick);
        c.DrawColoredPolygon(new[] { top + new Vector2(w, 0), top + new Vector2(w - head, -head * 0.7f), top + new Vector2(w - head, head * 0.7f) }, TileInk);
        var bottom = at + new Vector2(0, h);
        var green = new Color(0.2f, 0.6f, 0.3f);
        c.DrawLine(bottom + new Vector2(w, 0), bottom - new Vector2(w - head * 0.5f, 0), green, thick);
        c.DrawColoredPolygon(new[] { bottom - new Vector2(w, 0), bottom - new Vector2(w - head, -head * 0.7f), bottom - new Vector2(w - head, head * 0.7f) }, green);
    }

    /// <summary>A "skip to next" sign: a triangle and a bar.</summary>
    private static void EndIcon(CanvasItem c, Vector2 at, float s)
    {
        var ink = new Color(0.12f, 0.45f, 0.22f);
        float r = s * 0.3f;
        c.DrawColoredPolygon(new[] { at + new Vector2(-r * 0.8f, -r), at + new Vector2(r * 0.6f, 0), at + new Vector2(-r * 0.8f, r) }, ink);
        c.DrawRect(new Rect2(at + new Vector2(r * 0.7f, -r), new Vector2(r * 0.35f, r * 2)), ink);
    }
}
