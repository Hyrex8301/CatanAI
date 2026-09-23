using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class GameOverModelTests
{
    [Fact]
    public void BreakdownAddsUpAndTheWinnerLeads()
    {
        int finished = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 11 + (ulong)i)).ToArray(), new RngChance(seed));
            runner.RunAsync().GetAwaiter().GetResult();
            var s = runner.State;
            var summary = GameOverSummary.From(s, runner.Log.For(0));
            foreach (var f in summary.Standings)
            {
                Assert.Equal(s.TotalVP(f.Seat), f.Total);
                Assert.Equal(f.Total, f.Points); // every point is accounted for
            }
            Assert.Equal(summary.Standings.OrderByDescending(f => f.Total).Select(f => f.Total), summary.Standings.Select(f => f.Total));
            if (s.Winner >= 0)
            {
                Assert.Equal(s.Winner, summary.Standings[0].Seat);
                finished++;
            }
            Assert.Equal(runner.Log.For(0).OfType<DiceRolled>().Count(), summary.Rolls.Sum());
            Assert.Equal(0, summary.Rolls[0] + summary.Rolls[1]);
        }
        Assert.True(finished > 6);
    }

    [Fact]
    public void RefusesAGameInProgress() =>
        Assert.Throws<InvalidOperationException>(() => GameOverSummary.From(new GameState(TestBoards.Standard), Array.Empty<GameEvent>()));
}
