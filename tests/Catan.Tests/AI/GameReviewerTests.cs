using Catan.AI;
using Catan.Core;

namespace Catan.Tests.AI;

public class GameReviewerTests
{
    private static readonly string[] Names = { "Red", "Blue", "Orange", "White" };
    private static readonly BotWeights Bundled = BotWeights.Load(Path.Combine(RepoRoot(), "game", "bots", "best.json"));

    /// <summary>Seat 0's decisions in a bot game: the view it had, its options, and what the bot did.</summary>
    private static List<Decision> BotDecisions(ulong seed)
    {
        var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
            Enumerable.Range(0, GameConstants.PlayerCount).Select(i => (IPlayerAgent)new SmartBot(Bundled, SmartBotSettings.Training, seed + (ulong)i)).ToArray(),
            new RngChance(seed));
        var decisions = new List<Decision>();
        var legal = new List<GameAction>();
        while (!runner.IsOver)
        {
            var s = runner.State;
            PlayerView? view = null;
            List<GameAction>? options = null;
            if (Rules.ActingSeat(s) == 0)
            {
                Rules.GetLegalActions(s, 0, legal);
                (view, options) = (PlayerView.From(s, 0, runner.Log), legal.ToList());
            }
            int before = runner.Actions.Count;
            runner.StepAsync().GetAwaiter().GetResult();
            if (view is not null && runner.Actions.Count > before && runner.Actions[before] is { Seat: 0 } move)
                decisions.Add(new Decision(view, options!, move));
        }
        return decisions;
    }

    [Fact]
    public void EveryRealChoiceIsGradedAndForcedOrUnlistedMovesAreSkipped()
    {
        var decisions = BotDecisions(3);
        var review = GameReviewer.Review(Bundled, decisions, Names, samples: 4);
        int expected = decisions.Count(GameReviewer.IsGradable);
        Assert.True(expected > 10);
        Assert.Equal(expected, review.Plays.Count);
        Assert.All(review.Plays, p =>
        {
            Assert.InRange(p.Yours.Rating, 0, 100);
            Assert.True(p.CostVp >= 0);
            Assert.True(p.Round >= 1);
            Assert.Equal(1, p.Best.Rank);
        });
        Assert.InRange(review.Score, 0, 100);
    }

    [Fact]
    public void WorstMovesCostPointsAndShowAsMistakes()
    {
        var decisions = BotDecisions(4).Where(GameReviewer.IsGradable).Take(25).ToList();
        var good = GameReviewer.Review(Bundled, decisions, Names, samples: 4);
        // The same moments, answered with the move the coach ranks last.
        var bad = decisions.Select((d, i) =>
        {
            var grade = new DecisionCoach(Bundled).Grade(d.View, d.Legal, Names, 4, seed: (ulong)i + 1);
            return d with { Move = grade.Moves[^1].Move };
        }).ToList();
        var worse = GameReviewer.Review(Bundled, bad, Names, samples: 4);

        Assert.True(worse.Score < good.Score);
        Assert.True(worse.TotalCostVp > good.TotalCostVp);
        Assert.NotEmpty(worse.Mistakes);
        Assert.Equal(worse.Mistakes.OrderByDescending(p => p.CostVp), worse.Mistakes); // worst first
        Assert.All(worse.Mistakes, p => Assert.True(p.CostVp >= GameReview.MistakeVp));
    }

    [Fact]
    public void AGameWithNoDecisionsHasAnEmptyReview()
    {
        var review = GameReviewer.Review(Bundled, Array.Empty<Decision>(), Names);
        Assert.Empty(review.Plays);
        Assert.Equal(0, review.Score);
        Assert.Empty(review.Mistakes);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
