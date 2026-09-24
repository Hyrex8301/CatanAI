using System;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Start screen: a freshly generated board on the sea behind a cream panel with Continue (the autosave), New game, Load game,
/// Settings (points to win, bot speed, answer time for bot offers, friendly robber; saved between sessions), the M1 debug
/// viewer and Quit.
/// </summary>
public partial class Menu : Control
{
    private static readonly (string Label, double Seconds)[] Speeds = { ("Slow", 1.0), ("Normal", 0.5), ("Fast", 0.2), ("Instant", 0) };
    private static readonly int[] Windows = { 10, 20, 30, 60 };

    private VBoxContainer _column = null!;
    private VBoxContainer _saves = null!;
    private VBoxContainer _settings = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = Ui.Sea, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        // A board to look at: a new random one each visit, no pieces.
        var board = new BoardView { Modulate = new Color(1, 1, 1, 0.92f) };
        board.Setup(new Rect2(40, 40, 960, 820));
        board.Show(PlayerView.From(new GameState(BoardGenerator.Balanced(new Rng((ulong)System.Random.Shared.NextInt64()))), 0),
            new[] { SeatColor.Red, SeatColor.Blue, SeatColor.Orange, SeatColor.White });
        AddChild(board);

        var panel = new PanelContainer { Position = new Vector2(1040, 110), Size = new Vector2(480, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(Ui.Cream, radius: 14, margin: 26));
        AddChild(panel);
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(_column);

        var title = Ui.Label("Catan AI", 52);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(title);
        var subtitle = Ui.Label("You against three trained bots", 16, Ui.MutedText);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(subtitle);
        _column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var store = GameSession.Store;
        Button? first = null;
        if (store.HasAutosave)
            first = AddButton("Continue", () => Play(store.Load(store.AutosavePath)), primary: true);
        var newGame = AddButton("New game", () => Play(null), primary: first is null);
        first ??= newGame;
        AddButton("Load game", () => Toggle(_saves, FillSaves));
        _saves = Section();
        AddButton("Placement practice", () => GetTree().ChangeSceneToFile("res://scenes/Practice.tscn"));
        AddButton("Settings", () => Toggle(_settings, FillSettings));
        _settings = Section();
        AddButton("Debug viewer", () => GetTree().ChangeSceneToFile("res://scenes/Debug.tscn"));
        AddButton("Quit", () => GetTree().Quit());
        first.GrabFocus();
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

    private void Play(GameRecord? resume)
    {
        GameSession.Resume = resume;
        GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
    }

    private void FillSaves()
    {
        var saves = GameSession.Store.List();
        if (saves.Count == 0)
            _saves.AddChild(Ui.Label("No saved games yet.", 15, Ui.MutedText));
        foreach (var save in saves)
        {
            var button = new Button
            {
                Text = $"{save.Name}   ({save.Record.Actions.Count} moves, {save.SavedAt.ToLocalTime():MMM d HH:mm})",
                Alignment = HorizontalAlignment.Left,
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.Pressed += () => Play(save.Record);
            _saves.AddChild(button);
        }
    }

    private void FillSettings()
    {
        var o = GameSession.Options;
        void Save(GameOptions changed) => GameSession.SaveSettings(changed);

        var vp = new SpinBox { MinValue = 5, MaxValue = 15, Value = o.VpToWin, CustomMinimumSize = new Vector2(110, 0) };
        vp.ValueChanged += value => Save(GameSession.Options with { VpToWin = (int)value });
        _settings.AddChild(SettingRow("Points to win", vp));

        var speed = new OptionButton();
        foreach (var (label, _) in Speeds)
            speed.AddItem(label);
        speed.Selected = Array.FindIndex(Speeds, s => Math.Abs(s.Seconds - o.BotDelaySeconds) < 0.01) is var i and >= 0 ? i : 1;
        speed.ItemSelected += index => Save(GameSession.Options with { BotDelaySeconds = Speeds[index].Seconds });
        _settings.AddChild(SettingRow("Bot speed", speed));

        var window = new OptionButton();
        foreach (int seconds in Windows)
            window.AddItem($"{seconds} s");
        window.Selected = Array.IndexOf(Windows, (int)o.ResponseWindowSeconds) is var w and >= 0 ? w : 1;
        window.ItemSelected += index => Save(GameSession.Options with { ResponseWindowSeconds = Windows[index] });
        _settings.AddChild(SettingRow("Time to answer bot offers", window));

        var friendly = new CheckBox { ButtonPressed = o.FriendlyRobber, TooltipText = "The robber can't be placed next to a player with 2 or fewer points" };
        friendly.Toggled += on => Save(GameSession.Options with { FriendlyRobber = on });
        _settings.AddChild(SettingRow("Friendly robber", friendly));
    }

    private static HBoxContainer SettingRow(string label, Control control)
    {
        var row = new HBoxContainer();
        var text = Ui.Label(label, 16);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(text);
        row.AddChild(control);
        return row;
    }
}
