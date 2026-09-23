namespace Catan.Core;

/// <summary>Corners of a pointy-top hex, clockwise from the top.</summary>
public enum Corner : byte { N, NE, SE, S, SW, NW }

/// <summary>Sides of a pointy-top hex, clockwise from upper right. Side i joins corner i and corner (i + 1) % 6.</summary>
public enum Side : byte { NE, E, SE, SW, W, NW }

/// <summary>
/// Fixed board geometry shared by every game: axial (q, r) hexes, exact integer corner keys (q, r, N|S),
/// and the id tables derived from them. Ids are assigned in a fixed walk and never change, so saved games rely on them.
/// Padded table slots hold -1.
/// </summary>
public static class Topology
{
    public const int HexCount = 19;
    public const int VertexCount = 54;
    public const int EdgeCount = 72;
    public const int HarborCount = 9;

    // Neighbor offsets indexed by Side: NE, E, SE, SW, W, NW.
    private static readonly int[] SideDq = { +1, +1, 0, -1, -1, 0 };
    private static readonly int[] SideDr = { -1, 0, +1, +1, 0, -1 };

    // Corner key offsets indexed by Corner: N (q,r,N), NE (q+1,r-1,S), SE (q,r+1,N), S (q,r,S), SW (q-1,r+1,N), NW (q,r-1,S).
    private static readonly int[] CornerDq = { 0, +1, 0, 0, -1, 0 };
    private static readonly int[] CornerDr = { 0, -1, +1, 0, +1, -1 };
    private static readonly bool[] CornerNorth = { true, false, true, false, true, false };

    // Harbor spots from the brief, in spot order 0-8 (the brief's rows 1-9).
    private static readonly (int Q, int R, Side Side)[] HarborSpots =
    {
        (2, 0, Side.E), (1, 1, Side.SE), (-1, 2, Side.SE), (-2, 2, Side.SW), (-2, 1, Side.W),
        (-1, -1, Side.W), (0, -2, Side.NW), (1, -2, Side.NE), (2, -1, Side.NE),
    };

    // Hexes (land only), numbered row by row: r from -2 to 2, then q ascending.
    public static readonly int[] HexQ = new int[HexCount];
    public static readonly int[] HexR = new int[HexCount];
    public static readonly int[,] HexVertices = new int[HexCount, 6];   // [hex, Corner]
    public static readonly int[,] HexEdges = new int[HexCount, 6];      // [hex, Side]
    public static readonly int[,] HexNeighbors = new int[HexCount, 6];  // [hex, Side], -1 for sea

    // Vertices: key (VertexQ, VertexR, VertexIsNorth).
    public static readonly int[] VertexQ = new int[VertexCount];
    public static readonly int[] VertexR = new int[VertexCount];
    public static readonly bool[] VertexIsNorth = new bool[VertexCount];
    public static readonly int[,] VertexHexes = new int[VertexCount, 3];     // land hexes only
    public static readonly int[,] VertexEdges = new int[VertexCount, 3];
    public static readonly int[,] VertexNeighbors = new int[VertexCount, 3]; // VertexNeighbors[v, i] is the far end of VertexEdges[v, i]
    public static readonly int[] VertexHarbor = new int[VertexCount];        // harbor spot 0-8, or -1

    // Edges: EdgeVertices[e, 0] < EdgeVertices[e, 1].
    public static readonly int[,] EdgeVertices = new int[EdgeCount, 2];
    // Slots 0-1 meet this edge at EdgeVertices[e, 0], slots 2-3 at EdgeVertices[e, 1]. Road legality needs to know the shared vertex.
    public static readonly int[,] EdgeNeighbors = new int[EdgeCount, 4];

    // Harbor spots.
    public static readonly int[] HarborHex = new int[HarborCount];
    public static readonly Side[] HarborSide = new Side[HarborCount];
    public static readonly int[,] HarborVertices = new int[HarborCount, 2];

    // Lookups used while building and by the coordinate helpers.
    private static readonly int[] HexByCoord = new int[5 * 5];          // q, r in [-2, 2]
    private static readonly int[] VertexByKey = new int[7 * 7 * 2];     // q, r in [-3, 3]

    static Topology()
    {
        Array.Fill(HexByCoord, -1);
        Array.Fill(VertexByKey, -1);
        Fill(HexNeighbors);
        Fill(VertexHexes);
        Fill(VertexEdges);
        Fill(VertexNeighbors);
        Array.Fill(VertexHarbor, -1);
        Fill(EdgeNeighbors);

        int hex = 0;
        for (int r = -2; r <= 2; r++)
            for (int q = -2; q <= 2; q++)
                if (Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r))) <= 2)
                {
                    HexQ[hex] = q;
                    HexR[hex] = r;
                    HexByCoord[(q + 2) * 5 + (r + 2)] = hex;
                    hex++;
                }

        int nextVertex = 0;
        for (int h = 0; h < HexCount; h++)
            for (int c = 0; c < 6; c++)
            {
                int kq = HexQ[h] + CornerDq[c], kr = HexR[h] + CornerDr[c];
                bool north = CornerNorth[c];
                int key = KeyIndex(kq, kr, north);
                if (VertexByKey[key] < 0)
                {
                    VertexByKey[key] = nextVertex;
                    VertexQ[nextVertex] = kq;
                    VertexR[nextVertex] = kr;
                    VertexIsNorth[nextVertex] = north;
                    nextVertex++;
                }
                int v = VertexByKey[key];
                HexVertices[h, c] = v;
                Append(VertexHexes, v, h);
            }

        int nextEdge = 0;
        for (int h = 0; h < HexCount; h++)
            for (int s = 0; s < 6; s++)
            {
                int a = HexVertices[h, s], b = HexVertices[h, (s + 1) % 6];
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                int e = EdgeBetween(lo, hi);
                if (e < 0)
                {
                    e = nextEdge++;
                    EdgeVertices[e, 0] = lo;
                    EdgeVertices[e, 1] = hi;
                    Append(VertexEdges, lo, e);
                    Append(VertexNeighbors, lo, hi);
                    Append(VertexEdges, hi, e);
                    Append(VertexNeighbors, hi, lo);
                }
                HexEdges[h, s] = e;
            }

        for (int h = 0; h < HexCount; h++)
            for (int s = 0; s < 6; s++)
                HexNeighbors[h, s] = HexAt(HexQ[h] + SideDq[s], HexR[h] + SideDr[s]);

        for (int e = 0; e < EdgeCount; e++)
            for (int end = 0; end < 2; end++)
            {
                int v = EdgeVertices[e, end], slot = end * 2;
                for (int i = 0; i < 3; i++)
                {
                    int f = VertexEdges[v, i];
                    if (f >= 0 && f != e)
                        EdgeNeighbors[e, slot++] = f;
                }
            }

        for (int spot = 0; spot < HarborCount; spot++)
        {
            var (q, r, side) = HarborSpots[spot];
            int h = HexAt(q, r);
            HarborHex[spot] = h;
            HarborSide[spot] = side;
            for (int i = 0; i < 2; i++)
            {
                int v = HexVertices[h, ((int)side + i) % 6];
                HarborVertices[spot, i] = v;
                VertexHarbor[v] = spot;
            }
        }
    }

    /// <summary>Land hex id at axial (q, r), or -1 for sea or off the grid.</summary>
    public static int HexAt(int q, int r) =>
        q < -2 || q > 2 || r < -2 || r > 2 ? -1 : HexByCoord[(q + 2) * 5 + (r + 2)];

    /// <summary>Vertex id for corner key (q, r, N|S), or -1 if it isn't a board vertex.</summary>
    public static int VertexAtKey(int q, int r, bool north) =>
        q < -3 || q > 3 || r < -3 || r > 3 ? -1 : VertexByKey[KeyIndex(q, r, north)];

    /// <summary>Vertex id of a corner of hex (q, r). The hex may be a sea hex if the corner touches land.</summary>
    public static int Vertex(int q, int r, Corner corner)
    {
        int c = (int)corner;
        int v = VertexAtKey(q + CornerDq[c], r + CornerDr[c], CornerNorth[c]);
        return v >= 0 ? v : throw new ArgumentException($"Corner {corner} of hex ({q}, {r}) is not a board vertex.");
    }

    /// <summary>Edge id of a side of hex (q, r). The hex may be a sea hex if the side borders land.</summary>
    public static int Edge(int q, int r, Side side)
    {
        int s = (int)side;
        int a = VertexOrMinus1(q, r, s), b = VertexOrMinus1(q, r, (s + 1) % 6);
        int e = a >= 0 && b >= 0 ? EdgeBetween(a, b) : -1;
        return e >= 0 ? e : throw new ArgumentException($"Side {side} of hex ({q}, {r}) is not a board edge.");
    }

    /// <summary>Edge joining vertices a and b, or -1 if they aren't adjacent.</summary>
    public static int EdgeBetween(int a, int b)
    {
        for (int i = 0; i < 3; i++)
            if (VertexNeighbors[a, i] == b)
                return VertexEdges[a, i];
        return -1;
    }

    /// <summary>Pixel center of a land hex (y down), Red Blob Games' pointy-top formula.</summary>
    public static (double X, double Y) HexCenter(int hex, double size) => Center(HexQ[hex], HexR[hex], size);

    /// <summary>Pixel position of a vertex: its key hex's center, offset up (N) or down (S) by size.</summary>
    public static (double X, double Y) VertexPosition(int vertex, double size)
    {
        var (x, y) = Center(VertexQ[vertex], VertexR[vertex], size);
        return (x, VertexIsNorth[vertex] ? y - size : y + size);
    }

    private static (double X, double Y) Center(int q, int r, double size) =>
        (size * Math.Sqrt(3) * (q + r / 2.0), size * 1.5 * r);

    private static int VertexOrMinus1(int q, int r, int corner) =>
        VertexAtKey(q + CornerDq[corner], r + CornerDr[corner], CornerNorth[corner]);

    private static int KeyIndex(int q, int r, bool north) => ((q + 3) * 7 + (r + 3)) * 2 + (north ? 0 : 1);

    private static void Fill(int[,] table)
    {
        for (int i = 0; i < table.GetLength(0); i++)
            for (int j = 0; j < table.GetLength(1); j++)
                table[i, j] = -1;
    }

    private static void Append(int[,] table, int row, int value)
    {
        for (int j = 0; j < table.GetLength(1); j++)
            if (table[row, j] < 0)
            {
                table[row, j] = value;
                return;
            }
        throw new InvalidOperationException($"Topology table row {row} is full.");
    }
}
