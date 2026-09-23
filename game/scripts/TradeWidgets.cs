using System;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>A set of cards drawn small, one mini card per resource with "×n" beside it (offer strips).</summary>
public partial class MiniCards : Control
{
    private static readonly Vector2 Card = new(20, 28);
    private ResourceSet _cards;

    public MiniCards() => MouseFilter = MouseFilterEnum.Ignore;

    public void Show(ResourceSet cards)
    {
        _cards = cards;
        int types = 0;
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (cards[r] > 0)
                types++;
        CustomMinimumSize = new Vector2(Math.Max(1, types) * (Card.X + 22), Card.Y + 4);
        TooltipText = GameText.Cards(cards);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float x = 0, y = (Size.Y - Card.Y) / 2;
        for (int r = 0; r < GameConstants.ResourceCount; r++)
        {
            if (_cards[r] == 0)
                continue;
            Icons.Skin.CardFace(this, new Rect2(new Vector2(x, y), Card), false, r, false);
            Ui.DrawLeft(this, new Vector2(x + Card.X + 3, Size.Y / 2), $"×{_cards[r]}", 13, Ui.Text);
            x += Card.X + 22;
        }
    }
}

/// <summary>
/// An opponent's answer to one of your offers: their color with a check (accepted), cross (declined), dots (waiting) or a
/// return arrow (countered). Clickable when <see cref="Clickable"/> (an accepter you can trade with).
/// </summary>
public partial class StatusChip : Control
{
    private Color _seat;
    private OfferAnswer _answer;
    private bool _hover;

    public bool Clickable { get; private set; }
    public event Action? Clicked;
    public event Action? RightClicked;

    public StatusChip()
    {
        CustomMinimumSize = new Vector2(34, 34);
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public void Show(Color seat, OfferAnswer answer, bool clickable, string tooltip)
    {
        _seat = seat;
        _answer = answer;
        Clickable = clickable;
        TooltipText = tooltip;
        MouseDefaultCursorShape = clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Clickable || @event is not InputEventMouseButton { Pressed: true } button)
            return;
        if (button.ButtonIndex == MouseButton.Left)
            Clicked?.Invoke();
        else if (button.ButtonIndex == MouseButton.Right)
            RightClicked?.Invoke();
        AcceptEvent();
    }

    public override void _Draw()
    {
        var c = Size / 2;
        float r = 15;
        if (Clickable)
            DrawCircle(c, r + (_hover ? 5 : 3), new Color(0.2f, 0.7f, 0.3f, _hover ? 0.9f : 0.6f));
        DrawCircle(c, r, _seat);
        DrawArc(c, r, 0, Mathf.Tau, 32, _seat.Darkened(0.3f), 1.5f, true);
        var ink = _seat.Luminance > 0.7f ? Ui.Text : Colors.White;
        switch (_answer)
        {
            case OfferAnswer.Accepted:
                DrawPolyline(new[] { c + new Vector2(-7, 0), c + new Vector2(-2, 6), c + new Vector2(8, -6) }, ink, 3.5f, true);
                break;
            case OfferAnswer.Declined:
                DrawLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), ink, 3.5f, true);
                DrawLine(c + new Vector2(6, -6), c + new Vector2(-6, 6), ink, 3.5f, true);
                break;
            case OfferAnswer.Countered:
                DrawArc(c, 7, -0.3f, Mathf.Pi * 1.4f, 16, ink, 3, true);
                DrawColoredPolygon(new[] { c + new Vector2(7, -9), c + new Vector2(11, 0), c + new Vector2(2, -2) }, ink);
                break;
            default:
                for (int i = -1; i <= 1; i++)
                    DrawCircle(c + new Vector2(i * 6, 0), 2.2f, ink);
                break;
        }
    }
}
