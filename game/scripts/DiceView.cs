using System;
using Catan.UI;
using Godot;

/// <summary>
/// The two dice, colonist style. The game screen places them beside the row of the player who rolled, showing that roll.
/// When it's your turn to roll they glow beside your panel and a click rolls. A new roll tumbles briefly before landing.
/// </summary>
public partial class DiceView : Control
{
    public const float Die = 56, Gap = 10;
    public const double TumbleSeconds = 0.35;

    private RollShown? _roll;
    private bool _enabled, _hover;
    private double _tumbleLeft, _time;
    private readonly Random _faces = new();
    private int _faceA = 1, _faceB = 1;

    public event Action? Clicked;

    public DiceView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Size = new Vector2(2 * Die + Gap + 16, Die + 16);
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

    /// <summary>Shows a roll (null: blank dice before the first roll). <paramref name="animate"/>: it just happened, so tumble first.</summary>
    public void Show(RollShown? roll, string caption, bool animate)
    {
        _roll = roll;
        if (!_enabled)
            TooltipText = caption;
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
        var origin = new Vector2(8, 8 - (_hover && _enabled ? 3 : 0));
        if (_enabled)
        {
            float pulse = 0.45f + 0.35f * Mathf.Sin((float)_time * 5);
            FlatIcons.Rounded(this, new Rect2(origin - new Vector2(7, 7), new Vector2(2 * Die + Gap + 14, Die + 14)), new Color(Ui.Gold, pulse), 16);
        }
        bool tumbling = _tumbleLeft > 0;
        int a = tumbling ? _faceA : _roll?.D1 ?? 0, b = tumbling ? _faceB : _roll?.D2 ?? 0;
        DrawDie(new Rect2(origin, new Vector2(Die, Die)), a, tumbling ? -0.2f : 0);
        DrawDie(new Rect2(origin + new Vector2(Die + Gap, 0), new Vector2(Die, Die)), b, tumbling ? 0.25f : 0);
    }

    /// <summary>A light grey die with black pips (blank before the first roll), tilted a little while tumbling.</summary>
    private void DrawDie(Rect2 rect, int face, float tilt)
    {
        DrawSetTransform(rect.GetCenter(), tilt);
        var local = new Rect2(-rect.Size / 2, rect.Size);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.9f, 0.91f, 0.92f), BorderColor = new Color(0.22f, 0.24f, 0.28f), AntiAliasing = true,
            ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 4, ShadowOffset = new Vector2(0, 2),
        };
        style.SetCornerRadiusAll(10);
        style.SetBorderWidthAll(3);
        DrawStyleBox(style, local);
        float o = rect.Size.X * 0.26f, r = rect.Size.X * 0.085f;
        foreach (var (x, y) in Pips(face))
            DrawCircle(new Vector2(x * o, y * o), r, new Color(0.1f, 0.1f, 0.12f));
        DrawSetTransform(Vector2.Zero, 0);
    }

    private static (int, int)[] Pips(int face) => face switch
    {
        1 => new[] { (0, 0) },
        2 => new[] { (1, -1), (-1, 1) },
        3 => new[] { (1, -1), (0, 0), (-1, 1) },
        4 => new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) },
        5 => new[] { (-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1) },
        6 => new[] { (-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1) },
        _ => Array.Empty<(int, int)>(),
    };
}
