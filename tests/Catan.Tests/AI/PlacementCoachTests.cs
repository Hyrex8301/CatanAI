using Catan.AI;
using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests.AI;

public class PlacementCoachTests
{
    private static readonly PlacementCoach Coach = new(new BotWeights());

    [Fact]
    public void DealPlacesThePlayersBeforeYouAndLeavesYouToChoose()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = Coach.Deal(seed, out int seat);
            Assert.Equal(Phase.SetupSettlement, s.Phase);
            Assert.Equal(seat, Rules.ActingSeat(s));
            int settlements = Enumerable.Range(0, VertexCount).Count(v => s.VertexOwner[v] >= 0);
            Assert.Equal(seat, settlements); // everyone before you has placed one
            Assert.Equal(seat, Enumerable.Range(0, EdgeCount).Count(e => s.EdgeOwner[e] >= 0));
        }
    }

    [Fact]
    public void RatingsCoverEveryLegalSpotBestFirstFrom100Down()
    {
        var s = Coach.Deal(3, out int seat);
        var ratings = Coach.Rate(s, seat);
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, seat, legal);
        Assert.Equal(legal.Count(a => a.Type == ActionType.BuildSettlement), ratings.Count);
        Assert.Equal(1, ratings[0].Rank);
        Assert.Equal(100, ratings[0].Rating, 6);
        Assert.Equal(0, ratings[^1].Rating, 6);
        Assert.Equal(ratings.OrderByDescending(r => r.Score).Select(r => r.Vertex), ratings.Select(r => r.Vertex));
        Assert.Equal(Enumerable.Range(1, ratings.Count), ratings.Select(r => r.Rank));
    }

    [Fact]
    public void SameSeedGivesTheSamePracticeAndRatings()
    {
        var a = Coach.Deal(9, out int seatA);
        var b = Coach.Deal(9, out int seatB);
        Assert.Equal(seatA, seatB);
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.Equal(Coach.Rate(a, seatA).Select(r => r.Vertex), Coach.Rate(b, seatB).Select(r => r.Vertex));
    }

    [Fact]
    public void ProductionAddsThePipsAroundASpot()
    {
        // TestBoards.Standard: vertex N of hex (0,-2) touches only that hex (Hills 6, 5 pips) on the coast.
        var (total, perResource) = PlacementCoach.Production(TestBoards.Standard, Vertex(0, -2, Corner.N));
        Assert.Equal(5, total);
        Assert.Equal(5, perResource[(int)Resource.Brick]);
    }
}
