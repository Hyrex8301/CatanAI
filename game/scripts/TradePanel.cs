using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Player trades, colonist.io style (plain controls for now). On your turn: build an offer with the give / get pickers and
/// send it to everyone; each open offer shows every bot's answer, with Trade / Turn down / Edit / Withdraw. Counters to you
/// can be accepted or declined. On a bot's turn: its open offers appear with Accept / Decline / Counter, a countdown and Skip.
/// Every action is checked with the engine (through <see cref="_submit"/>) before it's sent.
/// </summary>
public sealed class TradePanel
{
    private enum Mode { Offer, Edit, Counter }

    private readonly Func<GameAction, bool> _submit;
    private readonly Action _skip;
    private readonly GameText _text;
    private readonly int _seat;

    private readonly Label _status;
    private readonly VBoxContainer _builder;
    private readonly Label _giveLabel, _getLabel;
    private readonly CardPicker _give = new(), _get = new(maxEach: 9);
    private readonly Button _send, _clear;
    private readonly VBoxContainer _list;

    private Mode _mode = Mode.Offer;
    private int _slot = -1;
    private PlayerView? _view;

    public TradePanel(VBoxContainer body, GameText text, int seat, Func<GameAction, bool> submit, Action skip)
    {
        _text = text;
        _seat = seat;
        _submit = submit;
        _skip = skip;

        _status = Ui.Label("", 14);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(320, 0);
        body.AddChild(_status);

        _builder = new VBoxContainer();
        _builder.AddThemeConstantOverride("separation", 2);
        _giveLabel = Ui.Label("You give", 13, Ui.MutedText);
        _builder.AddChild(_giveLabel);
        _builder.AddChild(new CardPickerView(_give));
        _getLabel = Ui.Label("You get", 13, Ui.MutedText);
        _builder.AddChild(_getLabel);
        _builder.AddChild(new CardPickerView(_get));
        var buttons = new HBoxContainer();
        _send = new Button();
        _send.Pressed += Send;
        _clear = new Button();
        _clear.Pressed += ClearOrCancel;
        buttons.AddChild(_send);
        buttons.AddChild(_clear);
        _builder.AddChild(buttons);
        body.AddChild(_builder);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 150), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);
        body.AddChild(scroll);

        _give.Changed += UpdateSendButton;
        _get.Changed += UpdateSendButton;
    }

    public void Update(PlayerView view, HumanPrompt? prompt)
    {
        _view = view;
        bool yourMainTurn = prompt is { IsOptional: false } && view.Phase == Phase.Main && view.CurrentPlayer == _seat;
        bool answering = prompt is { IsOptional: true };

        // Leave edit / counter mode if what it pointed at is gone.
        if (_mode != Mode.Offer && (_slot < 0 || !view.Offers[_slot].IsActive || (_mode == Mode.Counter && !answering) || (_mode == Mode.Edit && !yourMainTurn)))
            SetMode(Mode.Offer, -1);

        _give.SetLimits(view.Hand);
        _builder.Visible = yourMainTurn || (_mode == Mode.Counter && answering);
        _giveLabel.Text = _mode == Mode.Counter ? "Your counter: you give" : "You give";
        UpdateSendButton();

        _status.Text = yourMainTurn
            ? $"Offer a trade to everyone. {view.OffersThisTurn} of {view.Settings.MaxOffersPerTurn} offers / edits used this turn."
            : answering ? $"{_text.Seat(view.CurrentPlayer)} is trading. Answer below, or Skip."
            : view.Offers.Any(o => o.IsActive) ? "Open trades:" : "No open trades.";

        foreach (var child in _list.GetChildren())
            child.QueueFree();
        var legal = prompt?.Legal ?? Array.Empty<GameAction>();
        for (int slot = 0; slot < view.Offers.Length; slot++)
            if (view.Offers[slot].IsActive)
                AddOfferRow(view, slot, legal, yourMainTurn, answering);
        if (answering)
            _list.AddChild(RowButton("Skip (don't answer now)", _skip));
    }

    private void AddOfferRow(PlayerView v, int slot, IReadOnlyList<GameAction> legal, bool yourMainTurn, bool answering)
    {
        var o = v.Offers[slot];
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var buttons = new HFlowContainer();

        if (o.IsCounter)
        {
            box.AddChild(Ui.Label($"{_text.Seat(o.From)} counters: their {GameText.Cards(o.Give)} for your {GameText.Cards(o.Get)}", 13));
            AddLegal(buttons, legal, new GameAction(ActionType.AcceptOffer, _seat, slot), "Accept");
            AddLegal(buttons, legal, new GameAction(ActionType.DeclineOffer, _seat, slot), "Decline");
            AddLegal(buttons, legal, new GameAction(ActionType.CancelOffer, _seat, slot), "Withdraw my counter");
        }
        else
        {
            bool mine = o.From == _seat;
            string terms = mine ? $"You give {GameText.Cards(o.Give)}, get {GameText.Cards(o.Get)}"
                                : $"{_text.Seat(o.From)} gives {GameText.Cards(o.Give)} for your {GameText.Cards(o.Get)}";
            box.AddChild(Ui.Label($"#{slot}  {terms}", 13));
            box.AddChild(Ui.Label(Answers(o), 12, Ui.MutedText));

            if (mine && yourMainTurn)
            {
                for (int p = 0; p < GameConstants.PlayerCount; p++)
                {
                    AddLegal(buttons, legal, new GameAction(ActionType.ConfirmTrade, _seat, slot, p), $"Trade with {_text.Seat(p)}");
                    AddLegal(buttons, legal, new GameAction(ActionType.DeclineOffer, _seat, slot, p), $"Turn down {_text.Seat(p)}");
                }
                buttons.AddChild(RowButton("Edit", () => { SetMode(Mode.Edit, slot); _give.Set(o.Give); _get.Set(o.Get); }));
                AddLegal(buttons, legal, new GameAction(ActionType.CancelOffer, _seat, slot), "Withdraw");
            }
            else if (answering && o.ResponseOf(_seat) == TradeOffer.NoResponse)
            {
                AddLegal(buttons, legal, new GameAction(ActionType.AcceptOffer, _seat, slot), "Accept");
                AddLegal(buttons, legal, new GameAction(ActionType.DeclineOffer, _seat, slot), "Decline");
                buttons.AddChild(RowButton("Counter…", () => { SetMode(Mode.Counter, slot); _give.Set(o.Get); _get.Set(o.Give); }));
            }
        }
        box.AddChild(buttons);
        _list.AddChild(box);
    }

    private string Answers(TradeOffer o)
    {
        string[] marks = { "waiting", "no", "yes", "countered" };
        var parts = Enumerable.Range(0, GameConstants.PlayerCount).Where(p => p != o.From)
            .Select(p => $"{_text.Seat(p)}: {marks[o.ResponseOf(p)]}");
        return string.Join("   ", parts);
    }

    private void AddLegal(HFlowContainer buttons, IReadOnlyList<GameAction> legal, GameAction action, string label)
    {
        if (legal.Contains(action))
            buttons.AddChild(RowButton(label, () => _submit(action)));
    }

    private void SetMode(Mode mode, int slot)
    {
        _mode = mode;
        _slot = slot;
        if (mode == Mode.Offer)
        {
            _give.Clear();
            _get.Clear();
        }
        _builder.Visible = true;
        UpdateSendButton();
    }

    private void UpdateSendButton()
    {
        _send.Text = _mode switch
        {
            Mode.Edit => $"Save changes to #{_slot}",
            Mode.Counter => $"Send counter to #{_slot}",
            _ => "Offer to everyone",
        };
        _clear.Text = _mode == Mode.Offer ? "Clear" : "Cancel";
        _send.Disabled = _give.Total == 0 || _get.Total == 0;
        _send.TooltipText = _send.Disabled ? "Pick at least one card on each side." : "";
    }

    private void Send()
    {
        var action = _mode switch
        {
            Mode.Edit => new GameAction(ActionType.EditOffer, _seat, _slot, Give: _give.Cards, Get: _get.Cards),
            Mode.Counter => new GameAction(ActionType.CounterOffer, _seat, _slot, Give: _give.Cards, Get: _get.Cards),
            _ => new GameAction(ActionType.OfferTrade, _seat, Give: _give.Cards, Get: _get.Cards),
        };
        if (_submit(action))
            SetMode(Mode.Offer, -1);
    }

    private void ClearOrCancel() => SetMode(Mode.Offer, -1);

    private static Button RowButton(string text, Action onClick)
    {
        var button = new Button { Text = text };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += onClick;
        return button;
    }
}
