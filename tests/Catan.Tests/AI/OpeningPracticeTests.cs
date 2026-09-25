using Catan.AI;
using Catan.Core;

namespace Catan.Tests.AI;

public class OpeningPracticeTests
{
    private static readonly BotWeights Weights = new();

    /// <summary>Plays the whole opening, placing the coach's top spot and road at your turns; returns who placed each settlement.</summary>
    private static (OpeningPractice Practice, List<int> SettlementOrder) PlayThrough(int seat, ulong seed)
    {
        var practice = new OpeningPractice(Weights, BoardGenerator.Balanced(new Rng(seed)), new GameSettings(), seat, seed);
        var coach = new PlacementCoach(Weights);
        var order = new List<int>();
        while (!practice.Done)
        {
            if (practice.YourTurn)
            {
                var move = practice.State.Phase == Phase.SetupSettlement
                    ? new GameAction(ActionType.BuildSettlement, seat, coach.Rate(practice.State, seat)[0].Vertex)
                    : new GameAction(ActionType.BuildRoad, seat, coach.RateRoads(practice.State, seat)[0].Edge);
                practice.Place(move);
            }
            else
                Assert.NotNull(practice.BotStep());
            var last = practice.Actions[^1];
            if (last.Type == ActionType.BuildSettlement)
                order.Add(last.Seat);
        }
        return (practice, order);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TheOpeningGoesInSnakeOrder(int seat)
    {
        var (practice, order) = PlayThrough(seat, 7);
        Assert.Equal(new[] { 0, 1, 2, 3, 3, 2, 1, 0 }, order);
        Assert.Equal(16, practice.Actions.Count);
        Assert.Equal(Phase.PreRoll, practice.State.Phase);
    }

    [Fact]
    public void TheSameBoardAndChoicesReplayExactly()
    {
        var a = PlayThrough(1, 11).Practice;
        var b = PlayThrough(1, 11).Practice;
        Assert.Equal(a.Actions, b.Actions);
    }

    [Fact]
    public void AFinishedOpeningContinuesAsANormalGame()
    {
        var practice = PlayThrough(3, 5).Practice;
        var record = practice.ToRecord(99, new[] { "a", "b", "c", "d" });
        var agents = Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot((ulong)i + 1)).ToArray();
        var runner = GameRunner.Resume(GameRecord.FromJson(record.ToJson()), agents, new RngChance(3), validate: true);
        Assert.Equal(practice.State.ComputeHash(), runner.State.ComputeHash());
        for (int i = 0; i < 200 && !runner.IsOver; i++)
            runner.StepAsync().GetAwaiter().GetResult(); // and plays on legally
    }

    [Fact]
    public void RoadsAndSpotsComeWithRatingsAndFacts()
    {
        var practice = new OpeningPractice(Weights, BoardGenerator.Balanced(new Rng(3)), new GameSettings(), 0, 3);
        var coach = new PlacementCoach(Weights);
        var spots = coach.Rate(practice.State, 0, new[] { "Red", "Blue", "Orange", "White" });
        Assert.All(spots, s => Assert.NotEmpty(s.Facts!));
        Assert.Equal(100, spots[0].Rating);

        practice.Place(new GameAction(ActionType.BuildSettlement, 0, spots[0].Vertex));
        var roads = coach.RateRoads(practice.State, 0, new[] { "Red", "Blue", "Orange", "White" });
        Assert.Equal(practice.Legal().Count, roads.Count);
        Assert.Equal(Enumerable.Range(1, roads.Count), roads.Select(r => r.Rank));
        Assert.All(roads, r => Assert.NotEmpty(r.Facts));
        Assert.True(roads[0].TowardPips > 0, "the best opening road should point at an open spot");
    }

    [Fact]
    public void OnlyLegalPlacementsAreAccepted()
    {
        var practice = new OpeningPractice(Weights, BoardGenerator.Balanced(new Rng(2)), new GameSettings(), 1, 2);
        Assert.False(practice.YourTurn); // seat 0 places first
        Assert.Throws<InvalidOperationException>(() => practice.Place(new GameAction(ActionType.BuildSettlement, 1, 0)));
        practice.BotStep();
        practice.BotStep(); // seat 0: settlement and road
        Assert.True(practice.YourTurn);
        Assert.Throws<InvalidOperationException>(() => practice.Place(new GameAction(ActionType.BuildRoad, 1, 0)));
    }
}
