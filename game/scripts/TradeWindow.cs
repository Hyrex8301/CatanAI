using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The trade window, colonist style, drawn with card faces.
/// <list type="bullet">
/// <item>Your turn, Players tab: a "You give" row and a "You get" row of five cards each. Click a card to add one (or click
/// cards in your hand to give them), right-click or "−" to take one back, then offer it to everyone. Your open offers are
/// listed below with each opponent's answer as a colored chip; click an accepter's chip to trade with them.</item>
/// <item>Your turn, Bank tab: the same rows at your bank rates (shown under each card); any number of lots at once.</item>
/// <item>A bot's offer to you: Accept, Decline or Counter (the rows come back pre-filled), with a countdown and Skip.</item>
/// </list>
/// Every action is checked with the engine (<see cref="_whyNot"/>) before the Send button lights up.
/// </summary>
public sealed class TradeWindow
{
    private enum Mode { Offer, Bank, Edit, Counter }

    private static readonly Vector2 SlotSize = new(52, 80);

    private readonly GameText _text;
    private readonly int _seat;
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly Func<GameAction, bool> _submit;
    private readonly Action<List<GameAction>> _submitAll;
    private readonly Func<GameAction, string?> _whyNot;
    private readonly Action _skip;

    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Button _playersTab, _bankTab;
    private readonly VBoxContainer _incoming, _builder, _offers;
    private readonly ProgressBar _countdown;
    private readonly HBoxContainer _countdownRow;
    private readonly Label _giveLabel, _getLabel, _message;
    private readonly CardView[] _giveSlots = new CardView[5], _getSlots = new CardView[5];
    private readonly Label[] _giveNotes = new Label[5], _getNotes = new Label[5];
    private readonly Button[] _giveMinus = new Button[5], _getMinus = new Button[5];
    private readonly Button _send, _clear;
    private readonly ScrollContainer _offersScroll;
    private readonly CardPicker _give = new(), _get = new();

    private Mode _mode = Mode.Offer;
    private int _slot = -1;
    private PlayerView? _view;
    private HumanPrompt? _prompt;
    private int[] _ratios = { 4, 4, 4, 4, 4 };
    private List<GameAction> _bankTrades = new();
    private int _offerStrips;
    private readonly float _left, _width, _bottom;

    public TradeWindow(Control parent, Rect2 rect, GameText text, int seat, IReadOnlyList<SeatColor> colors,
        Func<GameAction, bool> submit, Action<List<GameAction>> submitAll, Func<GameAction, string?> whyNot, Action skip, Action close)
    {
        _text = text;
        _seat = seat;
        _colors = colors;
        _submit = submit;
        _submitAll = submitAll;
        _whyNot = whyNot;
        _skip = skip;
        (_left, _width, _bottom) = (rect.Position.X, rect.Size.X, rect.End.Y);

        _panel = new PanelContainer { Position = rect.Position, Size = rect.Size, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle());
        parent.AddChild(_panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(body);
        _panel.Resized += () => _panel.Position = new Vector2(_left, _bottom - _panel.Size.Y);

        // Header: tabs, title, close.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        var tabs = new ButtonGroup();
        _playersTab = Tab("Players", tabs, () => SetMode(Mode.Offer));
        _bankTab = Tab("Bank", tabs, () => SetMode(Mode.Bank));
        _playersTab.ButtonPressed = true;
        header.AddChild(_playersTab);
        header.AddChild(_bankTab);
        _title = Ui.Label("", 14, Ui.MutedText);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(_title);
        header.AddChild(SmallButton("✕", close, "Close (Esc)"));
        body.AddChild(header);

        // A bot's offers to you.
        _incoming = new VBoxContainer();
        _incoming.AddThemeConstantOverride("separation", 4);
        body.AddChild(_incoming);
        _countdownRow = new HBoxContainer();
        _countdown = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _countdownRow.AddChild(_countdown);
        _countdownRow.AddChild(SmallButton("Skip", () => _skip(), "Don't answer now"));
        body.AddChild(_countdownRow);

        // The give / get rows.
        _builder = new VBoxContainer();
        _builder.AddThemeConstantOverride("separation", 2);
        _giveLabel = Ui.Label("You give", 15);
        _builder.AddChild(Row(_giveLabel, _giveSlots, _giveNotes, _giveMinus, _give, isGive: true));
        _getLabel = Ui.Label("You get", 15);
        _builder.AddChild(Row(_getLabel, _getSlots, _getNotes, _getMinus, _get, isGive: false));
        var sendRow = new HBoxContainer();
        sendRow.AddThemeConstantOverride("separation", 8);
        _message = Ui.Label("", 14, Ui.MutedText);
        _message.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(300, 0); // a wrapping label needs a width, or it measures one word per line
        sendRow.AddChild(_message);
        _clear = SmallButton("Clear", () => SetMode(_mode is Mode.Bank ? Mode.Bank : Mode.Offer));
        _send = new Button { CustomMinimumSize = new Vector2(150, 34), FocusMode = Control.FocusModeEnum.None };
        _send.AddThemeFontSizeOverride("font_size", 15);
        _send.Pressed += Send;
        sendRow.AddChild(_clear);
        sendRow.AddChild(_send);
        _builder.AddChild(sendRow);
        body.AddChild(_builder);

        // Your open offers and counters to you.
        _offersScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _offers = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _offers.AddThemeConstantOverride("separation", 4);
        _offersScroll.AddChild(_offers);
        body.AddChild(_offersScroll);

        _give.Changed += SyncBuilder;
        _get.Changed += SyncBuilder;
    }

    public bool Visible
    {
        get => _panel.Visible;
        set => _panel.Visible = value;
    }

    /// <summary>A card clicked in your hand: give one more of it (at the bank: one more lot at your rate).</summary>
    public void OfferWith(int resource) => AddGive(resource);

    public void Update(PlayerView v, HumanPrompt? prompt)
    {
        _view = v;
        _prompt = prompt;
        bool yourTurn = prompt is { IsOptional: false } && v.Phase == Phase.Main && v.CurrentPlayer == _seat;
        bool answering = prompt is { IsOptional: true };

        // Leave a mode whose offer or moment has gone.
        if (_mode == Mode.Edit && (!yourTurn || _slot < 0 || !v.Offers[_slot].IsActive)
            || _mode == Mode.Counter && (!answering || _slot < 0 || !v.Offers[_slot].IsActive)
            || _mode == Mode.Bank && !yourTurn)
            SetMode(Mode.Offer);

        _ratios = TradeModel.Ratios(v);
        _give.SetLimits(v.Hand);
        _get.SetLimits(_mode == Mode.Bank ? v.Bank : new[] { 19, 19, 19, 19, 19 });

        _playersTab.Visible = _bankTab.Visible = yourTurn;
        _builder.Visible = yourTurn || _mode == Mode.Counter;
        _title.Text = yourTurn ? $"Offers this turn: {v.OffersThisTurn} of {v.Settings.MaxOffersPerTurn}"
            : answering ? $"{_text.Seat(v.CurrentPlayer)} wants to trade" : "";

        var legal = prompt?.Legal ?? Array.Empty<GameAction>();
        Clear(_incoming);
        Clear(_offers);
        _offerStrips = 0;
        for (int slot = 0; slot < v.Offers.Length; slot++)
        {
            var o = v.Offers[slot];
            if (!o.IsActive)
                continue;
            if (answering)
            {
                if (!o.IsCounter && o.From == v.CurrentPlayer)
                    _incoming.AddChild(IncomingStrip(slot, o, legal));
                else if (o.IsCounter && o.From == _seat)
                    _incoming.AddChild(MyCounterStrip(slot, o, legal));
            }
            else if (yourTurn)
            {
                if (!o.IsCounter && o.From == _seat)
                    AddOffer(MyOfferStrip(slot, o, legal));
                else if (o.IsCounter)
                    AddOffer(CounterToMeStrip(slot, o, legal));
            }
        }
        _incoming.Visible = answering;
        _countdownRow.Visible = answering;
        _offersScroll.Visible = yourTurn && _offerStrips > 0;
        _offersScroll.CustomMinimumSize = new Vector2(0, Math.Min(_offerStrips, 3) * 50);
        SyncBuilder();
    }

    /// <summary>Keeps the countdown bar on a bot's offer moving.</summary>
    public void Tick(DateTime? deadline, TimeSpan window)
    {
        if (deadline is { } d && window.TotalSeconds > 0)
            _countdown.Value = 100 * Math.Clamp((d - DateTime.UtcNow).TotalSeconds / window.TotalSeconds, 0, 1);
    }

    // ---- Building an offer ----

    private void SetMode(Mode mode, int slot = -1)
    {
        _mode = mode;
        _slot = slot;
        _give.Clear();
        _get.Clear();
        if (mode is Mode.Bank or Mode.Offer)
            (mode == Mode.Bank ? _bankTab : _playersTab).ButtonPressed = true;
        if (_view is { } v)
            _get.SetLimits(mode == Mode.Bank ? v.Bank : new[] { 19, 19, 19, 19, 19 });
        if (mode == Mode.Counter)
            _builder.Visible = true;
        SyncBuilder();
    }

    private void AddGive(int r)
    {
        if (_mode == Mode.Bank)
        {
            if (_give[r] + _ratios[r] > _give.Max(r))
                return;
            for (int i = 0; i < _ratios[r]; i++)
                _give.Add(r);
        }
        else
            _give.Add(r);
    }

    private void RemoveGive(int r)
    {
        int n = _mode == Mode.Bank ? Math.Max(1, _give[r] % _ratios[r] == 0 ? _ratios[r] : _give[r] % _ratios[r]) : 1;
        for (int i = 0; i < n; i++)
            _give.Remove(r);
    }

    private void SyncBuilder()
    {
        if (_view is not { } v)
            return;
        bool bank = _mode == Mode.Bank;
        for (int r = 0; r < 5; r++)
        {
            _giveSlots[r].Set(false, r, _give[r], dimmed: _give[r] == 0);
            _getSlots[r].Set(false, r, _get[r], dimmed: _get[r] == 0);
            _giveSlots[r].TooltipText = $"{GameText.ResourceTitle(r)}: you have {v.Hand[r]}" + (bank ? $", bank rate {_ratios[r]}:1" : "");
            _getSlots[r].TooltipText = bank ? $"{GameText.ResourceTitle(r)}: the bank has {v.Bank[r]}" : GameText.ResourceTitle(r);
            _giveNotes[r].Text = bank ? $"{_ratios[r]}:1" : "";
            _getNotes[r].Text = "";
            _giveMinus[r].Disabled = _give[r] == 0;
            _getMinus[r].Disabled = _get[r] == 0;
        }

        _giveLabel.Text = _mode == Mode.Counter ? "Your counter: you give" : "You give";
        _send.Text = _mode switch
        {
            Mode.Bank => "Trade with bank",
            Mode.Edit => "Save changes",
            Mode.Counter => "Send counter",
            _ => "Offer to everyone",
        };
        _clear.Text = _mode is Mode.Edit or Mode.Counter ? "Cancel" : "Clear";

        string? why;
        if (bank)
            why = TradeModel.TryBankTrades(v, _give.Cards, _get.Cards, out _bankTrades, out string reason) ? null : reason;
        else if (_give.Total == 0 && _get.Total == 0)
            why = "Click cards in your hand (or above) to give, and the cards you want below";
        else if (_give.Total == 0 || _get.Total == 0)
            why = _give.Total == 0 ? "Pick at least one card to give" : "Pick at least one card to get";
        else
            why = _whyNot(Built());
        _send.Disabled = why is not null;
        _message.Text = why ?? $"You give {GameText.Cards(_give.Cards)}, get {GameText.Cards(_get.Cards)}";
        Callable.From(Fit).CallDeferred();
    }

    private GameAction Built() => _mode switch
    {
        Mode.Edit => new GameAction(ActionType.EditOffer, _seat, _slot, Give: _give.Cards, Get: _get.Cards),
        Mode.Counter => new GameAction(ActionType.CounterOffer, _seat, _slot, Give: _give.Cards, Get: _get.Cards),
        _ => new GameAction(ActionType.OfferTrade, _seat, Give: _give.Cards, Get: _get.Cards),
    };

    private void Send()
    {
        if (_mode == Mode.Bank)
        {
            if (_bankTrades.Count > 0)
                _submitAll(new List<GameAction>(_bankTrades));
            SetMode(Mode.Bank);
        }
        else if (_submit(Built()))
            SetMode(Mode.Offer);
    }

    // ---- Offer strips ----

    private Control MyOfferStrip(int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var row = Strip(out var panel);
        row.AddChild(Ui.Label("You give", 13, Ui.MutedText));
        row.AddChild(Cards(o.Give));
        row.AddChild(Ui.Label("for", 13, Ui.MutedText));
        row.AddChild(Cards(o.Get));
        row.AddChild(Spacer());
        foreach (int p in HudModel.Opponents(_seat))
        {
            var answer = TradeModel.Answer(o, p);
            var confirm = new GameAction(ActionType.ConfirmTrade, _seat, slot, p);
            var decline = new GameAction(ActionType.DeclineOffer, _seat, slot, p);
            bool canConfirm = legal.Contains(confirm);
            string name = _text.Seat(p);
            string tip = answer switch
            {
                OfferAnswer.Accepted => canConfirm ? $"{name} accepted: click to trade (right-click to turn them down)" : $"{name} accepted",
                OfferAnswer.Declined => $"{name} declined",
                OfferAnswer.Countered => $"{name} sent a counter-offer (below)",
                _ => $"Waiting for {name}",
            };
            var chip = new StatusChip();
            chip.Show(Ui.SeatColor(_colors[p]), answer, canConfirm, tip);
            chip.Clicked += () => _submit(confirm);
            if (legal.Contains(decline))
                chip.RightClicked += () => _submit(decline);
            row.AddChild(chip);
        }
        row.AddChild(SmallButton("Edit", () =>
        {
            SetMode(Mode.Edit, slot);
            _give.Set(o.Give);
            _get.Set(o.Get);
        }, "Change this offer (answers reset)"));
        AddLegal(row, legal, new GameAction(ActionType.CancelOffer, _seat, slot), "Withdraw", "Take this offer back");
        return panel;
    }

    private Control CounterToMeStrip(int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var row = Strip(out var panel);
        row.AddChild(Avatar(o.From));
        row.AddChild(Ui.Label("counters: gives", 13, Ui.MutedText));
        row.AddChild(Cards(o.Give));
        row.AddChild(Ui.Label("for your", 13, Ui.MutedText));
        row.AddChild(Cards(o.Get));
        row.AddChild(Spacer());
        AddLegal(row, legal, new GameAction(ActionType.AcceptOffer, _seat, slot), "Accept", "Trade now");
        AddLegal(row, legal, new GameAction(ActionType.DeclineOffer, _seat, slot), "Decline", null);
        return panel;
    }

    private Control IncomingStrip(int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var row = Strip(out var panel);
        row.AddChild(Avatar(o.From));
        row.AddChild(Ui.Label("gives", 13, Ui.MutedText));
        row.AddChild(Cards(o.Give));
        row.AddChild(Ui.Label("for your", 13, Ui.MutedText));
        row.AddChild(Cards(o.Get));
        row.AddChild(Spacer());
        var answer = TradeModel.Answer(o, _seat);
        if (answer != OfferAnswer.Waiting)
        {
            row.AddChild(Ui.Label(answer switch { OfferAnswer.Accepted => "You accepted", OfferAnswer.Declined => "You declined", _ => "You countered" }, 13, Ui.MutedText));
            return panel;
        }
        var accept = new GameAction(ActionType.AcceptOffer, _seat, slot);
        var acceptButton = SmallButton("✓ Accept", () => _submit(accept));
        if (!legal.Contains(accept))
        {
            acceptButton.Disabled = true;
            acceptButton.TooltipText = _whyNot(accept) ?? "You can't accept this offer";
        }
        row.AddChild(acceptButton);
        AddLegal(row, legal, new GameAction(ActionType.DeclineOffer, _seat, slot), "✗ Decline", null);
        row.AddChild(SmallButton("Counter", () =>
        {
            SetMode(Mode.Counter, slot);
            _give.Set(o.Get);
            _get.Set(o.Give);
        }, "Propose different terms"));
        return panel;
    }

    private Control MyCounterStrip(int slot, TradeOffer o, IReadOnlyList<GameAction> legal)
    {
        var row = Strip(out var panel);
        row.AddChild(Ui.Label("Your counter: you give", 13, Ui.MutedText));
        row.AddChild(Cards(o.Give));
        row.AddChild(Ui.Label("for", 13, Ui.MutedText));
        row.AddChild(Cards(o.Get));
        row.AddChild(Spacer());
        AddLegal(row, legal, new GameAction(ActionType.CancelOffer, _seat, slot), "Withdraw", null);
        return panel;
    }

    /// <summary>Shrinks the window to its content, keeping its bottom edge just above your hand.</summary>
    private void Fit()
    {
        _panel.Size = new Vector2(_width, 0);
        _panel.Position = new Vector2(_left, _bottom - _panel.Size.Y);
    }

    private void AddOffer(Control strip)
    {
        _offers.AddChild(strip);
        _offerStrips++;
    }

    // ---- Widgets ----

    private HBoxContainer Row(Label label, CardView[] slots, Label[] notes, Button[] minus, CardPicker picker, bool isGive)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        label.CustomMinimumSize = new Vector2(96, 0);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(label);
        for (int r = 0; r < 5; r++)
        {
            int resource = r;
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 0);
            var card = new CardView { CustomMinimumSize = SlotSize, BadgeFromOne = true };
            card.Clicked += _ => { if (isGive) AddGive(resource); else picker.Add(resource); };
            card.RightClicked += _ => { if (isGive) RemoveGive(resource); else picker.Remove(resource); };
            slots[r] = card;
            column.AddChild(card);
            var under = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            minus[r] = SmallButton("−", () => { if (isGive) RemoveGive(resource); else picker.Remove(resource); }, "Take one back (or right-click the card)");
            notes[r] = Ui.Label("", 12, Ui.MutedText);
            under.AddChild(minus[r]);
            under.AddChild(notes[r]);
            column.AddChild(under);
            row.AddChild(column);
        }
        return row;
    }

    private static HBoxContainer Strip(out PanelContainer panel)
    {
        panel = new PanelContainer();
        var style = Ui.PanelStyle(new Color(0.94f, 0.95f, 0.97f), radius: 8, margin: 6);
        style.ShadowSize = 0;
        panel.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        panel.AddChild(row);
        return row;
    }

    private Control Avatar(int seat)
    {
        var chip = new StatusChip();
        chip.Show(Ui.SeatColor(_colors[seat]), OfferAnswer.Waiting, false, _text.Seat(seat));
        var label = Ui.Label(_text.Seat(seat), 14);
        var box = new HBoxContainer();
        box.AddChild(chip);
        box.AddChild(label);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return box;
    }

    private static MiniCards Cards(ResourceSet cards)
    {
        var m = new MiniCards();
        m.Show(cards);
        return m;
    }

    private static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    private void AddLegal(HBoxContainer row, IReadOnlyList<GameAction> legal, GameAction action, string label, string? tooltip)
    {
        if (legal.Contains(action))
            row.AddChild(SmallButton(label, () => _submit(action), tooltip));
    }

    private static Button SmallButton(string text, Action onClick, string? tooltip = null)
    {
        var button = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += onClick;
        return button;
    }

    private static Button Tab(string text, ButtonGroup group, Action onPressed)
    {
        var button = new Button { Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(90, 30) };
        button.AddThemeFontSizeOverride("font_size", 15);
        button.Pressed += onPressed;
        return button;
    }

    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            if (child is CanvasItem item)
                item.Visible = false; // freed at the end of the frame (a button may still be running its click handler)
            child.QueueFree();
        }
    }
}
