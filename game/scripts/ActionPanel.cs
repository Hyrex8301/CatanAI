using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Under the board: what the game wants from you, your cards, and buttons for the moves that aren't board clicks.
/// Step 3 shows plain buttons; steps 5-9 replace them with proper build / dev card / trade controls.
/// </summary>
public sealed class ActionPanel
{
    private readonly Label _prompt;
    private readonly Label _hand;
    private readonly HBoxContainer _extra;
    private readonly HFlowContainer _buttons;
    private Control? _extraContent;

    public ActionPanel(VBoxContainer body)
    {
        _prompt = Ui.Label("", 17);
        body.AddChild(_prompt);
        _hand = Ui.Label("", 14, Ui.MutedText);
        body.AddChild(_hand);
        _extra = new HBoxContainer();
        body.AddChild(_extra);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(880, 60), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _buttons = new HFlowContainer();
        _buttons.AddThemeConstantOverride("h_separation", 6);
        _buttons.AddThemeConstantOverride("v_separation", 6);
        _buttons.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_buttons);
        body.AddChild(scroll);
    }

    public void SetPrompt(string text) => _prompt.Text = text;

    /// <summary>Shows a widget above the buttons (e.g. the discard picker), or nothing. The same widget is kept, not rebuilt.</summary>
    public void SetExtra(Control? content)
    {
        if (ReferenceEquals(content, _extraContent))
            return;
        if (_extraContent is not null)
            _extra.RemoveChild(_extraContent);
        _extraContent = content;
        if (content is not null)
            _extra.AddChild(content);
    }

    public void SetHand(string text) => _hand.Text = text;

    /// <summary>Replaces the buttons. Each entry is a label, a tooltip (or null) and what clicking does.</summary>
    public void SetButtons(IEnumerable<(string Label, string? Tooltip, Action OnClick)> buttons)
    {
        foreach (var child in _buttons.GetChildren())
            child.QueueFree();
        foreach (var (label, tooltip, onClick) in buttons)
        {
            var button = new Button { Text = label, TooltipText = tooltip ?? "" };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.Pressed += onClick;
            _buttons.AddChild(button);
        }
    }
}
