using Catan.UI;
using Godot;

/// <summary>Shared look for the colonist-style HUD: colors and small drawing helpers. The art pass replaces this later.</summary>
public static class Ui
{
    public static readonly Color Sea = new(0.08f, 0.38f, 0.64f);
    public static readonly Color PanelFill = new(0.97f, 0.95f, 0.91f);
    public static readonly Color Cream = new(0.96f, 0.94f, 0.89f);
    public static readonly Color RowGray = new(0.76f, 0.75f, 0.73f);
    public static readonly Color PanelBorder = new(0.80f, 0.78f, 0.74f);
    public static readonly Color Text = new(0.15f, 0.15f, 0.17f);
    public static readonly Color MutedText = new(0.45f, 0.45f, 0.48f);
    public static readonly Color Gold = new(0.98f, 0.78f, 0.18f);
    public static readonly Color Highlight = new(1f, 0.96f, 0.82f);
    public static readonly Color ButtonBlue = new(0.64f, 0.82f, 0.93f);
    public static readonly Color ButtonInk = new(0.33f, 0.42f, 0.50f);
    public static readonly Color BadgeBlue = new(0.12f, 0.33f, 0.70f);
    public static readonly Color Good = new(0.22f, 0.66f, 0.30f);
    public static readonly Color Bad = new(0.86f, 0.22f, 0.20f);

    public static Color SeatColor(SeatColor color) => color switch
    {
        Catan.UI.SeatColor.Red => new Color(0.86f, 0.20f, 0.18f),
        Catan.UI.SeatColor.Blue => new Color(0.16f, 0.38f, 0.86f),
        Catan.UI.SeatColor.Orange => new Color(0.96f, 0.56f, 0.10f),
        _ => new Color(0.97f, 0.97f, 0.97f),
    };

    /// <summary>Text drawn on top of a seat color (dark on white, white on the others).</summary>
    public static Color OnSeatColor(SeatColor color) => color == Catan.UI.SeatColor.White ? Text : Colors.White;

    /// <summary>Seat color readable as text on a light background (white becomes grey).</summary>
    public static Color SeatInk(SeatColor color) => color == Catan.UI.SeatColor.White ? new Color(0.45f, 0.45f, 0.5f) : SeatColor(color).Darkened(0.1f);

    public static StyleBoxFlat PanelStyle(Color? fill = null, int radius = 8, int margin = 10)
    {
        var style = new StyleBoxFlat
        {
            BgColor = fill ?? PanelFill,
            ShadowColor = new Color(0, 0, 0, 0.25f),
            ShadowSize = 4,
            ShadowOffset = new Vector2(0, 2),
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.8f, ContentMarginBottom = margin * 0.8f,
            AntiAliasing = true,
        };
        style.SetCornerRadiusAll(radius);
        return style;
    }

    /// <summary>A rounded panel at a fixed spot on the 1600×900 layout, with an optional title.</summary>
    public static PanelContainer Panel(string? title, Rect2 rect, out VBoxContainer body)
    {
        var panel = new PanelContainer { Position = rect.Position, Size = rect.Size };
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        panel.AddChild(body);
        if (title is not null)
            body.AddChild(Label(title, 15, MutedText));
        return panel;
    }

    public static Label Label(string text, int size = 15, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    /// <summary>A settings line: a label on the left, its control on the right.</summary>
    public static HBoxContainer SettingRow(string label, Control control)
    {
        var row = new HBoxContainer();
        var text = Label(label, 16);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(text);
        row.AddChild(control);
        return row;
    }

    public static Button Button(string text, int size = 20)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(280, 52) };
        button.AddThemeFontSizeOverride("font_size", size);
        return button;
    }

    /// <summary>Draws text centered on a point (for custom-drawn controls).</summary>
    public static void DrawCentered(CanvasItem c, Vector2 center, string text, int size, Color color, float width = 200)
    {
        var font = ThemeDB.FallbackFont;
        c.DrawString(font, center + new Vector2(-width / 2, size * 0.36f), text, HorizontalAlignment.Center, width, size, color);
    }

    /// <summary>Draws text starting at a point, vertically centered on it.</summary>
    public static void DrawLeft(CanvasItem c, Vector2 at, string text, int size, Color color, float width = -1)
    {
        var font = ThemeDB.FallbackFont;
        c.DrawString(font, at + new Vector2(0, size * 0.36f), text, HorizontalAlignment.Left, width, size, color);
    }

    /// <summary>The blue count badge colonist puts on a card's top-right corner.</summary>
    public static void Badge(CanvasItem c, Vector2 topRight, int count, float scale = 1)
    {
        string text = count.ToString();
        float h = 18 * scale, w = Mathf.Max(h, (8 + 8 * text.Length) * scale);
        var rect = new Rect2(topRight + new Vector2(-w + 4 * scale, -4 * scale), new Vector2(w, h));
        FlatIcons.Rounded(c, rect, BadgeBlue, 4 * scale);
        DrawCentered(c, rect.GetCenter(), text, (int)(13 * scale), Colors.White, w + 10);
    }

    /// <summary>A player's round avatar: their color ring around a robot (bots) or a person (you).</summary>
    public static void Avatar(CanvasItem c, Vector2 at, float r, SeatColor color, bool you)
    {
        var seat = SeatColor(color);
        c.DrawCircle(at, r, color == Catan.UI.SeatColor.White ? new Color(0.8f, 0.8f, 0.82f) : seat);
        c.DrawCircle(at, r * 0.8f, new Color(0.97f, 0.97f, 0.97f));
        var ink = new Color(0.25f, 0.27f, 0.32f);
        if (you)
        {
            c.DrawCircle(at - new Vector2(0, r * 0.2f), r * 0.26f, ink);
            c.DrawColoredPolygon(new[]
            {
                at + new Vector2(-r * 0.48f, r * 0.55f), at + new Vector2(-r * 0.38f, r * 0.2f), at + new Vector2(0, r * 0.08f),
                at + new Vector2(r * 0.38f, r * 0.2f), at + new Vector2(r * 0.48f, r * 0.55f),
            }, ink);
        }
        else
        {
            // A little robot head with an antenna.
            var head = new Rect2(at - new Vector2(r * 0.4f, r * 0.26f), new Vector2(r * 0.8f, r * 0.58f));
            FlatIcons.Rounded(c, head, ink, r * 0.12f);
            c.DrawCircle(at + new Vector2(-r * 0.17f, r * 0.02f), r * 0.1f, seat == Colors.White ? Colors.White : seat.Lightened(0.4f));
            c.DrawCircle(at + new Vector2(r * 0.17f, r * 0.02f), r * 0.1f, seat == Colors.White ? Colors.White : seat.Lightened(0.4f));
            c.DrawLine(at - new Vector2(0, r * 0.26f), at - new Vector2(0, r * 0.45f), ink, Mathf.Max(1.5f, r * 0.07f));
            c.DrawCircle(at - new Vector2(0, r * 0.48f), r * 0.07f, ink);
        }
    }
}
