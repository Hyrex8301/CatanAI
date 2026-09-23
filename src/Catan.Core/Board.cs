namespace Catan.Core;

/// <summary>Producing terrains share their index with the resource they produce (Hills 0 = Brick ... Mountains 4 = Ore).</summary>
public enum Terrain : byte
{
    Hills = 0,
    Forest = 1,
    Pasture = 2,
    Fields = 3,
    Mountains = 4,
    Desert = 5,
}

/// <summary>2:1 types share their index with their resource; Generic is 3:1.</summary>
public enum HarborType : byte
{
    Brick = 0,
    Lumber = 1,
    Wool = 2,
    Grain = 3,
    Ore = 4,
    Generic = 5,
}

/// <summary>The base game's pieces: 19 terrain tiles, 18 number tokens, 9 harbors.</summary>
internal static class StandardPieces
{
    public static readonly Terrain[] Terrain =
    {
        Core.Terrain.Forest, Core.Terrain.Forest, Core.Terrain.Forest, Core.Terrain.Forest,
        Core.Terrain.Pasture, Core.Terrain.Pasture, Core.Terrain.Pasture, Core.Terrain.Pasture,
        Core.Terrain.Fields, Core.Terrain.Fields, Core.Terrain.Fields, Core.Terrain.Fields,
        Core.Terrain.Hills, Core.Terrain.Hills, Core.Terrain.Hills,
        Core.Terrain.Mountains, Core.Terrain.Mountains, Core.Terrain.Mountains,
        Core.Terrain.Desert,
    };

    public static readonly int[] Numbers = { 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12 };

    public static readonly HarborType[] Harbors =
    {
        HarborType.Generic, HarborType.Generic, HarborType.Generic, HarborType.Generic,
        HarborType.Brick, HarborType.Lumber, HarborType.Wool, HarborType.Grain, HarborType.Ore,
    };
}

/// <summary>
/// Fixed topology plus terrain, number tokens and harbor types. Immutable and shared by every copy of a game state.
/// The constructor only accepts the base game's exact piece set.
/// </summary>
public sealed class Board
{
    private readonly Terrain[] _terrain = new Terrain[Topology.HexCount];
    private readonly int[] _number = new int[Topology.HexCount];
    private readonly int[] _pips = new int[Topology.HexCount];
    private readonly HarborType[] _harbors = new HarborType[Topology.HarborCount];
    private readonly int[][] _hexesByNumber = new int[13][];

    /// <param name="terrain">Terrain per hex id.</param>
    /// <param name="numbers">Number token per hex id; 0 on the desert.</param>
    /// <param name="harbors">Harbor type per harbor spot (0-8).</param>
    public Board(ReadOnlySpan<Terrain> terrain, ReadOnlySpan<int> numbers, ReadOnlySpan<HarborType> harbors)
    {
        if (terrain.Length != Topology.HexCount || numbers.Length != Topology.HexCount || harbors.Length != Topology.HarborCount)
            throw new ArgumentException($"A board needs {Topology.HexCount} terrains, {Topology.HexCount} numbers and {Topology.HarborCount} harbors.");

        terrain.CopyTo(_terrain);
        numbers.CopyTo(_number);
        harbors.CopyTo(_harbors);
        Validate();

        DesertHex = Array.IndexOf(_terrain, Terrain.Desert);
        for (int h = 0; h < Topology.HexCount; h++)
            _pips[h] = PipsFor(_number[h]);
        for (int n = 0; n <= 12; n++)
        {
            int number = n;
            _hexesByNumber[n] = n is >= 2 and <= 12 and not 7
                ? Enumerable.Range(0, Topology.HexCount).Where(h => _number[h] == number).ToArray()
                : Array.Empty<int>();
        }
    }

    /// <summary>The robber starts here.</summary>
    public int DesertHex { get; }

    public Terrain TerrainAt(int hex) => _terrain[hex];

    /// <summary>Number token on the hex, or 0 for the desert.</summary>
    public int NumberAt(int hex) => _number[hex];

    public int PipsAt(int hex) => _pips[hex];

    /// <summary>Resource index the hex produces, or -1 for the desert.</summary>
    public int ResourceAt(int hex) => _terrain[hex] == Terrain.Desert ? -1 : (int)_terrain[hex];

    public HarborType HarborTypeAt(int spot) => _harbors[spot];

    /// <summary>Hex ids carrying number token n (empty for 7 or out of range).</summary>
    public ReadOnlySpan<int> HexesWithNumber(int n) => n is >= 0 and <= 12 ? _hexesByNumber[n] : ReadOnlySpan<int>.Empty;

    /// <summary>Pips = 6 - |7 - n| for a token; 0 for none (desert) or 7. Roll chance is pips / 36.</summary>
    public static int PipsFor(int number) => number is >= 2 and <= 12 and not 7 ? 6 - Math.Abs(7 - number) : 0;

    public BoardLayout ToLayout()
    {
        var hexes = new List<HexLayout>(Topology.HexCount);
        for (int h = 0; h < Topology.HexCount; h++)
            hexes.Add(new HexLayout(Topology.HexQ[h], Topology.HexR[h], _terrain[h], _number[h] == 0 ? null : _number[h]));
        var harbors = new List<HarborLayout>(Topology.HarborCount);
        for (int spot = 0; spot < Topology.HarborCount; spot++)
            harbors.Add(new HarborLayout(spot, _harbors[spot]));
        return new BoardLayout(hexes, harbors);
    }

    public static Board FromLayout(BoardLayout layout)
    {
        if (layout.Hexes is null || layout.Harbors is null)
            throw new ArgumentException("Board layout needs both hexes and harbors.");

        var terrain = new Terrain[Topology.HexCount];
        var numbers = new int[Topology.HexCount];
        var seenHex = new bool[Topology.HexCount];
        foreach (var hex in layout.Hexes)
        {
            int h = Topology.HexAt(hex.Q, hex.R);
            if (h < 0)
                throw new ArgumentException($"({hex.Q}, {hex.R}) is not a land hex.");
            if (seenHex[h])
                throw new ArgumentException($"Hex ({hex.Q}, {hex.R}) appears twice.");
            seenHex[h] = true;
            terrain[h] = hex.Terrain;
            numbers[h] = hex.Number ?? 0;
        }
        int missing = Array.IndexOf(seenHex, false);
        if (missing >= 0)
            throw new ArgumentException($"Hex ({Topology.HexQ[missing]}, {Topology.HexR[missing]}) is missing.");

        var harbors = new HarborType[Topology.HarborCount];
        var seenSpot = new bool[Topology.HarborCount];
        foreach (var harbor in layout.Harbors)
        {
            if (harbor.Spot is < 0 or >= Topology.HarborCount)
                throw new ArgumentException($"Harbor spot {harbor.Spot} is out of range 0-{Topology.HarborCount - 1}.");
            if (seenSpot[harbor.Spot])
                throw new ArgumentException($"Harbor spot {harbor.Spot} appears twice.");
            seenSpot[harbor.Spot] = true;
            harbors[harbor.Spot] = harbor.Type;
        }
        int missingSpot = Array.IndexOf(seenSpot, false);
        if (missingSpot >= 0)
            throw new ArgumentException($"Harbor spot {missingSpot} is missing.");

        return new Board(terrain, numbers, harbors);
    }

    private void Validate()
    {
        for (int h = 0; h < Topology.HexCount; h++)
        {
            bool desert = _terrain[h] == Terrain.Desert;
            if (!Enum.IsDefined(_terrain[h]))
                throw new ArgumentException($"Hex {h} has unknown terrain {(int)_terrain[h]}.");
            if (desert != (_number[h] == 0))
                throw new ArgumentException(desert ? $"The desert (hex {h}) can't have a number." : $"Hex {h} ({_terrain[h]}) needs a number.");
        }
        if (!SameMultiset(_terrain, StandardPieces.Terrain))
            throw new ArgumentException("Terrain must be 4 forest, 4 pasture, 4 fields, 3 hills, 3 mountains, 1 desert.");
        if (!SameMultiset(_number.Where(n => n != 0).ToArray(), StandardPieces.Numbers))
            throw new ArgumentException("Number tokens must be 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12.");
        if (!SameMultiset(_harbors, StandardPieces.Harbors))
            throw new ArgumentException("Harbors must be 4 generic plus one 2:1 of each resource.");
    }

    private static bool SameMultiset<T>(T[] actual, T[] expected) =>
        actual.Order().SequenceEqual(expected.Order());
}
