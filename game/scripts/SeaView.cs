using System;
using Godot;

/// <summary>
/// The sea behind every screen: a gentle gradient (lighter near the top) with faint wave marks drifting slowly sideways.
/// Fills whatever size the window is. Cheap: a few dozen short arcs, redrawn about 20 times a second.
/// </summary>
public partial class SeaView : Control
{
    private static readonly Color Top = Ui.Sea.Lightened(0.08f), Bottom = Ui.Sea.Darkened(0.12f);
    private static readonly Color Wave = new(1, 1, 1, 0.09f);
    private const float Spacing = 90, DriftPerSecond = 9;

    private double _time, _sinceDraw;

    public SeaView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _sinceDraw += delta;
        if (_sinceDraw >= 0.05)
        {
            _sinceDraw = 0;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var size = Size;
        DrawPolygon(new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) },
            new[] { Top, Top, Bottom, Bottom });

        // Wave marks on a staggered grid, each bobbing a little and the whole field drifting to the right.
        float drift = (float)(_time * DriftPerSecond % Spacing);
        int rows = (int)(size.Y / Spacing) + 2, columns = (int)(size.X / Spacing) + 2;
        for (int row = 0; row < rows; row++)
            for (int column = -1; column < columns; column++)
            {
                float stagger = row % 2 == 0 ? 0 : Spacing / 2;
                // A fixed pseudo-random offset per mark, so the pattern doesn't look like a grid.
                float jx = Hash(row, column) * 30 - 15, jy = Hash(column, row) * 24 - 12;
                var at = new Vector2(column * Spacing + stagger + drift + jx, row * Spacing + jy);
                float bob = (float)Math.Sin(_time * 0.8 + row * 1.7 + column * 0.9) * 2;
                DrawWave(at + new Vector2(0, bob), 11);
            }
    }

    /// <summary>A small "~": two shallow arcs.</summary>
    private void DrawWave(Vector2 at, float width)
    {
        DrawArc(at + new Vector2(-width / 2, 0), width / 2, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 8, Wave, 1.6f, true);
        DrawArc(at + new Vector2(width / 2, -width * 0.18f), width / 2, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 8, Wave, 1.6f, true);
    }

    private static float Hash(int a, int b)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }
}
