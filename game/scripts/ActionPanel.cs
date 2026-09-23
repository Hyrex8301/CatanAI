using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Bottom right: buttons for the moves that aren't board clicks, and room for a widget such as the discard picker.
/// Step 5 replaces the plain buttons with the colonist-style action bar.
/// </summary>
public sealed class ActionPanel
{
    private readonly HBoxContainer _extra;
    private readonly HFlowContainer _buttons;
    private Control? _extraContent;

    public ActionPanel(VBoxContainer body)
    {
        _extra = new HBoxContainer();
        body.AddChild(_extra);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _buttons = new HFlowContainer();
        _buttons.AddThemeConstantOverride("h_separation", 6);
        _buttons.AddThemeConstantOverride("v_separation", 6);
        _buttons.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_buttons);
        body.AddChild(scroll);
    }

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

    /// <summary>Replaces the buttons. Each entry is a label, a tooltip (or null) and what clicking does.</summary>
    public void SetButtons(IEnumerable<(string Label, string? Tooltip, Action OnClick)> buttons)
    {
        foreach (var child in _buttons.GetChildren())
            child.QueueFree();
        foreach (var (label, tooltip, onClick) in buttons)
        {
            var button = new Button { Text = label, TooltipText = tooltip ?? "", CustomMinimumSize = new Vector2(0, 36) };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.Pressed += onClick;
            _buttons.AddChild(button);
        }
    }
}
