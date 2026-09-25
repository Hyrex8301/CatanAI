using Godot;

/// <summary>
/// "How to play": the controls, the turn timer and the chat shorthand, over a dimmed screen. Opens from the menu and the
/// in-game gear menu; Esc, the Close button or a click outside closes it. Centres itself at any window size.
/// </summary>
public partial class HelpPanel : Control
{
    private const string Text = """
        [font_size=26][b]How to play[/b][/font_size]

        [b]Your turn[/b]
        • [b]Roll:[/b] click the dice or press [b]Space[/b]. Space also ends your turn (the hourglass).
        • [b]Build:[/b] pick Road, Settlement or City, then click a glowing spot. Or hover a spot you can build on: click once to see the piece, again to build.
        • [b]Trade:[/b] the Trade button, or click a card in your hand. Offer it to the players (people button) or the bank (bank button). Click an accepting player's picture on your offer to trade.
        • [b]Dev cards:[/b] click one in your hand to see what it does and play it.
        • [b]Esc[/b] closes a popup or cancels a choice. [b]F11[/b] switches full screen.

        [b]Turn timer[/b]
        5 seconds to roll, then 60 seconds for your turn, plus 10 seconds for every move you make and 30 for a knight. When it runs out the game rolls or ends your turn for you.

        [b]Chat and deals[/b] (until someone has 5 points)
        • [b]nb[/b] = non-block: I won't put the robber on your tiles. [b]ns[/b] = non-steal. "rob" means both. They cover the next time that player moves the robber; "for 2 turns" means the next two.
        • [b]wheat nb?[/b] = trade me a wheat and I won't block you. A trade always needs at least one card each way.
        • [b]wheat for sheep nb[/b] = I give a wheat for a sheep, and I won't block you.
        • [b]don't block me and I'll give you an ore[/b] = asking for a promise.
        • [b]nb for nb[/b] = we both promise, no cards.
        • A spot is the numbers around it: [b]I won't take 6 5 9[/b] (it lasts the whole game). While typing, Shift+click a corner to add its numbers.
        • Name a colour to talk to one player. [b]deal[/b] / [b]ok[/b] accepts, [b]no thanks[/b] refuses.
        • Promises aren't rules. Bots almost always keep theirs; a broken promise is announced, and the table trusts the breaker less for a while.

        [b]The bots[/b]
        Three trained bots that look several turns ahead. They trade, make deals, and remember what you have from what they've seen.
        """;

    public HelpPanel()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
                Visible = false; // a click outside the panel
        };
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 14, margin: 26));
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, CustomMinimumSize = new Vector2(808, 0), Text = Text };
        text.AddThemeFontSizeOverride("normal_font_size", 16);
        text.AddThemeFontSizeOverride("bold_font_size", 16);
        text.AddThemeColorOverride("default_color", Ui.Text);
        column.AddChild(text);

        var close = Ui.Button("Close", 18);
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        close.CustomMinimumSize = new Vector2(140, 44);
        close.Pressed += () => Visible = false;
        column.AddChild(close);
    }

    public void Open() => Visible = true;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }
}
