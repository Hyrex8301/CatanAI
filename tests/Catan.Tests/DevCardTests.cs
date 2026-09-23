using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

public class DevCardTests
{
    private static readonly int CenterN = Vertex(0, 0, Corner.N);

    private static void Do(GameState s, GameAction a, IChance? chance = null, List<GameEvent>? events = null)
    {
        Rules.ApplyChecked(s, a, chance ?? new ScriptedChance(), events);
        var errors = StateValidator.Check(s);
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
    }

    private static GameAction Play(ActionType type, int seat = 0, int target = -1, ResourceSet get = default) =>
        new(type, seat, target, Get: get);

    /// <summary>Seat 0 with a settlement and road at the center, holding one of each playable card.</summary>
    private static StateBuilder Holding(Phase phase = Phase.Main) => new StateBuilder(TestBoards.Standard)
        .Settlement(0, CenterN)
        .Road(0, Edge(0, 0, Side.NE))
        .DevCards(0, knight: 1, roadBuilding: 1, yearOfPlenty: 1, monopoly: 1)
        .Phase(phase, current: 0);

    // ---- Timing ----

    [Fact]
    public void CantPlayACardOnTheTurnItWasBought()
    {
        var s = new StateBuilder(TestBoards.Standard).Hand(0, wool: 1, grain: 1, ore: 1).Phase(Phase.Main).Build();
        Do(s, new GameAction(ActionType.BuyDevCard, 0), new ScriptedChance().Draw(DevCardType.Monopoly));
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayMonopoly, target: 0), out string reason));
        Assert.Contains("turn you bought it", reason);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.DoesNotContain(legal, a => a.Type == ActionType.PlayMonopoly);
    }

    [Fact]
    public void AnOlderCopyIsPlayableEvenIfAnotherWasJustBought()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .DevCards(0, knight: 2).BoughtThisTurn(DevCardType.Knight)
            .Phase(Phase.Main).Build();
        Assert.True(Rules.IsLegal(s, Play(ActionType.PlayKnight), out _));
    }

    [Fact]
    public void BoughtCardBecomesPlayableNextTurn()
    {
        var s = new StateBuilder(TestBoards.Standard).DevCards(1, knight: 1).Phase(Phase.PreRoll, current: 1).Build();
        Assert.True(Rules.IsLegal(s, Play(ActionType.PlayKnight, seat: 1), out _));
    }

    [Fact]
    public void OnlyOneCardPerTurn()
    {
        var s = Holding().Build();
        Do(s, Play(ActionType.PlayMonopoly, target: (int)Resource.Ore));
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayYearOfPlenty, get: new ResourceSet(1, 1, 0, 0, 0)), out string reason));
        Assert.Contains("already played", reason);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.DoesNotContain(legal, a => a.Type is ActionType.PlayKnight or ActionType.PlayRoadBuilding or ActionType.PlayYearOfPlenty);

        // The flag resets next turn.
        Do(s, new GameAction(ActionType.EndTurn, 0));
        Assert.False(s.DevPlayedThisTurn);
    }

    [Theory]
    [InlineData(ActionType.PlayKnight)]
    [InlineData(ActionType.PlayRoadBuilding)]
    [InlineData(ActionType.PlayYearOfPlenty)]
    [InlineData(ActionType.PlayMonopoly)]
    public void AnyCardCanBePlayedBeforeRolling(ActionType type)
    {
        var s = Holding(Phase.PreRoll).Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        var play = legal.First(a => a.Type == type);
        Do(s, play);

        // Knight and Road Building detour through their own phase; all return to PreRoll with the dice unrolled.
        if (s.Phase == Phase.MoveRobber)
            Do(s, new GameAction(ActionType.MoveRobber, 0, 18));
        while (s.Phase == Phase.RoadBuilding)
        {
            Rules.GetLegalActions(s, legal);
            Do(s, legal[0]);
        }
        Assert.Equal(Phase.PreRoll, s.Phase);
        Assert.False(s.HasRolled);
        Assert.True(s.DevPlayedThisTurn);
        Do(s, new GameAction(ActionType.RollDice, 0), new ScriptedChance().Roll(5));
        Assert.Equal(Phase.Main, s.Phase);
    }

    [Fact]
    public void YouNeedTheCard()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main).Build();
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayKnight), out string reason));
        Assert.Contains("don't have a Knight", reason);
    }

    // ---- Knight ----

    [Fact]
    public void KnightMovesTheRobberWithoutDiscards()
    {
        var s = Holding().Hand(1, ore: 10).Build();
        var events = new List<GameEvent>();
        Do(s, Play(ActionType.PlayKnight), events: events);
        Assert.Equal(Phase.MoveRobber, s.Phase);
        Assert.All(s.DiscardOwed, n => Assert.Equal(0, n));
        Assert.Equal(1, s.KnightsPlayed[0]);
        Assert.Equal(1, s.DevPlayed[(int)DevCardType.Knight]);
        Assert.Contains(new DevCardPlayed(0, DevCardType.Knight), events);

        Do(s, new GameAction(ActionType.MoveRobber, 0, 18));
        Assert.Equal(Phase.Main, s.Phase);
    }

    [Fact]
    public void ThirdKnightTakesLargestArmy()
    {
        var s = new StateBuilder(TestBoards.Standard).DevCards(0, knight: 1).KnightsPlayed(0, 2).Phase(Phase.Main).Build();
        var events = new List<GameEvent>();
        Do(s, Play(ActionType.PlayKnight), events: events);
        Assert.Equal(0, s.LargestArmyOwner);
        Assert.Equal(2, s.PublicVP[0]);
        Assert.Contains(new AwardChanged(Award.LargestArmy, -1, 0), events);
    }

    // ---- Road Building ----

    [Fact]
    public void RoadBuildingPlacesTwoFreeRoads()
    {
        var s = Holding().Build();
        Do(s, Play(ActionType.PlayRoadBuilding));
        Assert.Equal(Phase.RoadBuilding, s.Phase);
        Assert.Equal(2, s.FreeRoads);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.EndTurn, 0), out _));

        Do(s, new GameAction(ActionType.BuildRoad, 0, Edge(0, 0, Side.E)));
        Assert.Equal(Phase.RoadBuilding, s.Phase);
        Do(s, new GameAction(ActionType.BuildRoad, 0, Edge(0, 0, Side.SE)));
        Assert.Equal(Phase.Main, s.Phase);
        Assert.Equal(0, s.FreeRoads);
        Assert.Equal(12, s.RoadsLeft[0]);
        Assert.Equal(0, s.HandSize(0)); // free
    }

    [Fact]
    public void RoadBuildingWithOnePieceLeftPlacesOne()
    {
        var s = Holding().Build();
        s.RoadsLeft[0] = 1; // pretend 14 roads are elsewhere; not validated here
        Rules.ApplyChecked(s, Play(ActionType.PlayRoadBuilding), new ScriptedChance());
        Assert.Equal(1, s.FreeRoads);
        Rules.ApplyChecked(s, new GameAction(ActionType.BuildRoad, 0, Edge(0, 0, Side.E)), new ScriptedChance());
        Assert.Equal(Phase.Main, s.Phase);
        Assert.Equal(0, s.RoadsLeft[0]);
    }

    [Fact]
    public void RoadBuildingNeedsAPieceAndASpot()
    {
        var s = Holding().Build();
        s.RoadsLeft[0] = 0;
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayRoadBuilding), out string noPieces));
        Assert.Contains("no roads left", noPieces);

        // No buildings or roads at all: nowhere to connect a road.
        s = new StateBuilder(TestBoards.Standard).DevCards(0, roadBuilding: 1).Phase(Phase.Main).Build();
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayRoadBuilding), out string noSpot));
        Assert.Contains("nowhere", noSpot);
    }

    // ---- Year of Plenty ----

    [Fact]
    public void YearOfPlentyTakesAnyTwoTheBankHas()
    {
        var s = Holding().Build();
        var events = new List<GameEvent>();
        Do(s, Play(ActionType.PlayYearOfPlenty, get: new ResourceSet(0, 0, 0, 0, 2)), events: events);
        Assert.Equal(new ResourceSet(0, 0, 0, 0, 2), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(17, s.Bank[(int)Resource.Ore]);
        Assert.Contains(new ResourcesProduced(0, new ResourceSet(0, 0, 0, 0, 2)), events);
    }

    [Fact]
    public void YearOfPlentyRespectsTheBank()
    {
        var s = Holding().Hand(1, ore: 18).Build(); // bank has 1 ore
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayYearOfPlenty, get: new ResourceSet(0, 0, 0, 0, 2)), out string reason));
        Assert.Contains("bank doesn't have", reason);
        Assert.True(Rules.IsLegal(s, Play(ActionType.PlayYearOfPlenty, get: new ResourceSet(0, 0, 0, 1, 1)), out _));
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayYearOfPlenty, get: new ResourceSet(0, 0, 0, 3, 0)), out string three));
        Assert.Contains("exactly 2", three);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(14, legal.Count(a => a.Type == ActionType.PlayYearOfPlenty)); // 15 picks minus ore+ore
    }

    // ---- Monopoly ----

    [Fact]
    public void MonopolyTakesEveryOpponentsCardsOfThatResource()
    {
        var s = Holding().Hand(0, wool: 1).Hand(1, wool: 3, ore: 1).Hand(3, wool: 2).Build();
        var events = new List<GameEvent>();
        Do(s, Play(ActionType.PlayMonopoly, target: (int)Resource.Wool), events: events);
        Assert.Equal(6, s.Hand[0 * 5 + (int)Resource.Wool]);
        Assert.Equal(0, s.Hand[1 * 5 + (int)Resource.Wool]);
        Assert.Equal(1, s.Hand[1 * 5 + (int)Resource.Ore]);
        Assert.Equal(0, s.Hand[3 * 5 + (int)Resource.Wool]);
        Assert.Contains(new MonopolyTaken(0, 1, (int)Resource.Wool, 3), events);
        Assert.Contains(new MonopolyTaken(0, 2, (int)Resource.Wool, 0), events);
        Assert.Contains(new MonopolyTaken(0, 3, (int)Resource.Wool, 2), events);
    }

    [Fact]
    public void MonopolyNeedsAResource()
    {
        var s = Holding().Build();
        Assert.False(Rules.IsLegal(s, Play(ActionType.PlayMonopoly, target: 5), out string reason));
        Assert.Contains("Name a resource", reason);
    }

    // ---- Lists ----

    [Fact]
    public void LegalListMatchesIsLegalWithCardsInHand()
    {
        var legal = new List<GameAction>();
        foreach (var phase in new[] { Phase.PreRoll, Phase.Main })
        {
            var s = Holding(phase).Hand(0, brick: 1, lumber: 1).Build();
            Rules.GetLegalActions(s, legal);
            TestPlay.AssertListMatchesIsLegal(s, legal);
            Assert.Contains(Play(ActionType.PlayKnight), legal);
            Assert.Equal(5, legal.Count(a => a.Type == ActionType.PlayMonopoly));
            Assert.Equal(15, legal.Count(a => a.Type == ActionType.PlayYearOfPlenty));
        }
    }

    [Fact]
    public void RandomGamesWithDevCardsStayValid()
    {
        // Rich starting hands so dev cards get bought and played often.
        var legal = new List<GameAction>();
        int plays = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            var rng = new Rng(seed);
            var chance = new RngChance(rng);
            var s = new GameState(BoardGenerator.Balanced(rng), new GameSettings { MaxTurns = 150 });
            for (int r = 0; r < 5; r++)
            {
                s.Bank[r] -= 8;
                for (int seat = 0; seat < 4; seat++)
                    s.Hand[seat * 5 + r] += 2;
            }
            while (s.Phase != Phase.GameOver)
            {
                var action = TestPlay.RandomAction(s, legal, rng);
                if (s.Phase is Phase.RoadBuilding or Phase.PreRoll)
                    TestPlay.AssertListMatchesIsLegal(s, legal);
                if (action.Type is ActionType.PlayKnight or ActionType.PlayRoadBuilding or ActionType.PlayYearOfPlenty or ActionType.PlayMonopoly)
                    plays++;
                Rules.ApplyChecked(s, action, chance);
                var errors = StateValidator.Check(s);
                Assert.True(errors.Count == 0, $"seed {seed}: {string.Join(" | ", errors)}");
            }
        }
        Assert.True(plays > 100, $"only {plays} dev cards played");
    }
}
