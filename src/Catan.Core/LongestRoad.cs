namespace Catan.Core;

/// <summary>
/// Road length: the longest single path through a seat's roads, each segment counted once. Branches don't add,
/// and an opponent's building on a vertex cuts the path there. Computed from scratch by depth-first search.
/// </summary>
public static class LongestRoad
{
    public static int Compute(GameState state, int seat)
    {
        int best = 0;
        for (int e = 0; e < Topology.EdgeCount; e++)
        {
            if (state.EdgeOwner[e] != seat)
                continue;
            // Every path starts with some edge in some direction; extend from each end in turn.
            var used = (UInt128)1 << e;
            best = Math.Max(best, 1 + Extend(state, seat, Topology.EdgeVertices[e, 0], used));
            best = Math.Max(best, 1 + Extend(state, seat, Topology.EdgeVertices[e, 1], used));
        }
        return best;
    }

    /// <summary>Longest continuation from vertex v using unused roads of this seat.</summary>
    private static int Extend(GameState state, int seat, int v, UInt128 used)
    {
        int owner = state.VertexOwner[v];
        if (owner >= 0 && owner != seat)
            return 0;

        int best = 0;
        for (int i = 0; i < 3; i++)
        {
            int f = Topology.VertexEdges[v, i];
            if (f < 0 || state.EdgeOwner[f] != seat)
                continue;
            var bit = (UInt128)1 << f;
            if ((used & bit) != 0)
                continue;
            best = Math.Max(best, 1 + Extend(state, seat, Topology.VertexNeighbors[v, i], used | bit));
        }
        return best;
    }
}
