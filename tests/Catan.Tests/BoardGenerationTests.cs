using Catan.Core;

namespace Catan.Tests;

public class BoardGenerationTests
{
    private static readonly int[] ExpectedTokens = { 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12 };

    private static void AssertStandardPieces(Board board)
    {
        var terrain = Enumerable.Range(0, Topology.HexCount).Select(board.TerrainAt).ToList();
        Assert.Equal(4, terrain.Count(t => t == Terrain.Forest));
        Assert.Equal(4, terrain.Count(t => t == Terrain.Pasture));
        Assert.Equal(4, terrain.Count(t => t == Terrain.Fields));
        Assert.Equal(3, terrain.Count(t => t == Terrain.Hills));
        Assert.Equal(3, terrain.Count(t => t == Terrain.Mountains));
        Assert.Equal(1, terrain.Count(t => t == Terrain.Desert));

        var tokens = Enumerable.Range(0, Topology.HexCount).Select(board.NumberAt).Where(n => n != 0).Order();
        Assert.Equal(ExpectedTokens, tokens);

        var harbors = Enumerable.Range(0, Topology.HarborCount).Select(board.HarborTypeAt).ToList();
        Assert.Equal(4, harbors.Count(h => h == HarborType.Generic));
        foreach (var type in new[] { HarborType.Brick, HarborType.Lumber, HarborType.Wool, HarborType.Grain, HarborType.Ore })
            Assert.Equal(1, harbors.Count(h => h == type));

        Assert.Equal(Terrain.Desert, board.TerrainAt(board.DesertHex));
        Assert.Equal(0, board.NumberAt(board.DesertHex));
        Assert.Equal(-1, board.ResourceAt(board.DesertHex));
    }

    private static int[] Numbers(Board b) => Enumerable.Range(0, Topology.HexCount).Select(b.NumberAt).ToArray();

    // ---- Pieces and pips ----

    [Fact]
    public void RandomBoardsUseTheStandardPieces()
    {
        for (ulong seed = 0; seed < 200; seed++)
            AssertStandardPieces(BoardGenerator.Random(new Rng(seed)));
    }

    [Fact]
    public void BalancedBoardsUseTheStandardPieces()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            AssertStandardPieces(BoardGenerator.Balanced(new Rng(seed)));
            AssertStandardPieces(BoardGenerator.Balanced(new Rng(seed), strict: true));
        }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(6, 5)]
    [InlineData(7, 0)]
    [InlineData(8, 5)]
    [InlineData(11, 2)]
    [InlineData(12, 1)]
    [InlineData(0, 0)]
    public void PipsAreSixMinusDistanceFromSeven(int number, int pips) => Assert.Equal(pips, Board.PipsFor(number));

    [Fact]
    public void BoardStoresPipsAndProductionLookups()
    {
        var board = BoardGenerator.Random(new Rng(4));
        Assert.Equal(58, Enumerable.Range(0, Topology.HexCount).Sum(board.PipsAt)); // standard tokens: 1+4+6+8+10 + 10+8+6+4+1
        for (int h = 0; h < Topology.HexCount; h++)
        {
            Assert.Equal(Board.PipsFor(board.NumberAt(h)), board.PipsAt(h));
            if (board.TerrainAt(h) != Terrain.Desert)
                Assert.Equal((int)board.TerrainAt(h), board.ResourceAt(h));
        }

        int covered = 0;
        for (int n = 2; n <= 12; n++)
            foreach (int h in board.HexesWithNumber(n))
            {
                Assert.Equal(n, board.NumberAt(h));
                covered++;
            }
        Assert.Equal(18, covered);
        Assert.True(board.HexesWithNumber(7).IsEmpty);
    }

    // ---- Determinism ----

    [Fact]
    public void SameSeedGivesSameBoard()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            Assert.Equal(BoardJson.Serialize(BoardGenerator.Random(new Rng(seed))), BoardJson.Serialize(BoardGenerator.Random(new Rng(seed))));
            Assert.Equal(BoardJson.Serialize(BoardGenerator.Balanced(new Rng(seed))), BoardJson.Serialize(BoardGenerator.Balanced(new Rng(seed))));
        }
    }

    [Fact]
    public void DifferentSeedsGiveDifferentBoards()
    {
        var layouts = Enumerable.Range(0, 50).Select(s => BoardJson.Serialize(BoardGenerator.Random(new Rng((ulong)s)))).ToHashSet();
        Assert.Equal(50, layouts.Count);
    }

    // ---- Balanced ----

    [Fact]
    public void BalancedNeverPutsSixOrEightNextToSixOrEight()
    {
        for (ulong seed = 0; seed < 1000; seed++)
        {
            var board = BoardGenerator.Balanced(new Rng(seed));
            for (int h = 0; h < Topology.HexCount; h++)
            {
                if (board.NumberAt(h) is not (6 or 8)) continue;
                for (int s = 0; s < 6; s++)
                {
                    int n = Topology.HexNeighbors[h, s];
                    if (n >= 0)
                        Assert.False(board.NumberAt(n) is 6 or 8, $"seed {seed}: hexes {h} and {n} are both red");
                }
            }
        }
    }

    [Fact]
    public void StrictBalancedAlsoBansEqualNeighbors()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var board = BoardGenerator.Balanced(new Rng(seed), strict: true);
            Assert.True(BoardGenerator.IsBalanced(board));
            for (int h = 0; h < Topology.HexCount; h++)
                for (int s = 0; s < 6; s++)
                {
                    int n = Topology.HexNeighbors[h, s];
                    if (n >= 0 && board.NumberAt(h) != 0)
                        Assert.NotEqual(board.NumberAt(h), board.NumberAt(n));
                }
        }
    }

    [Fact]
    public void BalancedAttemptCountsAreInTheBriefsBallpark()
    {
        // The brief estimates ~7 tries for Balanced and ~40 for strict.
        double normal = Enumerable.Range(0, 2000).Average(s => { BoardGenerator.Balanced(new Rng((ulong)s), false, out int a); return a; });
        double strict = Enumerable.Range(0, 2000).Average(s => { BoardGenerator.Balanced(new Rng((ulong)s), true, out int a); return a; });
        Assert.InRange(normal, 3, 15);
        Assert.InRange(strict, 15, 100);
    }

    // ---- JSON ----

    [Fact]
    public void JsonRoundTripsAnyBoard()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var board = BoardGenerator.Balanced(new Rng(seed));
            var json = BoardJson.Serialize(board);
            var loaded = BoardGenerator.FromJson(json);
            Assert.Equal(json, BoardJson.Serialize(loaded));
            Assert.Equal(Numbers(board), Numbers(loaded));
        }
    }

    [Fact]
    public void JsonUsesTheBriefsShape()
    {
        var json = BoardJson.Serialize(BoardGenerator.Random(new Rng(1)));
        Assert.Contains("\"hexes\":[{\"q\":0,\"r\":-2,\"terrain\":\"", json);
        Assert.Contains("\"harbors\":[{\"spot\":0,\"type\":\"", json);
        Assert.Contains("\"terrain\":\"Desert\"}", json); // desert has no number
    }

    private static string LayoutJson(Func<HexLayout, int, HexLayout>? editHex = null, Func<HarborLayout, HarborLayout>? editHarbor = null)
    {
        var layout = BoardGenerator.Random(new Rng(9)).ToLayout();
        var hexes = layout.Hexes.Select((h, i) => editHex?.Invoke(h, i) ?? h).ToList();
        var harbors = layout.Harbors.Select(h => editHarbor?.Invoke(h) ?? h).ToList();
        return System.Text.Json.JsonSerializer.Serialize(new BoardLayout(hexes, harbors), BoardJson.Options);
    }

    [Fact]
    public void JsonRejectsBadLayouts()
    {
        // Duplicate hex (hex 1 moved onto hex 0's coordinates).
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson((h, i) => i == 1 ? h with { Q = 0, R = -2 } : h)));
        // Sea hex.
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson((h, i) => i == 0 ? h with { Q = 3, R = -3 } : h)));
        // Desert with a number, or a producing hex without one.
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson((h, _) => h.Terrain == Terrain.Desert ? h with { Number = 7 } : h)));
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson((h, _) => h.Terrain != Terrain.Desert && h.Number == 2 ? h with { Number = null } : h)));
        // Wrong token set (a 2 becomes a 7).
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson((h, _) => h.Number == 2 ? h with { Number = 7 } : h)));
        // Wrong terrain set.
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson((h, i) => h.Terrain == Terrain.Hills ? h with { Terrain = Terrain.Forest } : h)));
        // Harbor spot out of range, and wrong harbor set.
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson(editHarbor: h => h.Spot == 8 ? h with { Spot = 9 } : h)));
        Assert.Throws<ArgumentException>(() => BoardGenerator.FromJson(LayoutJson(editHarbor: h => h with { Type = HarborType.Generic })));
        // Unknown enum names and numeric enums are rejected by the JSON reader.
        Assert.ThrowsAny<Exception>(() => BoardGenerator.FromJson(LayoutJson().Replace("\"Desert\"", "\"Swamp\"")));
        Assert.ThrowsAny<Exception>(() => BoardGenerator.FromJson(LayoutJson().Replace("\"Desert\"", "5")));
    }
}
