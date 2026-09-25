using Catan.AI;
using Catan.Core;

namespace Catan.Tests.AI;

public class TradingTests
{
    private static readonly Evaluator Eval = new(new BotWeights());

    private static GameAction? Settle(GameState s, int seat = 0)
    {
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, seat, legal);
        var view = PlayerView.From(s, seat);
        return Trading.SettleOpenTrades(view, new HandTracker(seat), Eval, legal, new Rng(1));
    }

    private static GameState MainWithHands() =>
        new StateBuilder(TestBoards.Standard)
            .Phase(Phase.Main, current: 0)
            .Hand(0, brick: 2, lumber: 2)
            .Hand(1, wool: 2, grain: 2)
            .Build();

    [Fact]
    public void ACounterToUsIsAlwaysAnswered()
    {
        var s = MainWithHands();
        var brick = ResourceSet.Of(Resource.Brick);
        var grain = ResourceSet.Of(Resource.Grain);
        s.Offers[0] = new TradeOffer(true, 0, -1, brick, grain, 0).WithResponse(1, TradeOffer.Countered);
        s.Offers[GameConstants.MaxOpenOffers] = new TradeOffer(true, 1, 0, grain, brick, 0);

        var answer = Settle(s);
        Assert.NotNull(answer);
        Assert.Equal(GameConstants.MaxOpenOffers, answer.Value.Target);
        Assert.Contains(answer.Value.Type, new[] { ActionType.AcceptOffer, ActionType.DeclineOffer });
        Assert.True(Rules.IsLegal(s, answer.Value, out _));
    }

    [Fact]
    public void AnOfferNobodyAcceptedIsWithdrawn()
    {
        var s = MainWithHands();
        s.Offers[0] = new TradeOffer(true, 0, -1, ResourceSet.Of(Resource.Brick), ResourceSet.Of(Resource.Grain), 0)
            .WithResponse(1, TradeOffer.Declined).WithResponse(2, TradeOffer.Declined).WithResponse(3, TradeOffer.Declined);
        Assert.Equal(new GameAction(ActionType.CancelOffer, 0, 0), Settle(s));
    }

    [Fact]
    public void NothingOpenMeansNothingToSettle() => Assert.Null(Settle(MainWithHands()));

    [Fact]
    public void AnOfferIsNotMadeTwiceInOneTurn()
    {
        // Play SmartBot games and check no seat repeats an offer (same give and get) within one turn.
        var weights = new BotWeights();
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(weights, SmartBotSettings.Play, seed * 10 + (ulong)i)).ToArray(),
                new RngChance(seed));
            runner.RunAsync().GetAwaiter().GetResult();
            var thisTurn = new HashSet<(int, ResourceSet, ResourceSet)>();
            foreach (var e in runner.Log.For(0))
            {
                if (e is TurnEnded)
                    thisTurn.Clear();
                else if (e is TradeOffered o)
                    Assert.True(thisTurn.Add((o.Seat, o.Give, o.Get)), $"seed {seed}: seat {o.Seat} offered {o.Give} for {o.Get} twice in a turn");
            }
        }
    }
}
