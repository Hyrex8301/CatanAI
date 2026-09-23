using Godot;

/// <summary>The game log: a scrolling list of lines that keeps the newest in view.</summary>
public sealed class LogPanel
{
    private readonly RichTextLabel _text;

    public LogPanel(VBoxContainer body, Vector2 size)
    {
        _text = new RichTextLabel
        {
            ScrollFollowing = true,
            BbcodeEnabled = true,
            CustomMinimumSize = size,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FitContent = false,
        };
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.AddThemeColorOverride("default_color", Ui.Text);
        body.AddChild(_text);
    }

    public void Add(string line) => _text.AppendText(line + "\n");

    public void AddMuted(string line) => _text.AppendText($"[color=#6b5f4f]{line}[/color]\n");
}
