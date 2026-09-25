using Catan.AI;
using Catan.Core;

namespace Catan.Tests.AI;

/// <summary>Position practice: the random positions it deals and the grades the coach gives in them.</summary>
public class DecisionCoachTests
{
    private static readonly string[] Names = { "Red", "Blue", "Orange", "White" };
    private static readonly BotWeights Bundled = BotWeights.Load(Path.Combine(RepoRoot(), "game", "bots", "best.json"));

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void DealtPositionIsYourTurnWithSeveralKindsOfPlay(ulong seed)
    {
        var p = PositionDealer.Deal(Bundled, seed);
        var s = p.ToState();
        Assert.Empty(StateValidator.Check(s));
        Assert.Equal(ScenarioKind.Decision, p.Kind);
        Assert.Equal(s.CurrentPlayer, p.Seat);
        Assert.Equal(Phase.Main, s.Phase);
        Assert.NotEqual(7, s.LastRoll);
        Assert.Contains($"You rolled {s.LastRoll}", p.Description);
        Assert.True(PositionDealer.Fits(s, new List<GameAction>()));
        // It round-trips through JSON.
        Assert.Equal(s.ComputeHash(), Position.FromJson(p.ToJson()).ToState().ComputeHash());
    }

    [Fact]
    public void TheSameSeedDealsTheSamePosition()
    {
        Assert.Equal(PositionDealer.Deal(Bundled, 5).ToState().ComputeHash(), PositionDealer.Deal(Bundled, 5).ToState().ComputeHash());
        Assert.NotEqual(PositionDealer.Deal(Bundled, 5).ToState().ComputeHash(), PositionDealer.Deal(Bundled, 6).ToState().ComputeHash());
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void GradesRankEveryMoveOnce(ulong seed)
    {
        var p = PositionDealer.Deal(Bundled, seed);
        var runner = GameRunner.FromPosition(p, Enumerable.Range(0, GameConstants.PlayerCount)
            .Select(i => (IPlayerAgent)new RandomBot((ulong)i + 1)).ToArray(), new RngChance(1));
        var legal = new List<GameAction>();
        Rules.GetLegalActions(runner.State, p.Seat!.Value, legal);
        var grade = new DecisionCoach(Bundled).Grade(PlayerView.From(runner.State, p.Seat.Value, runner.Log), legal, Names);

        Assert.Equal(legal.Count, grade.Moves.Count);
        Assert.Equal(Enumerable.Range(1, legal.Count), grade.Moves.Select(m => m.Rank));
        Assert.Equal(100, grade.Moves[0].Rating, 3);
        Assert.All(grade.Moves, m => Assert.InRange(m.Rating, 0, 100));
        Assert.All(grade.Moves, m => Assert.NotEmpty(m.Reasons));
        Assert.All(grade.Moves, m => Assert.False(string.IsNullOrWhiteSpace(m.Description)));
        Assert.All(legal, a => Assert.NotNull(grade.Of(a)));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
