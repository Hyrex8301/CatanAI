namespace Catan.Core;

/// <summary>
/// Builds boards from a seeded <see cref="Rng"/>; the same seed always gives the same board.
/// Generated boards are always balanced: a 6 or 8 is never next to another 6 or 8.
/// </summary>
public static class BoardGenerator
{
    private const int MaxAttempts = 100_000;

    /// <summary>
    /// One unfiltered attempt: terrain onto hexes, tokens onto non-desert hexes (in hex id order), harbor types onto spots.
    /// Internal so games never use an unbalanced board; Balanced calls it and tests can reach it.
    /// </summary>
    internal static Board Random(Rng rng)
    {
        Span<Terrain> terrain = stackalloc Terrain[Topology.HexCount];
        StandardPieces.Terrain.CopyTo(terrain);
        rng.Shuffle(terrain);

        Span<int> tokens = stackalloc int[StandardPieces.Numbers.Length];
        StandardPieces.Numbers.CopyTo(tokens);
        rng.Shuffle(tokens);

        Span<int> numbers = stackalloc int[Topology.HexCount];
        for (int h = 0, t = 0; h < Topology.HexCount; h++)
            numbers[h] = terrain[h] == Terrain.Desert ? 0 : tokens[t++];

        Span<HarborType> harbors = stackalloc HarborType[Topology.HarborCount];
        StandardPieces.Harbors.CopyTo(harbors);
        rng.Shuffle(harbors);

        return new Board(terrain, numbers, harbors);
    }

    /// <summary>Random, retried until no 6 or 8 is next to another 6 or 8. Strict also bans equal numbers on neighbors.</summary>
    public static Board Balanced(Rng rng, bool strict = false) => Balanced(rng, strict, out _);

    public static Board Balanced(Rng rng, bool strict, out int attempts)
    {
        for (attempts = 1; attempts <= MaxAttempts; attempts++)
        {
            var board = Random(rng);
            if (IsBalanced(board, strict))
                return board;
        }
        throw new InvalidOperationException($"No balanced board found in {MaxAttempts} attempts.");
    }

    public static Board FromJson(string json) => BoardJson.Deserialize(json);

    public static bool IsBalanced(Board board, bool strict = false)
    {
        for (int h = 0; h < Topology.HexCount; h++)
        {
            int n = board.NumberAt(h);
            if (n == 0)
                continue;
            for (int s = 0; s < 6; s++)
            {
                int other = Topology.HexNeighbors[h, s];
                if (other < 0)
                    continue;
                int m = board.NumberAt(other);
                if (IsRed(n) && IsRed(m))
                    return false;
                if (strict && n == m)
                    return false;
            }
        }
        return true;
    }

    private static bool IsRed(int number) => number is 6 or 8;
}
