using System;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>Start screen: continue the autosave, load a save, new game, the M1 debug viewer, quit.</summary>
public partial class Menu : Control
{
    private VBoxContainer _column = null!;
    private VBoxContainer _saves = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = Ui.Sea };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        _column = new VBoxContainer { Position = new Vector2(560, 150), Size = new Vector2(480, 650) };
        _column.AddThemeConstantOverride("separation", 14);
        AddChild(_column);

        _column.AddChild(Ui.Label("Catan AI", 48, Colors.White));
        _column.AddChild(Ui.Label("M2 in progress", 16, new Color(1, 1, 1, 0.75f)));

        var store = GameSession.Store;
        if (store.HasAutosave)
            AddButton("Continue", () => Play(store.Load(store.AutosavePath)));
        AddButton("New game", () => Play(null));
        AddButton("Load game", ToggleSaves);
        _saves = new VBoxContainer { Visible = false };
        _column.AddChild(_saves);
        AddButton("Debug viewer", () => GetTree().ChangeSceneToFile("res://scenes/Debug.tscn"));
        AddButton("Quit", () => GetTree().Quit());

        ((Button)_column.GetChild(2)).GrabFocus();
    }

    private void AddButton(string text, Action onClick)
    {
        var button = Ui.Button(text);
        button.Pressed += onClick;
        _column.AddChild(button);
    }

    private void Play(GameRecord? resume)
    {
        GameSession.Options = new GameOptions();
        GameSession.Resume = resume;
        GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
    }

    private void ToggleSaves()
    {
        _saves.Visible = !_saves.Visible;
        foreach (var child in _saves.GetChildren())
            child.QueueFree();
        if (!_saves.Visible)
            return;

        var saves = GameSession.Store.List();
        if (saves.Count == 0)
            _saves.AddChild(Ui.Label("No saved games yet.", 15, Colors.White));
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
}
