using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A column of plain buttons at the board's right edge for choices that don't have a proper control yet (which player to
/// rob, dev card plays, bank trades). Hidden when empty. Later steps move these into the trade window and dev card flows.
/// </summary>
public sealed class ActionPanel
{
    private readonly Control _panel;
    private readonly VBoxContainer _buttons;

    public ActionPanel(Control parent, Rect2 rect)
    {
        _panel = Ui.Panel(null, rect, out var body);
        parent.AddChild(_panel);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _buttons = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _buttons.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_buttons);
        body.AddChild(scroll);
    }

    /// <summary>Replaces the buttons. Each entry is a label, a tooltip (or null) and what clicking does.</summary>
    public void SetButtons(IEnumerable<(string Label, string? Tooltip, Action OnClick)> buttons)
    {
        foreach (var child in _buttons.GetChildren())
            child.QueueFree();
        int count = 0;
        foreach (var (label, tooltip, onClick) in buttons)
        {
            var button = new Button
            {
                Text = label, TooltipText = tooltip ?? "", CustomMinimumSize = new Vector2(0, 32),
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.None,
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += onClick;
            _buttons.AddChild(button);
            count++;
        }
        _panel.Visible = count > 0;
    }
}
