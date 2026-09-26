using System;
using System.Collections.Generic;
using System.Linq;
using Catan.AI;
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
        new("Placement practice", "The whole opening in snake order; the bots rate each of your settlements and roads and say why.", PracticeSetup,
            tree => tree.ChangeSceneToFile("res://scenes/Practice.tscn")),
        new("Position practice", "A random moment from a real game, on your turn with several plays to choose from. Play the turn; the bots rank each of your plays.", PositionSetup, tree =>
        {
            GameSession.Resume = null;
            GameSession.StartPosition = DealPosition();
            tree.ChangeSceneToFile("res://scenes/Game.tscn");
        }),
    };

    /// <summary>
    /// Position practice: a new random position each time (<see cref="PositionDealer"/>: a bot game on a new board, stopped
    /// right after some player's roll when they have several kinds of play). That seat is yours.
    /// </summary>
    public static Catan.Core.Position DealPosition() =>
        PositionDealer.Deal(GameScreen.BotWeightsFile(), (ulong)System.Random.Shared.NextInt64());

    /// <summary>Position practice: your colour, and how you've been doing.</summary>
    private static void PositionSetup(VBoxContainer into)
    {
        AddColourRow(into);
        AddHistory(into, PracticeKind.Position, "Your position practice");
    }

    /// <summary>Your recent form in this kind of practice: a line of numbers and a chart of the last scores (nothing before the first).</summary>
    private static void AddHistory(VBoxContainer into, PracticeKind kind, string title)
    {
        if (GameSession.History.Describe(kind) is not { } text || GameSession.History.Summary(kind) is not { } summary)
            return;
        var line = Ui.Label($"{title}: {text}", 14, Ui.MutedText);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        line.CustomMinimumSize = new Vector2(320, 0);
        into.AddChild(line);
        if (summary.Recent.Count >= 2)
            into.AddChild(new ScoreChart(summary.Recent));
    }

    /// <summary>Normal game: your colour, and the board (a new random one, or a seed to get the same board again).</summary>
    private static void NormalSetup(VBoxContainer into)
    {
        AddColourRow(into);
        AddBoardRow(into);
        AddHistory(into, PracticeKind.GameReview, "Your game reviews");
    }

    private static void AddColourRow(VBoxContainer into)
    {
        var o = GameSession.Options;
        var colour = new OptionButton();
        colour.AddItem("Random");
        foreach (var c in Enum.GetValues<SeatColor>())
            colour.AddItem(c.ToString());
        colour.Selected = o.PreferredColor is { } chosen ? (int)chosen + 1 : 0;
        colour.ItemSelected += index => GameSession.SaveSettings(GameSession.Options with { PreferredColor = index == 0 ? null : (SeatColor)(index - 1) });
        into.AddChild(Ui.SettingRow("Your colour", colour));
    }

    /// <summary>Placement practice: the board, and whether each placement is rated as you make it or all at the end.</summary>
    private static void PracticeSetup(VBoxContainer into)
    {
        AddColourRow(into);
        AddBoardRow(into);
        var feedback = new OptionButton();
        feedback.AddItem("After each placement");
        feedback.AddItem("At the end");
        feedback.Selected = GameSession.Options.PracticeFeedbackEach ? 0 : 1;
        feedback.ItemSelected += index => GameSession.SaveSettings(GameSession.Options with { PracticeFeedbackEach = index == 0 });
        into.AddChild(Ui.SettingRow("Feedback", feedback));
        AddHistory(into, PracticeKind.Placement, "Your placement practice");
    }

    /// <summary>The board: a new random one each time, or a seed (the same seed always deals the same board).</summary>
    private static void AddBoardRow(VBoxContainer into)
    {
        var o = GameSession.Options;
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
