using System;
using System.Collections.Generic;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Your hand along the bottom of the screen: one card stack per resource, then your dev cards (bought this turn: dimmed,
/// "new"). Clicking a resource raises <see cref="ResourceClicked"/> (opens a trade with it, or picks it for a discard);
/// clicking a dev card raises <see cref="DevClicked"/>.
/// </summary>
public sealed class HandBar
{
    public static readonly Vector2 CardSize = new(82, 128);

    private readonly HBoxContainer _row;
    private readonly Label _empty;
    private readonly List<CardView> _cards = new();

    public event Action<int>? ResourceClicked;
    public event Action<DevCardType>? DevClicked;

    public HandBar(Control parent, Rect2 rect)
    {
        _row = new HBoxContainer { Position = rect.Position, Size = rect.Size, Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(_row);
        _empty = Ui.Label("No cards in hand", 16, new Color(1, 1, 1, 0.8f));
        _empty.Position = rect.Position + new Vector2(rect.Size.X / 2 - 70, rect.Size.Y / 2 - 10);
        parent.AddChild(_empty);
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
                : s.Name;
        }
        _empty.Visible = stacks.Count == 0;
    }

    private void OnClicked(CardView card)
    {
        if (card.IsDev)
            DevClicked?.Invoke((DevCardType)card.Type);
        else
            ResourceClicked?.Invoke(card.Type);
    }
}
