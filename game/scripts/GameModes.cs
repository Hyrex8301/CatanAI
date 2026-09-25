using System;
using System.Collections.Generic;
using Catan.UI;
using Godot;

/// <summary>
/// A way to play, listed under Play on the main menu: a title, one line about it, its setup controls (they save straight
/// into the settings), and how it starts. A new mode (coach, game review, multiplayer) is one more entry in <see cref="All"/>.
/// </summary>
public sealed record GameMode(string Title, string Description, Action<VBoxContainer> AddSetup, Action<SceneTree> Start);

public static class GameModes
{
    public static IReadOnlyList<GameMode> All { get; } = new GameMode[]
    {
        new("Normal game", "You against three bots, the whole game.", NormalSetup, tree =>
        {
            GameSession.Resume = null;
            tree.ChangeSceneToFile("res://scenes/Game.tscn");
        }),
        new("Placement practice", "Place your opening settlements; the bots rate every spot and say why.", _ => { },
            tree => tree.ChangeSceneToFile("res://scenes/Practice.tscn")),
    };

    /// <summary>Normal game: your colour, and the board (a new random one, or a seed to get the same board again).</summary>
    private static void NormalSetup(VBoxContainer into)
    {
        var o = GameSession.Options;

        var colour = new OptionButton();
        colour.AddItem("Random");
        foreach (var c in Enum.GetValues<SeatColor>())
            colour.AddItem(c.ToString());
        colour.Selected = o.PreferredColor is { } chosen ? (int)chosen + 1 : 0;
        colour.ItemSelected += index => GameSession.SaveSettings(GameSession.Options with { PreferredColor = index == 0 ? null : (SeatColor)(index - 1) });
        into.AddChild(Ui.SettingRow("Your colour", colour));

        var board = new OptionButton();
        board.AddItem("Random");
        board.AddItem("Seed");
        board.Selected = o.BoardSeed is null ? 0 : 1;
        var seed = new LineEdit
        {
            Text = o.BoardSeed?.ToString() ?? "",
            PlaceholderText = "e.g. 12345",
            CustomMinimumSize = new Vector2(120, 0),
            Visible = o.BoardSeed is not null,
            TooltipText = "The same seed always deals the same board. A game shows its board seed in the log.",
        };
        void SaveSeed(string text) =>
            GameSession.SaveSettings(GameSession.Options with { BoardSeed = ulong.TryParse(text.Trim(), out ulong s) ? s : null });
        seed.TextChanged += SaveSeed;
        board.ItemSelected += index =>
        {
            seed.Visible = index == 1;
            if (index == 0)
                GameSession.SaveSettings(GameSession.Options with { BoardSeed = null });
            else
            {
                seed.GrabFocus();
                SaveSeed(seed.Text);
            }
        };
        var boardControls = new HBoxContainer();
        boardControls.AddThemeConstantOverride("separation", 6);
        boardControls.AddChild(seed);
        boardControls.AddChild(board);
        into.AddChild(Ui.SettingRow("Board", boardControls));
    }
}
