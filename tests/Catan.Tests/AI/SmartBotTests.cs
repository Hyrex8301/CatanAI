using Catan.AI;
using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests.AI;

public class SmartBotTests
{
    private static SmartBot Bot(ulong seed = 1, SmartBotSettings? settings = null) =>
        new(new BotWeights(), settings ?? SmartBotSettings.Training, seed);

    private static GameRunner Game(ulong seed, Func<int, IPlayerAgent> agent) =>
        new(new GameState(BoardGenerator.Balanced(new Rng(seed))), Enumerable.Range(0, 4).Select(agent).ToArray(), new RngChance(seed + 7), validate: true);

    [Fact]
    public async Task PlaysValidGamesAndTrades()
    {
        int offers = 0, trades = 0, counterAnswers = 0;
        for (ulong seed = 0; seed < 12; seed++)
        {
            var runner = Game(seed, seat => Bot(seed * 10 + (ulong)seat));
            await runner.RunAsync(); // validate: throws on any illegal move or rules violation
            Assert.True(runner.State.Winner >= 0, $"seed {seed}: no winner");
            offers += runner.Actions.Count(a => a.Type == ActionType.OfferTrade);
            trades += runner.Log.All.Count(e => e is TradeDone);
            counterAnswers += runner.Actions.Count(a => a.Type is ActionType.AcceptOffer or ActionType.DeclineOffer);
        }
        Assert.True(offers > 0 && counterAnswers > 0, $"{offers} offers, {counterAnswers} answers");
        Assert.True(trades > 0, "no player trades completed");
    }

    [Fact]
    public async Task BeatsRandomBots()
    {
        int wins = 0;
        for (ulong seed = 0; seed < 20; seed++)
        {
            int smartSeat = (int)(seed % 4);
            var runner = Game(seed, seat => seat == smartSeat ? Bot(seed) : new RandomBot(seed * 10 + (ulong)seat));
            await runner.RunAsync();
            if (runner.State.Winner == smartSeat)
                wins++;
        }
        Assert.True(wins >= 18, $"won only {wins} of 20 against RandomBots");
    }

    [Fact]
    public async Task SameSeedsGiveTheSameGame()
    {
        async Task<ulong> Play()
        {
            var runner = Game(5, seat => Bot(100 + (ulong)seat));
            await runner.RunAsync();
            return runner.State.ComputeHash();
        }
        Assert.Equal(await Play(), await Play());
    }

    [Fact]
    public async Task BuildsTheCityItCanAfford()
    {
        // Seat 0 holds exactly a city's cards and a settlement on a strong hex: upgrading is clearly best.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, -2, Corner.N)).Road(0, Edge(0, -2, Side.NE))
            .Hand(0, grain: 2, ore: 3)
            .Phase(Phase.Main)
            .Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        var choice = await Bot().DecideAsync(PlayerView.From(s, 0), legal, default);
        Assert.Equal(new GameAction(ActionType.BuildCity, 0, Vertex(0, -2, Corner.N)), choice);
    }

    [Fact]
    public async Task PlansABankTradeThenABuild()
    {
        // Seat 0 has 4 ore, 1 lumber, 1 wool, 1 grain: trading 4 ore for a brick completes a settlement it can place.
        // Only a plan two moves deep sees that; the first move must be the trade.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E))
            .Hand(0, lumber: 1, wool: 1, grain: 1, ore: 4)
            .Phase(Phase.Main)
            .Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        var choice = await Bot(settings: SmartBotSettings.Play with { Trades = false }).DecideAsync(PlayerView.From(s, 0), legal, default);
        Assert.Equal(ActionType.BankTrade, choice.Type);
        Assert.Equal(ResourceSet.Of(Resource.Brick), choice.Get);
    }

    [Fact]
    public async Task DiscardsLegallyKeepingWhatItNeeds()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Hand(0, grain: 2, ore: 3, wool: 5)
            .Phase(Phase.PreRoll)
            .Build();
        Rules.ApplyChecked(s, new GameAction(ActionType.RollDice, 0), new ScriptedChance().Roll(7));
        var view = PlayerView.From(s, 0);
        var discard = await Bot().DecideAsync(view, Array.Empty<GameAction>(), default);
        Assert.True(Rules.IsLegal(s, discard, out string reason), reason);
        Assert.Equal(5, discard.Give.Total);
        Assert.True(discard.Give[(int)Resource.Wool] >= 3, $"discarded {discard.Give}"); // keeps the city cards over surplus wool
    }
}
