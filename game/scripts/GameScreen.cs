using System;
using System.Collections.Generic;
using System.Linq;
using Catan.AI;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The playable game screen, laid out for 1600×900. So far: the board drawn from your seat's view, with click hit-testing.
/// Later steps add the game loop, your hand and buttons, trades and the log.
/// Developer keys for checkpoint A: F2 advances a random game (bots play every seat), F3 toggles the acting seat's legal
/// targets. Esc returns to the menu.
/// </summary>
public partial class GameScreen : Control
{
    // Layout (1600×900): players on the left, trades and log on the right, your hand and buttons under the board.
    public static readonly Rect2 PlayersRect = new(12, 12, 290, 876);
    public static readonly Rect2 TradesRect = new(1238, 12, 350, 420);
    public static readonly Rect2 LogRect = new(1238, 444, 350, 444);
    public static readonly Rect2 HandRect = new(314, 718, 912, 170);
    public static readonly Rect2 BoardRect = new(314, 12, 912, 694);

    private GameSetup _setup = null!;
    private GameRunner _runner = null!;
    private BoardView _board = null!;
    private Label _clickLabel = null!;
    private Label _statusLabel = null!;
    private bool _showTargets;
    private readonly List<GameAction> _legal = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // let clicks reach the board (panels still stop the ones over them)
        var background = new ColorRect { Color = Ui.Sea, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var options = GameSession.Options;
        _setup = GameSetup.Create(options.Seed ?? (ulong)Random.Shared.NextInt64());

        // Until the real game loop (step 3), bots play every seat so F2 can show pieces being placed.
        var state = new GameState(BoardGenerator.Balanced(new Rng(_setup.BoardSeed)), options.ToSettings());
        var bots = Enumerable.Range(0, GameConstants.PlayerCount).Select(i => (IPlayerAgent)new RandomBot(_setup.BotSeed + (ulong)i)).ToArray();
        _runner = new GameRunner(state, bots, new RngChance(_setup.ChanceSeed));

        _board = new BoardView();
        _board.Setup(BoardRect);
        _board.Clicked += OnBoardClicked;
        AddChild(_board);

        AddChild(Ui.Panel("Players", PlayersRect, out var players));
        players.AddChild(Ui.Label($"You are seat {_setup.HumanSeat + 1} ({_setup.HumanColor})", 15, Ui.MutedText));
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            players.AddChild(Ui.Label($"Seat {seat + 1}: {_setup.Colors[seat]}{(seat == _setup.HumanSeat ? " (you)" : "")}", 15));

        AddChild(Ui.Panel("Trades", TradesRect, out var trades));
        trades.AddChild(Ui.Label("Trade offers appear here (step 9).", 14, Ui.MutedText));

        AddChild(Ui.Panel("Log", LogRect, out var log));
        log.AddChild(Ui.Label($"Game seed {_setup.Seed}", 14, Ui.MutedText));
        _statusLabel = Ui.Label("", 14);
        log.AddChild(_statusLabel);
        _clickLabel = Ui.Label("Click the board: corners, edges and hexes.", 14);
        _clickLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        log.AddChild(_clickLabel);
        log.AddChild(Ui.Label("F2: advance a random game\nF3: show the acting seat's legal targets\nEsc: menu", 13, Ui.MutedText));

        AddChild(Ui.Panel("Your hand", HandRect, out var hand));
        hand.AddChild(Ui.Label("Cards, dev cards and action buttons go here (steps 5-8).", 14, Ui.MutedText));

        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.F2 })
        {
            for (int i = 0; i < 12 && !_runner.IsOver; i++)
                _runner.StepAsync().GetAwaiter().GetResult();
            Refresh();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            _showTargets = !_showTargets;
            Refresh();
        }
    }

    private void Refresh()
    {
        var state = _runner.State;
        _board.Show(PlayerView.From(state, _setup.HumanSeat, _runner.Log), _setup.Colors);

        int acting = Rules.ActingSeat(state);
        Rules.GetLegalActions(state, _legal);
        _board.SetTargets(_showTargets ? _legal.Select(TargetOf).Where(t => t.Kind != HitKind.None).Distinct() : Array.Empty<BoardHit>());
        _statusLabel.Text = acting < 0
            ? $"Game over after {state.TurnNumber} turns"
            : $"Turn {state.TurnNumber}, {state.Phase}, {_setup.Colors[acting]} to act{(_showTargets ? $" ({_legal.Count} legal)" : "")}";
    }

    private static BoardHit TargetOf(GameAction a) => a.Type switch
    {
        ActionType.BuildRoad => BoardHit.Edge(a.Target),
        ActionType.BuildSettlement or ActionType.BuildCity => BoardHit.Vertex(a.Target),
        ActionType.MoveRobber => BoardHit.Hex(a.Target),
        _ => BoardHit.None,
    };

    private void OnBoardClicked(BoardHit hit)
    {
        var state = _runner.State;
        string detail = hit.Kind switch
        {
            HitKind.Vertex => $"vertex {hit.Id}" + (state.VertexOwner[hit.Id] >= 0
                ? $", {_setup.Colors[state.VertexOwner[hit.Id]]} {(state.VertexLevel[hit.Id] == 2 ? "city" : "settlement")}" : ""),
            HitKind.Edge => $"edge {hit.Id}" + (state.EdgeOwner[hit.Id] >= 0 ? $", {_setup.Colors[state.EdgeOwner[hit.Id]]} road" : ""),
            _ => $"hex {hit.Id}, {state.Board.TerrainAt(hit.Id)}" + (state.Board.NumberAt(hit.Id) > 0 ? $" {state.Board.NumberAt(hit.Id)}" : "")
                 + (state.RobberHex == hit.Id ? ", robber" : ""),
        };
        _clickLabel.Text = $"Clicked {detail}";
    }
}
