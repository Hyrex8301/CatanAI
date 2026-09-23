using Catan.AI;
using Catan.Core;

namespace Catan.Tests;

public class RandomBotTests
{
    private static GameRunner Game(ulong seed, bool pure) =>
        new(new GameState(BoardGenerator.Balanced(new Rng(seed))),
            Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 7 + (ulong)i, pure)).ToArray(),
            new RngChance(seed + 500), validate: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaysValidGamesToTheEnd(bool pure)
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var runner = Game(seed, pure);
            await runner.RunAsync(); // throws on any illegal choice or rules violation
            Assert.Equal(Phase.GameOver, runner.State.Phase);
        }
    }

    /// <summary>Counts, over Main-phase decisions where both a build and something else were legal, how often the bot built.</summary>
    private sealed class Watcher : IPlayerAgent
    {
        private readonly IPlayerAgent _bot;
        public int Chances, Builds;

        public Watcher(IPlayerAgent bot) => _bot = bot;

        public string Name => _bot.Name;

        public async Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
        {
            var choice = await _bot.DecideAsync(view, legal, ct);
            if (view.Phase == Phase.Main && legal.Any(IsBuild) && legal.Any(a => !IsBuild(a)))
            {
                Chances++;
                if (IsBuild(choice))
                    Builds++;
            }
            return choice;
        }

        public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
            _bot.RespondAsync(view, legal, ct);

        private static bool IsBuild(GameAction a) => a.Type is ActionType.BuildRoad or ActionType.BuildSettlement or ActionType.BuildCity;
    }

    [Theory]
    [InlineData(false, 0.70, 0.82)] // 80% builds, less the ~5% of Main decisions spent on trade offers
    [InlineData(true, 0.05, 0.70)]  // pure random: just the builds' share of the legal list
    public async Task BuildRateFollowsTheWeights(bool pure, double min, double max)
    {
        int chances = 0, builds = 0;
        for (ulong seed = 0; seed < 10; seed++)
        {
            var watchers = Enumerable.Range(0, 4).Select(i => new Watcher(new RandomBot(seed * 7 + (ulong)i, pure))).ToArray();
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))), watchers, new RngChance(seed + 500));
            await runner.RunAsync();
            chances += watchers.Sum(w => w.Chances);
            builds += watchers.Sum(w => w.Builds);
        }
        double rate = (double)builds / chances;
        Assert.InRange(rate, min, max);
    }

    [Fact]
    public async Task ItTradesAndAnswersTrades()
    {
        int offers = 0, answers = 0, done = 0;
        for (ulong seed = 0; seed < 10; seed++)
        {
            var runner = Game(seed, pure: false);
            await runner.RunAsync();
            offers += runner.Actions.Count(a => a.Type == ActionType.OfferTrade);
            answers += runner.Actions.Count(a => a.Type is ActionType.AcceptOffer or ActionType.DeclineOffer or ActionType.CounterOffer && a.Seat != runner.State.CurrentPlayer);
            done += runner.Log.All.Count(e => e is TradeDone);
        }
        Assert.True(offers > 20, $"{offers} offers");
        Assert.True(answers > offers, $"{answers} answers to {offers} offers");
        Assert.True(done > 5, $"{done} trades");
    }
}
