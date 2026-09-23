using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class LogTextTests
{
    private static string Name(int seat) => seat switch { 0 => "You", 1 => "Red", 2 => "Blue", _ => "White" };

    private static string Plain(GameEvent e) => LogText.Line(e).PlainText(Name);

    [Fact]
    public void LinesUseIconsForCardsAndDice()
    {
        Assert.Equal("Red rolled [3][5]", Plain(new DiceRolled(1, 3, 5)));
        Assert.Equal("You got [wool][wool][ore]", Plain(new ResourcesProduced(0, new ResourceSet(0, 0, 2, 0, 1))));
        Assert.Equal("Blue built a road [road]", Plain(new Built(2, PieceType.Road, 7)));
        Assert.Equal("Red gave [brick] and got [grain] from Blue", Plain(new TradeDone(1, 2, ResourceSet.Of(Resource.Brick), ResourceSet.Of(Resource.Grain))));
        Assert.Equal("Red offered [lumber] for [ore][ore]", Plain(new TradeOffered(1, 0, ResourceSet.Of(Resource.Lumber), ResourceSet.Of(Resource.Ore, 2))));
        Assert.Equal("You took [grain][grain][grain] from Blue", Plain(new MonopolyTaken(0, 2, (int)Resource.Grain, 3)));
        Assert.Equal("White took Largest Army", Plain(new AwardChanged(Award.LargestArmy, -1, 3)));
    }

    [Fact]
    public void HiddenCardsStayFaceDown()
    {
        Assert.Equal("Red stole [card] from Blue", Plain(new CardStolen(1, 2, -1)));
        Assert.Equal("Red stole [wool] from You", Plain(new CardStolen(1, 0, (int)Resource.Wool)));
        Assert.Equal("Blue bought [dev card]", Plain(new DevCardBought(2, null)));
        Assert.Equal("You bought [Knight]", Plain(new DevCardBought(0, DevCardType.Knight)));
    }

    [Fact]
    public void TurnEndsBecomeDividersAndLinesLeadWithTheirPlayer()
    {
        Assert.True(LogText.Line(new TurnEnded(2)).Divider);
        Assert.Equal(1, LogText.Line(new DiceRolled(1, 1, 1)).Seat);
        Assert.Equal(1, LogText.Line(new CardStolen(1, 2, -1)).Seat);
    }

    [Fact]
    public void EveryEventInRealGamesGetsALineWithoutLeakingHiddenCards()
    {
        var kinds = new HashSet<Type>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 3 + (ulong)i)).ToArray(), new RngChance(seed));
            runner.RunAsync().GetAwaiter().GetResult();
            foreach (var e in runner.Log.For(0))
            {
                kinds.Add(e.GetType());
                var line = LogText.Line(e);
                Assert.DoesNotContain(line.Parts, p => p.Kind == LogPartKind.Text && p.Text.Contains("{ "));  // no record ToString fallbacks
                if (e is CardStolen { Resource: < 0 })
                    Assert.Contains(line.Parts, p => p.Kind == LogPartKind.CardBack);
                if (e is DevCardBought { Type: null })
                    Assert.Contains(line.Parts, p => p.Kind == LogPartKind.DevBack);
            }
        }
        Assert.Contains(typeof(CardStolen), kinds);
        Assert.Contains(typeof(TradeOffered), kinds);
    }
}
