using System;
using System.Collections.Generic;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// The game log in the right column, colonist style: each line leads with the player's icon, names are in their color, and
/// cards, dice and pieces are small pictures (<see cref="LogText"/>). A thin divider separates turns. Keeps the newest in view.
/// </summary>
public sealed class LogPanel
{
    private const int MaxLines = 300;

    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _lines;
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly int _viewer;
    private readonly GameText _text;
    private bool _pendingDivider;

    public LogPanel(Control parent, Rect2 rect, IReadOnlyList<SeatColor> colors, int viewer, GameText text)
    {
        _colors = colors;
        _viewer = viewer;
        _text = text;
        var panel = new PanelContainer { Position = rect.Position, Size = rect.Size };
        var style = Ui.PanelStyle(Ui.Cream, radius: 6, margin: 8);
        style.ShadowSize = 2;
        panel.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(panel);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = false };
        panel.AddChild(_scroll);
        _lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _lines.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_lines);
        // Follow the newest line: scroll to the bottom whenever the content grows.
        _scroll.GetVScrollBar().Changed += () => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
        Width = rect.Size.X - 30;
    }

    private float Width { get; }

    public void Add(GameEvent e)
    {
        var line = LogText.Line(e);
        if (line.Divider)
        {
            _pendingDivider = true; // drawn before the next line, so the log never ends on a divider
            return;
        }
        if (_pendingDivider)
        {
            _pendingDivider = false;
            Append(new ColorRect { Color = Ui.PanelBorder, CustomMinimumSize = new Vector2(Width, 1) });
        }
        var view = new LogLineView(line, _colors, _viewer, Width);
        view.TooltipText = line.PlainText(_text.Seat);
        Append(view);
    }

    /// <summary>A plain line (game notes, errors).</summary>
    public void AddNote(string text, Color? color = null)
    {
        var label = Ui.Label(text, 14, color ?? Ui.MutedText);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Width, 0);
        Append(label);
    }

    private void Append(Control line)
    {
        _lines.AddChild(line);
        if (_lines.GetChildCount() > MaxLines)
            _lines.GetChild(0).QueueFree();
    }
}

/// <summary>One log line: a player icon, then words and small pictures, wrapped to the panel width.</summary>
public partial class LogLineView : Control
{
    private const int FontSize = 14;
    private const float LineHeight = 21, IconGap = 3;

    private readonly LogLine _line;
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly int _viewer;
    private readonly List<(Vector2 At, LogPart Part, string Word)> _placed = new();

    public LogLineView() : this(new LogLine(-1, Array.Empty<LogPart>()), Array.Empty<SeatColor>(), 0, 300)
    {
    }

    public LogLineView(LogLine line, IReadOnlyList<SeatColor> colors, int viewer, float width)
    {
        _line = line;
        _colors = colors;
        _viewer = viewer;
        MouseFilter = MouseFilterEnum.Pass;
        Layout(width);
    }

    private static Font Font => ThemeDB.FallbackFont;

    /// <summary>Places every word and icon, wrapping at the width, and sets the line's height.</summary>
    private void Layout(float width)
    {
        float x = 22, y = 0;
        void Place(float w, LogPart part, string word)
        {
            if (x + w > width && x > 22)
            {
                x = 22;
                y += LineHeight;
            }
            _placed.Add((new Vector2(x, y), part, word));
            x += w;
        }

        foreach (var part in _line.Parts)
        {
            switch (part.Kind)
            {
                case LogPartKind.Text:
                    foreach (string word in SplitKeepingSpaces(part.Text))
                        Place(Font.GetStringSize(word, HorizontalAlignment.Left, -1, FontSize).X, part, word);
                    break;
                case LogPartKind.Name:
                    string name = Name(part.Value);
                    Place(Font.GetStringSize(name, HorizontalAlignment.Left, -1, FontSize).X, part, name);
                    break;
                case LogPartKind.Die:
                    Place(18 + IconGap, part, "");
                    break;
                case LogPartKind.Piece or LogPartKind.Robber:
                    Place(20 + IconGap, part, "");
                    break;
                default:
                    Place(14 + IconGap, part, "");
                    break;
            }
        }
        CustomMinimumSize = new Vector2(width, y + LineHeight);
    }

    public override void _Draw()
    {
        if (_line.Seat >= 0 && _line.Seat < _colors.Count)
            Ui.Avatar(this, new Vector2(9, LineHeight / 2), 8, _colors[_line.Seat], _line.Seat == _viewer);
        foreach (var (at, part, word) in _placed)
        {
            var mid = at + new Vector2(0, LineHeight / 2);
            switch (part.Kind)
            {
                case LogPartKind.Text:
                    DrawString(Font, mid + new Vector2(0, FontSize * 0.36f), word, HorizontalAlignment.Left, -1, FontSize, Ui.Text);
                    break;
                case LogPartKind.Name:
                    var ink = part.Value >= 0 && part.Value < _colors.Count ? Ui.SeatInk(_colors[part.Value]) : Ui.Text;
                    DrawString(Font, mid + new Vector2(0, FontSize * 0.36f), word, HorizontalAlignment.Left, -1, FontSize, ink);
                    break;
                case LogPartKind.Card:
                    Icons.Skin.CardFace(this, new Rect2(at + new Vector2(0, 1), new Vector2(14, 19)), false, part.Value, false);
                    break;
                case LogPartKind.CardBack:
                    Icons.Skin.CardBack(this, new Rect2(at + new Vector2(0, 1), new Vector2(14, 19)), false);
                    break;
                case LogPartKind.DevCard:
                    Icons.Skin.CardFace(this, new Rect2(at + new Vector2(0, 1), new Vector2(14, 19)), true, part.Value, false);
                    break;
                case LogPartKind.DevBack:
                    Icons.Skin.CardBack(this, new Rect2(at + new Vector2(0, 1), new Vector2(14, 19)), true);
                    break;
                case LogPartKind.Die:
                    DrawDie(new Rect2(at + new Vector2(0, 2), new Vector2(17, 17)), part.Value);
                    break;
                case LogPartKind.Piece:
                    var color = _line.Seat >= 0 && _line.Seat < _colors.Count ? Ui.SeatColor(_colors[_line.Seat]) : Colors.Gray;
                    var skin = new FlatSkin();
                    var c = mid + new Vector2(10, 1);
                    if (part.Value == (int)PieceType.Road)
                        skin.Road(this, c + new Vector2(-8, 6), c + new Vector2(8, -6), 34, color);
                    else if (part.Value == (int)PieceType.Settlement)
                        skin.Settlement(this, c, 48, color);
                    else
                        skin.City(this, c, 40, color);
                    break;
                case LogPartKind.Robber:
                    new FlatSkin().Robber(this, mid + new Vector2(10, -2), 50);
                    break;
            }
        }
    }

    private string Name(int seat) => seat == _viewer ? "You" : seat >= 0 && seat < _colors.Count ? _colors[seat].ToString() : "?";

    private void DrawDie(Rect2 rect, int face)
    {
        FlatIcons.Rounded(this, rect, Colors.White, 3);
        DrawRect(rect, new Color(0.2f, 0.2f, 0.25f), false, 1.2f);
        var m = rect.GetCenter();
        float o = rect.Size.X * 0.27f;
        (int, int)[] pips = face switch
        {
            1 => new[] { (0, 0) },
            2 => new[] { (1, -1), (-1, 1) },
            3 => new[] { (1, -1), (0, 0), (-1, 1) },
            4 => new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) },
            5 => new[] { (-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1) },
            _ => new[] { (-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1) },
        };
        foreach (var (x, y) in pips)
            DrawCircle(m + new Vector2(x * o, y * o), 1.6f, new Color(0.1f, 0.1f, 0.12f));
    }

    /// <summary>"gave the bank " → "gave ", "the ", "bank " (each word keeps its trailing space, for wrapping).</summary>
    private static IEnumerable<string> SplitKeepingSpaces(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == ' ')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        if (start < text.Length)
            yield return text[start..];
    }
}
