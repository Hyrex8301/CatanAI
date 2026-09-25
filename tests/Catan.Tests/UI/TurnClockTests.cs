using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class TurnClockTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerView ViewAt(Phase phase, int current = 0, int turn = 5)
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(phase, current).Build();
        s.TurnNumber = turn;
        return PlayerView.From(s, 0);
    }

    [Fact]
    public void FiveSecondsToRollThenAMinuteForTheTurn()
    {
        var clock = new TurnClock();
        clock.Update(ViewAt(Phase.PreRoll), T0);
        Assert.Equal(5, clock.Remaining(T0));
        Assert.True(clock.Expired(T0.AddSeconds(5)));

        clock.OnAction(new GameAction(ActionType.RollDice, 0), 0, T0.AddSeconds(3));
        Assert.Equal(60, clock.Remaining(T0.AddSeconds(3)));
    }

    [Fact]
    public void EachMoveAddsTenSecondsButAnswersAndEndingDoNot()
    {
        var clock = new TurnClock();
        clock.Update(ViewAt(Phase.Main), T0);
        Assert.Equal(60, clock.Remaining(T0));

        clock.OnAction(new GameAction(ActionType.BuildRoad, 0, 3), 0, T0.AddSeconds(20));
        Assert.Equal(50, clock.Remaining(T0.AddSeconds(20)));
        clock.OnAction(new GameAction(ActionType.DeclineOffer, 2, 0), 0, T0.AddSeconds(20)); // someone else answering
        Assert.Equal(50, clock.Remaining(T0.AddSeconds(20)));
        clock.OnAction(new GameAction(ActionType.EndTurn, 0), 0, T0.AddSeconds(20));
        Assert.Equal(50, clock.Remaining(T0.AddSeconds(20)));
        clock.OnAction(new GameAction(ActionType.PlayKnight, 0), 0, T0.AddSeconds(20));
        Assert.Equal(80, clock.Remaining(T0.AddSeconds(20))); // a knight adds 30 (time to talk)
    }

    [Fact]
    public void ANewTurnRestartsTheClockAndTheSameTurnDoesNot()
    {
        var clock = new TurnClock();
        clock.Update(ViewAt(Phase.PreRoll, current: 0, turn: 5), T0);
        clock.Update(ViewAt(Phase.PreRoll, current: 0, turn: 5), T0.AddSeconds(4));
        Assert.Equal(1, clock.Remaining(T0.AddSeconds(4)));

        clock.Update(ViewAt(Phase.PreRoll, current: 1, turn: 6), T0.AddSeconds(30));
        Assert.Equal(5, clock.Remaining(T0.AddSeconds(30)));
        Assert.Equal(0, clock.Remaining(T0.AddSeconds(40)));
    }
}
