using Catan.AI;

namespace Catan.Tests.AI;

public class LadderTests
{
    [Fact]
    public void IdenticalBotsSplitEvenly()
    {
        var r = Ladder.Play("random", "random", 400, 1, 8);
        Assert.Equal(400, r.Games);
        Assert.InRange(r.AShare, 0.5 - r.Interval - 0.02, 0.5 + r.Interval + 0.02);
    }

    [Fact]
    public void AStrongerBotWinsTheirPairing()
    {
        var r = Ladder.Play("smart-fast", "random", 40, 7, 8);
        Assert.True(r.AShare > 0.9, $"SmartBot won only {r.AShare:P0} of 2v2 games against RandomBots");
    }

    [Fact]
    public void RatingsRecoverTheOrderAndAnchorTheFirstBot()
    {
        var results = new[]
        {
            new PairResult("mid", "weak", 70, 30, 100),
            new PairResult("strong", "mid", 70, 30, 100),
            new PairResult("strong", "weak", 85, 15, 100),
        };
        var ratings = Ladder.Ratings(new[] { "weak", "mid", "strong" }, results);
        Assert.Equal(1000, ratings["weak"], 6);
        Assert.True(ratings["mid"] > ratings["weak"] + 100);
        Assert.True(ratings["strong"] > ratings["mid"] + 100);
    }

    [Fact]
    public void EvenResultsGiveEqualRatingsAndOneSidedOnesStayFinite()
    {
        var even = Ladder.Ratings(new[] { "a", "b" }, new[] { new PairResult("a", "b", 50, 50, 100) });
        Assert.Equal(even["a"], even["b"], 6);
        var lopsided = Ladder.Ratings(new[] { "a", "b" }, new[] { new PairResult("a", "b", 0, 200, 200) });
        Assert.True(double.IsFinite(lopsided["b"]) && lopsided["b"] > 1500);
    }
}
