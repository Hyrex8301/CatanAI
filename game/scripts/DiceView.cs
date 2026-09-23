using System;
using Catan.UI;
using Godot;

/// <summary>
/// The two dice on the action bar. They always show the latest roll, edged in the roller's color with "Red rolled 9"
/// underneath. When it's your turn to roll they glow and a click rolls. A new roll tumbles briefly before landing.
/// </summary>
public partial class DiceView : Control
{
    private const float Die = 44, Gap = 8;
    private const double TumbleSeconds = 0.55;

    private RollShown? _roll;
    private string _caption = "";
    private Color _edge = Ui.PanelBorder;
    private bool _enabled, _hover;
    private double _tumbleLeft, _time;
    private readonly Random _faces = new();
    private int _faceA = 1, _faceB = 1;

    public event Action? Clicked;

    public DiceView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public void Set(bool enabled, string tooltip)
    {
        _enabled = enabled;
        TooltipText = tooltip;
        MouseDefaultCursorShape = enabled ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
    }

    /// <summary>Shows a roll. <paramref name="animate"/>: it just happened, so tumble first.</summary>
    public void Show(RollShown? roll, string caption, Color edge, bool animate)
    {
        _roll = roll;
        _caption = caption;
        _edge = edge;
        if (animate)
            _tumbleLeft = TumbleSeconds;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_tumbleLeft > 0)
        {
            _tumbleLeft -= delta;
            _faceA = _faces.Next(1, 7);
            _faceB = _faces.Next(1, 7);
            QueueRedraw();
        }
        else if (_enabled)
            QueueRedraw(); // the glow pulses
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_enabled && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Clicked?.Invoke();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        float width = 2 * Die + Gap;
        var origin = new Vector2((Size.X - width) / 2, 30 - (_hover && _enabled ? 3 : 0));
        if (_enabled)
        {
            float pulse = 0.45f + 0.35f * Mathf.Sin((float)_time * 5);
            var glow = new Rect2(origin - new Vector2(7, 7), new Vector2(width + 14, Die + 14));
            FlatIcons.Rounded(this, glow, new Color(Ui.Gold, pulse), 14);
        }

        bool tumbling = _tumbleLeft > 0;
        int a = tumbling ? _faceA : _roll?.D1 ?? 0, b = tumbling ? _faceB : _roll?.D2 ?? 0;
        var edge = _enabled ? Ui.Text : _edge;
        DrawDie(new Rect2(origin, new Vector2(Die, Die)), a, edge, tumbling ? -0.2f : 0);
        DrawDie(new Rect2(origin + new Vector2(Die + Gap, 0), new Vector2(Die, Die)), b, edge, tumbling ? 0.25f : 0);

        string text = _enabled ? "Click to roll" : tumbling ? "" : _caption;
        Ui.DrawCentered(this, new Vector2(Size.X / 2, 12), _enabled ? "Your roll" : "Dice", 12, Ui.MutedText, Size.X);
        Ui.DrawCentered(this, new Vector2(Size.X / 2, origin.Y + Die + 18), text, 13, Ui.Text, Size.X + 20);
    }

    /// <summary>A white die with its pips (a blank face before the first roll), tilted a little while tumbling.</summary>
    private void DrawDie(Rect2 rect, int face, Color edge, float tilt)
    {
        var center = rect.GetCenter();
        DrawSetTransform(center, tilt);
        var local = new Rect2(-rect.Size / 2, rect.Size);
        var style = new StyleBoxFlat { BgColor = Colors.White, BorderColor = edge, AntiAliasing = true, ShadowColor = new Color(0, 0, 0, 0.25f), ShadowSize = 3 };
        style.SetCornerRadiusAll(9);
        style.SetBorderWidthAll(3);
        DrawStyleBox(style, local);
        float o = rect.Size.X * 0.26f, r = rect.Size.X * 0.085f;
        var pip = new Color(0.12f, 0.12f, 0.15f);
        foreach (var (x, y) in Pips(face))
            DrawCircle(new Vector2(x * o, y * o), r, face == 1 ? new Color(0.8f, 0.1f, 0.1f) : pip);
        DrawSetTransform(Vector2.Zero, 0);
    }

    private static (int, int)[] Pips(int face) => face switch
    {
        1 => new[] { (0, 0) },
        2 => new[] { (-1, -1), (1, 1) },
        3 => new[] { (-1, -1), (0, 0), (1, 1) },
        4 => new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) },
        5 => new[] { (-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1) },
        6 => new[] { (-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1) },
        _ => Array.Empty<(int, int)>(),
    };
}
