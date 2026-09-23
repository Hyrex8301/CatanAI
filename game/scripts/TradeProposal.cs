using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Proposing a trade, colonist style: a panel that grows up out of your hand in the bottom left.
/// <list type="bullet">
/// <item>Top row: the five resources; click one to ask for it.</item>
/// <item>"You get" row (people icon, green arrow) and "you give" row (your avatar, red arrow). Clicking cards in your hand
/// adds them to "you give"; clicking a card in either row takes one back out.</item>
/// <item>Beside it, two buttons send the same proposal: the bank button (a bank trade at your rates, any number of lots)
/// and the people button (an offer to every player). Each shows a green check when the engine would accept it.</item>
/// </list>
/// The same panel edits one of your offers or builds a counter to a bot's offer (then only the people button shows).
/// </summary>
public sealed class TradeProposal
{
    private enum Mode { Offer, Edit, Counter }

    private readonly Panel _panel;
    private readonly CardStrip _picker, _getRow, _giveRow;
    private readonly Label _modeLabel, _message;
    private readonly ActionTile _bank, _people;
    private readonly int _seat;
    private readonly GameText _text;
    private readonly Func<GameAction, bool> _submit;
    private readonly Action<List<GameAction>> _submitAll;
    private readonly Func<GameAction, string?> _whyNot;
    private readonly CardPicker _give = new(), _get = new();

    private Mode _mode = Mode.Offer;
    private int _slot = -1, _counterTo = -1;
    private PlayerView? _view;
    private List<GameAction> _bankTrades = new();

    /// <summary>Raised after a proposal is sent (the screen closes the panel).</summary>
    public event Action? Sent;

    public TradeProposal(Control parent, Rect2 rect, Rect2 bankButton, Rect2 peopleButton, SeatColor you, int seat, GameText text,
        Func<GameAction, bool> submit, Action<List<GameAction>> submitAll, Func<GameAction, string?> whyNot)
    {
        _seat = seat;
        _text = text;
        _submit = submit;
        _submitAll = submitAll;
        _whyNot = whyNot;

        _panel = new Panel { Position = rect.Position, Size = rect.Size, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var style = Ui.PanelStyle(Ui.Cream, radius: 6);
        style.ShadowSize = 4;
        _panel.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(_panel);

        // Top: the five resources to ask for, and what this panel is doing.
        _picker = new CardStrip { AllFive = true, Clickable = true, CardSize = new Vector2(44, 62), Position = new Vector2(12, 2) };
        _picker.Clicked += r => _get.Add(r);
        _picker.Show(default);
        _picker.TooltipText = "Click a resource to ask for it";
        _panel.AddChild(_picker);
        _modeLabel = Ui.Label("", 15, Ui.MutedText);
        _modeLabel.Position = new Vector2(300, 12);
        _modeLabel.Size = new Vector2(rect.Size.X - 312, 24);
        _modeLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _panel.AddChild(_modeLabel);
        _panel.AddChild(new ColorRect { Color = Ui.PanelBorder, Position = new Vector2(8, 80), Size = new Vector2(rect.Size.X - 16, 1), MouseFilter = Control.MouseFilterEnum.Ignore });

        // You get / you give.
        var people = new PeopleIcon { Position = new Vector2(12, 96), Size = new Vector2(44, 44) };
        _panel.AddChild(people);
        _panel.AddChild(new TradeArrow { Up = false, Position = new Vector2(60, 104), Size = new Vector2(22, 30) });
        _getRow = new CardStrip { Clickable = true, Position = new Vector2(92, 86) };
        _getRow.Clicked += r => _get.Remove(r);
        _getRow.RightClicked += r => _get.Remove(r);
        _panel.AddChild(_getRow);

        var avatar = new PlayerChip { Position = new Vector2(12, 176), Size = new Vector2(44, 44) };
        avatar.Set(you, true, size: 44);
        _panel.AddChild(avatar);
        _panel.AddChild(new TradeArrow { Up = true, Position = new Vector2(60, 184), Size = new Vector2(22, 30) });
        _giveRow = new CardStrip { Clickable = true, Position = new Vector2(92, 166) };
        _giveRow.Clicked += r => _give.Remove(r);
        _giveRow.RightClicked += r => _give.Remove(r);
        _panel.AddChild(_giveRow);

        _message = Ui.Label("", 14, Ui.MutedText);
        _message.Position = new Vector2(400, 110);
        _message.Size = new Vector2(rect.Size.X - 412, 100);
        _message.HorizontalAlignment = HorizontalAlignment.Right;
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _panel.AddChild(_message);

        _bank = new ActionTile { Position = bankButton.Position, Size = bankButton.Size, Visible = false, Check = true,
            DrawIcon = (c, at, s, ink) => BankView.DrawBankIcon(c, at, s * 0.9f, ink) };
        _bank.Clicked += SendToBank;
        parent.AddChild(_bank);
        _people = new ActionTile { Position = peopleButton.Position, Size = peopleButton.Size, Visible = false, Check = true,
            DrawIcon = (c, at, s, ink) => PeopleIcon.Draw(c, at, s, ink) };
        _people.Clicked += SendToPlayers;
        parent.AddChild(_people);

        _give.Changed += Sync;
        _get.Changed += Sync;
    }

    public bool Visible
    {
        get => _panel.Visible;
        set
        {
            _panel.Visible = value;
            _people.Visible = value;
            _bank.Visible = value && _mode == Mode.Offer;
        }
    }

    public bool IsCounter => _mode == Mode.Counter;

    /// <summary>A card clicked in your hand: give one more of it.</summary>
    public void AddGive(int resource) => _give.Add(resource);

    public void Reset()
    {
        _mode = Mode.Offer;
        _slot = _counterTo = -1;
        _give.Clear();
        _get.Clear();
    }

    /// <summary>Edit one of your open offers: the rows start from its cards.</summary>
    public void StartEdit(int slot, TradeOffer o)
    {
        Reset();
        _mode = Mode.Edit;
        _slot = slot;
        _give.Set(o.Give);
        _get.Set(o.Get);
    }

    /// <summary>Counter a bot's offer: the rows start from its cards, turned round to your side.</summary>
    public void StartCounter(int slot, TradeOffer o)
    {
        Reset();
        _mode = Mode.Counter;
        _slot = slot;
        _counterTo = o.From;
        _give.Set(o.Get);
        _get.Set(o.Give);
    }

    public void Update(PlayerView v, HumanPrompt? prompt)
    {
        _view = v;
        bool yourTurn = prompt is { IsOptional: false } && v.Phase == Phase.Main && v.CurrentPlayer == _seat;
        bool answering = prompt is { IsOptional: true };
        if (_mode == Mode.Edit && (!yourTurn || !v.Offers[_slot].IsActive) || _mode == Mode.Counter && (!answering || !v.Offers[_slot].IsActive))
            Reset();
        _give.SetLimits(v.Hand);
        Sync();
    }

    private void Sync()
    {
        if (_view is not { } v)
            return;
        _getRow.Show(_get.Cards);
        _giveRow.Show(_give.Cards);
        _modeLabel.Text = _mode switch
        {
            Mode.Edit => "Editing your offer",
            Mode.Counter => $"Counter to {_text.Seat(_counterTo)}",
            _ => "Click what you want; click your cards to give",
        };

        string? playersWhy = _give.Total == 0 || _get.Total == 0 ? (_give.Total == 0 ? "Pick cards to give" : "Pick cards to get") : _whyNot(Built());
        string? bankWhy = TradeModel.TryBankTrades(v, _give.Cards, _get.Cards, out _bankTrades, out string reason) ? null : reason;
        _people.Set(playersWhy is null, playersWhy is null ? PeopleTip() : $"{PeopleTip()}\n{playersWhy}");
        var ratios = TradeModel.Ratios(v);
        string rates = "Your bank rates: " + string.Join(", ", Enumerable.Range(0, 5).Select(r => $"{GameText.Resource(r)} {ratios[r]}:1"));
        _bank.Set(bankWhy is null, bankWhy is null ? $"Trade with the bank\n{rates}" : $"Trade with the bank\n{bankWhy}\n{rates}");
        _bank.Visible = _panel.Visible && _mode == Mode.Offer;

        _message.Text = _give.Total + _get.Total == 0 ? "" : playersWhy is null || bankWhy is null ? "" : playersWhy;
    }

    private string PeopleTip() => _mode switch
    {
        Mode.Edit => "Save the changes to your offer",
        Mode.Counter => $"Send your counter to {_text.Seat(_counterTo)}",
        _ => "Offer this trade to everyone",
    };

    private GameAction Built() => _mode switch
    {
        Mode.Edit => new GameAction(ActionType.EditOffer, _seat, _slot, Give: _give.Cards, Get: _get.Cards),
        Mode.Counter => new GameAction(ActionType.CounterOffer, _seat, _slot, Give: _give.Cards, Get: _get.Cards),
        _ => new GameAction(ActionType.OfferTrade, _seat, Give: _give.Cards, Get: _get.Cards),
    };

    private void SendToPlayers()
    {
        if (!_submit(Built()))
            return;
        Reset();
        Sent?.Invoke();
    }

    private void SendToBank()
    {
        if (_bankTrades.Count == 0)
            return;
        var trades = new List<GameAction>(_bankTrades);
        Reset();
        _submitAll(trades);
        Sent?.Invoke();
    }
}
