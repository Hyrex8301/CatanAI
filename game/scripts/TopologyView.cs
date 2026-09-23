using Catan.Core;
using Godot;

/// <summary>
/// Debug view of Catan.Core.Topology: hex, vertex, edge and harbor-spot ids drawn straight from the tables.
/// Keys: H hex ids, V vertex ids, E edge ids, P harbor spots.
/// </summary>
public partial class TopologyView : Node2D
{
    private static readonly Color HexFill = new(0.93f, 0.87f, 0.72f);
    private static readonly Color Outline = new(0.35f, 0.28f, 0.2f);
    private static readonly Color HexText = new(0.35f, 0.28f, 0.2f, 0.6f);
    private static readonly Color VertexFill = new(0.15f, 0.3f, 0.6f);
    private static readonly Color EdgeText = new(0.7f, 0.2f, 0.15f);
    private static readonly Color Harbor = new(0.1f, 0.55f, 0.6f);

    private bool _showHexes = true, _showVertices = true, _showEdges = true, _showHarbors = true;

    public override void _Ready() => GetViewport().SizeChanged += QueueRedraw;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.H: _showHexes = !_showHexes; break;
            case Key.V: _showVertices = !_showVertices; break;
            case Key.E: _showEdges = !_showEdges; break;
            case Key.P: _showHarbors = !_showHarbors; break;
            default: return;
        }
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
            DrawColoredPolygon(corners, HexFill);
            DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[4], corners[5], corners[0] }, Outline, 2, true);
        }

        if (_showHarbors)
            for (int spot = 0; spot < Topology.HarborCount; spot++)
            {
                Vector2 a = VertexAt(Topology.HarborVertices[spot, 0]), b = VertexAt(Topology.HarborVertices[spot, 1]);
                DrawLine(a, b, Harbor, size * 0.12f, true);
                var mid = (a + b) / 2;
                var outward = (mid - HexAt(Topology.HarborHex[spot])).Normalized();
                Text(font, mid + outward * size * 0.4f, $"P{spot}", small, Harbor);
            }

        if (_showHexes)
            for (int h = 0; h < Topology.HexCount; h++)
                Text(font, HexAt(h), h.ToString(), (int)(size * 0.45f), HexText);

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

        DrawString(font, new Vector2(40, view.Y - 24), "H hex ids   V vertex ids   E edge ids   P harbor spots", HorizontalAlignment.Left, -1, 16, Outline);
    }

    private void Text(Font font, Vector2 center, string text, int fontSize, Color color)
    {
        const float width = 80;
        DrawString(font, center + new Vector2(-width / 2, fontSize * 0.35f), text, HorizontalAlignment.Center, width, fontSize, color);
    }
}
