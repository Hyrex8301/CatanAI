using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

public class SetupTests
{
    // Setup never uses chance; a ScriptedChance with nothing queued throws if it is called.
    private static readonly IChance NoChance = new ScriptedChance();

    private static GameState NewGame() => new(TestBoards.Standard);

    private static void Play(GameState s, ActionType type, int target, List<GameEvent>? events = null) =>
        Rules.ApplyChecked(s, new GameAction(type, Rules.ActingSeat(s), target), NoChance, events);

    /// <summary>Picks the first legal action each time, checking the state after every step.</summary>
    private static List<int> PlayFirstLegalSetup(GameState s)
    {
        var seats = new List<int>();
        var legal = new List<GameAction>();
        while (s.Phase is Phase.SetupSettlement or Phase.SetupRoad)
        {
            Rules.GetLegalActions(s, legal);
            seats.Add(legal[0].Seat);
            Rules.ApplyChecked(s, legal[0], NoChance);
            Assert.Empty(StateValidator.Check(s));
        }
        return seats;
    }

    [Fact]
    public void SeatsFollowSnakeOrder()
    {
        var seats = PlayFirstLegalSetup(NewGame());
        Assert.Equal(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 3, 3, 2, 2, 1, 1, 0, 0 }, seats);
    }

    [Fact]
    public void SetupEndsAtSeatZerosFirstTurn()
    {
        var s = NewGame();
        PlayFirstLegalSetup(s);
        Assert.Equal(Phase.PreRoll, s.Phase);
        Assert.Equal(0, s.CurrentPlayer);
        Assert.Equal(1, s.TurnNumber);
        Assert.False(s.HasRolled);
        Assert.All(s.SettlementsLeft, n => Assert.Equal(3, n));
        Assert.All(s.RoadsLeft, n => Assert.Equal(13, n));
        Assert.All(s.PublicVP, n => Assert.Equal(2, n));
    }

    [Fact]
    public void AnyEmptyVertexIsLegalAtTheStart()
    {
        var legal = new List<GameAction>();
        Rules.GetLegalActions(NewGame(), legal);
        Assert.Equal(54, legal.Count);
        Assert.All(legal, a => Assert.Equal(ActionType.BuildSettlement, a.Type));
    }

    [Fact]
    public void DistanceRuleBlocksTheVertexAndItsNeighbors()
    {
        var s = NewGame();
        int v = HexVertices[HexAt(0, 0), (int)Corner.N]; // an inland vertex with 3 neighbors
        Play(s, ActionType.BuildSettlement, v);
        Play(s, ActionType.BuildRoad, VertexEdges[v, 0]);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(50, legal.Count);

        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildSettlement, 1, v), out string occupied));
        Assert.Contains("already has a building", occupied);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildSettlement, 1, VertexNeighbors[v, 1]), out string tooClose));
        Assert.Contains("distance rule", tooClose);
    }

    [Fact]
    public void SetupSettlementNeedsNoRoad()
    {
        var s = NewGame();
        Assert.True(Rules.IsLegal(s, new GameAction(ActionType.BuildSettlement, 0, Vertex(2, -2, Corner.N)), out _));
    }

    [Fact]
    public void SetupRoadMustTouchTheSettlementJustPlaced()
    {
        var s = NewGame();
        int first = Vertex(0, 0, Corner.N);
        Play(s, ActionType.BuildSettlement, first);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(3, legal.Count);
        Assert.All(legal, a => Assert.True(EdgeVertices[a.Target, 0] == first || EdgeVertices[a.Target, 1] == first));

        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildRoad, 0, Edge(-2, 2, Side.SW)), out string reason));
        Assert.Contains("must touch the settlement just placed", reason);
    }

    [Fact]
    public void SecondRoundRoadCantHangOffTheFirstSettlement()
    {
        // Seat 3 places both settlements back to back (steps 3 and 4).
        var s = NewGame();
        for (int step = 0; step < 3; step++)
        {
            Play(s, ActionType.BuildSettlement, Vertex(step - 1, -2 + step, Corner.S));
            Play(s, ActionType.BuildRoad, Edge(step - 1, -2 + step, Side.SE));
        }
        int firstOfSeat3 = Vertex(2, -1, Corner.S);
        Play(s, ActionType.BuildSettlement, firstOfSeat3);
        Play(s, ActionType.BuildRoad, Edge(2, -1, Side.SW));
        int secondOfSeat3 = Vertex(-2, 2, Corner.S);
        Play(s, ActionType.BuildSettlement, secondOfSeat3);
        Assert.Equal(3, Rules.ActingSeat(s));
        Assert.Equal(secondOfSeat3, Rules.SetupRoadAnchor(s));

        // A free edge touching the first settlement is not allowed now.
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildRoad, 3, Edge(2, -1, Side.SE)), out string reason));
        Assert.Contains("must touch the settlement just placed", reason);
        Assert.True(Rules.IsLegal(s, new GameAction(ActionType.BuildRoad, 3, Edge(-2, 2, Side.SW)), out _));
    }

    [Fact]
    public void WrongSeatOrWrongActionIsIllegal()
    {
        var s = NewGame();
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildSettlement, 1, 0), out string wrongSeat));
        Assert.Contains("seat 0's turn", wrongSeat);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildRoad, 0, 0), out _));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.RollDice, 0), out _));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.EndTurn, 0), out _));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BuildSettlement, 0, 54), out string bad));
        Assert.Contains("doesn't exist", bad);
    }

    [Fact]
    public void OnlyTheSecondSettlementPaysOut()
    {
        var s = NewGame();
        var events = new List<GameEvent>();

        // Seat 0's first settlement: vertex N of the desert (hex 9), touching Mountains (hex 4) and Pasture (hex 5).
        int desertCorner = Vertex(0, 0, Corner.N);
        Play(s, ActionType.BuildSettlement, desertCorner, events);
        Assert.Equal(0, s.HandSize(0));
        Assert.DoesNotContain(events, e => e is ResourcesProduced);

        // Finish setup so seat 0's second settlement is on the same kind of spot: S of the desert.
        Play(s, ActionType.BuildRoad, Edge(0, 0, Side.NE));
        var legal = new List<GameAction>();
        while (s.SetupStep < 7)
        {
            Rules.GetLegalActions(s, legal);
            var pick = legal.First(a => a.Type != ActionType.BuildSettlement || a.Target != Vertex(0, 0, Corner.S) && !IsNeighbor(a.Target, Vertex(0, 0, Corner.S)));
            Rules.ApplyChecked(s, pick, NoChance);
        }
        Assert.Equal(0, Rules.ActingSeat(s));

        int[] bankBefore = (int[])s.Bank.Clone();
        events.Clear();
        int second = Vertex(0, 0, Corner.S); // touches desert (hex 9), Forest (hex 13 at (-1,1)) and Hills (hex 14 at (0,1))
        Play(s, ActionType.BuildSettlement, second, events);

        var expected = new ResourceSet(Brick: 1, Lumber: 1, Wool: 0, Grain: 0, Ore: 0);
        Assert.Equal(expected, ResourceSet.From(s.HandOf(0)));
        Assert.Equal(bankBefore[0] - 1, s.Bank[0]);
        Assert.Equal(bankBefore[1] - 1, s.Bank[1]);
        Assert.Contains(new ResourcesProduced(0, expected), events);
        Assert.Contains(new Built(0, PieceType.Settlement, second), events);
    }

    private static bool IsNeighbor(int a, int b) => Enumerable.Range(0, 3).Any(i => VertexNeighbors[a, i] == b);

    [Fact]
    public void LegalListAndIsLegalAgreeEverywhereDuringSetup()
    {
        var rng = new Rng(11);
        var s = NewGame();
        var legal = new List<GameAction>();
        while (s.Phase is Phase.SetupSettlement or Phase.SetupRoad)
        {
            Rules.GetLegalActions(s, legal);
            var listed = legal.ToHashSet();
            int seat = Rules.ActingSeat(s);
            foreach (var type in new[] { ActionType.BuildSettlement, ActionType.BuildRoad })
                for (int target = 0; target < (type == ActionType.BuildRoad ? EdgeCount : VertexCount); target++)
                {
                    var a = new GameAction(type, seat, target);
                    Assert.Equal(listed.Contains(a), Rules.IsLegal(s, a, out _));
                }
            Rules.ApplyChecked(s, legal[rng.NextInt(legal.Count)], NoChance);
        }
    }

    [Fact]
    public void RandomSetupsAlwaysStayValid()
    {
        var legal = new List<GameAction>();
        for (ulong seed = 0; seed < 300; seed++)
        {
            var rng = new Rng(seed);
            var s = new GameState(BoardGenerator.Balanced(rng));
            while (s.Phase is Phase.SetupSettlement or Phase.SetupRoad)
            {
                Rules.GetLegalActions(s, legal);
                Assert.NotEmpty(legal);
                Rules.ApplyChecked(s, legal[rng.NextInt(legal.Count)], NoChance);
                var errors = StateValidator.Check(s);
                Assert.True(errors.Count == 0, $"seed {seed}: {string.Join(" | ", errors)}");
            }
            Assert.Equal(Phase.PreRoll, s.Phase);
            // Each seat got exactly the cards its second settlement touches.
            int[] handTotal = Enumerable.Range(0, 4).Select(s.HandSize).ToArray();
            Assert.Equal(19 * 5 - s.Bank.Sum(), handTotal.Sum());
            Assert.All(handTotal, n => Assert.InRange(n, 0, 3));
        }
    }
}
