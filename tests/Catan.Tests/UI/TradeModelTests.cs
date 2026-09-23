using Catan.AI;
using Catan.Core;
using Catan.UI;
using static Catan.Core.Topology;

namespace Catan.Tests.UI;

public class TradeModelTests
{
    // TestBoards.Standard: harbor spot 0 (generic 3:1) touches this vertex, spot 1 (brick 2:1) touches the other.
    private static readonly int GenericHarbor = Vertex(2, 0, Corner.NE);
    private static readonly int BrickHarbor = Vertex(1, 1, Corner.S);

    private static GameState Main(StateBuilder b) => b.Phase(Phase.Main, current: 0, hasRolled: true).Build();

    [Fact]
    public void RatiosMatchTheEngineThroughRandomGames()
    {
        Span<int> expected = stackalloc int[5];
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed + (ulong)i * 7)).ToArray(), new RngChance(seed));
            for (int step = 0; step < 500 && !runner.IsOver; step++)
            {
                runner.StepAsync().GetAwaiter().GetResult();
                if (step % 25 != 0)
                    continue;
                for (int seat = 0; seat < 4; seat++)
                {
                    Rules.TradeRatios(runner.State, seat, expected);
                    Assert.Equal(expected.ToArray(), TradeModel.Ratios(PlayerView.From(runner.State, seat)));
                }
            }
        }
    }

    [Fact]
    public void HarborsLowerTheRates()
    {
        var s = Main(new StateBuilder(TestBoards.Standard).Settlement(0, GenericHarbor).Settlement(0, BrickHarbor));
        Assert.Equal(new[] { 2, 3, 3, 3, 3 }, TradeModel.Ratios(PlayerView.From(s, 0)));
        Assert.Equal(new[] { 4, 4, 4, 4, 4 }, TradeModel.Ratios(PlayerView.From(s, 1)));
    }

    [Fact]
    public void SeveralLotsBecomeLegalOneLotTradesInOrder()
    {
        var s = Main(new StateBuilder(TestBoards.Standard).Settlement(0, GenericHarbor).Hand(0, brick: 6, wool: 3, grain: 1));
        var v = PlayerView.From(s, 0);
        Assert.True(TradeModel.TryBankTrades(v, new ResourceSet(6, 0, 3, 0, 0), new ResourceSet(0, 0, 0, 1, 2), out var trades, out _));
        Assert.Equal(3, trades.Count);
        foreach (var trade in trades)
        {
            Assert.True(Rules.IsLegal(s, trade, out string reason), reason);
            Rules.Apply(s, trade, new ScriptedChance());
        }
        Assert.Equal(new[] { 0, 0, 0, 2, 2 }, s.HandOf(0).ToArray());
    }

    [Theory]
    [InlineData(5, 0, 0, 0, 0, 0, 0, 0, 0, 1, "Give brick in groups of 4 (your rate is 4:1)")]
    [InlineData(8, 0, 0, 0, 0, 0, 0, 0, 0, 1, "That pays for 2 cards: pick 1 more to get")]
    [InlineData(4, 0, 0, 0, 0, 0, 0, 0, 1, 1, "That pays for 1 card: get fewer, or give more")]
    [InlineData(4, 0, 0, 0, 0, 1, 0, 0, 0, 0, "You can't give and get brick at once")]
    [InlineData(0, 4, 0, 0, 0, 0, 0, 0, 0, 1, "You don't have those cards")]
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "Pick what you give and what you get")]
    public void BadPicksSayWhy(int gb, int gl, int gw, int gg, int go, int wb, int wl, int ww, int wg, int wo, string expected)
    {
        var s = Main(new StateBuilder(TestBoards.Standard).Hand(0, brick: 9));
        Assert.False(TradeModel.TryBankTrades(PlayerView.From(s, 0), new ResourceSet(gb, gl, gw, gg, go), new ResourceSet(wb, wl, ww, wg, wo), out _, out string reason));
        Assert.Equal(expected, reason);
    }

    [Fact]
    public void AnswersReadTheOfferResponses()
    {
        var offer = new TradeOffer(true, 0, -1, ResourceSet.Of(Resource.Brick), ResourceSet.Of(Resource.Ore), 0)
            .WithResponse(1, TradeOffer.Accepted).WithResponse(2, TradeOffer.Declined).WithResponse(3, TradeOffer.Countered);
        Assert.Equal(OfferAnswer.Accepted, TradeModel.Answer(offer, 1));
        Assert.Equal(OfferAnswer.Declined, TradeModel.Answer(offer, 2));
        Assert.Equal(OfferAnswer.Countered, TradeModel.Answer(offer, 3));
        Assert.Equal(OfferAnswer.Waiting, TradeModel.Answer(offer with { Responses = 0 }, 1));
    }
}
