using System;
using Catan.Core;
using Godot;

/// <summary>
/// One square button on the action bar: a picture, a name, an optional count badge (pieces or cards left) and the cost as
/// small card chips underneath. Greyed out when disabled, gold-edged when its build mode is on. The picture is drawn by
/// <see cref="DrawIcon"/>, so the art pass can swap it.
/// </summary>
public partial class ActionTile : Control
{
    public static readonly Vector2 TileSize = new(66, 112);

    private bool _hover;

    public string Title { get; set; } = "";
    public ResourceSet? Cost { get; set; }
    public bool Enabled { get; private set; }
    public bool Selected { get; private set; }
    public int? Badge { get; private set; }

    /// <summary>Draws the picture centered at a point, about the given size across.</summary>
    public Action<CanvasItem, Vector2, float>? DrawIcon { get; set; }

    /// <summary>Fill for an enabled tile (End Turn is green).</summary>
    public Color Fill { get; set; } = Colors.White;

    public event Action? Clicked;

    public ActionTile()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public void Set(bool enabled, string tooltip, bool selected = false, int? badge = null)
    {
        Enabled = enabled;
        Selected = selected;
        Badge = badge;
        TooltipText = tooltip;
        MouseDefaultCursorShape = enabled ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Enabled && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Clicked?.Invoke();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        float lift = _hover && Enabled ? 3 : 0;
        var rect = new Rect2(new Vector2(0, 6 - lift), new Vector2(TileSize.X, TileSize.Y - 34));
        var style = Ui.PanelStyle(Enabled ? Fill : new Color(0.9f, 0.91f, 0.93f), radius: 10);
        style.ShadowSize = Enabled ? 4 : 0;
        style.BorderColor = Selected ? Ui.Gold : new Color(0.78f, 0.81f, 0.86f);
        style.SetBorderWidthAll(Selected ? 4 : 1);
        DrawStyleBox(style, rect);

        DrawIcon?.Invoke(this, rect.GetCenter() - new Vector2(0, 8), 40);
        Ui.DrawCentered(this, rect.GetCenter() + new Vector2(0, 26), Title, 12, Ui.Text, TileSize.X);
        if (!Enabled)
            FlatIcons.Rounded(this, rect, new Color(0.92f, 0.93f, 0.95f, 0.6f), 10);

        if (Badge is { } badge)
        {
            var at = rect.Position + new Vector2(rect.Size.X - 6, 6);
            DrawCircle(at, 11, Enabled ? Ui.Text : Ui.MutedText);
            Ui.DrawCentered(this, at, badge.ToString(), 12, Colors.White, 24);
        }

        if (Cost is { } cost)
        {
            // One small chip per card, centered under the tile.
            int n = cost.Total;
            const float w = 9, h = 13, gap = 2;
            float x = TileSize.X / 2 - (n * w + (n - 1) * gap) / 2;
            for (int r = 0; r < GameConstants.ResourceCount; r++)
                for (int k = 0; k < cost[r]; k++, x += w + gap)
                    FlatIcons.Rounded(this, new Rect2(x, TileSize.Y - 22, w, h), Icons.Skin.ResourceColor(r), 2);
        }
    }
}
