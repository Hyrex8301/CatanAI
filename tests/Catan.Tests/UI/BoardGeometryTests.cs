using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class BoardGeometryTests
{
    // The game screen's board area.
    private static readonly BoardGeometry G = new(314, 12, 912, 694);

    [Fact]
    public void BoardFitsInsideItsRectangle()
    {
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            var (x, y) = G.Vertex(v);
            Assert.InRange(x, 314 + G.Size * 0.5, 314 + 912 - G.Size * 0.5);
            Assert.InRange(y, 12 + G.Size * 0.3, 12 + 694 - G.Size * 0.3);
        }
    }

    [Fact]
    public void EveryVertexEdgeAndHexIsHitAtItsOwnSpot()
    {
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            var (x, y) = G.Vertex(v);
            Assert.Equal(BoardHit.Vertex(v), G.HitTest(x, y));
        }
        for (int e = 0; e < Topology.EdgeCount; e++)
        {
            var (x, y) = G.EdgeMid(e);
            Assert.Equal(BoardHit.Edge(e), G.HitTest(x, y));
        }
        for (int h = 0; h < Topology.HexCount; h++)
        {
            var (x, y) = G.Hex(h);
            Assert.Equal(BoardHit.Hex(h), G.HitTest(x, y));
        }
    }

    [Fact]
    public void NearbyClicksStillLand()
    {
        double s = G.Size;
        var (vx, vy) = G.Vertex(10);
        Assert.Equal(BoardHit.Vertex(10), G.HitTest(vx + 0.2 * s, vy - 0.1 * s));

        var (ex, ey) = G.EdgeMid(20);
        var (ax, ay) = G.Vertex(Topology.EdgeVertices[20, 0]);
        var (bx, by) = G.Vertex(Topology.EdgeVertices[20, 1]);
        double nx = -(by - ay) / s, ny = (bx - ax) / s; // perpendicular, length ~1 hex size
        Assert.Equal(BoardHit.Edge(20), G.HitTest(ex + 0.15 * nx * s, ey + 0.15 * ny * s));

        var (hx, hy) = G.Hex(9);
        Assert.Equal(BoardHit.Hex(9), G.HitTest(hx + 0.4 * s, hy + 0.3 * s));
    }

    [Fact]
    public void VertexWinsOverItsEdges()
    {
        // A click right next to a vertex is also close to its edges; the vertex must win.
        var (x, y) = G.Vertex(Topology.EdgeVertices[5, 0]);
        Assert.Equal(HitKind.Vertex, G.HitTest(x + 1, y + 1).Kind);
    }

    [Fact]
    public void OffTheBoardHitsNothing()
    {
        Assert.Equal(BoardHit.None, G.HitTest(0, 0));
        Assert.Equal(BoardHit.None, G.HitTest(G.CenterX, 12 + 2));
        var (x, y) = G.Hex(0);
        Assert.Equal(BoardHit.None, G.HitTest(x, y - 2.2 * G.Size)); // out in the sea above the top row
    }
}
