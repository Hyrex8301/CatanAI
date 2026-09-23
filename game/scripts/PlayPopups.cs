using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Discarding on a 7, colonist style: a panel above your hand. Click cards in your hand to move them here, click one here
/// to put it back; the check button lights up once you've picked exactly as many as you owe.
/// </summary>
public sealed class DiscardPanel
{
    private readonly Panel _panel;
    private readonly Label _title;
    private readonly CardStrip _picked;
    private readonly ActionTile _confirm;
    private readonly CardPicker _picker;
    private int _owed;

    public DiscardPanel(Control parent, Rect2 rect, CardPicker picker, Action confirm)
    {
        _picker = picker;
        _panel = new Panel { Position = rect.Position, Size = rect.Size, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 6));
        parent.AddChild(_panel);
        _title = Ui.Label("", 18);
        _title.Position = new Vector2(14, 10);
        _panel.AddChild(_title);
        var hint = Ui.Label("Click cards in your hand to discard them; click one here to put it back.", 13, Ui.MutedText);
        hint.Position = new Vector2(14, 36);
        _panel.AddChild(hint);
        _picked = new CardStrip { Clickable = true, CardSize = new Vector2(46, 64), Position = new Vector2(12, 58) };
        _picked.Clicked += r => _picker.Remove(r);
        _picked.RightClicked += r => _picker.Remove(r);
        _panel.AddChild(_picked);
        _confirm = new ActionTile { Position = new Vector2(rect.Size.X - 84, rect.Size.Y / 2 - 36), Size = new Vector2(72, 72),
            DrawIcon = (c, at, s, ink) => TradePopups.DrawCheck(c, at, s, ink) };
        _confirm.Clicked += confirm;
        _panel.AddChild(_confirm);
        _picker.Changed += Sync;
    }

    public void Show(bool visible, int owed)
    {
        _panel.Visible = visible;
        _owed = owed;
        Sync();
    }

    private void Sync()
    {
        _title.Text = $"Discard {_owed} card{(_owed == 1 ? "" : "s")}  ({_picker.Total} picked)";
        _picked.Show(_picker.Cards);
        bool ready = _picker.Total == _owed;
        _confirm.Set(ready, ready ? "Discard these cards" : $"Pick {_owed - _picker.Total} more");
    }
}

/// <summary>After placing the robber next to several players: choose who to rob (their avatar and hand size).</summary>
public sealed class VictimPopup
{
    private readonly PanelContainer _panel;
    private readonly HBoxContainer _options;
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly GameText _text;
    private readonly Vector2 _center;

    public VictimPopup(Control parent, Vector2 center, IReadOnlyList<SeatColor> colors, GameText text, Action cancel)
    {
        _colors = colors;
        _text = text;
        _center = center;
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 10, margin: 16));
        parent.AddChild(_panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(body);
        var header = new HBoxContainer();
        var title = Ui.Label("Choose a player to rob", 18);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = TradePopups.Round(TradePopups.RoundIcon.Cross, true, "Pick a different hex (Esc)", cancel);
        close.CustomMinimumSize = new Vector2(36, 36);
        header.AddChild(close);
        body.AddChild(header);
        _options = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _options.AddThemeConstantOverride("separation", 18);
        body.AddChild(_options);
        _panel.Resized += () => _panel.Position = _center - _panel.Size / 2;
    }

    /// <summary>Shows one option per MoveRobber action (its victim), or hides when <paramref name="choices"/> is null.</summary>
    public void Show(IReadOnlyList<GameAction>? choices, PlayerView v, Func<GameAction, bool> submit)
    {
        foreach (var child in _options.GetChildren())
        {
            ((Control)child).Visible = false;
            child.QueueFree();
        }
        _panel.Visible = choices is not null;
        if (choices is null)
            return;
        foreach (var choice in choices)
        {
            int victim = choice.Target2;
            var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            var chip = new PlayerChip().Set(victim >= 0 ? _colors[victim] : SeatColor.White, false, clickable: true,
                tooltip: victim >= 0 ? $"Rob {_text.Seat(victim)}" : "Rob no one", size: 64);
            chip.Clicked += () => submit(choice);
            chip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            column.AddChild(chip);
            var name = Ui.Label(victim >= 0 ? _text.Seat(victim) : "No one", 15);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(name);
            var cards = Ui.Label(victim >= 0 ? $"{v.HandSizes[victim]} cards" : "", 13, Ui.MutedText);
            cards.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(cards);
            _options.AddChild(column);
        }
        _panel.Size = Vector2.Zero; // shrink to the new options; Resized re-centers it
    }
}

/// <summary>
/// Playing a development card: click it in your hand and this opens above your hand with the card, what it does, the
/// resources to pick for Year of Plenty (2) or Monopoly (1), and a check button to play it (greyed out with the reason
/// when it can't be played now).
/// </summary>
public sealed class DevCardPopup
{
    private readonly Panel _panel;
    private readonly CardFace _face;
    private readonly Label _name, _description, _why;
    private readonly Control _picks;
    private readonly CardStrip _picker, _chosen;
    private readonly ActionTile _play;
    private readonly CardPicker _picked = new();
    private readonly int _seat;
    private readonly Func<GameAction, bool> _submit;
    private DevCardType _type;
    private IReadOnlyList<GameAction>? _legal;
    private PlayerView? _view;

    public event Action? Closed;

    public DevCardPopup(Control parent, Rect2 rect, int seat, Func<GameAction, bool> submit)
    {
        _seat = seat;
        _submit = submit;
        _panel = new Panel { Position = rect.Position, Size = rect.Size, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 6));
        parent.AddChild(_panel);

        _face = new CardFace { Position = new Vector2(14, 14), Size = new Vector2(76, 108) };
        _panel.AddChild(_face);
        _name = Ui.Label("", 20);
        _name.Position = new Vector2(104, 12);
        _panel.AddChild(_name);
        _description = Ui.Label("", 14, Ui.MutedText);
        _description.Position = new Vector2(104, 42);
        _description.Size = new Vector2(rect.Size.X - 190, 60);
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _panel.AddChild(_description);

        _picks = new Control { Position = new Vector2(104, 100), Size = new Vector2(rect.Size.X - 110, 80), MouseFilter = Control.MouseFilterEnum.Pass };
        _panel.AddChild(_picks);
        _picker = new CardStrip { AllFive = true, Clickable = true, CardSize = new Vector2(38, 54) };
        _picker.Clicked += Pick;
        _picker.Show(default);
        _picks.AddChild(_picker);
        _chosen = new CardStrip { Clickable = true, CardSize = new Vector2(38, 54), Position = new Vector2(240, 0) };
        _chosen.Clicked += r => _picked.Remove(r);
        _picks.AddChild(_chosen);

        _why = Ui.Label("", 13, Ui.Bad);
        _why.Position = new Vector2(14, rect.Size.Y - 46);
        _why.Size = new Vector2(rect.Size.X - 110, 40);
        _why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _why.VerticalAlignment = VerticalAlignment.Bottom;
        _panel.AddChild(_why);

        var close = TradePopups.Round(TradePopups.RoundIcon.Cross, true, "Close (Esc)", () => Closed?.Invoke());
        close.Position = new Vector2(rect.Size.X - 50, 10);
        close.Size = new Vector2(38, 38);
        _panel.AddChild(close);
        _play = new ActionTile { Position = new Vector2(rect.Size.X - 84, rect.Size.Y - 84), Size = new Vector2(72, 72),
            DrawIcon = (c, at, s, ink) => TradePopups.DrawCheck(c, at, s, ink) };
        _play.Clicked += Play;
        _panel.AddChild(_play);
        _picked.Changed += Sync;
    }

    public bool Visible => _panel.Visible;

    public void Open(DevCardType type)
    {
        _type = type;
        _picked.Clear();
        _panel.Visible = true;
        Sync();
    }

    public void Close() => _panel.Visible = false;

    public void Update(PlayerView v, IReadOnlyList<GameAction>? legal)
    {
        _view = v;
        _legal = legal;
        if (_panel.Visible && v.DevHand[(int)_type] == 0)
            Close(); // played (or gone)
        Sync();
    }

    private void Pick(int r)
    {
        int max = DevCardModel.Picks(_type);
        if (max == 1)
            _picked.Clear();
        if (_picked.Total < max)
            _picked.Add(r);
    }

    private void Sync()
    {
        if (_view is not { } v || !_panel.Visible)
            return;
        _face.Show(_type);
        _name.Text = GameText.DevCard(_type);
        _description.Text = DevCardModel.Description(_type);
        int picks = DevCardModel.Picks(_type);
        _picks.Visible = picks > 0;
        _chosen.Show(_picked.Cards);
        _picker.TooltipText = picks == 2 ? "Pick 2 resources (the same one twice is fine)" : "Pick the resource to take";

        var (playable, why) = DevCardModel.State(v, _legal, _type);
        var action = DevCardModel.Action(_type, _seat, _picked.Cards);
        bool ready = playable && action is { } a && _legal is not null && _legal.Contains(a);
        string tip = !playable ? why
            : action is null ? (picks == 2 ? $"Pick {2 - _picked.Total} more" : "Pick a resource")
            : ready ? $"Play {GameText.DevCard(_type)}" : "The bank doesn't have those cards";
        _play.Set(ready, tip);
        _why.Text = playable ? "" : why;
    }

    private void Play()
    {
        if (DevCardModel.Action(_type, _seat, _picked.Cards) is { } action && _submit(action))
            Close();
    }
}

/// <summary>One card face drawn large (the dev card popup).</summary>
public partial class CardFace : Control
{
    private DevCardType _type;

    public void Show(DevCardType type)
    {
        _type = type;
        QueueRedraw();
    }

    public override void _Draw() => Icons.Skin.CardFace(this, new Rect2(Vector2.Zero, Size), true, (int)_type, false);
}
