using System;
using Godot;

/// <summary>
/// One square button on the action bar, colonist style: light blue with a white edge, a picture, and an optional count badge
/// (pieces or cards left). Faded when disabled, gold-edged when its build mode is on. The picture is drawn by
/// <see cref="DrawIcon"/>, so the art pass can swap it. The tooltip carries the name, cost and why it's disabled.
/// </summary>
public partial class ActionTile : Control
{
    public static readonly Vector2 TileSize = new(80, 80);

    private bool _hover;

    public bool Enabled { get; private set; }
    public bool Selected { get; private set; }
    public int? Badge { get; private set; }

    /// <summary>A green check in the corner while enabled (the trade send buttons: "ready to send").</summary>
    public bool Check { get; set; }

    /// <summary>Draws the picture centered at a point, about the given size across, in the given ink.</summary>
    public Action<CanvasItem, Vector2, float, Color>? DrawIcon { get; set; }

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
        var rect = new Rect2(Vector2.Zero, Size);
        var outer = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true, ShadowColor = new Color(0, 0, 0, 0.3f), ShadowSize = Enabled ? 3 : 1 };
        outer.SetCornerRadiusAll(8);
        if (Selected)
        {
            outer.BorderColor = Ui.Gold;
            outer.SetBorderWidthAll(4);
        }
        DrawStyleBox(outer, rect);
        var fill = Enabled ? (_hover ? Ui.ButtonBlue.Lightened(0.15f) : Ui.ButtonBlue) : new Color(0.8f, 0.87f, 0.92f);
        FlatIcons.Rounded(this, rect.Grow(-4), fill, 6);
        var ink = Enabled ? Ui.ButtonInk : new Color(0.6f, 0.68f, 0.75f);
        DrawIcon?.Invoke(this, rect.GetCenter(), Size.X * 0.55f, ink);
        if (Check && Enabled)
        {
            var at = new Vector2(Size.X - 12, 12);
            DrawCircle(at, 10, Ui.Good);
            DrawPolyline(new[] { at + new Vector2(-5, 0), at + new Vector2(-1.5f, 4), at + new Vector2(5, -4) }, Colors.White, 2.5f, true);
        }
        if (Badge is { } badge)
        {
            var at = new Vector2(Size.X - 6, 6);
            var box = new Rect2(at - new Vector2(22, 8), new Vector2(26, 20));
            FlatIcons.Rounded(this, box, new Color(0.45f, 0.55f, 0.65f, Enabled ? 1 : 0.6f), 4);
            Ui.DrawCentered(this, box.GetCenter(), badge.ToString(), 14, Colors.White, 30);
        }
    }
}
