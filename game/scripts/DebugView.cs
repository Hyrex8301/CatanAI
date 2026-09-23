using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Godot;

/// <summary>
/// Debug viewer: plays random legal moves on a real GameState and draws the board, pieces and every seat's full state
/// (not a player view). Runs StateValidator after each move and shows violations in red.
/// Keys: Space step, A autoplay, +/- speed, N new game, H hex ids, V vertex ids, E edge ids, P harbors, Esc menu.
/// </summary>
public partial class DebugView : Node2D
{
    private static readonly Color Background = new(0.36f, 0.6f, 0.78f);
    private static readonly Color Outline = new(0.25f, 0.2f, 0.15f);
    private static readonly Color PanelText = new(0.08f, 0.08f, 0.1f);
    private static readonly Color HexText = new(1f, 1f, 1f, 0.8f);
    private static readonly Color VertexFill = new(0.15f, 0.3f, 0.6f);
    private static readonly Color EdgeText = new(0.55f, 0.1f, 0.1f);
    private static readonly Color Harbor = new(0.05f, 0.3f, 0.4f);
    private static readonly Color TokenFill = new(0.98f, 0.95f, 0.85f);
    private static readonly Color TokenText = new(0.15f, 0.12f, 0.1f);
    private static readonly Color TokenRed = new(0.8f, 0.1f, 0.1f);
    private static readonly Color Error = new(0.85f, 0.05f, 0.05f);

    private static readonly Color[] TerrainColors =
    {
        new(0.76f, 0.35f, 0.18f), // Hills
        new(0.18f, 0.42f, 0.23f), // Forest
        new(0.55f, 0.77f, 0.35f), // Pasture
        new(0.91f, 0.77f, 0.28f), // Fields
        new(0.54f, 0.55f, 0.57f), // Mountains
        new(0.85f, 0.78f, 0.63f), // Desert
    };

    private static readonly Color[] SeatColors =
    {
        new(0.85f, 0.15f, 0.15f), // red
        new(0.15f, 0.35f, 0.85f), // blue
        new(0.95f, 0.55f, 0.1f),  // orange
        new(0.97f, 0.97f, 0.97f), // white
    };

    private static readonly string[] SeatNames = { "Red", "Blue", "Orange", "White" };
    private static readonly string[] HarborLabels = { "2:1 Brick", "2:1 Wood", "2:1 Sheep", "2:1 Wheat", "2:1 Ore", "3:1" };
    private static readonly double[] StepSeconds = { 1.0, 0.5, 0.25, 0.1, 0.03, 0.005 };

    private bool _showHexIds, _showVertices, _showEdges, _showHarbors = true;
    private ulong _seed;
    private Rng _picker = null!;
    private IChance _chance = null!;
    private GameState _state = null!;
    private readonly List<GameAction> _legal = new();
    private readonly List<GameEvent> _events = new();
    private readonly List<string> _log = new();
    private List<string> _errors = new();
    private int _actions;
    private bool _auto;
    private int _speed = 2;
    private double _elapsed;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Background);
        GetViewport().SizeChanged += QueueRedraw;
        NewGame();
    }

    public override void _Process(double delta)
    {
        if (!_auto)
            return;
        _elapsed += delta;
        int steps = 0;
        while (_elapsed >= StepSeconds[_speed] && steps++ < 200)
        {
            _elapsed -= StepSeconds[_speed];
            Step();
        }
        if (steps > 0)
            QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;
        switch (key.Keycode)
        {
            case Key.Space: Step(); break;
            case Key.A when !key.Echo: _auto = !_auto; _elapsed = 0; break;
            case Key.Equal or Key.KpAdd when !key.Echo: _speed = Math.Min(_speed + 1, StepSeconds.Length - 1); break;
            case Key.Minus or Key.KpSubtract when !key.Echo: _speed = Math.Max(_speed - 1, 0); break;
            case Key.N when !key.Echo: NewGame(); break;
            case Key.H when !key.Echo: _showHexIds = !_showHexIds; break;
            case Key.V when !key.Echo: _showVertices = !_showVertices; break;
            case Key.E when !key.Echo: _showEdges = !_showEdges; break;
            case Key.P when !key.Echo: _showHarbors = !_showHarbors; break;
            case Key.Escape when !key.Echo: GetTree().ChangeSceneToFile("res://scenes/Menu.tscn"); return;
            default: return;
        }
        QueueRedraw();
    }

    private void NewGame()
    {
        _seed = (ulong)(DateTime.Now.Ticks % 1_000_000);
        var rng = new Rng(_seed);
        _state = new GameState(BoardGenerator.Balanced(rng));
        _picker = new Rng(_seed + 1);
        _chance = new RngChance(_seed + 2);
        _log.Clear();
        _errors.Clear();
        _actions = 0;
        _auto = false;
        QueueRedraw();
    }

    /// <summary>Plays one random legal move: a random action type first, then a random target of that type.</summary>
    private void Step()
    {
        if (_state.Phase == Phase.GameOver || _errors.Count > 0)
        {
            _auto = false;
            return;
        }
        GameAction action;
        Rules.GetLegalActions(_state, _legal);
        var canAct = new bool[GameConstants.PlayerCount];
        int responders = Rules.OptionalSeats(_state, canAct);
        if (responders > 0 && _picker.NextInt(2) == 0)
        {
            // Opponents answer open trades at any time: pick one of them to act instead of the current player.
            var seats = Enumerable.Range(0, GameConstants.PlayerCount).Where(i => canAct[i]).ToList();
            int seat = seats[_picker.NextInt(seats.Count)];
            if (_picker.NextInt(4) == 0 && Rules.RandomCounterOffer(_state, seat, _picker) is { } counter)
                action = counter;
            else
            {
                Rules.GetLegalActions(_state, seat, _legal);
                action = _legal[_picker.NextInt(_legal.Count)];
            }
        }
        else if (_state.Phase == Phase.Discard)
            action = Rules.RandomDiscard(_state, Rules.ActingSeat(_state), _picker); // discards aren't enumerated
        else if (_legal.Count == 0)
        {
            _errors = new List<string> { $"No legal actions in {_state.Phase} for seat {Rules.ActingSeat(_state)}." };
            return;
        }
        else if (_state.Phase == Phase.Main && _picker.NextInt(20) == 0 && Rules.RandomTradeOffer(_state, _picker) is { } offer)
            action = offer; // offers aren't enumerated; 5% of Main decisions, like the brief's RandomBot
        else if (_state.Phase == Phase.Main && _picker.NextInt(50) == 0 && Rules.RandomEditOffer(_state, _picker) is { } edit)
            action = edit;
        else
        {
            var types = _legal.Select(a => a.Type).Distinct().ToList();
            var type = types[_picker.NextInt(types.Count)];
            var choices = _legal.Where(a => a.Type == type).ToList();
            action = choices[_picker.NextInt(choices.Count)];
        }

        _events.Clear();
        try
        {
            Rules.ApplyChecked(_state, action, _chance, _events);
        }
        catch (Exception ex)
        {
            _errors = new List<string> { ex.Message };
            return;
        }
        _actions++;
        foreach (var e in _events)
            _log.Add(Describe(e));
        if (_log.Count > 18)
            _log.RemoveRange(0, _log.Count - 18);
        _errors = StateValidator.Check(_state);
    }

    public override void _Draw()
    {
        var view = GetViewportRect().Size;
        const float leftPanel = 300, rightPanel = 280;
        float size = Mathf.Min((view.X - leftPanel - rightPanel) / 9f, view.Y / 9.2f);
        var origin = new Vector2(leftPanel + (view.X - leftPanel - rightPanel) / 2, view.Y / 2);
        var font = ThemeDB.FallbackFont;
        int small = Mathf.Max(10, (int)(size * 0.2f));
        var board = _state.Board;

        Vector2 VertexAt(int v)
        {
            var (x, y) = Topology.VertexPosition(v, size);
            return origin + new Vector2((float)x, (float)y);
        }

        Vector2 HexAt(int h)
        {
            var (x, y) = Topology.HexCenter(h, size);
            return origin + new Vector2((float)x, (float)y);
        }

        // Board
        for (int h = 0; h < Topology.HexCount; h++)
        {
            var corners = new Vector2[6];
            for (int c = 0; c < 6; c++)
                corners[c] = VertexAt(Topology.HexVertices[h, c]);
            DrawColoredPolygon(corners, TerrainColors[(int)board.TerrainAt(h)]);
            DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[4], corners[5], corners[0] }, Outline, 2, true);
        }

        if (_showHarbors)
            for (int spot = 0; spot < Topology.HarborCount; spot++)
            {
                Vector2 a = VertexAt(Topology.HarborVertices[spot, 0]), b = VertexAt(Topology.HarborVertices[spot, 1]);
                DrawLine(a, b, Harbor, size * 0.1f, true);
                var mid = (a + b) / 2;
                var outward = (mid - HexAt(Topology.HarborHex[spot])).Normalized();
                Text(font, mid + outward * size * 0.45f, HarborLabels[(int)board.HarborTypeAt(spot)], small, Harbor);
            }

        for (int h = 0; h < Topology.HexCount; h++)
        {
            int number = board.NumberAt(h);
            if (number == 0)
                continue;
            var center = HexAt(h);
            var color = number is 6 or 8 ? TokenRed : TokenText;
            DrawCircle(center, size * 0.28f, TokenFill);
            Text(font, center - new Vector2(0, size * 0.04f), number.ToString(), (int)(size * 0.26f), color);
            int pips = board.PipsAt(h);
            for (int i = 0; i < pips; i++)
                DrawCircle(center + new Vector2((i - (pips - 1) / 2f) * size * 0.065f, size * 0.16f), size * 0.022f, color);
        }

        // Robber
        var robber = HexAt(_state.RobberHex) + new Vector2(size * 0.42f, 0);
        DrawCircle(robber, size * 0.14f, new Color(0.1f, 0.1f, 0.1f));
        DrawCircle(robber - new Vector2(0, size * 0.17f), size * 0.08f, new Color(0.1f, 0.1f, 0.1f));

        // Roads
        for (int e = 0; e < Topology.EdgeCount; e++)
        {
            int owner = _state.EdgeOwner[e];
            if (owner < 0)
                continue;
            Vector2 a = VertexAt(Topology.EdgeVertices[e, 0]), b = VertexAt(Topology.EdgeVertices[e, 1]);
            var inset = (b - a) * 0.12f;
            DrawLine(a + inset, b - inset, Outline, size * 0.16f, true);
            DrawLine(a + inset, b - inset, SeatColors[owner], size * 0.1f, true);
        }

        // Buildings
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            int owner = _state.VertexOwner[v];
            if (owner < 0)
                continue;
            var p = VertexAt(v);
            float r = _state.VertexLevel[v] == 2 ? size * 0.2f : size * 0.13f;
            var shape = _state.VertexLevel[v] == 2
                ? new[] { p + new Vector2(-r, r), p + new Vector2(-r, -r * 0.3f), p + new Vector2(0, -r), p + new Vector2(r, -r * 0.3f), p + new Vector2(r, r) }
                : new[] { p + new Vector2(-r, r), p + new Vector2(-r, -r * 0.2f), p + new Vector2(0, -r), p + new Vector2(r, -r * 0.2f), p + new Vector2(r, r) };
            DrawColoredPolygon(shape, SeatColors[owner]);
            DrawPolyline(shape.Append(shape[0]).ToArray(), Outline, 2, true);
        }

        // Id overlays
        if (_showHexIds)
            for (int h = 0; h < Topology.HexCount; h++)
                Text(font, HexAt(h) - new Vector2(0, size * 0.5f), $"hex {h}", small, HexText);
        if (_showEdges)
            for (int e = 0; e < Topology.EdgeCount; e++)
                Text(font, (VertexAt(Topology.EdgeVertices[e, 0]) + VertexAt(Topology.EdgeVertices[e, 1])) / 2, e.ToString(), small, EdgeText);
        if (_showVertices)
            for (int v = 0; v < Topology.VertexCount; v++)
            {
                var p = VertexAt(v);
                DrawCircle(p, small * 0.95f, VertexFill);
                Text(font, p, v.ToString(), small, Colors.White);
            }

        DrawStatusPanel(font, view);
        DrawLogPanel(font, view, rightPanel);
    }

    private void DrawStatusPanel(Font font, Vector2 view)
    {
        var s = _state;
        float y = 28;
        void Line(string text, Color color, int fontSize = 15)
        {
            DrawString(font, new Vector2(16, y), text, HorizontalAlignment.Left, -1, fontSize, color);
            y += fontSize + 6;
        }

        Line($"Seed {_seed}   actions {_actions}", PanelText);
        Line($"Turn {s.TurnNumber}   {s.Phase}", PanelText, 17);
        int acting = Rules.ActingSeat(s);
        string status = acting >= 0 ? $"To act: {SeatNames[acting]}" + (s.HasRolled ? $"   rolled {s.LastRoll}" : "")
            : s.Winner >= 0 ? $"{SeatNames[s.Winner]} wins with {s.TotalVP(s.Winner)} VP!" : "Draw at the turn cap";
        Line(status, acting < 0 && s.Winner >= 0 ? SeatColors[s.Winner] * 0.7f : PanelText, acting >= 0 ? 15 : 18);
        Line($"Bank  B{s.Bank[0]} L{s.Bank[1]} W{s.Bank[2]} G{s.Bank[3]} O{s.Bank[4]}   dev deck {s.DevDeck.Sum()}", PanelText);
        y += 8;

        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
        {
            var hand = s.HandOf(seat);
            int hidden = s.DevHand[seat * 5 + (int)DevCardType.VictoryPoint];
            string awards = (s.LongestRoadOwner == seat ? " [Longest Road]" : "") + (s.LargestArmyOwner == seat ? " [Largest Army]" : "");
            DrawRect(new Rect2(16, y - 13, 12, 12), SeatColors[seat]);
            DrawRect(new Rect2(16, y - 13, 12, 12), Outline, false, 1);
            DrawString(font, new Vector2(34, y), $"{SeatNames[seat]}  VP {s.PublicVP[seat]}" + (hidden > 0 ? $" (+{hidden} hidden)" : "") + awards,
                HorizontalAlignment.Left, -1, 15, PanelText);
            y += 19;
            Line($"   cards {s.HandSize(seat)}: B{hand[0]} L{hand[1]} W{hand[2]} G{hand[3]} O{hand[4]}", PanelText, 13);
            int devs = 0;
            for (int t = 0; t < 5; t++)
                devs += s.DevHand[seat * 5 + t];
            Line($"   dev {devs}   road {s.RoadLength[seat]}   knights {s.KnightsPlayed[seat]}", PanelText, 13);
            y += 4;
        }

        y = view.Y - 44;
        Line("Space step   A autoplay   +/- speed   N new game", PanelText, 13);
        Line($"H hex ids   V vertex ids   E edge ids   P harbors   Esc menu   speed {_speed + 1}/{StepSeconds.Length}" + (_auto ? "   AUTO" : ""), PanelText, 13);
    }

    private void DrawLogPanel(Font font, Vector2 view, float width)
    {
        float x = view.X - width + 8, y = 28;
        if (_errors.Count > 0)
        {
            DrawString(font, new Vector2(x, y), "RULE VIOLATION", HorizontalAlignment.Left, -1, 16, Error);
            y += 22;
            foreach (var error in _errors.Take(6))
            {
                DrawMultilineString(font, new Vector2(x, y), error, HorizontalAlignment.Left, width - 16, 13, -1, Error);
                y += 34;
            }
            y += 10;
        }
        var open = Enumerable.Range(0, _state.Offers.Length).Where(i => _state.Offers[i].IsActive).ToList();
        if (open.Count > 0)
        {
            DrawString(font, new Vector2(x, y), "Open trades", HorizontalAlignment.Left, -1, 15, PanelText);
            y += 20;
            string[] marks = { "?", "no", "yes", "ctr" };
            foreach (int slot in open)
            {
                var o = _state.Offers[slot];
                string answers = o.IsCounter ? $"counter to #{o.Parent}"
                    : string.Join(" ", Enumerable.Range(0, GameConstants.PlayerCount).Where(p => p != o.From)
                        .Select(p => $"{SeatNames[p][0]}:{marks[o.ResponseOf(p)]}"));
                DrawString(font, new Vector2(x, y), $"#{slot} {SeatNames[o.From]}: {Cards(o.Give)} for {Cards(o.Get)}",
                    HorizontalAlignment.Left, width - 16, 13, PanelText);
                y += 16;
                DrawString(font, new Vector2(x + 12, y), answers, HorizontalAlignment.Left, width - 28, 12, PanelText);
                y += 18;
            }
            y += 8;
        }
        DrawString(font, new Vector2(x, y), "Recent events", HorizontalAlignment.Left, -1, 15, PanelText);
        y += 20;
        foreach (var line in _log)
        {
            DrawString(font, new Vector2(x, y), line, HorizontalAlignment.Left, width - 16, 13, PanelText);
            y += 17;
        }
    }

    private static string Describe(GameEvent e) => e switch
    {
        DiceRolled d => $"{SeatNames[d.Seat]} rolled {d.Total}",
        ResourcesProduced p => $"{SeatNames[p.Seat]} got {Cards(p.Gained)}",
        Built b => $"{SeatNames[b.Seat]} built a {b.Piece.ToString().ToLower()} ({b.Target})",
        DevCardBought d => $"{SeatNames[d.Seat]} bought {d.Type}",
        DevCardPlayed d => $"{SeatNames[d.Seat]} played {d.Type}",
        MonopolyTaken m => $"  took {m.Count} {((Catan.Core.Resource)m.Resource).ToString().ToLower()} from {SeatNames[m.Victim]}",
        BankTraded b => $"{SeatNames[b.Seat]} traded {Cards(b.Gave)} to the bank for {Cards(b.Got)}",
        TradeOffered o => $"{SeatNames[o.Seat]} offers {Cards(o.Give)} for {Cards(o.Get)} (#{o.Slot})",
        TradeEdited o => $"{SeatNames[o.Seat]} edits #{o.Slot}: {Cards(o.Give)} for {Cards(o.Get)}",
        TradeCountered c => $"  {SeatNames[c.Seat]} counters #{c.ParentSlot}: {Cards(c.Give)} for {Cards(c.Get)}",
        TradeReplied r => $"  {SeatNames[r.Seat]} {(r.Accepted ? "accepts" : "declines")} #{r.Slot}",
        TradeRejected r => $"{SeatNames[r.Seat]} turns down {SeatNames[r.Partner]} on #{r.Slot}",
        TradeDone t => $"{SeatNames[t.Seat]} traded {Cards(t.Gave)} to {SeatNames[t.Partner]} for {Cards(t.Got)}",
        TradeCancelled c => $"{SeatNames[c.Seat]} withdrew #{c.Slot}",
        Discarded d => $"{SeatNames[d.Seat]} discarded {Cards(d.Cards)}",
        RobberMoved r => $"{SeatNames[r.Seat]} moved the robber to hex {r.Hex}",
        CardStolen c => $"{SeatNames[c.Thief]} stole 1 {((Catan.Core.Resource)c.Resource).ToString().ToLower()} from {SeatNames[c.Victim]}",
        AwardChanged a => $"{a.Award}: {(a.From < 0 ? "nobody" : SeatNames[a.From])} -> {(a.To < 0 ? "nobody" : SeatNames[a.To])}",
        TurnEnded t => $"{SeatNames[t.Seat]} ended the turn",
        GameEnded g => g.Winner < 0 ? "Game over: draw at the turn cap" : $"{SeatNames[g.Winner]} wins!",
        _ => e.ToString(),
    };

    private static string Cards(ResourceSet c)
    {
        var parts = new List<string>();
        string[] names = { "brick", "wood", "sheep", "wheat", "ore" };
        for (int r = 0; r < 5; r++)
            if (c[r] > 0)
                parts.Add($"{c[r]} {names[r]}");
        return string.Join(", ", parts);
    }

    private void Text(Font font, Vector2 center, string text, int fontSize, Color color)
    {
        const float width = 160;
        DrawString(font, center + new Vector2(-width / 2, fontSize * 0.35f), text, HorizontalAlignment.Center, width, fontSize, color);
    }
}
