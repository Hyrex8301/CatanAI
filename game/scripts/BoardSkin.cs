using Catan.Core;
using Godot;

/// <summary>
/// How board pieces look. BoardView only says what to draw and where; the skin decides how. M2 uses <see cref="FlatSkin"/>
/// (shapes drawn in code); the later sprite / texture art pass adds a new skin without touching BoardView.
/// </summary>
public abstract class BoardSkin
{
    public abstract void Hex(CanvasItem c, Vector2[] corners, Terrain terrain);
    public abstract void Token(CanvasItem c, Vector2 center, float size, int number, int pips);
    public abstract void Harbor(CanvasItem c, Vector2 a, Vector2 b, Vector2 labelAt, float size, HarborType type);
    public abstract void Road(CanvasItem c, Vector2 a, Vector2 b, float size, Color color);
    public abstract void Settlement(CanvasItem c, Vector2 at, float size, Color color);
    public abstract void City(CanvasItem c, Vector2 at, float size, Color color);
    public abstract void Robber(CanvasItem c, Vector2 at, float size);

    /// <summary>A legal target (strong) or the thing under the mouse (hover).</summary>
    public abstract void HighlightVertex(CanvasItem c, Vector2 at, float size, bool hover);
    public abstract void HighlightEdge(CanvasItem c, Vector2 a, Vector2 b, float size, bool hover);
    public abstract void HighlightHex(CanvasItem c, Vector2[] corners, bool hover);
}

/// <summary>The flat M2 look: terrain colors, cream number tokens, seat-colored pieces with dark outlines.</summary>
public sealed class FlatSkin : BoardSkin
{
    private static readonly Color Outline = new(0.2f, 0.16f, 0.12f);
    private static readonly Color TokenFill = new(0.98f, 0.95f, 0.85f);
    private static readonly Color TokenText = new(0.15f, 0.12f, 0.1f);
    private static readonly Color TokenRed = new(0.8f, 0.1f, 0.1f);
    private static readonly Color HarborColor = new(0.95f, 0.9f, 0.75f);
    private static readonly Color HarborText = new(0.05f, 0.2f, 0.3f);
    private static readonly Color Target = new(1f, 1f, 0.55f, 0.9f);
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.9f);

    private static readonly Color[] TerrainColors =
    {
        new(0.76f, 0.35f, 0.18f), // Hills
        new(0.18f, 0.42f, 0.23f), // Forest
        new(0.55f, 0.77f, 0.35f), // Pasture
        new(0.91f, 0.77f, 0.28f), // Fields
        new(0.54f, 0.55f, 0.57f), // Mountains
        new(0.85f, 0.78f, 0.63f), // Desert
    };

    private static readonly string[] HarborLabels = { "2:1\nBrick", "2:1\nLumber", "2:1\nWool", "2:1\nGrain", "2:1\nOre", "3:1" };

    private static Font Font => ThemeDB.FallbackFont;

    public override void Hex(CanvasItem c, Vector2[] corners, Terrain terrain)
    {
        c.DrawColoredPolygon(corners, TerrainColors[(int)terrain]);
        c.DrawPolyline(Closed(corners), Outline, 3, true);
    }

    public override void Token(CanvasItem c, Vector2 center, float size, int number, int pips)
    {
        var color = number is 6 or 8 ? TokenRed : TokenText;
        c.DrawCircle(center, size * 0.3f, TokenFill);
        c.DrawArc(center, size * 0.3f, 0, Mathf.Tau, 32, Outline, 1.5f, true);
        Text(c, center - new Vector2(0, size * 0.05f), number.ToString(), (int)(size * 0.28f), color);
        for (int i = 0; i < pips; i++)
            c.DrawCircle(center + new Vector2((i - (pips - 1) / 2f) * size * 0.065f, size * 0.17f), size * 0.022f, color);
    }

    public override void Harbor(CanvasItem c, Vector2 a, Vector2 b, Vector2 labelAt, float size, HarborType type)
    {
        c.DrawLine(a, labelAt, HarborColor, size * 0.06f, true);
        c.DrawLine(b, labelAt, HarborColor, size * 0.06f, true);
        c.DrawCircle(labelAt, size * 0.3f, HarborColor);
        c.DrawArc(labelAt, size * 0.3f, 0, Mathf.Tau, 32, Outline, 1.5f, true);
        string label = HarborLabels[(int)type];
        int fontSize = (int)(size * 0.16f);
        if (label.Contains('\n'))
        {
            var parts = label.Split('\n');
            Text(c, labelAt - new Vector2(0, fontSize * 0.55f), parts[0], fontSize, HarborText);
            Text(c, labelAt + new Vector2(0, fontSize * 0.55f), parts[1], (int)(fontSize * 0.85f), HarborText);
        }
        else
            Text(c, labelAt, label, (int)(fontSize * 1.3f), HarborText);
    }

    public override void Road(CanvasItem c, Vector2 a, Vector2 b, float size, Color color)
    {
        var inset = (b - a) * 0.14f;
        c.DrawLine(a + inset, b - inset, Outline, size * 0.17f, true);
        c.DrawLine(a + inset, b - inset, color, size * 0.11f, true);
    }

    public override void Settlement(CanvasItem c, Vector2 at, float size, Color color) =>
        House(c, at, size * 0.14f, 0.25f, color);

    public override void City(CanvasItem c, Vector2 at, float size, Color color)
    {
        float r = size * 0.2f;
        // A tall gabled part on the left joined to a lower block on the right.
        var shape = new[]
        {
            at + new Vector2(-r, r), at + new Vector2(-r, -r * 0.3f), at + new Vector2(-r * 0.5f, -r),
            at + new Vector2(0, -r * 0.3f), at + new Vector2(r, -r * 0.3f), at + new Vector2(r, r),
        };
        c.DrawColoredPolygon(shape, color);
        c.DrawPolyline(Closed(shape), Outline, 2, true);
    }

    public override void Robber(CanvasItem c, Vector2 at, float size)
    {
        var body = new Color(0.12f, 0.12f, 0.14f);
        c.DrawCircle(at + new Vector2(0, size * 0.08f), size * 0.15f, body);
        c.DrawCircle(at - new Vector2(0, size * 0.14f), size * 0.09f, body);
    }

    public override void HighlightVertex(CanvasItem c, Vector2 at, float size, bool hover) =>
        c.DrawArc(at, size * 0.2f, 0, Mathf.Tau, 32, hover ? HoverColor : Target, hover ? 4 : 3, true);

    public override void HighlightEdge(CanvasItem c, Vector2 a, Vector2 b, float size, bool hover)
    {
        var inset = (b - a) * 0.2f;
        c.DrawLine(a + inset, b - inset, hover ? HoverColor : Target, size * (hover ? 0.12f : 0.08f), true);
    }

    public override void HighlightHex(CanvasItem c, Vector2[] corners, bool hover) =>
        c.DrawPolyline(Closed(corners), hover ? HoverColor : Target, hover ? 6 : 4, true);

    private static void House(CanvasItem c, Vector2 at, float r, float roof, Color color)
    {
        var shape = new[] { at + new Vector2(-r, r), at + new Vector2(-r, -r * roof), at + new Vector2(0, -r), at + new Vector2(r, -r * roof), at + new Vector2(r, r) };
        c.DrawColoredPolygon(shape, color);
        c.DrawPolyline(Closed(shape), Outline, 2, true);
    }

    private static Vector2[] Closed(Vector2[] points)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        return closed;
    }

    private static void Text(CanvasItem c, Vector2 center, string text, int fontSize, Color color)
    {
        const float width = 120;
        c.DrawString(Font, center + new Vector2(-width / 2, fontSize * 0.35f), text, HorizontalAlignment.Center, width, fontSize, color);
    }
}
