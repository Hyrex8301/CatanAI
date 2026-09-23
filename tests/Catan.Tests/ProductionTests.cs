using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

/// <summary>
/// Production on TestBoards.Standard. Hex 0 at (0,-2) is Hills 6 (brick); hex 16 at (-2,2) is Pasture 6 (wool);
/// hexes 2 at (2,-2) and 18 at (0,2) are Forest 8 (lumber).
/// </summary>
public class ProductionTests
{
    private static readonly int HillsSixN = Vertex(0, -2, Corner.N);
    private static readonly int HillsSixS = Vertex(0, -2, Corner.S);
    private static readonly int PastureSix = Vertex(-2, 2, Corner.S);

    private static GameState Roll(GameState s, int total, List<GameEvent>? events = null)
    {
        Rules.ApplyChecked(s, new GameAction(ActionType.RollDice, s.CurrentPlayer), new ScriptedChance().Roll(total), events);
        Assert.Empty(StateValidator.Check(s));
        return s;
    }

    private static StateBuilder OnHillsSix() => new StateBuilder(TestBoards.Standard)
        .Settlement(0, HillsSixN)
        .City(1, HillsSixS)
        .Phase(Phase.PreRoll, current: 0);

    [Fact]
    public void RollingMovesToMainAndRecordsTheRoll()
    {
        var s = OnHillsSix().Build();
        var events = new List<GameEvent>();
        Rules.ApplyChecked(s, new GameAction(ActionType.RollDice, 0), new ScriptedChance().Dice(2, 3), events);

        Assert.Equal(Phase.Main, s.Phase);
        Assert.True(s.HasRolled);
        Assert.Equal(5, s.LastRoll);
        Assert.Equal(new DiceRolled(0, 2, 3), events[0]);
    }

    [Fact]
    public void OnlyTheCurrentSeatCanRollAndOnlyRollingIsLegalBeforeIt()
    {
        var s = OnHillsSix().Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(new[] { new GameAction(ActionType.RollDice, 0) }, legal);

        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.RollDice, 1), out _));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.EndTurn, 0), out string reason));
        Assert.Contains("Roll the dice first", reason);
    }

    [Fact]
    public void SettlementPaysOneCityPaysTwo()
    {
        var events = new List<GameEvent>();
        var s = Roll(OnHillsSix().Build(), 6, events);

        Assert.Equal(new ResourceSet(1, 0, 0, 0, 0), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(new ResourceSet(2, 0, 0, 0, 0), ResourceSet.From(s.HandOf(1)));
        Assert.Equal(16, s.Bank[(int)Resource.Brick]);
        Assert.Contains(new ResourcesProduced(0, new ResourceSet(1, 0, 0, 0, 0)), events);
        Assert.Contains(new ResourcesProduced(1, new ResourceSet(2, 0, 0, 0, 0)), events);
    }

    [Fact]
    public void OtherNumbersPayNothing()
    {
        var s = Roll(OnHillsSix().Build(), 5);
        Assert.Equal(0, s.HandSize(0));
        Assert.Equal(0, s.HandSize(1));
    }

    [Fact]
    public void EveryHexWithTheNumberPays()
    {
        var s = Roll(new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(2, -2, Corner.N))   // Forest 8, hex 2
            .Settlement(1, Vertex(0, 2, Corner.S))    // Forest 8, hex 18
            .Phase(Phase.PreRoll).Build(), 8);
        Assert.Equal(1, s.Hand[0 * 5 + (int)Resource.Lumber]);
        Assert.Equal(1, s.Hand[1 * 5 + (int)Resource.Lumber]);
    }

    [Fact]
    public void RobberBlocksItsHex()
    {
        var s = Roll(OnHillsSix().Robber(0).Build(), 6);
        Assert.Equal(0, s.HandSize(0));
        Assert.Equal(0, s.HandSize(1));
        Assert.Equal(19, s.Bank[(int)Resource.Brick]);
    }

    [Fact]
    public void SevenProducesNothing()
    {
        var s = Roll(OnHillsSix().Build(), 7);
        Assert.Equal(Phase.MoveRobber, s.Phase);
        Assert.Equal(0, s.HandSize(0) + s.HandSize(1));
    }

    // ---- Bank shortage ----

    [Fact]
    public void ShortageWithSeveralClaimantsPaysNobody()
    {
        // Seat 3 holds 17 brick, so the bank has 2 but seats 0 and 1 are owed 1 + 2 = 3.
        var s = Roll(OnHillsSix().Hand(3, brick: 17).Build(), 6);
        Assert.Equal(0, s.HandSize(0));
        Assert.Equal(0, s.HandSize(1));
        Assert.Equal(2, s.Bank[(int)Resource.Brick]);
    }

    [Fact]
    public void ShortageWithOneClaimantPaysWhatIsLeft()
    {
        // Only seat 1's city is on the hex: owed 2, bank has 1.
        var s = Roll(new StateBuilder(TestBoards.Standard)
            .City(1, HillsSixS)
            .Hand(3, brick: 18)
            .Phase(Phase.PreRoll).Build(), 6);
        Assert.Equal(1, s.Hand[1 * 5 + (int)Resource.Brick]);
        Assert.Equal(0, s.Bank[(int)Resource.Brick]);
    }

    [Fact]
    public void BankThatCoversExactlyPaysEveryone()
    {
        var s = Roll(OnHillsSix().Hand(3, brick: 16).Build(), 6);
        Assert.Equal(1, s.Hand[0 * 5 + (int)Resource.Brick]);
        Assert.Equal(2, s.Hand[1 * 5 + (int)Resource.Brick]);
        Assert.Equal(0, s.Bank[(int)Resource.Brick]);
    }

    [Fact]
    public void ShortageOnlyAffectsThatResource()
    {
        // A 6 pays brick (short, two claimants: nobody) and wool (plenty: paid).
        var s = Roll(OnHillsSix().Settlement(2, PastureSix).Hand(3, brick: 17).Build(), 6);
        Assert.Equal(0, s.HandSize(0) + s.HandSize(1));
        Assert.Equal(1, s.Hand[2 * 5 + (int)Resource.Wool]);
    }

    [Fact]
    public void RandomRollsAfterRandomSetupsStayValid()
    {
        var legal = new List<GameAction>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            var rng = new Rng(seed);
            var chance = new RngChance(rng);
            var s = new GameState(BoardGenerator.Balanced(rng));
            while (s.Phase != Phase.Main)
                Rules.ApplyChecked(s, TestPlay.RandomAction(s, legal, rng), chance);
            var errors = StateValidator.Check(s);
            Assert.True(errors.Count == 0, $"seed {seed}: {string.Join(" | ", errors)}");
            Assert.InRange(s.LastRoll, 2, 12);
        }
    }
}
