using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Catan.AI.Talk;
using Catan.UI;
using Godot;

/// <summary>
/// The table chat, under the game log in the right column. Every line shows the speaker's name in their colour, and under
/// your own lines what the bots understood. Type at the bottom (Enter sends). Shift+click a corner on the board (or click
/// one that isn't a move) while typing to insert its numbers ("6 5 9"). Lines can arrive from the bots' worker threads, so
/// they are queued and shown on the next frame.
/// </summary>
public sealed class ChatPanel
{
    private readonly PanelContainer _chat;
    private readonly RichTextLabel _text;
    private readonly LineEdit _input;
    private readonly IReadOnlyList<SeatColor> _colors;
    private readonly int _viewer;
    private readonly ConcurrentQueue<ChatLine> _incoming = new();

    /// <summary>Raised with the text when you press Enter.</summary>
    public event Action<string>? Sent;

    public ChatPanel(Control parent, Rect2 body, IReadOnlyList<SeatColor> colors, int viewer)
    {
        _colors = colors;
        _viewer = viewer;

        _chat = new PanelContainer { Position = body.Position, Size = body.Size };
        var style = Ui.PanelStyle(Ui.Cream, radius: 6, margin: 8);
        style.ShadowSize = 2;
        _chat.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(_chat);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _chat.AddChild(column);
        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectionEnabled = true,
        };
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.AddThemeFontSizeOverride("italics_font_size", 12);
        _text.AddThemeColorOverride("default_color", Ui.Text);
        column.AddChild(_text);
        _input = new LineEdit { PlaceholderText = "Chat… e.g. wheat nb?", ClearButtonEnabled = true };
        _input.AddThemeFontSizeOverride("font_size", 14);
        _input.TextSubmitted += text =>
        {
            text = text.Trim();
            _input.Clear();
            _input.ReleaseFocus(); // back to the game: Space rolls again
            if (text.Length > 0)
                Sent?.Invoke(text);
        };
        column.AddChild(_input);
    }

    /// <summary>True while the chat box has the keyboard (board clicks then insert spots, and Space doesn't roll).</summary>
    public bool Typing => _input.HasFocus();

    /// <summary>Queues a line (any thread).</summary>
    public void Add(ChatLine line) => _incoming.Enqueue(line);

    /// <summary>Shows queued lines. Call every frame.</summary>
    public void Tick()
    {
        while (_incoming.TryDequeue(out var line))
            _text.AppendText(Format(line));
    }

    /// <summary>Gives the keyboard back to the game (keeps what was typed).</summary>
    public void Leave() => _input.ReleaseFocus();

    /// <summary>Adds text at the cursor (a clicked spot's numbers).</summary>
    public void Insert(string text)
    {
        string before = _input.Text[.._input.CaretColumn], after = _input.Text[_input.CaretColumn..];
        string spaced = (before.Length > 0 && !before.EndsWith(' ') ? " " : "") + text + " ";
        _input.Text = before + spaced + after;
        _input.CaretColumn = before.Length + spaced.Length;
        _input.GrabFocus();
    }

    private string Format(ChatLine line)
    {
        string text = Escape(line.Text);
        // Bots address you by your colour ("blue, wheat nb?"): show it as "you".
        string yours = _colors[_viewer].ToString().ToLowerInvariant();
        if (line.Seat != _viewer && text.StartsWith(yours + ",", StringComparison.OrdinalIgnoreCase))
            text = "you" + text[yours.Length..];
        if (line.Seat < 0) // the table's own lines name everyone by colour: you are "you"
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"\b{yours}\b", m => char.IsUpper(m.Value[0]) ? "You" : "you",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string body = line.Seat < 0
            ? $"[i][color=#{Ui.MutedText.ToHtml(false)}]{text}[/color][/i]"
            : $"[b][color=#{Ui.SeatInk(_colors[line.Seat]).ToHtml(false)}]{(line.Seat == _viewer ? "You" : _colors[line.Seat].ToString())}[/color][/b]: {text}";
        if (line.Note is { } note)
            body += $"\n[i][color=#{Ui.MutedText.ToHtml(false)}]   → {Escape(note)}[/color][/i]";
        return (_text.GetParsedText().Length > 0 ? "\n" : "") + body;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
