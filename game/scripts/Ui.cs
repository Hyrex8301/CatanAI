using Catan.UI;
using Godot;

/// <summary>Shared look for the flat M2 style: colors and small widget helpers. The sprite art pass replaces this later.</summary>
public static class Ui
{
    public static readonly Color Sea = new(0.22f, 0.47f, 0.66f);
    public static readonly Color PanelFill = new(0.96f, 0.93f, 0.86f, 0.96f);
    public static readonly Color PanelBorder = new(0.35f, 0.28f, 0.2f);
    public static readonly Color Text = new(0.12f, 0.1f, 0.08f);
    public static readonly Color MutedText = new(0.4f, 0.36f, 0.3f);

    public static Color SeatColor(SeatColor color) => color switch
    {
        Catan.UI.SeatColor.Red => new Color(0.85f, 0.15f, 0.15f),
        Catan.UI.SeatColor.Blue => new Color(0.15f, 0.35f, 0.85f),
        Catan.UI.SeatColor.Orange => new Color(0.95f, 0.55f, 0.1f),
        _ => new Color(0.97f, 0.97f, 0.97f),
    };

    /// <summary>A rounded, bordered panel at a fixed spot on the 1600×900 layout, with a title.</summary>
    public static PanelContainer Panel(string title, Rect2 rect, out VBoxContainer body)
    {
        var panel = new PanelContainer { Position = rect.Position, Size = rect.Size };
        var style = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = PanelBorder,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", style);

        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        panel.AddChild(body);
        body.AddChild(Label(title, 18));
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
}
