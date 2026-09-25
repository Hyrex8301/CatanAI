using System;
using System.Collections.Generic;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Your hand in the cream bar along the bottom left: one card stack per resource with a count badge, then your dev cards
/// (bought this turn: dimmed, "new"). Clicking a resource raises <see cref="ResourceClicked"/> (adds it to a trade
/// proposal, or picks it for a discard); clicking a dev card raises <see cref="DevClicked"/>.
/// </summary>
public sealed class HandBar
{
    public static readonly Vector2 CardSize = new(58, 88);

    private readonly Panel _bar;
    private readonly HBoxContainer _row;
    private readonly List<CardView> _cards = new();

    public event Action<int>? ResourceClicked;
    public event Action<DevCardType>? DevClicked;

    public HandBar(Control parent, Rect2 rect)
    {
        var bar = _bar = new Panel { Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Stop };
        var style = Ui.PanelStyle(Ui.Cream, radius: 6);
        style.ShadowSize = 3;
        bar.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(bar);
        _row = new HBoxContainer { Position = new Vector2(12, (rect.Size.Y - CardSize.Y) / 2 - 3), Size = new Vector2(rect.Size.X - 24, CardSize.Y) };
        _row.AddThemeConstantOverride("separation", 6);
        bar.AddChild(_row);
    }

    /// <summary>Widens the bar (a wide window gives the hand more room up to the buttons).</summary>
    public void SetWidth(float width)
    {
        _bar.Size = new Vector2(width, _bar.Size.Y);
        _row.Size = new Vector2(width - 24, _row.Size.Y);
    }

    public void Update(IReadOnlyList<CardStack> stacks)
    {
        while (_cards.Count < stacks.Count)
        {
            var card = new CardView { CustomMinimumSize = CardSize };
            card.Clicked += OnClicked;
            _row.AddChild(card);
            _cards.Add(card);
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].Visible = i < stacks.Count;
            if (i >= stacks.Count)
                continue;
            var s = stacks[i];
            bool allFresh = s.IsDev && s.Fresh == s.Count;
            _cards[i].Set(s.IsDev, s.Type, s.Count, dimmed: allFresh, note: s.Fresh > 0 ? $"{s.Fresh} new" : null);
            _cards[i].TooltipText = s.IsDev
                ? s.Name + (s.Fresh > 0 ? $" ({s.Fresh} bought this turn: playable from your next turn)" : "")
                : $"{s.Name}: click to trade it";
        }
    }

    private void OnClicked(CardView card)
    {
        if (card.IsDev)
            DevClicked?.Invoke((DevCardType)card.Type);
        else
            ResourceClicked?.Invoke(card.Type);
    }
}
