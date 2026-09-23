using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

public class TopologyTests
{
    private static IEnumerable<int> Row(int[,] table, int row)
    {
        for (int j = 0; j < table.GetLength(1); j++)
            if (table[row, j] >= 0)
                yield return table[row, j];
    }

    // ---- Counts from the brief ----

    [Fact]
    public void Has19Hexes54Vertices72Edges()
    {
        Assert.Equal(19, HexCount);
        Assert.Equal(54, VertexCount);
        Assert.Equal(72, EdgeCount);

        var vertices = new HashSet<int>();
        var edges = new HashSet<int>();
        for (int h = 0; h < HexCount; h++)
            for (int i = 0; i < 6; i++)
            {
                vertices.Add(HexVertices[h, i]);
                edges.Add(HexEdges[h, i]);
            }
        Assert.Equal(Enumerable.Range(0, 54), vertices.Order());
        Assert.Equal(Enumerable.Range(0, 72), edges.Order());
    }

    [Fact]
    public void HexRowsAre3_4_5_4_3InRowMajorOrder()
    {
        var rowSizes = Enumerable.Range(0, HexCount).GroupBy(h => HexR[h]).OrderBy(g => g.Key).Select(g => g.Count());
        Assert.Equal(new[] { 3, 4, 5, 4, 3 }, rowSizes);

        for (int h = 1; h < HexCount; h++)
            Assert.True(HexR[h] > HexR[h - 1] || (HexR[h] == HexR[h - 1] && HexQ[h] > HexQ[h - 1]));

        Assert.Equal((0, -2), (HexQ[0], HexR[0]));
        Assert.Equal((0, 0), (HexQ[9], HexR[9]));
        Assert.Equal((0, 2), (HexQ[18], HexR[18]));
    }

    [Fact]
    public void VerticesTouching3_2_1LandHexesAre24_12_18()
    {
        var byCount = Enumerable.Range(0, VertexCount).GroupBy(v => Row(VertexHexes, v).Count()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(24, byCount[3]);
        Assert.Equal(12, byCount[2]);
        Assert.Equal(18, byCount[1]);
        Assert.Equal(3, byCount.Count);
    }

    [Fact]
    public void Edges42InteriorAnd30Coastal()
    {
        var landSides = new int[EdgeCount];
        for (int h = 0; h < HexCount; h++)
            for (int s = 0; s < 6; s++)
                landSides[HexEdges[h, s]]++;
        Assert.Equal(42, landSides.Count(n => n == 2));
        Assert.Equal(30, landSides.Count(n => n == 1));
    }

    [Fact]
    public void Vertices36With3EdgesAnd18With2()
    {
        var byCount = Enumerable.Range(0, VertexCount).GroupBy(v => Row(VertexEdges, v).Count()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(36, byCount[3]);
        Assert.Equal(18, byCount[2]);
        Assert.Equal(2, byCount.Count);
    }

    // ---- Table consistency ----

    [Fact]
    public void HexNeighborsAreSymmetricAndLandOnly()
    {
        for (int h = 0; h < HexCount; h++)
            for (int s = 0; s < 6; s++)
            {
                int n = HexNeighbors[h, s];
                if (n < 0) continue;
                Assert.Equal(h, HexNeighbors[n, (s + 3) % 6]);
                Assert.Equal(HexEdges[h, s], HexEdges[n, (s + 3) % 6]);
            }
        // The center hex has 6 land neighbors; a corner hex of the board has 3.
        Assert.Equal(6, Row(HexNeighbors, HexAt(0, 0)).Count());
        Assert.Equal(3, Row(HexNeighbors, HexAt(0, -2)).Count());
    }

    [Fact]
    public void HexSidesJoinConsecutiveCorners()
    {
        for (int h = 0; h < HexCount; h++)
            for (int s = 0; s < 6; s++)
            {
                int e = HexEdges[h, s];
                var ends = new[] { HexVertices[h, s], HexVertices[h, (s + 1) % 6] }.Order();
                Assert.Equal(ends, new[] { EdgeVertices[e, 0], EdgeVertices[e, 1] });
            }
    }

    [Fact]
    public void VertexHexesMatchHexVertices()
    {
        for (int v = 0; v < VertexCount; v++)
            foreach (int h in Row(VertexHexes, v))
                Assert.Contains(v, Row(HexVertices, h));
        for (int h = 0; h < HexCount; h++)
            foreach (int v in Row(HexVertices, h))
                Assert.Contains(h, Row(VertexHexes, v));
    }

    [Fact]
    public void BothCornersOfEveryEdgeAreNeighbors()
    {
        for (int e = 0; e < EdgeCount; e++)
        {
            int a = EdgeVertices[e, 0], b = EdgeVertices[e, 1];
            Assert.True(a < b);
            Assert.Contains(b, Row(VertexNeighbors, a));
            Assert.Contains(a, Row(VertexNeighbors, b));
            Assert.Contains(e, Row(VertexEdges, a));
            Assert.Contains(e, Row(VertexEdges, b));
            Assert.Equal(e, EdgeBetween(a, b));
            Assert.Equal(e, EdgeBetween(b, a));
        }
    }

    [Fact]
    public void VertexNeighborsLineUpWithVertexEdges()
    {
        for (int v = 0; v < VertexCount; v++)
            for (int i = 0; i < 3; i++)
            {
                int e = VertexEdges[v, i], n = VertexNeighbors[v, i];
                Assert.Equal(e < 0, n < 0);
                if (e < 0) continue;
                Assert.Equal(new[] { v, n }.Order(), new[] { EdgeVertices[e, 0], EdgeVertices[e, 1] });
            }
    }

    [Fact]
    public void EdgeNeighborsShareTheSlotsVertexAndAreSymmetric()
    {
        for (int e = 0; e < EdgeCount; e++)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                int f = EdgeNeighbors[e, slot];
                if (f < 0) continue;
                int shared = EdgeVertices[e, slot / 2];
                Assert.True(EdgeVertices[f, 0] == shared || EdgeVertices[f, 1] == shared);
                Assert.Contains(e, Row(EdgeNeighbors, f));
            }
            // Each end contributes its vertex's other edges: 1 or 2.
            for (int end = 0; end < 2; end++)
            {
                int expected = Row(VertexEdges, EdgeVertices[e, end]).Count() - 1;
                int actual = (EdgeNeighbors[e, end * 2] >= 0 ? 1 : 0) + (EdgeNeighbors[e, end * 2 + 1] >= 0 ? 1 : 0);
                Assert.Equal(expected, actual);
            }
        }
    }

    // ---- Ids never change ----

    [Fact]
    public void FirstIdsFollowTheBriefsWalk()
    {
        // Hex 0 (0,-2) creates vertices 0-5 in corner order; hex 1 (1,-2) reuses hex 0's SE (2) and NE (1).
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Row(HexVertices, 0));
        Assert.Equal(new[] { 6, 7, 8, 9, 2, 1 }, Row(HexVertices, 1));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Row(HexEdges, 0));
        Assert.Equal((0, 1), (EdgeVertices[0, 0], EdgeVertices[0, 1]));
        Assert.Equal((0, 5), (EdgeVertices[5, 0], EdgeVertices[5, 1]));
    }

    [Fact]
    public void IdTablesMatchGoldenChecksum()
    {
        // Saved games depend on these ids. If this fails, ids changed: that breaks every save.
        ulong hash = 14695981039346656037UL;
        void Mix(int x) { hash ^= (uint)x; hash *= 1099511628211UL; }
        foreach (int x in HexVertices) Mix(x);
        foreach (int x in HexEdges) Mix(x);
        foreach (int x in EdgeVertices) Mix(x);
        for (int v = 0; v < VertexCount; v++) { Mix(VertexQ[v]); Mix(VertexR[v]); Mix(VertexIsNorth[v] ? 1 : 0); }
        Assert.Equal(GoldenChecksum, hash);
    }

    private const ulong GoldenChecksum = 9788613723331110787UL;

    // ---- Harbors ----

    [Fact]
    public void HarborCornersMatchTheBrief()
    {
        (int, int, bool)[][] expected =
        {
            new[] { (3, -1, false), (2, 1, true) },
            new[] { (1, 2, true), (1, 1, false) },
            new[] { (-1, 3, true), (-1, 2, false) },
            new[] { (-2, 2, false), (-3, 3, true) },
            new[] { (-2, 0, false), (-3, 2, true) },
            new[] { (-1, -2, false), (-2, 0, true) },
            new[] { (0, -2, true), (0, -3, false) },
            new[] { (1, -2, true), (2, -3, false) },
            new[] { (2, -1, true), (3, -2, false) },
        };
        for (int spot = 0; spot < HarborCount; spot++)
        {
            var want = expected[spot].Select(k => VertexAtKey(k.Item1, k.Item2, k.Item3)).Order();
            Assert.DoesNotContain(-1, want);
            Assert.Equal(want, new[] { HarborVertices[spot, 0], HarborVertices[spot, 1] }.Order());
        }
    }

    [Fact]
    public void NineHarborsCover18DistinctCoastalVertices()
    {
        var vertices = new HashSet<int>();
        for (int spot = 0; spot < HarborCount; spot++)
        {
            Assert.Equal(-1, HexNeighbors[HarborHex[spot], (int)HarborSide[spot]]); // faces the sea
            vertices.Add(HarborVertices[spot, 0]);
            vertices.Add(HarborVertices[spot, 1]);
        }
        Assert.Equal(18, vertices.Count);
        foreach (int v in vertices)
            Assert.True(Row(VertexHexes, v).Count() < 3, $"harbor vertex {v} is not on the coast");

        Assert.Equal(18, VertexHarbor.Count(s => s >= 0));
        foreach (int v in vertices)
        {
            int spot = VertexHarbor[v];
            Assert.True(HarborVertices[spot, 0] == v || HarborVertices[spot, 1] == v);
        }
    }

    // ---- Coordinate helpers and geometry ----

    [Fact]
    public void CoordinateHelpersAgreeAcrossHexes()
    {
        // The same corner named from each hex that touches it.
        Assert.Equal(Vertex(0, 0, Corner.N), Vertex(0, -1, Corner.SE));
        Assert.Equal(Vertex(0, 0, Corner.N), Vertex(1, -1, Corner.SW));
        // A side named from a sea hex equals the land hex's side.
        Assert.Equal(Edge(2, -1, Side.E), Edge(3, -1, Side.W));
        // Side NE joins corners N and NE.
        int e = Edge(0, 0, Side.NE);
        Assert.Equal(new[] { Vertex(0, 0, Corner.N), Vertex(0, 0, Corner.NE) }.Order(), new[] { EdgeVertices[e, 0], EdgeVertices[e, 1] });
    }

    [Fact]
    public void HelpersRejectSpotsOffTheBoard()
    {
        Assert.Equal(-1, HexAt(3, 0));
        Assert.Equal(-1, VertexAtKey(3, 0, true));
        Assert.Throws<ArgumentException>(() => Vertex(4, -2, Corner.S));
        Assert.Throws<ArgumentException>(() => Edge(3, 0, Side.E));
    }

    [Fact]
    public void VertexPositionsMatchHexCornerOffsets()
    {
        const double size = 10;
        double h = Math.Sqrt(3) / 2 * size;
        (double dx, double dy)[] offsets = { (0, -size), (h, -size / 2), (h, size / 2), (0, size), (-h, size / 2), (-h, -size / 2) };
        for (int hex = 0; hex < HexCount; hex++)
        {
            var (cx, cy) = HexCenter(hex, size);
            for (int c = 0; c < 6; c++)
            {
                var (vx, vy) = VertexPosition(HexVertices[hex, c], size);
                Assert.Equal(cx + offsets[c].dx, vx, 9);
                Assert.Equal(cy + offsets[c].dy, vy, 9);
            }
        }
    }

    [Fact]
    public void DistinctVerticesHaveDistinctPositions()
    {
        const double size = 10;
        for (int a = 0; a < VertexCount; a++)
            for (int b = a + 1; b < VertexCount; b++)
            {
                var (ax, ay) = VertexPosition(a, size);
                var (bx, by) = VertexPosition(b, size);
                Assert.True(Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by)) > size / 2, $"vertices {a} and {b} overlap");
            }
    }
}
