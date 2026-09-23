using Catan.Core;
using Godot;

/// <summary>Small HUD icons besides resources and dev cards.</summary>
public enum StatIcon { Cards, DevCards, Knight, Road, Trophy }

/// <summary>
/// How cards and icons look (resource and dev card art, card faces and backs, stat icons). The HUD and the board's harbors
/// and terrain say what to draw and where; the skin decides how. <see cref="FlatIcons"/> draws simple shapes in code; the
/// later art pass adds a sprite skin without touching the views. <see cref="Icons.Skin"/> is the one in use.
/// </summary>
public abstract class IconSkin
{
    /// <summary>A resource picture centered at <paramref name="at"/>, about <paramref name="size"/> across.</summary>
    public abstract void Resource(CanvasItem c, Vector2 at, float size, int resource);

    public abstract void DevCard(CanvasItem c, Vector2 at, float size, DevCardType type);

    public abstract void Stat(CanvasItem c, Vector2 at, float size, StatIcon icon, Color color);

    /// <summary>A resource or dev card face filling <paramref name="rect"/>.</summary>
    public abstract void CardFace(CanvasItem c, Rect2 rect, bool isDev, int type, bool dimmed);

    public abstract void CardBack(CanvasItem c, Rect2 rect, bool isDev);

    public abstract Color ResourceColor(int resource);
}

public static class Icons
{
    public static IconSkin Skin { get; set; } = new FlatIcons();
}

/// <summary>The flat look: colored cards with simple vector pictures.</summary>
public sealed class FlatIcons : IconSkin
{
    private static readonly Color[] CardColors =
    {
        new(0.80f, 0.36f, 0.22f), // Brick
        new(0.20f, 0.52f, 0.28f), // Lumber
        new(0.56f, 0.80f, 0.36f), // Wool
        new(0.96f, 0.78f, 0.26f), // Grain
        new(0.52f, 0.58f, 0.66f), // Ore
    };

    private static readonly Color DevColor = new(0.42f, 0.30f, 0.62f);
    private static readonly Color BackColor = new(0.18f, 0.30f, 0.52f);
    private static readonly Color Ink = new(0.16f, 0.14f, 0.13f);
    private static readonly Color Gold = new(0.98f, 0.80f, 0.20f);

    public override Color ResourceColor(int resource) => CardColors[resource];

    public override void Resource(CanvasItem c, Vector2 at, float s, int resource)
    {
        switch ((Catan.Core.Resource)resource)
        {
            case Catan.Core.Resource.Brick: Brick(c, at, s); break;
            case Catan.Core.Resource.Lumber: Logs(c, at, s); break;
            case Catan.Core.Resource.Wool: Sheep(c, at, s); break;
            case Catan.Core.Resource.Grain: Wheat(c, at, s); break;
            default: Ore(c, at, s); break;
        }
    }

    public override void DevCard(CanvasItem c, Vector2 at, float s, DevCardType type)
    {
        switch (type)
        {
            case DevCardType.Knight: Shield(c, at, s, new Color(0.75f, 0.78f, 0.84f)); break;
            case DevCardType.VictoryPoint: Star(c, at, s * 0.5f, Gold); break;
            case DevCardType.RoadBuilding:
                RoadPiece(c, at + new Vector2(-s * 0.18f, s * 0.05f), s * 0.8f, -0.6f);
                RoadPiece(c, at + new Vector2(s * 0.18f, -s * 0.05f), s * 0.8f, 0.6f);
                break;
            case DevCardType.YearOfPlenty:
                MiniCard(c, at + new Vector2(-s * 0.14f, 0), s * 0.55f, -0.25f, CardColors[3]);
                MiniCard(c, at + new Vector2(s * 0.14f, 0), s * 0.55f, 0.25f, CardColors[4]);
                Plus(c, at + new Vector2(0, s * 0.02f), s * 0.28f);
                break;
            default: Crown(c, at, s); break;
        }
    }

    public override void Stat(CanvasItem c, Vector2 at, float s, StatIcon icon, Color color)
    {
        switch (icon)
        {
            case StatIcon.Cards:
                var r = new Rect2(at - new Vector2(s * 0.32f, s * 0.45f), new Vector2(s * 0.64f, s * 0.9f));
                CardBack(c, r, false);
                break;
            case StatIcon.DevCards:
                CardBack(c, new Rect2(at - new Vector2(s * 0.32f, s * 0.45f), new Vector2(s * 0.64f, s * 0.9f)), true);
                break;
            case StatIcon.Knight: Shield(c, at, s * 0.95f, color); break;
            case StatIcon.Road: RoadPiece(c, at, s * 1.1f, -0.7f, color); break;
            default: Trophy(c, at, s, color); break;
        }
    }

    public override void CardFace(CanvasItem c, Rect2 rect, bool isDev, int type, bool dimmed)
    {
        var fill = isDev ? DevColor : CardColors[type];
        float radius = rect.Size.X * 0.12f;
        Rounded(c, rect, Colors.White, radius, shadow: true);
        var inner = rect.Grow(-rect.Size.X * 0.07f);
        Rounded(c, inner, fill, radius * 0.7f);
        // A lighter circle behind the picture, like a medallion.
        var center = inner.GetCenter();
        float size = inner.Size.X * 0.78f;
        c.DrawCircle(center, size * 0.55f, fill.Lightened(0.28f));
        if (isDev)
            DevCard(c, center, size, (DevCardType)type);
        else
            Resource(c, center, size, type);
        if (dimmed)
            Rounded(c, rect, new Color(1, 1, 1, 0.55f), radius);
    }

    public override void CardBack(CanvasItem c, Rect2 rect, bool isDev)
    {
        float radius = rect.Size.X * 0.14f;
        Rounded(c, rect, Colors.White, radius);
        var inner = rect.Grow(-Mathf.Max(1.5f, rect.Size.X * 0.08f));
        Rounded(c, inner, isDev ? DevColor : BackColor, radius * 0.7f);
        var m = inner.GetCenter();
        float d = inner.Size.X * 0.28f;
        c.DrawColoredPolygon(new[] { m + new Vector2(0, -d * 1.3f), m + new Vector2(d, 0), m + new Vector2(0, d * 1.3f), m + new Vector2(-d, 0) },
            isDev ? Gold : new Color(1, 1, 1, 0.35f));
    }

    // ---- Pictures ----

    private static void Brick(CanvasItem c, Vector2 at, float s)
    {
        var brick = new Color(0.62f, 0.2f, 0.12f);
        float w = s * 0.3f, h = s * 0.15f, gap = s * 0.03f;
        // Rows of 2, 3, 2 bricks, offset like a wall.
        int[] counts = { 2, 3, 2 };
        for (int row = 0; row < 3; row++)
        {
            float y = at.Y + (row - 1) * (h + gap) - h / 2;
            float rowWidth = counts[row] * w + (counts[row] - 1) * gap;
            for (int i = 0; i < counts[row]; i++)
            {
                var rect = new Rect2(at.X - rowWidth / 2 + i * (w + gap), y, w, h);
                c.DrawRect(rect, brick);
                c.DrawRect(new Rect2(rect.Position, new Vector2(w, h * 0.25f)), brick.Lightened(0.2f));
            }
        }
    }

    private static void Logs(CanvasItem c, Vector2 at, float s)
    {
        float r = s * 0.16f;
        var bark = new Color(0.40f, 0.24f, 0.10f);
        var wood = new Color(0.85f, 0.66f, 0.40f);
        Vector2[] ends = { at + new Vector2(-r * 1.05f, r * 0.9f), at + new Vector2(r * 1.05f, r * 0.9f), at + new Vector2(0, -r * 0.95f) };
        foreach (var e in ends)
        {
            c.DrawCircle(e, r, bark);
            c.DrawCircle(e, r * 0.78f, wood);
            c.DrawArc(e, r * 0.45f, 0, Mathf.Tau, 20, bark.Lightened(0.15f), Mathf.Max(1, r * 0.1f), true);
            c.DrawCircle(e, r * 0.12f, bark);
        }
    }

    private static void Sheep(CanvasItem c, Vector2 at, float s)
    {
        var fleece = new Color(0.98f, 0.98f, 0.96f);
        var face = new Color(0.22f, 0.2f, 0.2f);
        float r = s * 0.12f;
        c.DrawLine(at + new Vector2(-r * 1.2f, r), at + new Vector2(-r * 1.2f, r * 2.4f), face, r * 0.35f);
        c.DrawLine(at + new Vector2(r * 0.9f, r), at + new Vector2(r * 0.9f, r * 2.4f), face, r * 0.35f);
        foreach (var o in new[] { new Vector2(-1.3f, 0), new Vector2(-0.5f, -0.7f), new Vector2(0.5f, -0.7f), new Vector2(1.1f, 0), new Vector2(-0.6f, 0.6f), new Vector2(0.4f, 0.6f), new Vector2(0, 0) })
            c.DrawCircle(at + o * r, r * 0.85f, fleece);
        c.DrawCircle(at + new Vector2(r * 2.1f, -r * 0.4f), r * 0.62f, face);
        c.DrawCircle(at + new Vector2(r * 2.25f, -r * 0.55f), r * 0.12f, Colors.White);
    }

    private static void Wheat(CanvasItem c, Vector2 at, float s)
    {
        var stalk = new Color(0.62f, 0.45f, 0.12f);
        var grain = new Color(0.86f, 0.62f, 0.14f);
        for (int k = -1; k <= 1; k++)
        {
            float angle = k * 0.28f;
            var dir = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
            var baseAt = at + new Vector2(0, s * 0.42f);
            var top = baseAt + dir * s * 0.8f;
            c.DrawLine(baseAt, top, stalk, Mathf.Max(1.5f, s * 0.035f), true);
            for (int i = 0; i < 4; i++)
            {
                var p = baseAt + dir * s * (0.4f + i * 0.11f);
                var side = new Vector2(dir.Y, -dir.X);
                Kernel(c, p + side * s * 0.05f, dir.Rotated(0.6f), s * 0.07f, grain);
                Kernel(c, p - side * s * 0.05f, dir.Rotated(-0.6f), s * 0.07f, grain);
            }
            Kernel(c, top, dir, s * 0.07f, grain);
        }
    }

    private static void Kernel(CanvasItem c, Vector2 at, Vector2 dir, float len, Color color)
    {
        var side = new Vector2(dir.Y, -dir.X) * len * 0.45f;
        c.DrawColoredPolygon(new[] { at + dir * len, at + side, at - dir * len * 0.6f, at - side }, color);
    }

    private static void Ore(CanvasItem c, Vector2 at, float s)
    {
        var rock = new Color(0.36f, 0.40f, 0.46f);
        float r = s * 0.36f;
        var outline = new[]
        {
            at + new Vector2(-r, r * 0.55f), at + new Vector2(-r * 0.75f, -r * 0.25f), at + new Vector2(-r * 0.2f, -r * 0.75f),
            at + new Vector2(r * 0.45f, -r * 0.6f), at + new Vector2(r, 0), at + new Vector2(r * 0.8f, r * 0.55f),
        };
        c.DrawColoredPolygon(outline, rock);
        c.DrawColoredPolygon(new[] { outline[1], outline[2], outline[3], at + new Vector2(0, -r * 0.05f) }, rock.Lightened(0.3f));
        c.DrawColoredPolygon(new[] { outline[3], outline[4], at + new Vector2(0, -r * 0.05f) }, rock.Lightened(0.12f));
        // A glint of metal.
        Star(c, at + new Vector2(r * 0.3f, r * 0.2f), r * 0.22f, new Color(0.9f, 0.95f, 1f));
    }

    private static void Shield(CanvasItem c, Vector2 at, float s, Color color)
    {
        float w = s * 0.34f, h = s * 0.42f;
        var shape = new[]
        {
            at + new Vector2(-w, -h), at + new Vector2(w, -h), at + new Vector2(w, h * 0.1f),
            at + new Vector2(0, h), at + new Vector2(-w, h * 0.1f),
        };
        c.DrawColoredPolygon(shape, color);
        c.DrawPolyline(Closed(shape), Ink, Mathf.Max(1, s * 0.04f), true);
        c.DrawLine(at + new Vector2(0, -h * 0.7f), at + new Vector2(0, h * 0.6f), Ink, Mathf.Max(1, s * 0.05f));
        c.DrawLine(at + new Vector2(-w * 0.6f, -h * 0.25f), at + new Vector2(w * 0.6f, -h * 0.25f), Ink, Mathf.Max(1, s * 0.05f));
    }

    private static void Star(CanvasItem c, Vector2 at, float r, Color color)
    {
        var points = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
            points[i] = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? r : r * 0.45f);
        }
        c.DrawColoredPolygon(points, color);
    }

    private static void RoadPiece(CanvasItem c, Vector2 at, float s, float angle, Color? color = null)
    {
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * s * 0.36f;
        c.DrawLine(at - dir, at + dir, Ink, s * 0.2f, true);
        c.DrawLine(at - dir * 0.94f, at + dir * 0.94f, color ?? new Color(0.78f, 0.55f, 0.3f), s * 0.13f, true);
    }

    private void MiniCard(CanvasItem c, Vector2 at, float s, float angle, Color fill)
    {
        var half = new Vector2(s * 0.3f, s * 0.42f);
        var corners = new[] { -half, new Vector2(half.X, -half.Y), half, new Vector2(-half.X, half.Y) };
        var points = new Vector2[4];
        for (int i = 0; i < 4; i++)
            points[i] = at + corners[i].Rotated(angle);
        c.DrawColoredPolygon(points, Colors.White);
        var inner = new Vector2[4];
        for (int i = 0; i < 4; i++)
            inner[i] = at + (corners[i] * 0.8f).Rotated(angle);
        c.DrawColoredPolygon(inner, fill);
    }

    private static void Plus(CanvasItem c, Vector2 at, float s)
    {
        c.DrawCircle(at, s * 0.55f, Colors.White);
        c.DrawLine(at - new Vector2(s * 0.3f, 0), at + new Vector2(s * 0.3f, 0), new Color(0.2f, 0.6f, 0.25f), s * 0.16f);
        c.DrawLine(at - new Vector2(0, s * 0.3f), at + new Vector2(0, s * 0.3f), new Color(0.2f, 0.6f, 0.25f), s * 0.16f);
    }

    private static void Crown(CanvasItem c, Vector2 at, float s)
    {
        float w = s * 0.38f, h = s * 0.3f;
        var shape = new[]
        {
            at + new Vector2(-w, h), at + new Vector2(-w, -h * 0.6f), at + new Vector2(-w * 0.5f, 0), at + new Vector2(0, -h),
            at + new Vector2(w * 0.5f, 0), at + new Vector2(w, -h * 0.6f), at + new Vector2(w, h),
        };
        c.DrawColoredPolygon(shape, Gold);
        c.DrawPolyline(Closed(shape), Ink, Mathf.Max(1, s * 0.035f), true);
        c.DrawCircle(at + new Vector2(0, h * 0.45f), s * 0.06f, new Color(0.8f, 0.15f, 0.2f));
    }

    private static void Trophy(CanvasItem c, Vector2 at, float s, Color color)
    {
        float w = s * 0.3f;
        var cup = new[] { at + new Vector2(-w, -s * 0.38f), at + new Vector2(w, -s * 0.38f), at + new Vector2(w * 0.7f, 0), at + new Vector2(-w * 0.7f, 0) };
        c.DrawColoredPolygon(cup, color);
        c.DrawLine(at, at + new Vector2(0, s * 0.25f), color, s * 0.1f);
        c.DrawRect(new Rect2(at + new Vector2(-w * 0.7f, s * 0.25f), new Vector2(w * 1.4f, s * 0.12f)), color);
    }

    // ---- Helpers ----

    public static void Rounded(CanvasItem c, Rect2 rect, Color fill, float radius, bool shadow = false)
    {
        var style = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        style.SetCornerRadiusAll((int)radius);
        if (shadow)
        {
            style.ShadowColor = new Color(0, 0, 0, 0.28f);
            style.ShadowSize = 3;
            style.ShadowOffset = new Vector2(0, 2);
        }
        c.DrawStyleBox(style, rect);
    }

    private static Vector2[] Closed(Vector2[] points)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        return closed;
    }
}
