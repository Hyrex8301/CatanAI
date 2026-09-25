using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// A set of cards drawn colonist style: one card per resource with a count badge. With <see cref="AllFive"/> it shows all
/// five resources (a picker row) without badges. Clicks raise <see cref="Clicked"/> / <see cref="RightClicked"/>.
/// </summary>
public partial class CardStrip : Control
{
    private ResourceSet _cards;
    private readonly List<(Rect2 Rect, int Resource)> _hits = new();
    private int _hover = -1;

    public Vector2 CardSize { get; set; } = new(40, 56);
    public bool AllFive { get; set; }
    public bool Clickable { get; set; }

    public event Action<int>? Clicked;
    public event Action<int>? RightClicked;

    public CardStrip() => MouseFilter = MouseFilterEnum.Stop;

    public void Show(ResourceSet cards)
    {
        _cards = cards;
        int n = AllFive ? 5 : Enumerable.Range(0, 5).Count(r => cards[r] > 0);
        CustomMinimumSize = new Vector2(Math.Max(n, 1) * (CardSize.X + 8), CardSize.Y + 10);
        MouseDefaultCursorShape = Clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        TooltipText = AllFive ? "" : GameText.Cards(cards);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int hover = HitAt(motion.Position);
            if (hover != _hover)
            {
                _hover = hover;
                QueueRedraw();
            }
        }
        if (!Clickable || @event is not InputEventMouseButton { Pressed: true } button)
            return;
        int r = HitAt(button.Position);
        if (r < 0)
            return;
        if (button.ButtonIndex == MouseButton.Left)
            Clicked?.Invoke(r);
        else if (button.ButtonIndex == MouseButton.Right)
            RightClicked?.Invoke(r);
        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover >= 0)
        {
            _hover = -1;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _hits.Clear();
        float x = 0;
        for (int r = 0; r < 5; r++)
        {
            if (!AllFive && _cards[r] == 0)
                continue;
            float lift = Clickable && _hover == r ? 4 : 0;
            var rect = new Rect2(new Vector2(x, 8 - lift), CardSize);
            Icons.Skin.CardFace(this, rect, false, r, false);
            if (!AllFive)
                Ui.Badge(this, rect.Position + new Vector2(CardSize.X, 0), _cards[r], 0.9f);
            _hits.Add((rect, r));
            x += CardSize.X + 8;
        }
    }

    private int HitAt(Vector2 p)
    {
        foreach (var (rect, r) in _hits)
            if (rect.Grow(3).HasPoint(p))
                return r;
        return -1;
    }
}

/// <summary>A player's avatar, optionally with an answer badge (check, cross, counter). Clickable when <see cref="Clickable"/>.</summary>
public partial class PlayerChip : Control
{
    private SeatColor _color;
    private bool _you, _hover, _faded;
    private OfferAnswer? _answer;

    public bool Clickable { get; private set; }
    public event Action? Clicked;
    public event Action? RightClicked;

    public PlayerChip()
    {
        CustomMinimumSize = new Vector2(40, 40);
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public PlayerChip Set(SeatColor color, bool you, OfferAnswer? answer = null, bool clickable = false, string tooltip = "", float size = 40)
    {
        _color = color;
        _you = you;
        _answer = answer;
        _faded = answer == OfferAnswer.Waiting;
        Clickable = clickable;
        TooltipText = tooltip;
        CustomMinimumSize = new Vector2(size, size);
        MouseDefaultCursorShape = clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
        return this;
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
        float r = Size.X / 2 - 2;
        if (Clickable)
            DrawCircle(c, r + 2, new Color(Ui.Good, _hover ? 1f : 0.7f));
        Ui.Avatar(this, c, r, _color, _you);
        if (_faded)
            DrawCircle(c, r, new Color(1, 1, 1, 0.45f));
        if (_answer is not { } answer || answer == OfferAnswer.Waiting)
            return;
        var at = c + new Vector2(r * 0.72f, r * 0.72f);
        var fill = answer switch { OfferAnswer.Accepted => Ui.Good, OfferAnswer.Declined => Ui.Bad, _ => Ui.BadgeBlue };
        DrawCircle(at, 8, fill);
        DrawArc(at, 8, 0, Mathf.Tau, 16, Colors.White, 1.5f, true);
        switch (answer)
        {
            case OfferAnswer.Accepted:
                DrawPolyline(new[] { at + new Vector2(-4, 0), at + new Vector2(-1, 3), at + new Vector2(4, -3) }, Colors.White, 2, true);
                break;
            case OfferAnswer.Declined:
                DrawLine(at + new Vector2(-3, -3), at + new Vector2(3, 3), Colors.White, 2);
                DrawLine(at + new Vector2(3, -3), at + new Vector2(-3, 3), Colors.White, 2);
                break;
            default:
                DrawArc(at, 3.5f, -0.3f, Mathf.Pi * 1.4f, 10, Colors.White, 1.8f, true);
                break;
        }
    }
}

/// <summary>A small arrow: green pointing down (cards coming to you) or red pointing up (cards you give).</summary>
public partial class TradeArrow : Control
{
    public bool Up { get; set; }

    public TradeArrow() => CustomMinimumSize = new Vector2(22, 30);

    public override void _Draw()
    {
        var c = Size / 2;
        var color = Up ? Ui.Bad : Ui.Good;
        float d = Up ? -1 : 1;
        DrawLine(c + new Vector2(0, -9 * d), c + new Vector2(0, 2 * d), color, 7);
        DrawColoredPolygon(new[] { c + new Vector2(-9, 1 * d), c + new Vector2(9, 1 * d), c + new Vector2(0, 11 * d) }, color);
    }
}

/// <summary>A people icon (the other players), for "you get from players" and the send-to-players button.</summary>
public partial class PeopleIcon : Control
{
    public PeopleIcon() => CustomMinimumSize = new Vector2(40, 40);

    public override void _Draw() => Draw(this, Size / 2, Size.X * 0.9f, Ui.ButtonInk);

    public static void Draw(CanvasItem c, Vector2 at, float s, Color ink)
    {
        foreach (var dx in new[] { -0.28f, 0.28f, 0f })
        {
            var p = at + new Vector2(dx * s, dx == 0 ? s * 0.04f : -s * 0.04f);
            c.DrawCircle(p - new Vector2(0, s * 0.14f), s * 0.12f, Colors.White);
            c.DrawArc(p - new Vector2(0, s * 0.14f), s * 0.12f, 0, Mathf.Tau, 16, ink, 2, true);
            var body = new[] { p + new Vector2(-s * 0.18f, s * 0.26f), p + new Vector2(-s * 0.16f, s * 0.06f), p + new Vector2(s * 0.16f, s * 0.06f), p + new Vector2(s * 0.18f, s * 0.26f) };
            c.DrawColoredPolygon(body, Colors.White);
            c.DrawPolyline(new[] { body[0], body[1], body[2], body[3] }, ink, 2, true);
        }
    }
}

/// <summary>
/// Trade offers as colonist-style cards in the board's top-right corner, newest at the bottom:
/// <list type="bullet">
/// <item>A bot's offer to you: their avatar, "they give" (green arrow down) and "you give" (red arrow up), the other players'
/// answers, and pencil (counter), cross (decline), check (accept) buttons, with a countdown.</item>
/// <item>Your own offer: what you get and give, each opponent's answer (click an accepter to trade; right-click to turn
/// them down), pencil (edit) and cross (withdraw).</item>
/// <item>A counter-offer to you: cross (decline) and check (accept). Your own counter: cross (withdraw).</item>
/// </list>
/// Buttons only appear when the engine lists the move; everything is checked again when sent.
/// </summary>
public sealed class TradePopups
{
    private const float Width = 404;
    private static readonly Vector2 Card = new(34, 48);

    private readonly VBoxContainer _stack;
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly int _seat;
    private readonly GameText _text;
    private readonly Func<GameAction, bool> _submit;
    private readonly Func<GameAction, string?> _whyNot;
    private readonly Action<int, TradeOffer> _edit, _counter;
    private readonly List<ProgressBar> _countdowns = new();

    public TradePopups(Control parent, Vector2 topRight, IReadOnlyList<SeatColor> colors, int seat, GameText text,
        Func<GameAction, bool> submit, Func<GameAction, string?> whyNot, Action<int, TradeOffer> edit, Action<int, TradeOffer> counter)
    {
        _colors = colors;
        _seat = seat;
        _text = text;
        _submit = submit;
        _whyNot = whyNot;
        _edit = edit;
        _counter = counter;
        _stack = new VBoxContainer { Position = topRight - new Vector2(Width, 0), Size = new Vector2(Width, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 6);
        parent.AddChild(_stack);
    }

    /// <summary>The promises riding on the offer in a slot (table talk), shown on its card; null for none.</summary>
    public Func<int, string?>? DealNote { get; set; }

    public void Update(PlayerView v, HumanPrompt? prompt)
    {
        foreach (var child in _stack.GetChildren())
        {
            ((Control)child).Visible = false; // freed at the end of the frame (a button may still be running its click handler)
            child.QueueFree();
        }
        _countdowns.Clear();

        bool yourTurn = v.CurrentPlayer == _seat && v.Phase == Phase.Main;
        bool answering = prompt is { IsOptional: true };
        var legal = prompt?.Legal ?? Array.Empty<GameAction>();
        for (int slot = 0; slot < v.Offers.Length; slot++)
        {
            var o = v.Offers[slot];
            if (!o.IsActive)
                continue;
            if (!o.IsCounter && o.From == _seat)
                _stack.AddChild(MyOffer(v, slot, o, legal));
            else if (o.IsCounter && o.From != _seat && yourTurn)
                _stack.AddChild(CounterToMe(slot, o, legal));
            else if (!o.IsCounter && o.From != _seat)
                _stack.AddChild(Incoming(v, slot, o, legal, answering));
            else if (o.IsCounter && o.From == _seat)
                _stack.AddChild(MyCounter(v, slot, o, legal));
        }
    }

    /// <summary>Keeps the countdown bars on offers waiting for your answer moving.</summary>
    public void Tick(DateTime? deadline, TimeSpan window)
    {
        if (deadline is not { } d || window.TotalSeconds <= 0)
            return;
        double left = 100 * Math.Clamp((d - DateTime.UtcNow).TotalSeconds / window.TotalSeconds, 0, 1);
        foreach (var bar in _countdowns)
            if (GodotObject.IsInstanceValid(bar))
                bar.Value = left;
    }

    // ---- The four kinds of card ----

    private Control Incoming(PlayerView v, int slot, TradeOffer o, IReadOnlyList<GameAction> legal, bool answering)
    {
        var body = Popup(o.From, $"{_text.Seat(o.From)} wants to trade", out var panel);
        var rows = Rows(body, (o.From, false, o.Give), (_seat, true, o.Get));
        AddDealNote(body, slot);
        Answers(rows, o, except: new[] { o.From, _seat }, v, slot, legal, clickable: false);

        var answer = TradeModel.Answer(o, _seat);
        if (answer != OfferAnswer.Waiting)
        {
            body.AddChild(Note(answer switch { OfferAnswer.Accepted => "You accepted", OfferAnswer.Declined => "You declined", _ => "You countered" }));
            return panel;
        }
        if (!answering)
            return panel;
        var accept = new GameAction(ActionType.AcceptOffer, _seat, slot);
        var decline = new GameAction(ActionType.DeclineOffer, _seat, slot);
        var buttons = Buttons(body);
        buttons.AddChild(Round(RoundIcon.Pencil, true, "Counter: propose different cards", () => _counter(slot, o)));
        buttons.AddChild(Round(RoundIcon.Cross, legal.Contains(decline), "Decline", () => _submit(decline)));
        buttons.AddChild(Round(RoundIcon.Check, legal.Contains(accept), legal.Contains(accept) ? "Accept" : _whyNot(accept) ?? "You can't accept this", () => _submit(accept)));
        var countdown = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), Value = 100 };
        _countdowns.Add(countdown);
        body.AddChild(countdown);
        return panel;
    }

    private Control MyOffer(PlayerView v, int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var body = Popup(_seat, "Your offer", out var panel);
        var rows = Rows(body, (-1, false, o.Get), (_seat, true, o.Give));
        AddDealNote(body, slot);
        Answers(rows, o, except: new[] { _seat }, v, slot, legal, clickable: true);
        var buttons = Buttons(body);
        bool yourTurn = v.CurrentPlayer == _seat && v.Phase == Phase.Main;
        buttons.AddChild(Round(RoundIcon.Pencil, yourTurn, "Edit this offer (answers reset)", () => _edit(slot, o)));
        var cancel = new GameAction(ActionType.CancelOffer, _seat, slot);
        buttons.AddChild(Round(RoundIcon.Cross, legal.Contains(cancel), "Withdraw this offer", () => _submit(cancel)));
        return panel;
    }

    private Control CounterToMe(int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var body = Popup(o.From, $"{_text.Seat(o.From)} counters", out var panel);
        Rows(body, (o.From, false, o.Give), (_seat, true, o.Get));
        var accept = new GameAction(ActionType.AcceptOffer, _seat, slot);
        var decline = new GameAction(ActionType.DeclineOffer, _seat, slot);
        var buttons = Buttons(body);
        buttons.AddChild(Round(RoundIcon.Cross, legal.Contains(decline), "Decline", () => _submit(decline)));
        buttons.AddChild(Round(RoundIcon.Check, legal.Contains(accept), legal.Contains(accept) ? "Accept: trade now" : _whyNot(accept) ?? "You can't accept this", () => _submit(accept)));
        return panel;
    }

    private Control MyCounter(PlayerView v, int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var body = Popup(_seat, $"Your counter to {_text.Seat(v.CurrentPlayer)}", out var panel);
        Rows(body, (v.CurrentPlayer, false, o.Get), (_seat, true, o.Give));
        var cancel = new GameAction(ActionType.CancelOffer, _seat, slot);
        if (legal.Contains(cancel))
            Buttons(body).AddChild(Round(RoundIcon.Cross, true, "Withdraw your counter", () => _submit(cancel)));
        return panel;
    }

    // ---- Pieces ----

    private VBoxContainer Popup(int seat, string title, out PanelContainer panel)
    {
        panel = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 8, margin: 10));
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        panel.AddChild(body);
        var header = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        header.AddThemeConstantOverride("separation", 8);
        header.AddChild(new PlayerChip().Set(_colors[seat], seat == _seat, size: 34));
        var label = Ui.Label(title, 15);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        header.AddChild(label);
        body.AddChild(header);
        return body;
    }

    /// <summary>The "gets" row (green arrow down) and the "gives" row (red arrow up). Seat -1 = the other players.</summary>
    private HBoxContainer Rows(VBoxContainer body, (int Seat, bool Up, ResourceSet Cards) first, (int Seat, bool Up, ResourceSet Cards) second)
    {
        var outer = new HBoxContainer();
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        foreach (var (seat, up, cards) in new[] { first, second })
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            if (seat < 0)
                row.AddChild(new PeopleIcon());
            else
                row.AddChild(new PlayerChip().Set(_colors[seat], seat == _seat, size: 36));
            row.AddChild(new TradeArrow { Up = up, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var strip = new CardStrip { CardSize = Card };
            strip.Show(cards);
            row.AddChild(strip);
            column.AddChild(row);
        }
        outer.AddChild(column);
        body.AddChild(outer);
        return outer;
    }

    /// <summary>The other players' answers as small avatars; for your own offers an accepter can be clicked to trade.</summary>
    private void Answers(HBoxContainer rows, TradeOffer o, int[] except, PlayerView v, int slot, IReadOnlyList<GameAction> legal, bool clickable)
    {
        var chips = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        chips.AddThemeConstantOverride("separation", 2);
        foreach (int p in HudModel.Opponents(o.From).Append(o.From))
        {
            if (except.Contains(p))
                continue;
            var answer = TradeModel.Answer(o, p);
            var confirm = new GameAction(ActionType.ConfirmTrade, _seat, slot, p);
            var turnDown = new GameAction(ActionType.DeclineOffer, _seat, slot, p);
            bool canConfirm = clickable && legal.Contains(confirm);
            string name = _text.Seat(p);
            string tip = answer switch
            {
                OfferAnswer.Accepted => canConfirm ? $"{name} accepted: click to trade (right-click to turn them down)" : $"{name} accepted",
                OfferAnswer.Declined => $"{name} declined",
                OfferAnswer.Countered => $"{name} sent a counter-offer",
                _ => $"Waiting for {name}",
            };
            var chip = new PlayerChip().Set(_colors[p], p == _seat, answer, canConfirm, tip, 34);
            chip.Clicked += () => _submit(confirm);
            if (clickable && legal.Contains(turnDown))
                chip.RightClicked += () => _submit(turnDown);
            chips.AddChild(chip);
        }
        rows.AddChild(chips);
    }

    private static HBoxContainer Buttons(VBoxContainer body)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 6);
        body.AddChild(row);
        return row;
    }

    private void AddDealNote(VBoxContainer body, int slot)
    {
        if (DealNote?.Invoke(slot) is not { } note)
            return;
        var label = Ui.Label(note, 13, Ui.BadgeBlue);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Width - 30, 0);
        body.AddChild(label);
    }

    private static Label Note(string text)
    {
        var label = Ui.Label(text, 13, Ui.MutedText);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    public enum RoundIcon { Pencil, Cross, Check }

    /// <summary>A small light-blue square button with a pencil, cross or check.</summary>
    public static ActionTile Round(RoundIcon icon, bool enabled, string tooltip, Action onClick)
    {
        var tile = new ActionTile { CustomMinimumSize = new Vector2(48, 48), DrawIcon = (c, at, s, ink) => DrawRoundIcon(c, at, s, ink, icon) };
        tile.Set(enabled, tooltip);
        tile.Clicked += onClick;
        return tile;
    }

    /// <summary>The check-mark picture (confirm buttons elsewhere use it too).</summary>
    public static void DrawCheck(CanvasItem c, Vector2 at, float s, Color ink) => DrawRoundIcon(c, at, s, ink, RoundIcon.Check);

    private static void DrawRoundIcon(CanvasItem c, Vector2 at, float s, Color ink, RoundIcon icon)
    {
        float r = s * 0.42f;
        switch (icon)
        {
            case RoundIcon.Check:
                c.DrawPolyline(new[] { at + new Vector2(-r, 0), at + new Vector2(-r * 0.3f, r * 0.7f), at + new Vector2(r, -r * 0.6f) }, Colors.White, s * 0.24f, true);
                c.DrawPolyline(new[] { at + new Vector2(-r, 0), at + new Vector2(-r * 0.3f, r * 0.7f), at + new Vector2(r, -r * 0.6f) }, ink, s * 0.13f, true);
                break;
            case RoundIcon.Cross:
                foreach (var (a, b) in new[] { (new Vector2(-r, -r), new Vector2(r, r)), (new Vector2(r, -r), new Vector2(-r, r)) })
                {
                    c.DrawLine(at + a * 0.8f, at + b * 0.8f, Colors.White, s * 0.24f, true);
                    c.DrawLine(at + a * 0.8f, at + b * 0.8f, ink, s * 0.13f, true);
                }
                break;
            default:
                var tip = at + new Vector2(-r * 0.8f, r * 0.8f);
                var end = at + new Vector2(r * 0.7f, -r * 0.7f);
                c.DrawLine(tip, end, Colors.White, s * 0.3f, true);
                c.DrawLine(tip + new Vector2(r * 0.2f, -r * 0.2f), end, ink, s * 0.18f, true);
                c.DrawColoredPolygon(new[] { tip, tip + new Vector2(r * 0.35f, 0), tip + new Vector2(0, -r * 0.35f) }, ink);
                break;
        }
    }
}
