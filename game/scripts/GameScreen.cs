using System;
using Catan.UI;
using Godot;

/// <summary>
/// The playable game screen, laid out for 1600×900. Step 1 is the skeleton: the panels are placeholders that later steps
/// fill in (board in step 2, players / hand / buttons / trades / log in steps 3-10). Esc returns to the menu.
/// </summary>
public partial class GameScreen : Control
{
    // Layout (1600×900): players on the left, trades and log on the right, your hand and buttons under the board.
    public static readonly Rect2 PlayersRect = new(12, 12, 290, 876);
    public static readonly Rect2 TradesRect = new(1238, 12, 350, 420);
    public static readonly Rect2 LogRect = new(1238, 444, 350, 444);
    public static readonly Rect2 HandRect = new(314, 718, 912, 170);
    public static readonly Rect2 BoardRect = new(314, 12, 912, 694);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = Ui.Sea };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var options = GameSession.Options;
        var setup = GameSetup.Create(options.Seed ?? (ulong)Random.Shared.NextInt64());

        AddChild(Ui.Panel("Players", PlayersRect, out var players));
        players.AddChild(Ui.Label($"You are seat {setup.HumanSeat + 1} ({setup.HumanColor})", 15, Ui.MutedText));

        AddChild(Ui.Panel("Trades", TradesRect, out var trades));
        trades.AddChild(Ui.Label("Trade offers appear here (step 9).", 14, Ui.MutedText));

        AddChild(Ui.Panel("Log", LogRect, out var log));
        log.AddChild(Ui.Label($"Game seed {setup.Seed}", 14, Ui.MutedText));

        AddChild(Ui.Panel("Your hand", HandRect, out var hand));
        hand.AddChild(Ui.Label("Cards, dev cards and action buttons go here (steps 5-8).", 14, Ui.MutedText));

        var boardLabel = Ui.Label("Board (step 2)", 24, Colors.White);
        boardLabel.Position = BoardRect.Position + BoardRect.Size / 2 - new Vector2(90, 16);
        AddChild(boardLabel);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
    }
}
