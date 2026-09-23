using Catan.Core;

namespace Catan.UI;

public enum HitKind : byte { None, Vertex, Edge, Hex }

/// <summary>What a point on the board lands on: a vertex, an edge, a hex, or nothing.</summary>
public readonly record struct BoardHit(HitKind Kind, int Id)
{
    public static readonly BoardHit None = new(HitKind.None, -1);

    public static BoardHit Vertex(int v) => new(HitKind.Vertex, v);
    public static BoardHit Edge(int e) => new(HitKind.Edge, e);
    public static BoardHit Hex(int h) => new(HitKind.Hex, h);
}

/// <summary>
/// Pixel layout of the board inside a rectangle, and hit-testing from a pixel to what's under it. No Godot types, so it
/// is unit-tested. Vertices win over edges, and edges over hexes, since vertices and edges are the smaller targets.
/// </summary>
public sealed class BoardGeometry
{
    /// <summary>Clicks within this many hex sizes of a vertex hit it.</summary>
    public const double VertexRadius = 0.3;

    /// <summary>Clicks within this many hex sizes of an edge (and not on a vertex) hit it.</summary>
    public const double EdgeRadius = 0.2;

    /// <summary>Clicks within this many hex sizes of a hex center (and not on an edge or vertex) hit it.</summary>
    public const double HexRadius = 0.8;

    private readonly (double X, double Y)[] _vertices = new (double, double)[Topology.VertexCount];
    private readonly (double X, double Y)[] _hexes = new (double, double)[Topology.HexCount];

    /// <summary>Fits the board, with room around it for harbor labels, centered in the given rectangle.</summary>
    public BoardGeometry(double left, double top, double width, double height)
    {
        Size = Math.Min(width / 10.0, height / 9.4);
        CenterX = left + width / 2;
        CenterY = top + height / 2;
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            var (x, y) = Topology.VertexPosition(v, Size);
            _vertices[v] = (CenterX + x, CenterY + y);
        }
        for (int h = 0; h < Topology.HexCount; h++)
        {
            var (x, y) = Topology.HexCenter(h, Size);
            _hexes[h] = (CenterX + x, CenterY + y);
        }
    }

    /// <summary>Hex size in pixels (center to corner).</summary>
    public double Size { get; }

    public double CenterX { get; }
    public double CenterY { get; }

    public (double X, double Y) Vertex(int v) => _vertices[v];

    public (double X, double Y) Hex(int h) => _hexes[h];

    public (double X, double Y) EdgeMid(int e)
    {
        var (ax, ay) = _vertices[Topology.EdgeVertices[e, 0]];
        var (bx, by) = _vertices[Topology.EdgeVertices[e, 1]];
        return ((ax + bx) / 2, (ay + by) / 2);
    }

    public BoardHit HitTest(double x, double y)
    {
        int bestVertex = -1;
        double bestVertexDistance = double.MaxValue;
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            double d = Distance(x, y, _vertices[v]);
            if (d < bestVertexDistance)
                (bestVertex, bestVertexDistance) = (v, d);
        }
        if (bestVertexDistance <= VertexRadius * Size)
            return BoardHit.Vertex(bestVertex);

        int bestEdge = -1;
        double bestEdgeDistance = double.MaxValue;
        for (int e = 0; e < Topology.EdgeCount; e++)
        {
            double d = SegmentDistance(x, y, _vertices[Topology.EdgeVertices[e, 0]], _vertices[Topology.EdgeVertices[e, 1]]);
            if (d < bestEdgeDistance)
                (bestEdge, bestEdgeDistance) = (e, d);
        }
        if (bestEdgeDistance <= EdgeRadius * Size)
            return BoardHit.Edge(bestEdge);

        for (int h = 0; h < Topology.HexCount; h++)
            if (Distance(x, y, _hexes[h]) <= HexRadius * Size)
                return BoardHit.Hex(h);

        return BoardHit.None;
    }

    private static double Distance(double x, double y, (double X, double Y) p) =>
        Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));

    private static double SegmentDistance(double x, double y, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(x, y, (a.X + t * dx, a.Y + t * dy));
    }
}
