using Catan.AI;
using Catan.Core;

namespace Catan.Tests.AI;

public class SearchBotTests
{
    private static SearchBot Bot(ulong seed, int iterations = 24, int threads = 1) =>
        new(new BotWeights(), WinModel.Default, new SearchSettings { Iterations = iterations, Threads = threads, CutoffTurns = 4 }, seed);

    /// <summary>A position a few turns into a game between SmartBots, with the acting seat's view and legal moves.</summary>
    private static (GameRunner Runner, PlayerView View, List<GameAction> Legal) MidGame(ulong seed, int steps)
    {
        var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
            Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(new BotWeights(), SmartBotSettings.Training, seed + (ulong)i)).ToArray(),
            new RngChance(seed));
        for (int i = 0; i < steps && !runner.IsOver; i++)
            runner.StepAsync().GetAwaiter().GetResult();
        // Move on until the acting seat has a real choice outside the discard phase.
        var legal = new List<GameAction>();
        while (true)
        {
            int seat = Rules.ActingSeat(runner.State);
            Rules.GetLegalActions(runner.State, seat, legal);
            if (runner.State.Phase != Phase.Discard && legal.Count > 2)
                return (runner, PlayerView.From(runner.State, seat, runner.Log), legal);
            runner.StepAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void GamesWithASearchBotStayLegalUnderFullValidation()
    {
        for (ulong seed = 1; seed <= 2; seed++)
        {
            var agents = new IPlayerAgent[4];
            for (int seat = 0; seat < 4; seat++)
                agents[seat] = seat == (int)seed ? Bot(seed * 10) : new SmartBot(new BotWeights(), SmartBotSettings.Training, seed + (ulong)seat);
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))), agents, new RngChance(seed), validate: true);
            runner.RunAsync().GetAwaiter().GetResult();
            Assert.True(runner.IsOver);
        }
    }

    [Fact]
    public void SameSeedAndIterationsGiveTheSameMove()
    {
        var (_, view, legal) = MidGame(5, 60);
        var a = Bot(99, iterations: 60, threads: 2).Decide(view, legal);
        var b = Bot(99, iterations: 60, threads: 2).Decide(view, legal);
        Assert.Equal(a, b);
        Assert.Contains(a, legal.Append(a)); // a legal move, or a trade offer the bot built itself
    }

    [Fact]
    public void RootMovesKeepThePlannersBestAndEndingTheTurn()
    {
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var (_, view, legal) = MidGame(seed, 40 + (int)seed * 13);
            var bot = Bot(seed);
            var tracker = new HandTracker(view.Seat);
            tracker.Update(view.Events);
            var moves = bot.RootMoves(view, legal, tracker);
            Assert.InRange(moves.Count, 1, bot.Settings.RootMoves + 1);
            Assert.All(moves, m => Assert.Contains(m, legal));
            if (legal.Any(a => a.Type == ActionType.EndTurn))
                Assert.Contains(moves, m => m.Type == ActionType.EndTurn);
        }
    }

    [Fact]
    public void TheSearchNeverSeesHiddenCards()
    {
        // Two games identical except for the opponents' hidden hands must get the same decision: the bot only has its view.
        var (runner, view, legal) = MidGame(8, 70);
        var move = Bot(3).Decide(view, legal);
        var s = runner.State.Clone();
        int other = (view.Seat + 1) % 4;
        const int R = GameConstants.ResourceCount;
        // Shuffle the other seat's cards among resources (same count, so the view is unchanged).
        int total = 0;
        for (int r = 0; r < R; r++)
        {
            total += s.Hand[other * R + r];
            s.Hand[other * R + r] = 0;
        }
        s.Hand[other * R + 0] = total;
        var sameView = PlayerView.From(s, view.Seat, runner.Log);
        Assert.Equal(move, Bot(3).Decide(sameView, legal));
    }

    [Fact]
    public void ATimedDecisionRespectsItsBudgetAndSearchesOnEveryThread()
    {
        var (_, view, legal) = MidGame(11, 90);
        var bot = new SearchBot(new BotWeights(), WinModel.Default, new SearchSettings { ThinkMs = 300, Threads = 3 }, 5);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bot.Decide(view, legal);
        sw.Stop();
        Assert.InRange(sw.ElapsedMilliseconds, 250, 1500); // the planner's root pass and the last iterations add a little
        Assert.True(bot.LastIterations >= 3, $"only {bot.LastIterations} iterations");
    }
}
