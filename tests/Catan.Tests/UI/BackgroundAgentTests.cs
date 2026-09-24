using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class BackgroundAgentTests
{
    /// <summary>Records which thread it decided on, and can be held until released.</summary>
    private sealed class SlowBot : IPlayerAgent
    {
        public readonly ManualResetEventSlim Release = new();
        public int DecidedOnThread;

        public string Name => "Slow";

        public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
        {
            DecidedOnThread = Environment.CurrentManagedThreadId;
            Release.Wait(ct);
            return Task.FromResult(legal[0]);
        }
    }

    [Fact]
    public async Task ThinksOffTheCallersThreadAndReportsWhileBusy()
    {
        var slow = new SlowBot();
        var agent = new BackgroundAgent(slow);
        var view = PlayerView.From(new GameState(TestBoards.Standard), 0);
        var legal = new[] { new GameAction(ActionType.EndTurn, 0) };

        var task = agent.DecideAsync(view, legal, default);
        Assert.False(task.IsCompleted); // the caller wasn't blocked while the bot thinks
        await WaitUntil(() => agent.Thinking);
        slow.Release.Set();
        Assert.Equal(legal[0], await task);
        Assert.False(agent.Thinking);
        Assert.NotEqual(Environment.CurrentManagedThreadId, slow.DecidedOnThread);
    }

    [Fact]
    public async Task GamesWithBackgroundBotsPlayTheSameAsWithout()
    {
        async Task<ulong> Play(bool background)
        {
            var agents = Enumerable.Range(0, 4)
                .Select(i => (IPlayerAgent)new SmartBot(new BotWeights(), SmartBotSettings.Training, (ulong)i + 1))
                .Select(a => background ? new BackgroundAgent(a) : a).ToArray();
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(5))), agents, new RngChance(5));
            await runner.RunAsync();
            return runner.State.ComputeHash();
        }
        Assert.Equal(await Play(false), await Play(true));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }
}
