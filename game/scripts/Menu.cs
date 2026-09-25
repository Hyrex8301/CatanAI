using System;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Start screen: a freshly generated board on the sea behind a cream panel with Play (the modes, each with its setup:
/// <see cref="GameModes"/>), Settings (points to win, bot speed, answer time for bot offers,
/// friendly robber, full screen, volume; saved between sessions), How to play and Quit (the M1 debug viewer with CATAN_DEV=1).
/// The panel scrolls when an open section makes it taller than the window.
/// </summary>
public partial class Menu : Control
{
    private static readonly (string Label, double Seconds)[] Speeds = { ("Slow", 1.0), ("Normal", 0.5), ("Fast", 0.2), ("Instant", 0) };
    private static readonly int[] Windows = { 10, 20, 30, 60 };

    private VBoxContainer _column = null!;
    private PanelContainer _panel = null!;
    private ScrollContainer _scroll = null!;
    private Vector2 _extra;
    private VBoxContainer _settings = null!;
    private VBoxContainer _modes = null!;

    /// <summary>
    /// Sizes the menu to its contents, up to the window's height (then it scrolls), and keeps it centred up and down.
    /// Every frame, since opening a section changes its height.
    /// </summary>
    public override void _Process(double delta)
    {
        const float Margin = 24, Padding = 52; // the panel's margins, top and bottom together
        float window = ScreenLayout.Design.Y + _extra.Y;
        float content = _column.GetCombinedMinimumSize().Y;
        float height = Math.Min(content, window - 2 * Margin - Padding);
        _scroll.CustomMinimumSize = new Vector2(428, height);
        _panel.Size = new Vector2(480, height + Padding);
        _panel.Position = new Vector2(1040 + _extra.X, Math.Max(Margin, (window - _panel.Size.Y) / 2));
    }

    public override void _Input(InputEvent @event)
    {
        if (GameSession.HandleFullscreenKey(@event))
            GetViewport().SetInputAsHandled();
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        if (GameSession.Options.Fullscreen != (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen))
            GameSession.SetFullscreen(GameSession.Options.Fullscreen); // as last time
        AddChild(new SeaView());

        // A board to look at: a new random one each visit, no pieces.
        var board = new BoardView { Modulate = new Color(1, 1, 1, 0.92f) };
        board.Setup(new Rect2(40, 40, 960, 820));
        board.Show(PlayerView.From(new GameState(BoardGenerator.Balanced(new Rng((ulong)System.Random.Shared.NextInt64()))), 0),
            new[] { SeatColor.Red, SeatColor.Blue, SeatColor.Orange, SeatColor.White });
        AddChild(board);

        var panel = new PanelContainer { Position = new Vector2(1040, 110), Size = new Vector2(480, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 14, margin: 26));
        AddChild(panel);
        _panel = panel;
        // Fill any window: the board takes the extra room, the menu stays on the right (FitPanel centres it up and down).
        ScreenLayout.Watch(this, extra =>
        {
            board.Setup(new Rect2(40, 40, 960 + extra.X, 820 + extra.Y));
            _extra = extra;
        });
        // The menu scrolls when Play or Settings makes it taller than the window.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(_scroll);
        var gutter = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; // room for the scrollbar
        gutter.AddThemeConstantOverride("margin_right", 14);
        _scroll.AddChild(gutter);
        _column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _column.AddThemeConstantOverride("separation", 12);
        gutter.AddChild(_column);

        _column.AddChild(new TitleEmblem());
        var title = Ui.Label("Catan AI", 52);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(title);
        var subtitle = Ui.Label("You against three trained bots", 16, Ui.MutedText);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(subtitle);
        _column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var play = AddButton("Play", () => Toggle(_modes, FillModes), primary: true);
        _modes = Section();
        AddButton("Settings", () => Toggle(_settings, FillSettings));
        _settings = Section();
        var help = new HelpPanel();
        AddButton("How to play", help.Open);
        if (System.Environment.GetEnvironmentVariable("CATAN_DEV") == "1") // developer tool: only with CATAN_DEV=1
            AddButton("Debug viewer", () => GetTree().ChangeSceneToFile("res://scenes/Debug.tscn"));
        AddButton("Quit", () => GetTree().Quit());
        AddChild(help); // over everything
        if (System.Environment.GetEnvironmentVariable("CATAN_SHOW_HELP") == "1")
            help.Open(); // developer screenshots
        play.GrabFocus();
        if (System.Environment.GetEnvironmentVariable("CATAN_SHOW_SETTINGS") == "1")
            Toggle(_settings, FillSettings); // developer screenshots
        if (System.Environment.GetEnvironmentVariable("CATAN_SHOW_MODES") == "1")
            Toggle(_modes, FillModes);
        DevShots.Run(this);
    }

    private Button AddButton(string text, Action onClick, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 54) };
        button.AddThemeFontSizeOverride("font_size", 21);
        foreach (var (state, shade) in new[] { ("normal", 0f), ("hover", 0.12f), ("pressed", -0.1f), ("focus", 0.05f) })
        {
            var baseColor = primary ? new Color(0.35f, 0.62f, 0.86f) : Ui.ButtonBlue;
            var style = new StyleBoxFlat { BgColor = shade >= 0 ? baseColor.Lightened(shade) : baseColor.Darkened(-shade), AntiAliasing = true };
            style.SetCornerRadiusAll(10);
            style.BorderColor = Colors.White;
            style.SetBorderWidthAll(3);
            style.ShadowColor = new Color(0, 0, 0, 0.2f);
            style.ShadowSize = 3;
            button.AddThemeStyleboxOverride(state, style);
        }
        var ink = primary ? Colors.White : new Color(0.15f, 0.25f, 0.35f);
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(name, ink);
        button.Pressed += onClick;
        _column.AddChild(button);
        return button;
    }

    private VBoxContainer Section()
    {
        var section = new VBoxContainer { Visible = false };
        section.AddThemeConstantOverride("separation", 6);
        _column.AddChild(section);
        return section;
    }

    private static void Toggle(VBoxContainer section, Action fill)
    {
        section.Visible = !section.Visible;
        foreach (var child in section.GetChildren())
            child.QueueFree();
        if (section.Visible)
            fill();
    }

    /// <summary>Play: one card per mode (<see cref="GameModes"/>) with its setup and a Start button.</summary>
    private void FillModes()
    {
        foreach (var mode in GameModes.All)
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.PanelFill.Darkened(0.04f), radius: 10, margin: 12));
            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 6);
            card.AddChild(body);
            body.AddChild(Ui.Label(mode.Title, 19));
            var about = Ui.Label(mode.Description, 14, Ui.MutedText);
            about.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(about);
            mode.AddSetup(body);
            var start = Ui.Button("Start", 17);
            start.CustomMinimumSize = new Vector2(120, 40);
            start.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            start.AddThemeStyleboxOverride("normal", Ui.PanelStyle(Ui.ButtonBlue, radius: 8, margin: 6));
            start.AddThemeStyleboxOverride("hover", Ui.PanelStyle(Ui.ButtonBlue.Lightened(0.12f), radius: 8, margin: 6));
            start.AddThemeStyleboxOverride("pressed", Ui.PanelStyle(Ui.ButtonBlue.Darkened(0.1f), radius: 8, margin: 6));
            foreach (string ink in new[] { "font_color", "font_hover_color", "font_pressed_color" })
                start.AddThemeColorOverride(ink, Ui.Text);
            start.Pressed += () => mode.Start(GetTree());
            body.AddChild(start);
            _modes.AddChild(card);
        }
    }

    private void FillSettings()
    {
        var o = GameSession.Options;
        void Save(GameOptions changed) => GameSession.SaveSettings(changed);

        var vp = new SpinBox { MinValue = 5, MaxValue = 15, Value = o.VpToWin, CustomMinimumSize = new Vector2(110, 0) };
        vp.ValueChanged += value => Save(GameSession.Options with { VpToWin = (int)value });
        _settings.AddChild(Ui.SettingRow("Points to win", vp));

        var speed = new OptionButton();
        foreach (var (label, _) in Speeds)
            speed.AddItem(label);
        speed.Selected = Array.FindIndex(Speeds, s => Math.Abs(s.Seconds - o.BotDelaySeconds) < 0.01) is var i and >= 0 ? i : 1;
        speed.ItemSelected += index => Save(GameSession.Options with { BotDelaySeconds = Speeds[index].Seconds });
        _settings.AddChild(Ui.SettingRow("Bot speed", speed));

        var window = new OptionButton();
        foreach (int seconds in Windows)
            window.AddItem($"{seconds} s");
        window.Selected = Array.IndexOf(Windows, (int)o.ResponseWindowSeconds) is var w and >= 0 ? w : 1;
        window.ItemSelected += index => Save(GameSession.Options with { ResponseWindowSeconds = Windows[index] });
        _settings.AddChild(Ui.SettingRow("Time to answer bot offers", window));

        var friendly = new CheckBox { ButtonPressed = o.FriendlyRobber, TooltipText = "The robber can't be placed next to a player with 2 or fewer points" };
        friendly.Toggled += on => Save(GameSession.Options with { FriendlyRobber = on });
        _settings.AddChild(Ui.SettingRow("Friendly robber", friendly));

        var fullscreen = new CheckBox { ButtonPressed = o.Fullscreen, TooltipText = "F11 switches too" };
        fullscreen.Toggled += on => GameSession.SetFullscreen(on);
        _settings.AddChild(Ui.SettingRow("Full screen", fullscreen));

        var volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = o.Volume * 100, CustomMinimumSize = new Vector2(160, 0), TooltipText = "0 turns sound off" };
        volume.ValueChanged += v => Save(GameSession.Options with { Volume = v / 100 });
        volume.DragEnded += _ => Sounds.Play(GameSound.YourTurn); // hear the new level
        _settings.AddChild(Ui.SettingRow("Sound volume", volume));
    }

}
