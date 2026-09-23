using System.Reflection;
using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

public class GameStateTests
{
    private static GameState NewGame() => new(TestBoards.Standard);

    [Fact]
    public void TestBoardIsBalanced() => Assert.True(BoardGenerator.IsBalanced(TestBoards.Standard));

    [Fact]
    public void NewGameStartsAtSetupWithFullSupplies()
    {
        var s = NewGame();
        Assert.Empty(StateValidator.Check(s));
        Assert.Equal(Phase.SetupSettlement, s.Phase);
        Assert.Equal(0, s.CurrentPlayer);
        Assert.Equal(TestBoards.Standard.DesertHex, s.RobberHex);
        Assert.All(s.Bank, n => Assert.Equal(19, n));
        Assert.Equal(new[] { 14, 5, 2, 2, 2 }, s.DevDeck);
        Assert.All(s.RoadsLeft, n => Assert.Equal(15, n));
        Assert.All(s.SettlementsLeft, n => Assert.Equal(5, n));
        Assert.All(s.CitiesLeft, n => Assert.Equal(4, n));
        Assert.All(s.VertexOwner, o => Assert.Equal(-1, o));
        Assert.All(s.EdgeOwner, o => Assert.Equal(-1, o));
        Assert.All(s.OfferReply, r => Assert.Equal(-1, r));
        Assert.Equal(-1, s.Winner);
        Assert.Equal(-1, s.LongestRoadOwner);
        Assert.Equal(-1, s.LargestArmyOwner);
    }

    // ---- Clone, CopyFrom, hash ----

    [Fact]
    public void CloneHashesEqualAndChangingItLeavesTheOriginalUntouched()
    {
        var original = SampleState();
        ulong before = original.ComputeHash();
        var clone = original.Clone();
        Assert.Equal(before, clone.ComputeHash());

        clone.Hand[0]++;
        clone.VertexOwner[0] = 3;
        clone.Phase = Phase.GameOver;
        Assert.NotEqual(before, clone.ComputeHash());
        Assert.Equal(before, original.ComputeHash());
        Assert.Equal(1, original.Hand[0]);
    }

    [Fact]
    public void CopyFromReusesAnInstance()
    {
        var a = SampleState();
        var pooled = NewGame();
        pooled.CopyFrom(a);
        Assert.Equal(a.ComputeHash(), pooled.ComputeHash());
    }

    [Fact]
    public void CopyFromRejectsAnotherGamesState()
    {
        var other = new GameState(BoardGenerator.Balanced(new Rng(3)));
        Assert.Throws<ArgumentException>(() => NewGame().CopyFrom(other));
        Assert.Throws<ArgumentException>(() => NewGame().CopyFrom(new GameState(TestBoards.Standard, new GameSettings { VpToWin = 12 })));
    }

    /// <summary>
    /// Every dynamic field must be covered by both ComputeHash and CopyFrom. Changes each public field of a clone in turn;
    /// a field someone adds later but forgets in either method fails here.
    /// </summary>
    [Fact]
    public void EveryFieldIsHashedAndCopied()
    {
        var fields = typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.Name is not (nameof(GameState.Board) or nameof(GameState.Settings)))
            .ToList();
        Assert.True(fields.Count >= 30, $"only found {fields.Count} fields");

        var original = SampleState();
        ulong baseHash = original.ComputeHash();
        foreach (var field in fields)
        {
            var changed = original.Clone();
            Mutate(field, changed);
            Assert.True(changed.ComputeHash() != baseHash, $"{field.Name} is not included in ComputeHash");

            var copy = original.Clone();
            copy.CopyFrom(changed);
            Assert.True(copy.ComputeHash() == changed.ComputeHash(), $"{field.Name} is not copied by CopyFrom");
        }
    }

    private static void Mutate(FieldInfo field, GameState s)
    {
        object value = field.GetValue(s)!;
        switch (value)
        {
            case Array array:
                var element = array.GetValue(0)!;
                array.SetValue(Convert.ChangeType(Convert.ToInt32(element) + 1, element.GetType()), 0);
                break;
            case int i:
                field.SetValue(s, i + 1);
                break;
            case bool b:
                field.SetValue(s, !b);
                break;
            case Phase p:
                field.SetValue(s, p == Phase.Main ? Phase.PreRoll : Phase.Main);
                break;
            case TradeOffer offer:
                field.SetValue(s, offer with { Give = offer.Give + ResourceSet.Of(Resource.Ore) });
                break;
            default:
                throw new InvalidOperationException($"Test doesn't know how to change {field.Name} ({field.FieldType.Name}); extend Mutate.");
        }
    }

    [Fact]
    public void NewGameHashIsPinned()
    {
        // The hash format is part of saved games (finalHash). If this changes, old records stop verifying.
        Assert.Equal(NewGameGoldenHash, NewGame().ComputeHash());
    }

    private const ulong NewGameGoldenHash = 4180353660270957042UL;

    // ---- StateBuilder ----

    [Fact]
    public void BuilderMatchesTheBriefsExample()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(seat: 0, Vertex(0, 0, Corner.N))
            .Road(seat: 0, Edge(0, 0, Side.NE))
            .Hand(seat: 0, brick: 1, lumber: 1)
            .Phase(Phase.Main, current: 0)
            .Build();

        Assert.Equal(new[] { 18, 18, 19, 19, 19 }, s.Bank);
        Assert.Equal(4, s.SettlementsLeft[0]);
        Assert.Equal(14, s.RoadsLeft[0]);
        Assert.Equal(1, s.PublicVP[0]);
        Assert.Equal(1, s.RoadLength[0]);
        Assert.True(s.HasRolled);
    }

    [Fact]
    public void BuilderRejectsInvalidPositions()
    {
        // Distance rule.
        Assert.Throws<InvalidOperationException>(() => new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N)).Settlement(1, Vertex(0, 0, Corner.NE)).Build());
        // Road not connected to any building.
        Assert.Throws<InvalidOperationException>(() => new StateBuilder(TestBoards.Standard)
            .Road(0, Edge(0, 0, Side.E)).Build());
        // More cards than the bank holds.
        Assert.Throws<InvalidOperationException>(() => new StateBuilder(TestBoards.Standard)
            .Hand(0, ore: 10).Hand(1, ore: 10).Build());
    }

    [Fact]
    public void BuilderDerivesDevDeckAndAwards()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .DevCards(0, knight: 1, victoryPoint: 2)
            .KnightsPlayed(0, 3)
            .Phase(Phase.PreRoll)
            .Build();

        Assert.Equal(new[] { 10, 3, 2, 2, 2 }, s.DevDeck);
        Assert.Equal(3, s.DevPlayed[(int)DevCardType.Knight]);
        Assert.Equal(0, s.LargestArmyOwner);
        Assert.Equal(3, s.PublicVP[0]);   // settlement + Largest Army
        Assert.Equal(5, s.TotalVP(0));    // + 2 hidden VP cards
        Assert.False(s.HasRolled);
    }

    // ---- StateValidator catches drift ----

    [Fact]
    public void ValidatorCatchesEachKindOfCorruption()
    {
        void Expect(string fragment, Action<GameState> corrupt)
        {
            var s = SampleState();
            corrupt(s);
            var errors = StateValidator.Check(s);
            Assert.True(errors.Any(e => e.Contains(fragment)), $"expected an error containing \"{fragment}\", got: {string.Join(" | ", errors)}");
        }

        Expect("bank + hands", s => s.Bank[0]++);
        Expect("negative", s => { s.Hand[0] = -1; s.Bank[0] += 2; });
        Expect("deck + hands + played", s => s.DevDeck[2]--);
        Expect("KnightsPlayed sums", s => { s.DevPlayed[0]++; s.DevDeck[0]--; });
        Expect("bought this turn", s => s.DevBoughtThisTurn[(int)DevCardType.RoadBuilding] = 1); // seat 0 holds none
        Expect("settlements on board", s => s.SettlementsLeft[0]--);
        Expect("roads on board", s => s.RoadsLeft[1]++);
        Expect("level", s => s.VertexLevel[Vertex(0, 0, Corner.N)] = 0);
        Expect("distance rule", s => { s.VertexOwner[Vertex(0, 0, Corner.NE)] = 2; s.VertexLevel[Vertex(0, 0, Corner.NE)] = 1; s.SettlementsLeft[2]--; s.PublicVP[2]++; });
        Expect("isn't connected", s => { s.EdgeOwner[Edge(-2, 2, Side.SW)] = 0; s.RoadsLeft[0]--; });
        Expect("cached road length", s => s.RoadLength[0]++);
        Expect("cached public VP", s => s.PublicVP[1]++);
        Expect("holds Longest Road", s => { s.LongestRoadOwner = 1; s.PublicVP[1] += 2; });
        Expect("holds Largest Army", s => { s.LargestArmyOwner = 0; s.PublicVP[0] += 2; });
        Expect("Robber", s => s.RobberHex = 19);
        Expect("CurrentPlayer", s => s.CurrentPlayer = 4);
    }

    [Fact]
    public void SampleStateIsValid() => Assert.Empty(StateValidator.Check(SampleState()));

    /// <summary>A mid-game position touching most fields: two seats with buildings, roads, cards and a played Knight.</summary>
    private static GameState SampleState() => new StateBuilder(TestBoards.Standard)
        .Settlement(0, Vertex(0, 0, Corner.N))
        .Road(0, Edge(0, 0, Side.NE))
        .City(1, Vertex(-1, 1, Corner.S))
        .Road(1, Edge(-1, 1, Side.SW))
        .Hand(0, brick: 1, lumber: 2)
        .Hand(1, grain: 3, ore: 1)
        .DevCards(0, knight: 1, monopoly: 1)
        .DevCards(1, victoryPoint: 1)
        .BoughtThisTurn(DevCardType.Knight)
        .KnightsPlayed(1, 1)
        .Robber(4)
        .Phase(Phase.Main, current: 0)
        .Build();
}

public class LongestRoadTests
{
    private static int Ring(int side) => HexEdges[HexAt(0, 0), side];
    private static int Corner(Catan.Core.Corner c) => HexVertices[HexAt(0, 0), (int)c];

    [Fact]
    public void SingleRoadIsLengthOne()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, Corner(Core.Corner.N)).Road(0, Ring(0)).Build();
        Assert.Equal(1, s.RoadLength[0]);
    }

    [Fact]
    public void FiveInALineIsFiveAndClaimsLongestRoad()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Corner(Core.Corner.N))
            .Roads(0, Ring(0), Ring(1), Ring(2), Ring(3), Ring(4))
            .Build();
        Assert.Equal(5, s.RoadLength[0]);
        Assert.Equal(0, s.LongestRoadOwner);
        Assert.Equal(3, s.PublicVP[0]);
    }

    [Fact]
    public void RingOfSixCountsSix()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Corner(Core.Corner.N))
            .Roads(0, Ring(0), Ring(1), Ring(2), Ring(3), Ring(4), Ring(5))
            .Build();
        Assert.Equal(6, s.RoadLength[0]);
    }

    [Fact]
    public void ForkCountsOnlyTheLongerBranch()
    {
        // N -> NE -> SE, then at SE fork: one road on to S and one road outward.
        int se = Corner(Core.Corner.SE);
        int outward = Enumerable.Range(0, 3).Select(i => VertexEdges[se, i]).Single(e => e >= 0 && e != Ring(1) && e != Ring(2));
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Corner(Core.Corner.N))
            .Roads(0, Ring(0), Ring(1), Ring(2), outward)
            .Build();
        Assert.Equal(3, s.RoadLength[0]);
    }

    [Fact]
    public void OpponentSettlementCutsTheRoad()
    {
        // Five roads N -> NE -> SE -> S -> SW -> NW; an opponent settlement on S splits it into 3 + 2.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Corner(Core.Corner.N))
            .Roads(0, Ring(0), Ring(1), Ring(2), Ring(3), Ring(4))
            .Settlement(1, Corner(Core.Corner.S))
            .Build();
        Assert.Equal(3, s.RoadLength[0]);
        Assert.Equal(-1, s.LongestRoadOwner);
    }

    [Fact]
    public void OwnBuildingDoesNotCutTheRoad()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Corner(Core.Corner.N))
            .Settlement(0, Corner(Core.Corner.S))
            .Roads(0, Ring(0), Ring(1), Ring(2), Ring(3), Ring(4))
            .Build();
        Assert.Equal(5, s.RoadLength[0]);
    }
}
