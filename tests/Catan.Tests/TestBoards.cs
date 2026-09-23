using Catan.Core;

namespace Catan.Tests;

/// <summary>Fixed boards for tests, written out by hand so generator changes can't move them.</summary>
public static class TestBoards
{
    /// <summary>
    /// Balanced fixed layout, desert in the center (hex 9). Reds sit on the four corner hexes 0, 2, 16, 18.
    /// Hex: 0 Hills 6 | 1 Pasture 2 | 2 Forest 8 | 3 Fields 3 | 4 Mountains 4 | 5 Pasture 5 | 6 Fields 9 |
    /// 7 Forest 10 | 8 Hills 11 | 9 Desert | 10 Mountains 12 | 11 Pasture 3 | 12 Fields 4 | 13 Forest 5 |
    /// 14 Hills 9 | 15 Mountains 10 | 16 Pasture 6 | 17 Fields 11 | 18 Forest 8.
    /// Harbor spots 0-8: Generic, Brick, Generic, Lumber, Generic, Wool, Generic, Grain, Ore.
    /// </summary>
    public static readonly Board Standard = new(
        new[]
        {
            Terrain.Hills, Terrain.Pasture, Terrain.Forest,
            Terrain.Fields, Terrain.Mountains, Terrain.Pasture, Terrain.Fields,
            Terrain.Forest, Terrain.Hills, Terrain.Desert, Terrain.Mountains, Terrain.Pasture,
            Terrain.Fields, Terrain.Forest, Terrain.Hills, Terrain.Mountains,
            Terrain.Pasture, Terrain.Fields, Terrain.Forest,
        },
        new[] { 6, 2, 8, 3, 4, 5, 9, 10, 11, 0, 12, 3, 4, 5, 9, 10, 6, 11, 8 },
        new[]
        {
            HarborType.Generic, HarborType.Brick, HarborType.Generic, HarborType.Lumber, HarborType.Generic,
            HarborType.Wool, HarborType.Generic, HarborType.Grain, HarborType.Ore,
        });
}
