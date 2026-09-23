using Catan.UI;

namespace Catan.Tests.UI;

public class GameSetupTests
{
    [Fact]
    public void SameSeedGivesTheSameSetup()
    {
        var a = GameSetup.Create(42);
        var b = GameSetup.Create(42);
        Assert.Equal(a.HumanSeat, b.HumanSeat);
        Assert.Equal(a.Colors, b.Colors);
        Assert.Equal((a.BoardSeed, a.ChanceSeed, a.BotSeed), (b.BoardSeed, b.ChanceSeed, b.BotSeed));
    }

    [Fact]
    public void ColorsArePermutedAndSeatsVary()
    {
        var seats = new HashSet<int>();
        var humanColors = new HashSet<SeatColor>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            var setup = GameSetup.Create(seed);
            Assert.Equal(4, setup.Colors.Distinct().Count());
            Assert.InRange(setup.HumanSeat, 0, 3);
            Assert.Equal(setup.Colors[setup.HumanSeat], setup.HumanColor);
            Assert.NotEqual(setup.BoardSeed, setup.ChanceSeed);
            seats.Add(setup.HumanSeat);
            humanColors.Add(setup.HumanColor);
        }
        Assert.Equal(4, seats.Count);
        Assert.Equal(4, humanColors.Count);
    }

    [Fact]
    public void OptionsMakeRealGameSettings()
    {
        var settings = new GameOptions { VpToWin = 12, FriendlyRobber = true }.ToSettings();
        Assert.Equal(12, settings.VpToWin);
        Assert.True(settings.FriendlyRobber);
        Assert.Equal(10, settings.MaxOffersPerTurn);
        Assert.True(settings.MaxTurns >= 10_000);
    }
}
