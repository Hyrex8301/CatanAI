using Catan.Core;

namespace Catan.Tests;

public class InfoTests
{
    [Fact]
    public void BaseBoardHas19Hexes()
    {
        Assert.Equal(19, Info.HexCount);
    }
}
