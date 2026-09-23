using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class HudModelTests
{
    private static readonly SeatColor[] Colors = { SeatColor.Blue, SeatColor.Red, SeatColor.White, SeatColor.Orange };

    [Fact]
    public void HandGroupsResourcesThenDevCardsAndMarksFreshOnes()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 0, hasRolled: true)
            .Hand(0, brick: 2, grain: 1, ore: 3).DevCards(0, knight: 2, monopoly: 1).BoughtThisTurn(DevCardType.Knight).Build();
        var hand = HudModel.Hand(PlayerView.From(s, 0));

        Assert.Equal(new[]
        {
            new CardStack(false, (int)Resource.Brick, 2), new CardStack(false, (int)Resource.Grain, 1), new CardStack(false, (int)Resource.Ore, 3),
            new CardStack(true, (int)DevCardType.Knight, 2, Fresh: 1), new CardStack(true, (int)DevCardType.Monopoly, 1),
        }, hand);
        Assert.Equal("Brick", hand[0].Name);
        Assert.Equal("Knight", hand[3].Name);
    }

    [Fact]
    public void EmptyHandHasNoStacks() =>
        Assert.Empty(HudModel.Hand(PlayerView.From(new GameState(TestBoards.Standard), 0)));

    [Fact]
    public void OnlyYourOwnHiddenVictoryPointsAreCounted()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 1)
            .Settlement(0, 0).Settlement(1, 10).DevCards(0, victoryPoint: 1).DevCards(1, victoryPoint: 2).Build();

        var mine = HudModel.Seat(PlayerView.From(s, 0), 0, Colors);
        Assert.True(mine.IsYou);
        Assert.Equal("You", mine.Name);
        Assert.Equal(2, mine.Vp);
        Assert.Equal(1, mine.HiddenVp);

        var theirs = HudModel.Seat(PlayerView.From(s, 0), 1, Colors);
        Assert.Equal("Red", theirs.Name);
        Assert.Equal(1, theirs.Vp); // their 2 VP cards stay hidden
        Assert.Equal(0, theirs.HiddenVp);
        Assert.Equal(2, theirs.DevCards);
        Assert.True(theirs.IsCurrent);
        Assert.True(theirs.IsActing);
    }

    [Fact]
    public void SeatSummaryMatchesTheViewThroughRandomGames()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 10 + (ulong)i)).ToArray(), new RngChance(seed));
            for (int step = 0; step < 400 && !runner.IsOver; step++)
            {
                runner.StepAsync().GetAwaiter().GetResult();
                if (step % 20 != 0)
                    continue;
                int viewer = (int)(seed % 4);
                var v = PlayerView.From(runner.State, viewer, runner.Log);
                Assert.Equal(v.Hand.Sum() + v.DevHand.Sum(), HudModel.Hand(v).Sum(c => c.Count));
                for (int seat = 0; seat < 4; seat++)
                {
                    var p = HudModel.Seat(v, seat, Colors);
                    Assert.Equal(seat == viewer ? runner.State.TotalVP(seat) : v.PublicVP[seat], p.Vp);
                    Assert.Equal(v.HandSizes[seat], p.Cards);
                    Assert.Equal(v.LongestRoadOwner == seat, p.LongestRoad);
                    Assert.Equal(v.LargestArmyOwner == seat, p.LargestArmy);
                }
            }
        }
    }

    [Fact]
    public void OpponentsFollowTurnOrderAfterYou()
    {
        Assert.Equal(new[] { 1, 2, 3 }, HudModel.Opponents(0));
        Assert.Equal(new[] { 3, 0, 1 }, HudModel.Opponents(2));
    }
}
