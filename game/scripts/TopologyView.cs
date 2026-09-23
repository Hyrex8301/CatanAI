using System;
using Catan.Core;
using Godot;

/// <summary>
/// Debug view of a generated board plus Catan.Core.Topology ids, drawn straight from the tables.
/// Keys: N new board, H hex ids, V vertex ids, E edge ids, P harbors.
/// </summary>
public partial class TopologyView : Node2D
{
    private static readonly Color Outline = new(0.35f, 0.28f, 0.2f);
    private static readonly Color HexText = new(1f, 1f, 1f, 0.75f);
    private static readonly Color VertexFill = new(0.15f, 0.3f, 0.6f);
    private static readonly Color EdgeText = new(0.55f, 0.1f, 0.1f);
    private static readonly Color Harbor = new(0.1f, 0.45f, 0.55f);
    private static readonly Color TokenFill = new(0.98f, 0.95f, 0.85f);
    private static readonly Color TokenText = new(0.15f, 0.12f, 0.1f);
    private static readonly Color TokenRed = new(0.8f, 0.1f, 0.1f);

    private static readonly Color[] TerrainColors =
    {
        new(0.76f, 0.35f, 0.18f), // Hills
        new(0.18f, 0.42f, 0.23f), // Forest
        new(0.55f, 0.77f, 0.35f), // Pasture
        new(0.91f, 0.77f, 0.28f), // Fields
        new(0.54f, 0.55f, 0.57f), // Mountains
        new(0.85f, 0.78f, 0.63f), // Desert
    };

    private static readonly string[] HarborLabels = { "2:1 Brick", "2:1 Lumber", "2:1 Wool", "2:1 Grain", "2:1 Ore", "3:1" };

    private bool _showHexIds, _showVertices, _showEdges, _showHarbors = true;
    private Board _board = null!;
    private ulong _seed;

    public override void _Ready()
    {
        GetViewport().SizeChanged += QueueRedraw;
        NewBoard();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.N: NewBoard(); return;
            case Key.H: _showHexIds = !_showHexIds; break;
            case Key.V: _showVertices = !_showVertices; break;
            case Key.E: _showEdges = !_showEdges; break;
            case Key.P: _showHarbors = !_showHarbors; break;
            default: return;
        }
        QueueRedraw();
    }

    private void NewBoard()
    {
        _seed = (ulong)(DateTime.Now.Ticks % 1_000_000);
        _board = BoardGenerator.Balanced(new Rng(_seed));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var view = GetViewportRect().Size;
        float size = Mathf.Min(view.X / 10f, view.Y / 9.5f);
        var origin = view / 2 + new Vector2(0, size * 0.4f);
        var font = ThemeDB.FallbackFont;
        int small = Mathf.Max(10, (int)(size * 0.2f));

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

        for (int h = 0; h < Topology.HexCount; h++)
        {
            var corners = new Vector2[6];
            for (int c = 0; c < 6; c++)
                corners[c] = VertexAt(Topology.HexVertices[h, c]);
            DrawColoredPolygon(corners, TerrainColors[(int)_board.TerrainAt(h)]);
            DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[4], corners[5], corners[0] }, Outline, 2, true);
        }

        if (_showHarbors)
            for (int spot = 0; spot < Topology.HarborCount; spot++)
            {
                Vector2 a = VertexAt(Topology.HarborVertices[spot, 0]), b = VertexAt(Topology.HarborVertices[spot, 1]);
                DrawLine(a, b, Harbor, size * 0.12f, true);
                var mid = (a + b) / 2;
                var outward = (mid - HexAt(Topology.HarborHex[spot])).Normalized();
                Text(font, mid + outward * size * 0.45f, $"P{spot} {HarborLabels[(int)_board.HarborTypeAt(spot)]}", small, Harbor);
            }

        for (int h = 0; h < Topology.HexCount; h++)
        {
            int number = _board.NumberAt(h);
            if (number == 0)
                continue;
            var center = HexAt(h);
            var color = number is 6 or 8 ? TokenRed : TokenText;
            DrawCircle(center, size * 0.3f, TokenFill);
            Text(font, center - new Vector2(0, size * 0.04f), number.ToString(), (int)(size * 0.28f), color);
            int pips = _board.PipsAt(h);
            float dot = size * 0.025f, gap = size * 0.07f;
            for (int i = 0; i < pips; i++)
                DrawCircle(center + new Vector2((i - (pips - 1) / 2f) * gap, size * 0.17f), dot, color);
        }

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

        DrawString(font, new Vector2(40, 90), $"Board seed {_seed}", HorizontalAlignment.Left, -1, 16, Outline);
        DrawString(font, new Vector2(40, view.Y - 24), "N new board   H hex ids   V vertex ids   E edge ids   P harbors",
            HorizontalAlignment.Left, -1, 16, Outline);
    }

    private void Text(Font font, Vector2 center, string text, int fontSize, Color color)
    {
        const float width = 160;
        DrawString(font, center + new Vector2(-width / 2, fontSize * 0.35f), text, HorizontalAlignment.Center, width, fontSize, color);
    }
}
