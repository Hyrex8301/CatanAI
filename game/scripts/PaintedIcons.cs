using Catan.Core;
using Godot;

/// <summary>
/// Card art to match <see cref="PaintedSkin"/>: each resource card is a little scene on a sky-to-ground gradient (a pine
/// grove for wood, a sheep on a hill, a wheat sheaf, a brick stack, a snowy peak with ore), dev cards have a gold frame, and
/// card backs a patterned design. Small icons (log lines, harbors, stats) stay the simple <see cref="FlatIcons"/> ones, since
/// detail is lost at that size.
/// </summary>
public sealed class PaintedIcons : IconSkin
{
    private static readonly FlatIcons Flat = new();
    private static readonly Color DevColor = new(0.42f, 0.30f, 0.62f);
    private static readonly Color BackColor = new(0.14f, 0.34f, 0.72f);
    private static readonly Color Gold = new(0.95f, 0.78f, 0.3f);

    public override void Resource(CanvasItem c, Vector2 at, float size, int resource) => Flat.Resource(c, at, size, resource);
    public override void DevCard(CanvasItem c, Vector2 at, float size, DevCardType type) => Flat.DevCard(c, at, size, type);
    public override void Terrain(CanvasItem c, Vector2 at, float size, Terrain terrain) => Flat.Terrain(c, at, size, terrain);
    public override void Stat(CanvasItem c, Vector2 at, float size, StatIcon icon, Color color) => Flat.Stat(c, at, size, icon, color);
    public override Color ResourceColor(int resource) => Flat.ResourceColor(resource);

    public override void CardFace(CanvasItem c, Rect2 rect, bool isDev, int type, bool dimmed)
    {
        // Tiny cards (log lines, the bank row) get the simple face: scenes don't read at that size.
        if (rect.Size.X < 30)
        {
            Flat.CardFace(c, rect, isDev, type, dimmed);
            return;
        }
        float radius = rect.Size.X * 0.12f;
        FlatIcons.Rounded(c, rect, Colors.White, radius, shadow: true);
        var inner = rect.Grow(-rect.Size.X * 0.07f);
        if (isDev)
            DevFace(c, inner, (DevCardType)type);
        else
            ResourceScene(c, inner, type);
        if (dimmed)
            FlatIcons.Rounded(c, rect, new Color(1, 1, 1, 0.55f), radius);
    }

    public override void CardBack(CanvasItem c, Rect2 rect, bool isDev)
    {
        if (rect.Size.X < 30)
        {
            Flat.CardBack(c, rect, isDev);
            return;
        }
        float radius = rect.Size.X * 0.12f;
        FlatIcons.Rounded(c, rect, Colors.White, radius, shadow: true);
        var inner = rect.Grow(-rect.Size.X * 0.07f);
        var fill = isDev ? DevColor : BackColor;
        Gradient(c, inner, fill.Lightened(0.15f), fill.Darkened(0.15f));
        // A diamond lattice.
        float step = inner.Size.X / 4;
        for (float x = inner.Position.X - inner.Size.Y; x < inner.End.X; x += step)
            c.DrawLine(new Vector2(x, inner.End.Y), new Vector2(x + inner.Size.Y, inner.Position.Y), new Color(1, 1, 1, 0.08f), 1.5f);
        var m = inner.GetCenter();
        float d = inner.Size.X * 0.3f;
        var diamond = new[] { m + new Vector2(0, -d * 1.3f), m + new Vector2(d, 0), m + new Vector2(0, d * 1.3f), m + new Vector2(-d, 0) };
        c.DrawColoredPolygon(diamond, isDev ? Gold : new Color(1, 1, 1, 0.25f));
        c.DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, new Color(1, 1, 1, 0.5f), 1.5f, true);
        if (!isDev)
        {
            int size = (int)(inner.Size.X * 0.5f);
            c.DrawString(ThemeDB.FallbackFont, new Vector2(inner.Position.X, m.Y + size * 0.36f), "?", HorizontalAlignment.Center, inner.Size.X, size, BackColor.Darkened(0.2f));
        }
    }

    private static void ResourceScene(CanvasItem c, Rect2 r, int resource)
    {
        var sky = new Color(0.72f, 0.86f, 0.96f);
        float w = r.Size.X, h = r.Size.Y;
        var ground = r.Position + new Vector2(w / 2, h * 0.72f);
        switch ((Catan.Core.Resource)resource)
        {
            case Catan.Core.Resource.Lumber:
                Gradient(c, r, sky, new Color(0.3f, 0.6f, 0.3f));
                Hill(c, r, new Color(0.25f, 0.52f, 0.25f));
                PaintedSkin.Tree(c, ground + new Vector2(-w * 0.22f, h * 0.02f), h * 0.42f, new Color(0.09f, 0.33f, 0.14f));
                PaintedSkin.Tree(c, ground + new Vector2(w * 0.2f, 0), h * 0.5f, new Color(0.11f, 0.36f, 0.15f));
                PaintedSkin.Tree(c, ground + new Vector2(0, h * 0.12f), h * 0.46f, new Color(0.08f, 0.3f, 0.13f));
                break;
            case Catan.Core.Resource.Wool:
                Gradient(c, r, sky, new Color(0.55f, 0.8f, 0.35f));
                Hill(c, r, new Color(0.48f, 0.74f, 0.3f));
                Flat.Resource(c, ground + new Vector2(0, -h * 0.08f), w * 0.85f, resource);
                break;
            case Catan.Core.Resource.Grain:
                Gradient(c, r, new Color(0.98f, 0.9f, 0.62f), new Color(0.92f, 0.7f, 0.25f));
                Sheaf(c, ground + new Vector2(0, h * 0.08f), h * 0.6f);
                break;
            case Catan.Core.Resource.Brick:
                Gradient(c, r, new Color(0.95f, 0.72f, 0.55f), new Color(0.72f, 0.36f, 0.2f));
                Hill(c, r, new Color(0.66f, 0.32f, 0.18f));
                PaintedSkin.BrickStack(c, ground + new Vector2(0, h * 0.08f), w * 0.3f, rows: 3);
                break;
            default:
                Gradient(c, r, sky, new Color(0.55f, 0.58f, 0.64f));
                PaintedSkin.Peak(c, ground + new Vector2(w * 0.1f, h * 0.02f), w * 0.4f, h * 0.5f, new Color(0.5f, 0.52f, 0.58f));
                PaintedSkin.Peak(c, ground + new Vector2(-w * 0.22f, h * 0.06f), w * 0.3f, h * 0.34f, new Color(0.45f, 0.47f, 0.52f));
                PaintedSkin.DrawEllipse(c, ground + new Vector2(-w * 0.1f, h * 0.16f), new Vector2(w * 0.12f, h * 0.04f), new Color(0.35f, 0.37f, 0.42f));
                PaintedSkin.DrawEllipse(c, ground + new Vector2(w * 0.2f, h * 0.18f), new Vector2(w * 0.09f, h * 0.035f), new Color(0.4f, 0.42f, 0.5f));
                break;
        }
    }

    private static void DevFace(CanvasItem c, Rect2 r, DevCardType type)
    {
        Gradient(c, r, DevColor.Lightened(0.2f), DevColor.Darkened(0.2f));
        var frame = r.Grow(-r.Size.X * 0.07f);
        c.DrawRect(frame, Gold, false, 2);
        var m = r.GetCenter();
        c.DrawCircle(m, r.Size.X * 0.36f, DevColor.Lightened(0.35f));
        c.DrawArc(m, r.Size.X * 0.36f, 0, Mathf.Tau, 32, Gold, 2, true);
        Flat.DevCard(c, m, r.Size.X * 0.6f, type);
    }

    /// <summary>A soft hill across the lower part of the card.</summary>
    private static void Hill(CanvasItem c, Rect2 r, Color color)
    {
        var points = new Vector2[18];
        for (int i = 0; i < 16; i++)
        {
            float t = i / 15f;
            points[i] = new Vector2(r.Position.X + t * r.Size.X, r.Position.Y + r.Size.Y * (0.62f - 0.1f * Mathf.Sin(t * Mathf.Pi)));
        }
        points[16] = r.End;
        points[17] = new Vector2(r.Position.X, r.End.Y);
        c.DrawColoredPolygon(points, color);
    }

    /// <summary>A bundle of wheat stalks tied in the middle.</summary>
    private static void Sheaf(CanvasItem c, Vector2 at, float h)
    {
        var stalk = new Color(0.7f, 0.5f, 0.12f);
        var grain = new Color(0.98f, 0.82f, 0.35f);
        for (int k = -3; k <= 3; k++)
        {
            float lean = k * 0.09f;
            var bottom = at + new Vector2(k * h * 0.035f, 0);
            var mid = at + new Vector2(0, -h * 0.35f);
            var top = mid + new Vector2(lean * h * 1.6f, -h * 0.55f);
            c.DrawLine(bottom, mid, stalk, 2, true);
            c.DrawLine(mid, top, stalk, 2, true);
            for (int g = 0; g < 4; g++)
            {
                var p = top.Lerp(mid, 0.1f + g * 0.14f);
                PaintedSkin.DrawEllipse(c, p + new Vector2(-h * 0.02f, 0), new Vector2(h * 0.02f, h * 0.035f), grain);
                PaintedSkin.DrawEllipse(c, p + new Vector2(h * 0.02f, 0), new Vector2(h * 0.02f, h * 0.035f), grain);
            }
        }
        c.DrawRect(new Rect2(at + new Vector2(-h * 0.12f, -h * 0.38f), new Vector2(h * 0.24f, h * 0.06f)), new Color(0.55f, 0.3f, 0.12f));
    }

    /// <summary>A rectangle shaded from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    private static void Gradient(CanvasItem c, Rect2 r, Color top, Color bottom) =>
        c.DrawPolygon(new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) },
            new[] { top, top, bottom, bottom });
}
