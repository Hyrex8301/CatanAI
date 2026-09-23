using Catan.UI;
using Godot;

/// <summary>Shared look for the flat, colonist-style HUD: colors and small widget helpers. The art pass replaces this later.</summary>
public static class Ui
{
    public static readonly Color Sea = new(0.30f, 0.60f, 0.84f);
    public static readonly Color PanelFill = new(1f, 1f, 1f, 0.97f);
    public static readonly Color PanelBorder = new(0.80f, 0.84f, 0.89f);
    public static readonly Color Text = new(0.15f, 0.17f, 0.21f);
    public static readonly Color MutedText = new(0.45f, 0.49f, 0.55f);
    public static readonly Color Gold = new(0.98f, 0.78f, 0.18f);
    public static readonly Color Highlight = new(1f, 0.96f, 0.82f);

    public static Color SeatColor(SeatColor color) => color switch
    {
        Catan.UI.SeatColor.Red => new Color(0.86f, 0.20f, 0.18f),
        Catan.UI.SeatColor.Blue => new Color(0.16f, 0.38f, 0.86f),
        Catan.UI.SeatColor.Orange => new Color(0.96f, 0.56f, 0.10f),
        _ => new Color(0.97f, 0.97f, 0.97f),
    };

    /// <summary>Text drawn on top of a seat color (dark on white, white on the others).</summary>
    public static Color OnSeatColor(SeatColor color) => color == Catan.UI.SeatColor.White ? Text : Colors.White;

    public static StyleBoxFlat PanelStyle(Color? fill = null, int radius = 12, int margin = 10)
    {
        var style = new StyleBoxFlat
        {
            BgColor = fill ?? PanelFill,
            ShadowColor = new Color(0, 0, 0, 0.22f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(0, 2),
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.8f, ContentMarginBottom = margin * 0.8f,
            AntiAliasing = true,
        };
        style.SetCornerRadiusAll(radius);
        return style;
    }

    /// <summary>A rounded white panel at a fixed spot on the 1600×900 layout, with an optional title.</summary>
    public static PanelContainer Panel(string? title, Rect2 rect, out VBoxContainer body)
    {
        var panel = new PanelContainer { Position = rect.Position, Size = rect.Size };
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        panel.AddChild(body);
        if (title is not null)
            body.AddChild(Label(title, 16, MutedText));
        return panel;
    }

    public static Label Label(string text, int size = 15, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
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
}
