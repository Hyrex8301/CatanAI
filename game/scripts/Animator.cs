using System;
using System.Collections.Generic;
using Catan.UI;
using Godot;

/// <summary>
/// The animation layer over the whole screen. Plays <see cref="Cue"/>s: cards fly along a little arc between hexes, player
/// rows, your hand and the bank; toasts appear at the top of the board and fade. Hex flashes, pops and the robber slide
/// are handed to the <see cref="BoardView"/>. <see cref="Busy"/> tells the game loop to hold the next bot move until the
/// cards have landed.
/// </summary>
public partial class Animator : Control
{
    private const double FlySeconds = 0.6, Stagger = 0.07, ToastSeconds = 3.2, BoardSeconds = 0.9;
    private static readonly Vector2 Card = new(30, 42);

    private readonly List<(int Resource, Vector2 From, Vector2 To, double Start)> _flights = new();
    private readonly List<(string Text, double Start)> _toasts = new();
    private readonly List<(double At, Action Run)> _scheduled = new();
    private bool _wasBusy;

    /// <summary>Raised when the last card has landed (the screen then updates hands and counts).</summary>
    public event Action? Finished;
    private BoardView _board = null!;
    private Func<Spot, Vector2> _where = _ => Vector2.Zero;
    private Vector2 _toastAt;
    private double _busyUntil;
    private bool _drew;

    public Animator() => MouseFilter = MouseFilterEnum.Ignore;

    public void Setup(BoardView board, Func<Spot, Vector2> where, Vector2 toastAt)
    {
        _board = board;
        _where = where;
        _toastAt = toastAt;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>True while cards are still flying or the board is still animating.</summary>
    public bool Busy => Now < _busyUntil;

    public void Play(IEnumerable<Cue> cues)
    {
        double start = Now;
        foreach (var cue in cues)
        {
            switch (cue)
            {
                case FlashNumber f:
                    // Wait for the dice to stop tumbling, light up the hexes, then send the cards.
                    start += DiceView.TumbleSeconds;
                    int number = f.Number;
                    _scheduled.Add((start, () => _board.Flash(number)));
                    Hold(start + BoardSeconds);
                    start += 0.35;
                    break;
                case FlyCard f:
                    _flights.Add((f.Resource, _where(f.From), _where(f.To), start));
                    Hold(start + FlySeconds);
                    start += Stagger;
                    break;
                case PopPiece p:
                    _board.Pop(p.Piece, p.Target);
                    break;
                case SlideRobber:
                    _board.SlideRobber();
                    Hold(Now + 0.5);
                    break;
                case Toast t:
                    _toasts.Add((t.Text, Now));
                    if (_toasts.Count > 4)
                        _toasts.RemoveAt(0);
                    break;
            }
        }
    }

    private void Hold(double until) => _busyUntil = Math.Max(_busyUntil, until);

    public override void _Process(double delta)
    {
        double now = Now;
        foreach (var job in _scheduled.FindAll(j => now >= j.At))
        {
            _scheduled.Remove(job);
            job.Run();
        }
        _flights.RemoveAll(f => now > f.Start + FlySeconds);
        if (_wasBusy && !Busy)
            Finished?.Invoke();
        _wasBusy = Busy;
        _toasts.RemoveAll(t => now > t.Start + ToastSeconds);
        bool active = _flights.Count > 0 || _toasts.Count > 0;
        if (active || _drew)
            QueueRedraw(); // keep drawing while anything moves, plus one frame to clear
        _drew = active;
    }

    public override void _Draw()
    {
        double now = Now;
        foreach (var (resource, from, to, start) in _flights)
        {
            double t = (now - start) / FlySeconds;
            if (t < 0)
                continue;
            float e = (float)(1 - Math.Pow(1 - t, 3)); // ease out
            // A quadratic arc bowing upward.
            var control = (from + to) / 2 - new Vector2(0, 60 + from.DistanceTo(to) * 0.12f);
            var at = from.Lerp(control, e).Lerp(control.Lerp(to, e), e);
            // Grows in as it leaves and shrinks as it lands.
            float grow = (float)Math.Min(1, Math.Min(t / 0.1, (1 - t) / 0.15));
            var card = Card * (0.55f + 0.45f * grow);
            var rect = new Rect2(at - card / 2, card);
            if (resource >= 0)
                Icons.Skin.CardFace(this, rect, false, resource, false);
            else
                Icons.Skin.CardBack(this, rect, false);
        }

        float y = _toastAt.Y;
        foreach (var (text, start) in _toasts)
        {
            double age = now - start;
            float alpha = (float)Math.Clamp(Math.Min(age / 0.2, (ToastSeconds - age) / 0.5), 0, 1);
            var font = ThemeDB.FallbackFont;
            var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 17);
            var pill = new Rect2(new Vector2(_toastAt.X - size.X / 2 - 18, y), new Vector2(size.X + 36, 38));
            FlatIcons.Rounded(this, pill, new Color(0.1f, 0.12f, 0.16f, 0.85f * alpha), 19);
            DrawString(font, pill.Position + new Vector2(18, 25), text, HorizontalAlignment.Left, -1, 17, new Color(1, 1, 1, alpha));
            y += 44;
        }
    }
}
