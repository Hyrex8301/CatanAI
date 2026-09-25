using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>
/// Spots the way players say them: by the numbers on the hexes around the corner ("6 5 9", "11 3 4"; a coast spot has two).
/// The desert and the sea have no number, so they don't count.
/// </summary>
public static class Spots
{
    /// <summary>The numbers around vertex <paramref name="v"/>, most pips first ("6 5 9").</summary>
    public static string Name(Board board, int v) => string.Join(" ", NumbersAt(board, v));

    /// <summary>Every vertex whose numbers are exactly <paramref name="numbers"/> (any order).</summary>
    public static List<int> Find(Board board, IReadOnlyCollection<int> numbers)
    {
        var wanted = numbers.Order().ToList();
        var found = new List<int>();
        for (int v = 0; v < Topology.VertexCount; v++)
            if (NumbersAt(board, v).Order().SequenceEqual(wanted))
                found.Add(v);
        return found;
    }

    /// <summary>True if <paramref name="v"/> is empty with no building next to it (someone could still settle there).</summary>
    public static bool IsOpen(GameState s, int v)
    {
        if (s.VertexOwner[v] >= 0)
            return false;
        for (int i = 0; i < 3; i++)
        {
            int nb = Topology.VertexNeighbors[v, i];
            if (nb >= 0 && s.VertexOwner[nb] >= 0)
                return false;
        }
        return true;
    }

    public static List<int> NumbersAt(Board board, int v)
    {
        var numbers = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            int h = Topology.VertexHexes[v, i];
            if (h >= 0 && board.NumberAt(h) > 0)
                numbers.Add(board.NumberAt(h));
        }
        return numbers.OrderByDescending(Board.PipsFor).ThenBy(n => n).ToList();
    }
}
