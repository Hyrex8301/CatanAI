using System;
using Godot;

/// <summary>
/// One card (or a stack of the same card) drawn through <see cref="Icons.Skin"/>: a face with a count badge, lifted on hover.
/// Raises <see cref="Clicked"/> on a left click when <see cref="Clickable"/>.
/// </summary>
public partial class CardView : Control
{
    private bool _hover;

    public bool IsDev { get; private set; }
    public int Type { get; private set; }
    public int Count { get; private set; }
    public bool Dimmed { get; private set; }
    public bool Clickable { get; set; } = true;

    /// <summary>Text in a small label under the count (e.g. "1 new").</summary>
    public string? Note { get; private set; }

    public event Action<CardView>? Clicked;

    public CardView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public void Set(bool isDev, int type, int count, bool dimmed = false, string? note = null)
    {
        IsDev = isDev;
        Type = type;
        Count = count;
        Dimmed = dimmed;
        Note = note;
        MouseDefaultCursorShape = Clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
    }

    /// <summary>A right click (the trade window uses it to take a card back out).</summary>
    public event Action<CardView>? RightClicked;

    public override void _GuiInput(InputEvent @event)
    {
        if (Clickable && @event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.Left)
                Clicked?.Invoke(this);
            else if (button.ButtonIndex == MouseButton.Right)
                RightClicked?.Invoke(this);
            else
                return;
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        float lift = _hover && Clickable ? 8 : 0;
        var rect = new Rect2(new Vector2(0, 10 - lift), new Vector2(Size.X, Size.Y - 10));
        Icons.Skin.CardFace(this, rect, IsDev, Type, Dimmed);
        if (Count > 0)
            Ui.Badge(this, rect.Position + new Vector2(rect.Size.X, 2), Count);
        if (Note is not null)
        {
            var at = rect.Position + new Vector2(rect.Size.X / 2, rect.Size.Y - 10);
            FlatIcons.Rounded(this, new Rect2(at - new Vector2(22, 8), new Vector2(44, 16)), Ui.Text, 8);
            Ui.DrawCentered(this, at, Note, 11, Colors.White, 60);
        }
    }
}
