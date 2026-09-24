using System;
using Catan.Core;
using Godot;

/// <summary>
/// The detailed look, still drawn in code: tiles with a soft gradient and scattered scenery (pine forests, grassy pastures
/// with sheep, rows of wheat, clay hills, snow-capped mountains, desert dunes), a foamy coast, shaded pieces with roofs and
/// windows, plank roads, a cloaked robber and proper sailing ships at the harbors. Scenery positions come from a hash of the
/// tile's position, so a board always looks the same. <see cref="FlatSkin"/> is the simple fallback.
/// </summary>
public sealed class PaintedSkin : BoardSkin
{
    private static readonly Color Outline = new(0.12f, 0.10f, 0.10f);
    private static readonly Color Shallow = new(0.30f, 0.58f, 0.84f);
    private static readonly Color Foam = new(0.80f, 0.90f, 0.96f);
    private static readonly Color Sand = new(0.94f, 0.84f, 0.60f);
    private static readonly Color Seam = new(0.93f, 0.86f, 0.68f);
    private static readonly Color TokenFill = new(0.99f, 0.97f, 0.92f);
    private static readonly Color TokenGreen = new(0.10f, 0.33f, 0.14f);
    private static readonly Color TokenRed = new(0.80f, 0.10f, 0.10f);
    private static readonly Color Wood = new(0.62f, 0.42f, 0.22f);
    private static readonly Color Target = new(1f, 0.93f, 0.35f);

    private static readonly Color[] Base =
    {
        new(0.80f, 0.40f, 0.22f), // Hills
        new(0.16f, 0.46f, 0.22f), // Forest
        new(0.52f, 0.76f, 0.28f), // Pasture
        new(0.93f, 0.73f, 0.24f), // Fields
        new(0.58f, 0.60f, 0.63f), // Mountains
        new(0.90f, 0.82f, 0.60f), // Desert
    };

    private static Font Font => ThemeDB.FallbackFont;

    public override Color Sea => new(0.07f, 0.35f, 0.61f);

    // ---- Board ----

    public override void Coast(CanvasItem c, Vector2 center, Vector2[] corners, int pass)
    {
        if (pass == 0)
        {
            Fan(c, center, corners, 1.26f, Shallow.Lightened(0.1f), Shallow);
            return;
        }
        c.DrawColoredPolygon(Scaled(center, corners, 1.16f), new Color(Foam, 0.85f));
        Fan(c, center, corners, 1.12f, Sand.Lightened(0.08f), Sand.Darkened(0.05f));
    }

    public override void Hex(CanvasItem c, Vector2 center, Vector2[] corners, Terrain terrain)
    {
        var color = Base[(int)terrain];
        float s = corners[0].DistanceTo(center);
        c.DrawColoredPolygon(corners, Seam);
        Fan(c, center, corners, 0.95f, color.Lightened(0.14f), color.Darkened(0.14f));
        uint seed = (uint)(Mathf.RoundToInt(center.X) * 73856093) ^ (uint)(Mathf.RoundToInt(center.Y) * 19349663);
        switch (terrain)
        {
            case Terrain.Forest: Forest(c, center, s, seed); break;
            case Terrain.Pasture: Pasture(c, center, s, seed); break;
            case Terrain.Fields: Fields(c, center, s, seed); break;
            case Terrain.Hills: Hills(c, center, s, seed); break;
            case Terrain.Mountains: Mountains(c, center, s, seed); break;
            default: Desert(c, center, s, seed); break;
        }
        c.DrawPolyline(Closed(Scaled(center, corners, 0.95f)), color.Darkened(0.3f), 1.5f, true);
    }

    public override void Token(CanvasItem c, Vector2 center, float size, int number, int pips)
    {
        var color = number is 6 or 8 ? TokenRed : TokenGreen;
        var at = center + new Vector2(0, size * 0.24f);
        float half = size * 0.25f;
        var rect = new Rect2(at - new Vector2(half, half), new Vector2(half * 2, half * 2));
        var style = new StyleBoxFlat { BgColor = TokenFill, AntiAliasing = true, ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 3, ShadowOffset = new Vector2(0, 2) };
        style.SetCornerRadiusAll((int)(half * 0.3f));
        style.BorderColor = new Color(0.82f, 0.76f, 0.62f);
        style.SetBorderWidthAll(2);
        c.DrawStyleBox(style, rect);
        int fontSize = (int)(size * 0.33f);
        var textAt = at - new Vector2(60, size * 0.04f - fontSize * 0.35f);
        c.DrawStringOutline(Font, textAt, number.ToString(), HorizontalAlignment.Center, 120, fontSize, 3, color);
        c.DrawString(Font, textAt, number.ToString(), HorizontalAlignment.Center, 120, fontSize, color);
        for (int i = 0; i < pips; i++)
            c.DrawCircle(at + new Vector2((i - (pips - 1) / 2f) * size * 0.07f, half * 0.7f), size * 0.026f, color);
    }

    public override void Harbor(CanvasItem c, Vector2 a, Vector2 b, Vector2 labelAt, float size, HarborType type)
    {
        // Wooden piers from each corner toward the ship.
        foreach (var corner in new[] { a, b })
        {
            var end = corner.Lerp(labelAt, 0.6f);
            var along = (end - corner).Normalized();
            var across = new Vector2(-along.Y, along.X) * size * 0.07f;
            c.DrawColoredPolygon(new[] { corner - across, corner + across, end + across, end - across }, Wood);
            for (float t = 0.1f; t < 1f; t += 0.18f)
            {
                var p = corner.Lerp(end, t);
                c.DrawLine(p - across, p + across, Wood.Darkened(0.35f), 1.5f);
            }
            c.DrawLine(corner - across, end - across, Wood.Darkened(0.4f), 1.2f);
            c.DrawLine(corner + across, end + across, Wood.Darkened(0.4f), 1.2f);
        }

        // The ship: a curved hull with planks, a mast and a sail showing the harbor.
        float s = size * 0.38f;
        var hullTop = labelAt.Y + s * 0.3f;
        var hull = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float t = i / 9f;
            float x = Mathf.Lerp(-s, s, t);
            float dip = Mathf.Sin(t * Mathf.Pi) * s * 0.5f;
            hull[i] = new Vector2(labelAt.X + x * (i is 0 or 9 ? 1.05f : 0.95f), hullTop + dip);
        }
        var hullShape = new Vector2[12];
        hullShape[0] = new Vector2(labelAt.X - s * 1.1f, hullTop - s * 0.12f);
        Array.Copy(hull, 0, hullShape, 1, 10);
        hullShape[11] = new Vector2(labelAt.X + s * 1.1f, hullTop - s * 0.12f);
        var hullColor = new Color(0.50f, 0.30f, 0.15f);
        c.DrawColoredPolygon(hullShape, hullColor);
        c.DrawPolyline(new[] { hullShape[0], hullShape[11] }, hullColor.Lightened(0.35f), 2.5f);
        c.DrawLine(new Vector2(labelAt.X - s * 0.8f, hullTop + s * 0.18f), new Vector2(labelAt.X + s * 0.8f, hullTop + s * 0.18f), hullColor.Darkened(0.3f), 1.2f);
        c.DrawLine(new Vector2(labelAt.X, hullTop), new Vector2(labelAt.X, labelAt.Y - s * 1.35f), new Color(0.35f, 0.22f, 0.1f), 2.5f);
        c.DrawColoredPolygon(new[] { new Vector2(labelAt.X, labelAt.Y - s * 1.4f), new Vector2(labelAt.X + s * 0.35f, labelAt.Y - s * 1.3f), new Vector2(labelAt.X, labelAt.Y - s * 1.2f) }, new Color(0.85f, 0.2f, 0.2f));

        var sail = new Rect2(labelAt - new Vector2(s * 0.72f, s * 1.1f), new Vector2(s * 1.44f, s * 1.3f));
        var style = new StyleBoxFlat { BgColor = new Color(0.99f, 0.98f, 0.94f), AntiAliasing = true, BorderColor = new Color(0.72f, 0.68f, 0.6f), ShadowColor = new Color(0, 0, 0, 0.25f), ShadowSize = 2 };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll((int)(s * 0.3f));
        c.DrawStyleBox(style, sail);
        int fontSize = (int)(size * 0.15f);
        var ink = new Color(0.2f, 0.2f, 0.25f);
        var iconAt = sail.GetCenter() - new Vector2(0, sail.Size.Y * 0.18f);
        if (type == HarborType.Generic)
            Text(c, iconAt, "?", (int)(fontSize * 1.4f), ink);
        else
            Icons.Skin.Resource(c, iconAt, s * 0.75f, (int)type);
        Text(c, sail.GetCenter() + new Vector2(0, sail.Size.Y * 0.25f), type == HarborType.Generic ? "3:1" : "2:1", fontSize, ink);
    }

    // ---- Pieces ----

    public override void Road(CanvasItem c, Vector2 a, Vector2 b, float size, Color color)
    {
        var inset = (b - a) * 0.14f;
        Vector2 p = a + inset, q = b - inset;
        var along = (q - p).Normalized();
        var across = new Vector2(-along.Y, along.X) * size * 0.075f;
        var body = new[] { p - across, q - across, q + across, p + across };
        c.DrawColoredPolygon(new[] { body[0] + new Vector2(1.5f, 2.5f), body[1] + new Vector2(1.5f, 2.5f), body[2] + new Vector2(1.5f, 2.5f), body[3] + new Vector2(1.5f, 2.5f) }, new Color(0, 0, 0, 0.3f));
        c.DrawColoredPolygon(body, color);
        c.DrawColoredPolygon(new[] { p - across, q - across, q - across * 0.2f, p - across * 0.2f }, color.Lightened(0.25f));
        c.DrawPolyline(Closed(body), Outline, 1.6f, true);
    }

    public override void Settlement(CanvasItem c, Vector2 at, float size, Color color)
    {
        float r = size * 0.19f;
        var roofColor = color.Darkened(0.35f);
        Shadow(c, at + new Vector2(0, r * 0.95f), r * 1.1f);
        var wall = new[] { at + new Vector2(-r * 0.85f, r), at + new Vector2(-r * 0.85f, -r * 0.1f), at + new Vector2(r * 0.85f, -r * 0.1f), at + new Vector2(r * 0.85f, r) };
        c.DrawColoredPolygon(wall, color);
        c.DrawColoredPolygon(new[] { wall[2], wall[3], at + new Vector2(r * 0.3f, r), at + new Vector2(r * 0.3f, -r * 0.1f) }, color.Darkened(0.15f));
        var roof = new[] { at + new Vector2(-r * 1.05f, -r * 0.05f), at + new Vector2(0, -r * 1.05f), at + new Vector2(r * 1.05f, -r * 0.05f) };
        c.DrawColoredPolygon(roof, roofColor);
        c.DrawColoredPolygon(new[] { roof[1], roof[2], at + new Vector2(0, -r * 0.05f) }, roofColor.Darkened(0.2f));
        c.DrawRect(new Rect2(at + new Vector2(-r * 0.2f, r * 0.35f), new Vector2(r * 0.4f, r * 0.65f)), color.Darkened(0.55f));
        c.DrawRect(new Rect2(at + new Vector2(-r * 0.65f, r * 0.15f), new Vector2(r * 0.3f, r * 0.3f)), new Color(1f, 0.93f, 0.6f));
        c.DrawPolyline(Closed(wall), Outline, 1.6f, true);
        c.DrawPolyline(Closed(roof), Outline, 1.6f, true);
    }

    public override void City(CanvasItem c, Vector2 at, float size, Color color)
    {
        float r = size * 0.26f;
        var roofColor = color.Darkened(0.35f);
        Shadow(c, at + new Vector2(0, r * 0.95f), r * 1.2f);
        // A tall tower on the left, a hall on the right.
        var tower = new[] { at + new Vector2(-r, r), at + new Vector2(-r, -r * 0.45f), at + new Vector2(-r * 0.2f, -r * 0.45f), at + new Vector2(-r * 0.2f, r) };
        var towerRoof = new[] { at + new Vector2(-r * 1.1f, -r * 0.4f), at + new Vector2(-r * 0.6f, -r * 1.15f), at + new Vector2(-r * 0.1f, -r * 0.4f) };
        var hall = new[] { at + new Vector2(-r * 0.2f, r), at + new Vector2(-r * 0.2f, -r * 0.05f), at + new Vector2(r, -r * 0.05f), at + new Vector2(r, r) };
        var hallRoof = new[] { at + new Vector2(-r * 0.25f, 0), at + new Vector2(r * 0.4f, -r * 0.6f), at + new Vector2(r * 1.05f, 0) };
        c.DrawColoredPolygon(hall, color.Darkened(0.1f));
        c.DrawColoredPolygon(hallRoof, roofColor);
        c.DrawColoredPolygon(tower, color);
        c.DrawColoredPolygon(towerRoof, roofColor);
        var window = new Color(1f, 0.93f, 0.6f);
        c.DrawRect(new Rect2(at + new Vector2(-r * 0.75f, -r * 0.2f), new Vector2(r * 0.3f, r * 0.35f)), window);
        c.DrawRect(new Rect2(at + new Vector2(-r * 0.75f, r * 0.35f), new Vector2(r * 0.3f, r * 0.3f)), window);
        c.DrawRect(new Rect2(at + new Vector2(r * 0.15f, r * 0.25f), new Vector2(r * 0.25f, r * 0.3f)), window);
        c.DrawRect(new Rect2(at + new Vector2(r * 0.55f, r * 0.25f), new Vector2(r * 0.25f, r * 0.3f)), window);
        foreach (var shape in new[] { tower, towerRoof, hall, hallRoof })
            c.DrawPolyline(Closed(shape), Outline, 1.6f, true);
    }

    public override void Robber(CanvasItem c, Vector2 at, float size)
    {
        float r = size * 0.1f;
        var cloak = new Color(0.25f, 0.25f, 0.29f);
        Shadow(c, at + new Vector2(0, r * 2.2f), r * 1.5f);
        var body = new[] { at + new Vector2(-r * 1.35f, r * 2.2f), at + new Vector2(-r * 0.7f, -r * 0.2f), at + new Vector2(r * 0.7f, -r * 0.2f), at + new Vector2(r * 1.35f, r * 2.2f) };
        c.DrawColoredPolygon(body, cloak);
        c.DrawColoredPolygon(new[] { body[0], body[1], at + new Vector2(-r * 0.2f, r * 2.2f) }, cloak.Lightened(0.15f));
        c.DrawCircle(at - new Vector2(0, r * 0.55f), r * 0.95f, cloak);
        c.DrawCircle(at - new Vector2(0, r * 0.45f), r * 0.55f, new Color(0.08f, 0.08f, 0.1f)); // the hood's shadow
        c.DrawCircle(at + new Vector2(-r * 0.2f, -r * 0.5f), r * 0.1f, new Color(1f, 0.85f, 0.3f));
        c.DrawCircle(at + new Vector2(r * 0.2f, -r * 0.5f), r * 0.1f, new Color(1f, 0.85f, 0.3f));
        c.DrawPolyline(Closed(body), Outline, 1.4f, true);
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

    // ---- Scenery ----

    /// <summary>Spots for scenery inside the tile, away from its number tile, in a jittered pattern.</summary>
    private static System.Collections.Generic.List<Vector2> Spots(Vector2 center, float s, uint seed, int count, float spacing)
    {
        var spots = new System.Collections.Generic.List<Vector2>();
        var token = center + new Vector2(0, s * 0.24f);
        for (int i = 0; i < count * 6 && spots.Count < count; i++)
        {
            var p = center + new Vector2((Hash(seed, i * 2) - 0.5f) * 1.5f * s, (Hash(seed, i * 2 + 1) - 0.5f) * 1.6f * s);
            if (!InHex(p - center, s * 0.8f) || p.DistanceTo(token) < s * 0.38f)
                continue;
            bool crowded = false;
            foreach (var q in spots)
                crowded |= q.DistanceTo(p) < spacing * s;
            if (!crowded)
                spots.Add(p);
        }
        spots.Sort((a, b) => a.Y.CompareTo(b.Y)); // back to front
        return spots;
    }

    private static void Forest(CanvasItem c, Vector2 center, float s, uint seed)
    {
        foreach (var p in Spots(center, s, seed, 8, 0.26f))
        {
            float h = s * (0.32f + 0.1f * Hash(seed, (int)p.X));
            var dark = new Color(0.07f, 0.30f, 0.13f).Lerp(new Color(0.12f, 0.38f, 0.16f), Hash(seed, (int)p.Y));
            Tree(c, p, h, dark);
        }
    }

    /// <summary>A pine tree standing at <paramref name="p"/>, about <paramref name="h"/> tall (also used on the wood card).</summary>
    public static void Tree(CanvasItem c, Vector2 p, float h, Color dark)
    {
        Shadow(c, p + new Vector2(h * 0.1f, h * 0.05f), h * 0.35f);
        c.DrawRect(new Rect2(p + new Vector2(-h * 0.05f, -h * 0.1f), new Vector2(h * 0.1f, h * 0.18f)), new Color(0.38f, 0.24f, 0.12f));
        for (int tier = 0; tier < 3; tier++)
        {
            float y = p.Y - h * 0.1f - tier * h * 0.28f, w = h * (0.4f - tier * 0.09f);
            var tri = new[] { new Vector2(p.X - w, y), new Vector2(p.X + w, y), new Vector2(p.X, y - h * 0.45f) };
            c.DrawColoredPolygon(tri, dark.Lightened(tier * 0.08f));
            c.DrawColoredPolygon(new[] { tri[0], tri[2], new Vector2(p.X, y) }, dark.Lightened(0.12f + tier * 0.08f));
        }
    }

    private static void Pasture(CanvasItem c, Vector2 center, float s, uint seed)
    {
        var grass = new Color(0.36f, 0.62f, 0.2f);
        foreach (var p in Spots(center, s, seed, 16, 0.14f))
        {
            float h = s * 0.07f;
            c.DrawLine(p, p + new Vector2(-h * 0.5f, -h), grass, 1.5f, true);
            c.DrawLine(p, p + new Vector2(0, -h * 1.2f), grass, 1.5f, true);
            c.DrawLine(p, p + new Vector2(h * 0.5f, -h), grass, 1.5f, true);
            if (Hash(seed, (int)(p.X * 3)) > 0.75f)
                c.DrawCircle(p + new Vector2(h * 0.8f, -h * 0.4f), s * 0.018f, Hash(seed, (int)p.Y) > 0.5f ? Colors.White : new Color(1f, 0.9f, 0.3f));
        }
        Icons.Skin.Resource(c, center + new Vector2(-s * 0.3f, -s * 0.4f), s * 0.42f, (int)Catan.Core.Resource.Wool);
        Icons.Skin.Resource(c, center + new Vector2(s * 0.34f, -s * 0.22f), s * 0.36f, (int)Catan.Core.Resource.Wool);
    }

    private static void Fields(CanvasItem c, Vector2 center, float s, uint seed)
    {
        // Rows of wheat across the tile, clipped to its shape.
        var dark = new Color(0.78f, 0.55f, 0.12f);
        var light = new Color(1f, 0.88f, 0.5f);
        int row = 0;
        for (float dy = -s * 0.78f; dy <= s * 0.78f; dy += s * 0.13f, row++)
        {
            float half = HalfWidth(dy, s * 0.86f);
            if (half < s * 0.08f)
                continue;
            var y = center.Y + dy;
            c.DrawLine(new Vector2(center.X - half, y), new Vector2(center.X + half, y), row % 2 == 0 ? dark : dark.Lightened(0.15f), 2.5f, true);
            for (float x = -half + s * 0.05f; x < half; x += s * 0.1f)
            {
                var p = new Vector2(center.X + x, y);
                c.DrawLine(p, p + new Vector2(-s * 0.025f, -s * 0.05f), light, 1.5f, true);
                c.DrawLine(p, p + new Vector2(s * 0.025f, -s * 0.05f), light, 1.5f, true);
            }
        }
    }

    private static void Hills(CanvasItem c, Vector2 center, float s, uint seed)
    {
        var clay = new Color(0.62f, 0.28f, 0.14f);
        // Terraces: gentle arcs.
        for (int k = 0; k < 3; k++)
        {
            float y = center.Y - s * 0.55f + k * s * 0.22f, w = s * (0.35f + k * 0.12f);
            c.DrawArc(new Vector2(center.X - s * 0.1f, y + w * 0.9f), w, -Mathf.Pi * 0.8f, -Mathf.Pi * 0.2f, 18, clay.Lightened(0.25f), 2, true);
        }
        foreach (var p in Spots(center, s, seed, 10, 0.18f))
        {
            float r = s * (0.03f + 0.03f * Hash(seed, (int)p.X));
            DrawEllipse(c, p, new Vector2(r * 1.4f, r * 0.8f), clay.Darkened(0.2f));
            DrawEllipse(c, p - new Vector2(r * 0.3f, r * 0.25f), new Vector2(r * 0.6f, r * 0.3f), clay.Lightened(0.3f));
        }
        BrickStack(c, center + new Vector2(s * 0.34f, -s * 0.42f), s * 0.14f);
    }

    /// <summary>A little pyramid of fired bricks, bottom row centered on <paramref name="at"/> (also used on the brick card).</summary>
    public static void BrickStack(CanvasItem c, Vector2 at, float brick, int rows = 3)
    {
        float bh = brick * 0.5f;
        for (int layer = 0; layer < rows; layer++)
            for (int i = 0; i < rows - layer; i++)
            {
                var rect = new Rect2(at.X - (rows - layer) * brick / 2 + i * brick, at.Y - layer * bh, brick - 1.5f, bh - 1.5f);
                c.DrawRect(rect, new Color(0.72f, 0.26f, 0.14f));
                c.DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * 0.35f)), new Color(0.86f, 0.42f, 0.28f));
            }
    }

    private static void Mountains(CanvasItem c, Vector2 center, float s, uint seed)
    {
        var rock = new Color(0.5f, 0.52f, 0.56f);
        (Vector2 Base, float W, float H)[] peaks =
        {
            (center + new Vector2(-s * 0.05f, -s * 0.05f), s * 0.42f, s * 0.7f),
            (center + new Vector2(-s * 0.45f, s * 0.05f), s * 0.3f, s * 0.45f),
            (center + new Vector2(s * 0.42f, s * 0.08f), s * 0.3f, s * 0.5f),
        };
        foreach (var (b, w, h) in peaks)
            Peak(c, b, w, h, rock);
        foreach (var p in Spots(center, s, seed, 5, 0.25f))
            if (p.Y > center.Y + s * 0.1f)
                DrawEllipse(c, p, new Vector2(s * 0.05f, s * 0.03f), rock.Darkened(0.25f));
    }

    /// <summary>A snow-capped peak with a lit and a shaded face (also used on the ore card).</summary>
    public static void Peak(CanvasItem c, Vector2 b, float w, float h, Color rock)
    {
        var top = b - new Vector2(0, h);
        c.DrawColoredPolygon(new[] { b - new Vector2(w, 0), top, b }, rock.Lightened(0.18f));
        c.DrawColoredPolygon(new[] { top, b + new Vector2(w, 0), b }, rock.Darkened(0.15f));
        // Snowcap.
        float t = 0.32f;
        var left = top.Lerp(b - new Vector2(w, 0), t);
        var right = top.Lerp(b + new Vector2(w, 0), t);
        c.DrawColoredPolygon(new[] { left, top, right, top.Lerp(b, t * 0.8f) + new Vector2(w * 0.08f, 0), top.Lerp(b, t) - new Vector2(w * 0.1f, 0) }, new Color(0.97f, 0.98f, 1f));
    }

    private static void Desert(CanvasItem c, Vector2 center, float s, uint seed)
    {
        var dune = new Color(0.82f, 0.72f, 0.48f);
        for (int k = 0; k < 4; k++)
        {
            var at = center + new Vector2((Hash(seed, k) - 0.5f) * s * 0.9f, -s * 0.5f + k * s * 0.3f);
            c.DrawArc(at + new Vector2(0, s * 0.25f), s * 0.28f, -Mathf.Pi * 0.85f, -Mathf.Pi * 0.15f, 16, dune, 2.5f, true);
        }
        Icons.Skin.Terrain(c, center + new Vector2(s * 0.12f, -s * 0.2f), s * 0.8f, Terrain.Desert);
        DrawEllipse(c, center + new Vector2(-s * 0.45f, s * 0.3f), new Vector2(s * 0.06f, s * 0.035f), new Color(0.6f, 0.52f, 0.4f));
    }

    // ---- Helpers ----

    /// <summary>A hex filled with a gradient from its center to its corners.</summary>
    private static void Fan(CanvasItem c, Vector2 center, Vector2[] corners, float scale, Color inner, Color outer)
    {
        var points = Scaled(center, corners, scale);
        for (int i = 0; i < points.Length; i++)
        {
            var j = (i + 1) % points.Length;
            c.DrawPolygon(new[] { center, points[i], points[j] }, new[] { inner, outer, outer });
        }
    }

    public static void Shadow(CanvasItem c, Vector2 at, float r) => DrawEllipse(c, at, new Vector2(r, r * 0.35f), new Color(0, 0, 0, 0.22f));

    public static void DrawEllipse(CanvasItem c, Vector2 at, Vector2 radius, Color color)
    {
        var points = new Vector2[16];
        for (int i = 0; i < points.Length; i++)
        {
            float a = i * Mathf.Tau / points.Length;
            points[i] = at + new Vector2(Mathf.Cos(a) * radius.X, Mathf.Sin(a) * radius.Y);
        }
        c.DrawColoredPolygon(points, color);
    }

    /// <summary>Half the width of a pointy-top hex (apothem <paramref name="apothem"/>) at height dy from its center.</summary>
    private static float HalfWidth(float dy, float apothem)
    {
        float s = apothem / 0.866f, ady = Mathf.Abs(dy);
        if (ady <= s / 2)
            return apothem;
        return ady >= s ? 0 : apothem * (s - ady) / (s / 2);
    }

    private static bool InHex(Vector2 p, float s) => HalfWidth(p.Y, s * 0.866f) >= Mathf.Abs(p.X);

    private static float Hash(uint seed, int i)
    {
        uint h = seed ^ (uint)i * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
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
