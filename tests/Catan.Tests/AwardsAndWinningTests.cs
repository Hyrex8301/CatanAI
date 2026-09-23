using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

/// <summary>Longest Road (FAQ cut cases), Largest Army, and winning. Road layouts are on TestBoards.Standard.</summary>
public class AwardsAndWinningTests
{
    private static void Do(GameState s, GameAction a, IChance? chance = null, List<GameEvent>? events = null)
    {
        Rules.ApplyChecked(s, a, chance ?? new ScriptedChance(), events);
        var errors = StateValidator.Check(s);
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
    }

    /// <summary>Edges joining consecutive vertices of a path.</summary>
    private static int[] Path(params int[] vertices) =>
        Enumerable.Range(0, vertices.Length - 1).Select(i =>
        {
            int e = EdgeBetween(vertices[i], vertices[i + 1]);
            Assert.True(e >= 0, $"vertices {vertices[i]} and {vertices[i + 1]} aren't adjacent");
            return e;
        }).ToArray();

    private static int[] Corners(int q, int r, params Corner[] corners) => corners.Select(c => Vertex(q, r, c)).ToArray();

    // Seat 0: settlement on the center hex's N corner, 7 roads N -> NE -> SE -> S -> SW -> NW -> (-1,0,N) -> (-1,-1,S).
    private static readonly int CenterN = Vertex(0, 0, Corner.N);
    private static readonly int CenterSE = Vertex(0, 0, Corner.SE);
    private static readonly int CenterS = Vertex(0, 0, Corner.S);
    private static readonly int[] SevenLine = Path(
        Corners(0, 0, Corner.N, Corner.NE, Corner.SE, Corner.S, Corner.SW, Corner.NW)
            .Append(Vertex(-1, 0, Corner.N)).Append(Vertex(-1, -1, Corner.S)).ToArray());

    // Seat 2: settlement on hex (2,-2)'s N corner, 5 roads clockwise to its NW corner. Its NW side closes a ring of 6.
    private static readonly int TopRightN = Vertex(2, -2, Corner.N);
    private static readonly int[] TopRightFive = Path(Corners(2, -2, Corner.N, Corner.NE, Corner.SE, Corner.S, Corner.SW, Corner.NW));

    // Seat 3: settlement on hex (-2,2)'s S corner, 5 roads clockwise from S to SE.
    private static readonly int BottomLeftS = Vertex(-2, 2, Corner.S);
    private static readonly int[] BottomLeftFive = Path(Corners(-2, 2, Corner.S, Corner.SW, Corner.NW, Corner.N, Corner.NE, Corner.SE));

    /// <summary>
    /// Seat 1 (current, Main) is set up to cut seat 0's line with one settlement:
    /// at S (splits it 3 + 4) from home (0,1,S), or at SE (splits it 2 + 5) from home (1,1,N).
    /// </summary>
    private static StateBuilder CutSetup(bool cutAtS, bool seat2Chain, bool seat3Chain)
    {
        var b = new StateBuilder(TestBoards.Standard)
            .Settlement(0, CenterN).Roads(0, SevenLine)
            .Hand(1, brick: 1, lumber: 1, wool: 1, grain: 1)
            .Phase(Phase.Main, current: 1);
        if (cutAtS)
        {
            int home = Vertex(0, 1, Corner.S), mid = Vertex(-1, 2, Corner.N);
            b.Settlement(1, home).Roads(1, Path(home, mid, CenterS));
        }
        else
        {
            int home = Vertex(1, 1, Corner.N), mid = Vertex(1, 0, Corner.S);
            b.Settlement(1, home).Roads(1, Path(home, mid, CenterSE));
        }
        if (seat2Chain)
            b.Settlement(2, TopRightN).Roads(2, TopRightFive);
        if (seat3Chain)
            b.Settlement(3, BottomLeftS).Roads(3, BottomLeftFive);
        return b;
    }

    private static GameAction Settle(int seat, int vertex) => new(ActionType.BuildSettlement, seat, vertex);

    // ---- Longest Road ----

    [Fact]
    public void FourRoadsIsNotEnough()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(2, TopRightN).Roads(2, TopRightFive.Take(4).ToArray()).Build();
        Assert.Equal(4, s.RoadLength[2]);
        Assert.Equal(-1, s.LongestRoadOwner);
    }

    [Fact]
    public void TyingTheHolderIsNotEnoughButBeatingItTakesIt()
    {
        // Seat 0 holds with 5. Seat 2 builds its chain from 4 to 5 (a tie), then closes a ring of 6.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, CenterN).Roads(0, SevenLine.Take(5).ToArray())
            .Settlement(2, TopRightN).Roads(2, TopRightFive.Take(4).ToArray())
            .Hand(2, brick: 2, lumber: 2)
            .Phase(Phase.Main, current: 2)
            .Build();
        Assert.Equal(0, s.LongestRoadOwner);

        Do(s, new GameAction(ActionType.BuildRoad, 2, TopRightFive[4]));
        Assert.Equal(5, s.RoadLength[2]);
        Assert.Equal(0, s.LongestRoadOwner); // tie stays with the holder

        var events = new List<GameEvent>();
        Do(s, new GameAction(ActionType.BuildRoad, 2, Edge(2, -2, Side.NW)), events: events);
        Assert.Equal(6, s.RoadLength[2]); // ring of 6
        Assert.Equal(2, s.LongestRoadOwner);
        Assert.Equal(1, s.PublicVP[0]);
        Assert.Equal(3, s.PublicVP[2]);
        Assert.Contains(new AwardChanged(Award.LongestRoad, 0, 2), events);
    }

    [Fact]
    public void CutHolderStillTiedForLongestKeepsIt()
    {
        var s = CutSetup(cutAtS: false, seat2Chain: true, seat3Chain: true).Build();
        Assert.Equal(0, s.LongestRoadOwner);
        Do(s, Settle(1, CenterSE));
        Assert.Equal(5, s.RoadLength[0]);
        Assert.Equal(0, s.LongestRoadOwner);
    }

    [Fact]
    public void CutHolderLosesItToTheSingleLongest()
    {
        var s = CutSetup(cutAtS: true, seat2Chain: true, seat3Chain: false).Build();
        var events = new List<GameEvent>();
        Do(s, Settle(1, CenterS), events: events);
        Assert.Equal(4, s.RoadLength[0]);
        Assert.Equal(2, s.LongestRoadOwner);
        Assert.Contains(new AwardChanged(Award.LongestRoad, 0, 2), events);
    }

    [Fact]
    public void CutHolderWithATieBehindSetsItAside()
    {
        var s = CutSetup(cutAtS: true, seat2Chain: true, seat3Chain: true).Build();
        Do(s, Settle(1, CenterS));
        Assert.Equal(-1, s.LongestRoadOwner);
        Assert.Equal(new[] { 1, 2, 1, 1 }, s.PublicVP); // seat 1 has two settlements now
    }

    [Fact]
    public void CutHolderWithNobodyAtFiveSetsItAside()
    {
        var s = CutSetup(cutAtS: true, seat2Chain: false, seat3Chain: false).Build();
        Do(s, Settle(1, CenterS));
        Assert.Equal(-1, s.LongestRoadOwner);
    }

    [Fact]
    public void SetAsideAwardIsReclaimedByTheFirstToBeLongestAlone()
    {
        var s = CutSetup(cutAtS: true, seat2Chain: true, seat3Chain: true).Hand(2, brick: 1, lumber: 1).Build();
        Do(s, Settle(1, CenterS));
        Assert.Equal(-1, s.LongestRoadOwner);

        Do(s, new GameAction(ActionType.EndTurn, 1));
        Do(s, new GameAction(ActionType.RollDice, 2), new ScriptedChance().Roll(2));
        var events = new List<GameEvent>();
        Do(s, new GameAction(ActionType.BuildRoad, 2, Edge(2, -2, Side.NW)), events: events);
        Assert.Equal(2, s.LongestRoadOwner);
        Assert.Contains(new AwardChanged(Award.LongestRoad, -1, 2), events);
    }

    // ---- Largest Army ----

    [Fact]
    public void TwoKnightsIsNotEnough()
    {
        var s = new StateBuilder(TestBoards.Standard).DevCards(0, knight: 1).KnightsPlayed(0, 1).Phase(Phase.Main).Build();
        Do(s, new GameAction(ActionType.PlayKnight, 0));
        Assert.Equal(-1, s.LargestArmyOwner);
    }

    [Fact]
    public void MatchingTheHoldersKnightsIsNotEnough()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .KnightsPlayed(0, 3).KnightsPlayed(1, 2).DevCards(1, knight: 1)
            .Phase(Phase.Main, current: 1).Build();
        Assert.Equal(0, s.LargestArmyOwner);
        Do(s, new GameAction(ActionType.PlayKnight, 1));
        Assert.Equal(3, s.KnightsPlayed[1]);
        Assert.Equal(0, s.LargestArmyOwner);
    }

    [Fact]
    public void PlayingMoreKnightsThanTheHolderTakesIt()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .KnightsPlayed(0, 3).KnightsPlayed(1, 3).DevCards(1, knight: 1).LargestArmyOwner(0)
            .Phase(Phase.Main, current: 1).Build();
        var events = new List<GameEvent>();
        Do(s, new GameAction(ActionType.PlayKnight, 1), events: events);
        Assert.Equal(1, s.LargestArmyOwner);
        Assert.Equal(0, s.PublicVP[0]);
        Assert.Equal(2, s.PublicVP[1]);
        Assert.Contains(new AwardChanged(Award.LargestArmy, 0, 1), events);
    }

    // ---- Winning ----

    private static readonly int[] FarCorners =
    {
        Vertex(2, -2, Corner.N), Vertex(-2, 2, Corner.S), Vertex(0, 2, Corner.S), Vertex(-2, 0, Corner.N),
    };

    [Fact]
    public void ReachingTenOnYourTurnWinsImmediately()
    {
        // 4 settlements + 5 hidden VP = 9; the 5th settlement makes 10.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, CenterN).Settlement(0, FarCorners[0]).Settlement(0, FarCorners[1]).Settlement(0, FarCorners[2])
            .Roads(0, SevenLine.Take(2).ToArray())
            .DevCards(0, victoryPoint: 5)
            .Hand(0, brick: 1, lumber: 1, wool: 1, grain: 1)
            .Phase(Phase.Main)
            .Build();
        Assert.Equal(9, s.TotalVP(0));

        var events = new List<GameEvent>();
        Do(s, Settle(0, CenterSE), events: events);
        Assert.Equal(Phase.GameOver, s.Phase);
        Assert.Equal(0, s.Winner);
        Assert.Contains(new GameEnded(0), events);
    }

    [Fact]
    public void AVictoryPointCardCanWinOnTheTurnItIsBought()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, CenterN).Settlement(0, FarCorners[0]).Settlement(0, FarCorners[1]).Settlement(0, FarCorners[2]).Settlement(0, FarCorners[3])
            .DevCards(0, victoryPoint: 4)
            .Hand(0, wool: 1, grain: 1, ore: 1)
            .Phase(Phase.Main)
            .Build();
        Assert.Equal(5, s.PublicVP[0]);
        Assert.Equal(9, s.TotalVP(0));

        Do(s, new GameAction(ActionType.BuyDevCard, 0), new ScriptedChance().Draw(DevCardType.VictoryPoint));
        Assert.Equal(0, s.Winner);
        Assert.Equal(5, s.PublicVP[0]); // the winning points were hidden
    }

    [Fact]
    public void LargestArmyThatReachesTenEndsTheGameBeforeTheRobberMoves()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, CenterN).Settlement(0, FarCorners[0]).Settlement(0, FarCorners[1]).Settlement(0, FarCorners[2])
            .DevCards(0, knight: 1, victoryPoint: 4)
            .KnightsPlayed(0, 2)
            .Phase(Phase.PreRoll)
            .Build();
        Do(s, new GameAction(ActionType.PlayKnight, 0));
        Assert.Equal(Phase.GameOver, s.Phase);
        Assert.Equal(0, s.Winner);
    }

    [Fact]
    public void ReachingTenOnSomeoneElsesTurnWinsAtTheStartOfYourOwn()
    {
        // Seat 2: 3 settlements + 5 hidden VP = 8, and a 5-road chain behind seat 0's 7.
        // Seat 1 cuts seat 0's road, Longest Road passes to seat 2 (10 VP), but it's seat 1's turn.
        var s = CutSetup(cutAtS: true, seat2Chain: true, seat3Chain: false)
            .Settlement(2, FarCorners[1]).Settlement(2, FarCorners[3])
            .DevCards(2, victoryPoint: 5)
            .Build();
        Assert.Equal(8, s.TotalVP(2));

        Do(s, Settle(1, CenterS));
        Assert.Equal(2, s.LongestRoadOwner);
        Assert.Equal(10, s.TotalVP(2));
        Assert.Equal(Phase.Main, s.Phase); // not seat 2's turn: no win yet
        Assert.Equal(-1, s.Winner);

        var events = new List<GameEvent>();
        Do(s, new GameAction(ActionType.EndTurn, 1), events: events);
        Assert.Equal(Phase.GameOver, s.Phase);
        Assert.Equal(2, s.Winner);
        Assert.Contains(new GameEnded(2), events);
    }

    [Fact]
    public void NothingIsLegalAfterTheGameEnds()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, CenterN).Settlement(0, FarCorners[0]).Settlement(0, FarCorners[1]).Settlement(0, FarCorners[2]).Settlement(0, FarCorners[3])
            .DevCards(0, victoryPoint: 4)
            .Hand(0, wool: 1, grain: 1, ore: 1)
            .Phase(Phase.Main)
            .Build();
        Do(s, new GameAction(ActionType.BuyDevCard, 0), new ScriptedChance().Draw(DevCardType.VictoryPoint));

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Empty(legal);
        Assert.Equal(-1, Rules.ActingSeat(s));
        foreach (var type in Enum.GetValues<ActionType>())
            Assert.False(Rules.IsLegal(s, new GameAction(type, 0), out _));
    }

    [Fact]
    public void RandomGamesEndWithAValidWinner()
    {
        var legal = new List<GameAction>();
        int won = 0;
        for (ulong seed = 0; seed < 100; seed++)
        {
            var rng = new Rng(seed);
            var chance = new RngChance(rng);
            var s = new GameState(BoardGenerator.Balanced(rng));
            while (s.Phase != Phase.GameOver)
                Rules.ApplyChecked(s, TestPlay.RandomAction(s, legal, rng), chance);
            Assert.Empty(StateValidator.Check(s));
            if (s.Winner >= 0)
            {
                won++;
                Assert.True(s.TotalVP(s.Winner) >= 10);
            }
        }
        Assert.True(won > 50, $"only {won} of 100 random games had a winner");
    }
}
