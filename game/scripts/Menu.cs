using Catan.UI;
using Godot;

/// <summary>Start screen: new game, the M1 debug viewer, quit. Settings and Load arrive in steps 10 and 11.</summary>
public partial class Menu : Control
{
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = Ui.Sea };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var column = new VBoxContainer { Position = new Vector2(660, 250), Size = new Vector2(280, 400) };
        column.AddThemeConstantOverride("separation", 16);
        AddChild(column);

        column.AddChild(Ui.Label("Catan AI", 48, Colors.White));
        column.AddChild(Ui.Label("M2 in progress", 16, new Color(1, 1, 1, 0.75f)));

        var newGame = Ui.Button("New game");
        newGame.Pressed += () =>
        {
            GameSession.Options = new GameOptions();
            GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
        };
        column.AddChild(newGame);

        var debug = Ui.Button("Debug viewer");
        debug.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Debug.tscn");
        column.AddChild(debug);

        var quit = Ui.Button("Quit");
        quit.Pressed += () => GetTree().Quit();
        column.AddChild(quit);

        newGame.GrabFocus();
    }
}
