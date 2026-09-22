using Catan.Core;

namespace Catan.Tests;

public class ResourceSetTests
{
    [Fact]
    public void ResourceOrderIsFixed()
    {
        Assert.Equal(0, (int)Resource.Brick);
        Assert.Equal(1, (int)Resource.Lumber);
        Assert.Equal(2, (int)Resource.Wool);
        Assert.Equal(3, (int)Resource.Grain);
        Assert.Equal(4, (int)Resource.Ore);
    }

    [Fact]
    public void IndexerFollowsResourceOrder()
    {
        var s = new ResourceSet(1, 2, 3, 4, 5);
        for (int i = 0; i < GameConstants.ResourceCount; i++)
        {
            Assert.Equal(i + 1, s[i]);
            Assert.Equal(i + 1, s[(Resource)i]);
        }
    }

    [Fact]
    public void IndexerRejectsOutOfRange()
    {
        var s = new ResourceSet(1, 2, 3, 4, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => s[5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => s[-1]);
    }

    [Fact]
    public void TotalSumsAllCounts()
    {
        Assert.Equal(15, new ResourceSet(1, 2, 3, 4, 5).Total);
        Assert.Equal(0, default(ResourceSet).Total);
    }

    [Fact]
    public void AddAndSubtractAreComponentWise()
    {
        var a = new ResourceSet(1, 2, 3, 4, 5);
        var b = new ResourceSet(5, 4, 3, 2, 1);
        Assert.Equal(new ResourceSet(6, 6, 6, 6, 6), a + b);
        Assert.Equal(new ResourceSet(-4, -2, 0, 2, 4), a - b);
        Assert.Equal(a, a + b - b);
    }

    [Fact]
    public void OfPutsCountInTheNamedSlot()
    {
        for (int i = 0; i < GameConstants.ResourceCount; i++)
        {
            var s = ResourceSet.Of((Resource)i, 3);
            Assert.Equal(3, s[i]);
            Assert.Equal(3, s.Total);
        }
    }

    [Fact]
    public void FromReadsFiveInts()
    {
        int[] hands = { 9, 9, 9, 9, 9, 1, 2, 3, 4, 5 };
        Assert.Equal(new ResourceSet(1, 2, 3, 4, 5), ResourceSet.From(hands.AsSpan(5, 5)));
    }

    [Fact]
    public void FitsInWhenEveryCountIsCovered()
    {
        var hand = new ResourceSet(1, 1, 1, 1, 0);
        Assert.True(Costs.Settlement.FitsIn(hand));
        Assert.True(default(ResourceSet).FitsIn(hand));
        Assert.True(hand.FitsIn(hand));
    }

    [Fact]
    public void DoesNotFitWhenAnyCountIsShort()
    {
        var hand = new ResourceSet(1, 1, 1, 0, 9);
        Assert.False(Costs.Settlement.FitsIn(hand));
        Assert.False(Costs.City.FitsIn(new ResourceSet(0, 0, 0, 2, 2)));
    }

    [Fact]
    public void FitsInSpanMatchesFitsInSet()
    {
        int[] hand = { 0, 0, 0, 2, 3 };
        Assert.True(Costs.City.FitsIn(hand));
        hand[4] = 2;
        Assert.False(Costs.City.FitsIn(hand));
    }

    [Fact]
    public void CostsMatchRulebook()
    {
        Assert.Equal(new ResourceSet(Brick: 1, Lumber: 1, Wool: 0, Grain: 0, Ore: 0), Costs.Road);
        Assert.Equal(new ResourceSet(Brick: 1, Lumber: 1, Wool: 1, Grain: 1, Ore: 0), Costs.Settlement);
        Assert.Equal(new ResourceSet(Brick: 0, Lumber: 0, Wool: 0, Grain: 2, Ore: 3), Costs.City);
        Assert.Equal(new ResourceSet(Brick: 0, Lumber: 0, Wool: 1, Grain: 1, Ore: 1), Costs.DevCard);
        Assert.Equal(15, Costs.RoadsPerPlayer);
        Assert.Equal(5, Costs.SettlementsPerPlayer);
        Assert.Equal(4, Costs.CitiesPerPlayer);
    }
}
