using System.Collections.Generic;
using Godot;

/// <summary>
/// A small line chart of recent practice scores, oldest on the left, from the nearest 10 below the lowest score up to 100;
/// the last score is a bigger dot, with guide lines at 50 and 90.
/// </summary>
public partial class ScoreChart : Control
{
    private readonly IReadOnlyList<double> _scores;

    public ScoreChart(IReadOnlyList<double> scores)
    {
        _scores = scores;
        CustomMinimumSize = new Vector2(320, 56);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public ScoreChart() : this(new List<double>()) { }

    public override void _Draw()
    {
        var box = new Rect2(Vector2.Zero, Size);
        DrawRect(box, new Color(0, 0, 0, 0.04f));
        // Guide lines at 50 and 90 (a "good" score).
        foreach (double level in new[] { 50.0, 90.0 })
            if (level > Floor)
                DrawLine(At(0, level) with { X = 0 }, At(0, level) with { X = Size.X }, new Color(0, 0, 0, 0.12f), 1);
        if (_scores.Count < 2)
            return;
        var points = new Vector2[_scores.Count];
        for (int i = 0; i < _scores.Count; i++)
            points[i] = At(i, _scores[i]);
        DrawPolyline(points, Ui.ButtonInk, 2, true);
        for (int i = 0; i < points.Length; i++)
            DrawCircle(points[i], i == points.Length - 1 ? 4.5f : 2.5f, Tone(_scores[i]));
    }

    /// <summary>The bottom of the chart: the nearest 10 below the lowest score, so small changes show.</summary>
    private double Floor => _scores.Count == 0 ? 0 : System.Math.Clamp(System.Math.Floor(System.Linq.Enumerable.Min(_scores) / 10 - 0.001) * 10, 0, 90);

    private Vector2 At(int index, double score)
    {
        const float pad = 6;
        float x = pad + (Size.X - 2 * pad) * index / System.Math.Max(1, _scores.Count - 1);
        float y = pad + (Size.Y - 2 * pad) * (1 - (float)((score - Floor) / (100 - Floor)));
        return new Vector2(x, y);
    }

    private static Color Tone(double score) => score >= 90 ? Ui.Good : score >= 60 ? Ui.ButtonInk : Ui.Bad;
}
