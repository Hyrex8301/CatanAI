using System;
using Godot;

/// <summary>
/// Screens are laid out at 1600×900 (the design size). The window keeps that scale in both directions (stretch aspect
/// "expand": nothing is ever stretched), so a wider or taller window just has more room: each screen pins its panels to
/// the edges they belong to (layers moved by the extra room) and gives the rest to its board.
/// </summary>
public static class ScreenLayout
{
    public static readonly Vector2 Design = new(1600, 900);

    /// <summary>Room beyond the design size (never negative).</summary>
    public static Vector2 Extra(Node node)
    {
        var size = node.GetViewport().GetVisibleRect().Size;
        return new Vector2(Math.Max(0, size.X - Design.X), Math.Max(0, size.Y - Design.Y));
    }

    /// <summary>A layer for panels pinned to one edge: children use design coordinates; the screen moves the layer.</summary>
    public static Control Layer(Node parent)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = Design };
        parent.AddChild(layer);
        return layer;
    }

    /// <summary>Calls <paramref name="relayout"/> with the extra room now and whenever the window changes size, while <paramref name="node"/> lives.</summary>
    public static void Watch(Node node, Action<Vector2> relayout)
    {
        var viewport = node.GetViewport();
        void Changed()
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                relayout(Extra(node));
        }
        viewport.SizeChanged += Changed;
        node.TreeExiting += () => viewport.SizeChanged -= Changed;
        relayout(Extra(node));
    }
}
