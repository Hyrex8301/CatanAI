using Catan.Core;
using Godot;

/// <summary>
/// How board pieces look. BoardView only says what to draw and where; the skin decides how. <see cref="FlatSkin"/> draws
/// everything in code; the later sprite / texture art pass adds a new skin without touching BoardView.
/// </summary>
public abstract class BoardSkin
{
    /// <summary>The water around the board.</summary>
    public abstract Color Sea { get; }

    /// <summary>Drawn for every hex before any hex: shallow water (pass 0), then the sandy coast (pass 1).</summary>
    public abstract void Coast(CanvasItem c, Vector2 center, Vector2[] corners, int pass);

    public abstract void Hex(CanvasItem c, Vector2 center, Vector2[] corners, Terrain terrain);
    public abstract void Token(CanvasItem c, Vector2 center, float size, int number, int pips);
    public abstract void Harbor(CanvasItem c, Vector2 a, Vector2 b, Vector2 labelAt, float size, HarborType type);
    public abstract void Road(CanvasItem c, Vector2 a, Vector2 b, float size, Color color);
    public abstract void Settlement(CanvasItem c, Vector2 at, float size, Color color);
    public abstract void City(CanvasItem c, Vector2 at, float size, Color color);
    public abstract void Robber(CanvasItem c, Vector2 at, float size);

    /// <summary>A legal target (soft) or the thing under the mouse (hover).</summary>
    public abstract void HighlightVertex(CanvasItem c, Vector2 at, float size, bool hover);
    public abstract void HighlightEdge(CanvasItem c, Vector2 a, Vector2 b, float size, bool hover);
    public abstract void HighlightHex(CanvasItem c, Vector2[] corners, bool hover);
}

/// <summary>
/// The colonist-like look drawn in code: dark blue sea, a sandy island, terrain tiles with a big picture, white number
/// tiles, harbor ships on wooden docks, seat-colored pieces with a shaded side, a grey pawn robber.
/// </summary>
public sealed class FlatSkin : BoardSkin
{
    private static readonly Color Outline = new(0.12f, 0.10f, 0.10f);
    private static readonly Color Shallow = new(0.36f, 0.62f, 0.86f);
    private static readonly Color Sand = new(0.95f, 0.85f, 0.62f);
    private static readonly Color Seam = new(0.96f, 0.90f, 0.74f);
    private static readonly Color TokenFill = new(0.99f, 0.98f, 0.95f);
    private static readonly Color TokenGreen = new(0.10f, 0.33f, 0.14f);
    private static readonly Color TokenRed = new(0.80f, 0.10f, 0.10f);
    private static readonly Color Plank = new(0.66f, 0.45f, 0.24f);
    private static readonly Color Target = new(1f, 0.93f, 0.35f);

    private static readonly Color[] TerrainColors =
    {
        new(0.89f, 0.43f, 0.24f), // Hills
        new(0.18f, 0.55f, 0.24f), // Forest
        new(0.58f, 0.80f, 0.26f), // Pasture
        new(0.96f, 0.77f, 0.24f), // Fields
        new(0.68f, 0.70f, 0.72f), // Mountains
        new(0.88f, 0.84f, 0.64f), // Desert
    };

    private static Font Font => ThemeDB.FallbackFont;

    public override Color Sea => new(0.08f, 0.38f, 0.64f);

    public override void Coast(CanvasItem c, Vector2 center, Vector2[] corners, int pass) =>
        c.DrawColoredPolygon(Scaled(center, corners, pass == 0 ? 1.24f : 1.12f), pass == 0 ? Shallow : Sand);

    public override void Hex(CanvasItem c, Vector2 center, Vector2[] corners, Terrain terrain)
    {
        var color = TerrainColors[(int)terrain];
        c.DrawColoredPolygon(Scaled(center, corners, 1.0f), Seam);
        c.DrawColoredPolygon(Scaled(center, corners, 0.955f), color.Darkened(0.12f));
        c.DrawColoredPolygon(Scaled(center, corners, 0.9f), color);
        float size = corners[0].DistanceTo(center);
        Icons.Skin.Terrain(c, center - new Vector2(0, size * (terrain == Terrain.Desert ? 0.05f : 0.38f)), size * 0.72f, terrain);
    }

    public override void Token(CanvasItem c, Vector2 center, float size, int number, int pips)
    {
        var color = number is 6 or 8 ? TokenRed : TokenGreen;
        var at = center + new Vector2(0, size * 0.24f);
        float half = size * 0.25f;
        var rect = new Rect2(at - new Vector2(half, half), new Vector2(half * 2, half * 2));
        var style = new StyleBoxFlat { BgColor = TokenFill, AntiAliasing = true, ShadowColor = new Color(0, 0, 0, 0.3f), ShadowSize = 2, ShadowOffset = new Vector2(0, 1.5f) };
        style.SetCornerRadiusAll((int)(half * 0.3f));
        c.DrawStyleBox(style, rect);
        int fontSize = (int)(size * 0.33f);
        var textAt = at - new Vector2(60, size * 0.04f - fontSize * 0.35f);
        // Bold: the text with a thin outline in its own color.
        c.DrawStringOutline(Font, textAt, number.ToString(), HorizontalAlignment.Center, 120, fontSize, 3, color);
        c.DrawString(Font, textAt, number.ToString(), HorizontalAlignment.Center, 120, fontSize, color);
        for (int i = 0; i < pips; i++)
            c.DrawCircle(at + new Vector2((i - (pips - 1) / 2f) * size * 0.07f, half * 0.7f), size * 0.026f, color);
    }

    public override void Harbor(CanvasItem c, Vector2 a, Vector2 b, Vector2 labelAt, float size, HarborType type)
    {
        // Wooden docks from each corner out toward the ship.
        foreach (var corner in new[] { a, b })
        {
            var end = corner.Lerp(labelAt, 0.62f);
            c.DrawLine(corner, end, Plank.Darkened(0.35f), size * 0.13f);
            c.DrawLine(corner, end, Plank, size * 0.09f);
            var along = (end - corner).Normalized();
            var across = new Vector2(-along.Y, along.X) * size * 0.07f;
            for (float t = 0.2f; t < 1f; t += 0.25f)
            {
                var p = corner.Lerp(end, t);
                c.DrawLine(p - across, p + across, Plank.Darkened(0.35f), 1.5f);
            }
        }

        // The ship: a brown hull and a white sail showing the harbor.
        float s = size * 0.36f;
        var hullColor = new Color(0.55f, 0.33f, 0.16f);
        var hull = new[]
        {
            labelAt + new Vector2(-s * 0.95f, s * 0.35f), labelAt + new Vector2(s * 0.95f, s * 0.35f),
            labelAt + new Vector2(s * 0.65f, s * 0.8f), labelAt + new Vector2(-s * 0.65f, s * 0.8f),
        };
        c.DrawColoredPolygon(hull, hullColor);
        c.DrawLine(hull[0], hull[1], new Color(0.95f, 0.8f, 0.5f), 2);
        var sail = new Rect2(labelAt - new Vector2(s * 0.7f, s * 1.05f), new Vector2(s * 1.4f, s * 1.3f));
        var style = new StyleBoxFlat { BgColor = new Color(0.99f, 0.99f, 0.97f), AntiAliasing = true, BorderColor = new Color(0.75f, 0.75f, 0.78f), ShadowColor = new Color(0, 0, 0, 0.25f), ShadowSize = 2 };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll((int)(s * 0.35f));
        c.DrawStyleBox(style, sail);
        c.DrawLine(labelAt + new Vector2(0, -s * 1.3f), labelAt + new Vector2(0, -s * 1.05f), hullColor, 2);
        int fontSize = (int)(size * 0.15f);
        var ink = new Color(0.2f, 0.2f, 0.25f);
        var iconAt = sail.GetCenter() - new Vector2(0, sail.Size.Y * 0.18f);
        if (type == HarborType.Generic)
            Text(c, iconAt, "?", (int)(fontSize * 1.4f), ink);
        else
            Icons.Skin.Resource(c, iconAt, s * 0.75f, (int)type);
        Text(c, sail.GetCenter() + new Vector2(0, sail.Size.Y * 0.25f), type == HarborType.Generic ? "3:1" : "2:1", fontSize, ink);
    }

    public override void Road(CanvasItem c, Vector2 a, Vector2 b, float size, Color color)
    {
        var inset = (b - a) * 0.15f;
        c.DrawLine(a + inset, b - inset, Outline, size * 0.16f, true);
        c.DrawLine(a + inset, b - inset, color, size * 0.11f, true);
        // A lighter top edge gives the bar some depth.
        var up = new Vector2(0, -size * 0.02f);
        c.DrawLine(a + inset * 1.1f + up, b - inset * 1.1f + up, color.Lightened(0.25f), size * 0.03f, true);
    }

    public override void Settlement(CanvasItem c, Vector2 at, float size, Color color)
    {
        float r = size * 0.19f;
        var front = new[] { at + new Vector2(-r, r), at + new Vector2(-r, -r * 0.2f), at + new Vector2(-r * 0.2f, -r), at + new Vector2(r * 0.6f, -r * 0.2f), at + new Vector2(r * 0.6f, r) };
        var side = new[] { at + new Vector2(r * 0.6f, r), at + new Vector2(r * 0.6f, -r * 0.2f), at + new Vector2(r, -r * 0.45f), at + new Vector2(r, r * 0.75f) };
        c.DrawColoredPolygon(side, color.Darkened(0.3f));
        c.DrawColoredPolygon(front, color);
        c.DrawPolyline(Closed(front), Outline, 1.8f, true);
        c.DrawPolyline(Closed(side), Outline, 1.8f, true);
        c.DrawColoredPolygon(new[] { at + new Vector2(-r * 0.25f, r), at + new Vector2(-r * 0.25f, r * 0.35f), at + new Vector2(r * 0.15f, r * 0.35f), at + new Vector2(r * 0.15f, r) }, color.Darkened(0.45f));
    }

    public override void City(CanvasItem c, Vector2 at, float size, Color color)
    {
        float r = size * 0.26f;
        var tower = new[] { at + new Vector2(-r, r), at + new Vector2(-r, -r * 0.35f), at + new Vector2(-r * 0.55f, -r), at + new Vector2(-r * 0.1f, -r * 0.35f), at + new Vector2(-r * 0.1f, r) };
        var hall = new[] { at + new Vector2(-r * 0.1f, r), at + new Vector2(-r * 0.1f, -r * 0.1f), at + new Vector2(r * 0.75f, -r * 0.1f), at + new Vector2(r * 0.75f, r) };
        var side = new[] { at + new Vector2(r * 0.75f, r), at + new Vector2(r * 0.75f, -r * 0.1f), at + new Vector2(r, -r * 0.3f), at + new Vector2(r, r * 0.8f) };
        c.DrawColoredPolygon(side, color.Darkened(0.3f));
        c.DrawColoredPolygon(hall, color.Darkened(0.1f));
        c.DrawColoredPolygon(tower, color);
        foreach (var shape in new[] { tower, hall, side })
            c.DrawPolyline(Closed(shape), Outline, 1.8f, true);
    }

    public override void Robber(CanvasItem c, Vector2 at, float size)
    {
        var body = new Color(0.62f, 0.64f, 0.67f);
        var dark = body.Darkened(0.35f);
        float r = size * 0.1f;
        var cone = new[] { at + new Vector2(-r * 1.3f, r * 2.2f), at + new Vector2(r * 1.3f, r * 2.2f), at + new Vector2(r * 0.8f, 0), at + new Vector2(-r * 0.8f, 0) };
        c.DrawColoredPolygon(cone, body);
        c.DrawPolyline(Closed(cone), dark, 1.5f, true);
        c.DrawCircle(at - new Vector2(0, r * 0.4f), r * 0.95f, body);
        c.DrawArc(at - new Vector2(0, r * 0.4f), r * 0.95f, 0, Mathf.Tau, 24, dark, 1.5f, true);
    }

    public override void HighlightVertex(CanvasItem c, Vector2 at, float size, bool hover)
    {
        float r = size * (hover ? 0.2f : 0.17f);
        c.DrawCircle(at, r, new Color(Target, hover ? 0.75f : 0.4f));
        c.DrawArc(at, r, 0, Mathf.Tau, 32, new Color(0.85f, 0.75f, 0.1f, hover ? 1f : 0.8f), hover ? 3 : 2, true);
    }

    public override void HighlightEdge(CanvasItem c, Vector2 a, Vector2 b, float size, bool hover)
    {
        var inset = (b - a) * 0.22f;
        c.DrawLine(a + inset, b - inset, new Color(0.85f, 0.75f, 0.1f, hover ? 1f : 0.8f), size * (hover ? 0.15f : 0.12f), true);
        c.DrawLine(a + inset, b - inset, new Color(Target, hover ? 0.95f : 0.7f), size * (hover ? 0.1f : 0.07f), true);
    }

    public override void HighlightHex(CanvasItem c, Vector2[] corners, bool hover)
    {
        var center = Vector2.Zero;
        foreach (var p in corners)
            center += p / corners.Length;
        c.DrawColoredPolygon(Scaled(center, corners, 0.9f), new Color(Target, hover ? 0.4f : 0.18f));
        c.DrawPolyline(Closed(Scaled(center, corners, 0.93f)), new Color(0.95f, 0.85f, 0.15f, hover ? 1f : 0.85f), hover ? 6 : 4, true);
    }

    private static Vector2[] Scaled(Vector2 center, Vector2[] corners, float scale)
    {
        var points = new Vector2[corners.Length];
        for (int i = 0; i < corners.Length; i++)
            points[i] = center + (corners[i] - center) * scale;
        return points;
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
