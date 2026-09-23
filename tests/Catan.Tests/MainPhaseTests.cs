using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

public class MainPhaseTests
{
    private static readonly int CenterN = Vertex(0, 0, Corner.N);
    private static readonly int CenterNE = Vertex(0, 0, Corner.NE);
    private static readonly int CenterSE = Vertex(0, 0, Corner.SE);
    private static readonly int CenterS = Vertex(0, 0, Corner.S);

    private static int Ring(Side side) => Edge(0, 0, side);

    private static void Do(GameState s, ActionType type, int target = -1, IChance? chance = null, List<GameEvent>? events = null)
    {
        Rules.ApplyChecked(s, new GameAction(type, s.CurrentPlayer, target), chance ?? new ScriptedChance(), events);
        var errors = StateValidator.Check(s);
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
    }

    private static bool Legal(GameState s, ActionType type, int target, out string reason) =>
        Rules.IsLegal(s, new GameAction(type, s.CurrentPlayer, target), out reason);

    /// <summary>Seat 0: settlement at the center hex's N corner with a road on its NE side.</summary>
    private static StateBuilder Base() => new StateBuilder(TestBoards.Standard)
        .Settlement(0, CenterN)
        .Road(0, Ring(Side.NE))
        .Phase(Phase.Main, current: 0);

    // ---- Costs ----

    [Fact]
    public void EachBuildCostsExactlyItsPrice()
    {
        var s = Base().Hand(0, brick: 3, lumber: 3, wool: 2, grain: 4, ore: 4).Road(0, Ring(Side.E)).Build();

        Do(s, ActionType.BuildRoad, Ring(Side.SE));
        Assert.Equal(new ResourceSet(2, 2, 2, 4, 4), ResourceSet.From(s.HandOf(0)));

        Do(s, ActionType.BuildSettlement, CenterSE);
        Assert.Equal(new ResourceSet(1, 1, 1, 3, 4), ResourceSet.From(s.HandOf(0)));

        Do(s, ActionType.BuildCity, CenterN);
        Assert.Equal(new ResourceSet(1, 1, 1, 1, 1), ResourceSet.From(s.HandOf(0)));

        Do(s, ActionType.BuyDevCard, chance: new ScriptedChance().Draw(DevCardType.Knight));
        Assert.Equal(new ResourceSet(1, 1, 0, 0, 0), ResourceSet.From(s.HandOf(0)));
    }

    [Fact]
    public void CantBuildWithoutTheCards()
    {
        var s = Base().Hand(0, brick: 1, grain: 2, ore: 2).Build(); // no lumber, too little ore, no wool
        Assert.False(Legal(s, ActionType.BuildRoad, Ring(Side.E), out string road));
        Assert.Contains("1 brick and 1 lumber", road);
        Assert.False(Legal(s, ActionType.BuildCity, CenterN, out string city));
        Assert.Contains("2 grain and 3 ore", city);
        Assert.False(Legal(s, ActionType.BuyDevCard, -1, out _));

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(new[] { new GameAction(ActionType.EndTurn, 0) }, legal);
    }

    // ---- Piece limits ----

    [Fact]
    public void PieceLimitsHold()
    {
        var s = Base().Hand(0, brick: 5, lumber: 5, wool: 5, grain: 5, ore: 5).Road(0, Ring(Side.E)).Build();
        s.RoadsLeft[0] = 0;
        s.SettlementsLeft[0] = 0;
        s.CitiesLeft[0] = 0;
        Assert.False(Legal(s, ActionType.BuildRoad, Ring(Side.SE), out string r));
        Assert.Contains("no roads left", r);
        Assert.False(Legal(s, ActionType.BuildSettlement, CenterSE, out string st));
        Assert.Contains("no settlements left", st);
        Assert.False(Legal(s, ActionType.BuildCity, CenterN, out string c));
        Assert.Contains("no cities left", c);
    }

    [Fact]
    public void CityReturnsTheSettlementPiece()
    {
        var s = Base().Hand(0, grain: 2, ore: 3).Build();
        var events = new List<GameEvent>();
        Do(s, ActionType.BuildCity, CenterN, events: events);
        Assert.Equal(2, s.VertexLevel[CenterN]);
        Assert.Equal(5, s.SettlementsLeft[0]);
        Assert.Equal(3, s.CitiesLeft[0]);
        Assert.Equal(2, s.PublicVP[0]);
        Assert.Contains(new Built(0, PieceType.City, CenterN), events);
    }

    [Fact]
    public void CityMustReplaceYourOwnSettlement()
    {
        var s = Base().Settlement(1, Vertex(2, -2, Corner.N)).Hand(0, grain: 2, ore: 3).Build();
        Assert.False(Legal(s, ActionType.BuildCity, Vertex(2, -2, Corner.N), out string theirs));
        Assert.Contains("one of your settlements", theirs);
        Assert.False(Legal(s, ActionType.BuildCity, CenterS, out _));
    }

    // ---- Placement ----

    [Fact]
    public void SettlementNeedsARoadOutsideSetup()
    {
        var s = Base().Road(0, Ring(Side.E)).Hand(0, brick: 1, lumber: 1, wool: 1, grain: 1).Build();
        Assert.True(Legal(s, ActionType.BuildSettlement, CenterSE, out _));
        Assert.False(Legal(s, ActionType.BuildSettlement, Vertex(2, -2, Corner.N), out string reason));
        Assert.Contains("touch one of your roads", reason);
    }

    [Fact]
    public void SettlementStillObeysTheDistanceRule()
    {
        var s = Base().Hand(0, brick: 1, lumber: 1, wool: 1, grain: 1).Build();
        Assert.False(Legal(s, ActionType.BuildSettlement, CenterNE, out string reason)); // on your road, but next to your settlement
        Assert.Contains("distance rule", reason);
    }

    [Fact]
    public void RoadConnectsToYourBuildingOrRoad()
    {
        var s = Base().Hand(0, brick: 2, lumber: 2).Build();
        Assert.True(Legal(s, ActionType.BuildRoad, Ring(Side.NW), out _));  // touches the settlement
        Assert.True(Legal(s, ActionType.BuildRoad, Ring(Side.E), out _));   // extends the road
        Assert.False(Legal(s, ActionType.BuildRoad, Ring(Side.SW), out string reason));
        Assert.Contains("must connect", reason);
    }

    [Fact]
    public void CantBuildARoadThroughAnOpponentsSettlement()
    {
        // Seat 0's road runs N -> NE -> SE; seat 1 has a settlement on SE.
        var s = Base().Road(0, Ring(Side.E)).Settlement(1, CenterSE).Hand(0, brick: 1, lumber: 1).Build();
        Assert.False(Legal(s, ActionType.BuildRoad, Ring(Side.SE), out string reason));
        Assert.Contains("opponent's settlement", reason);
    }

    [Fact]
    public void CantBuildOnAnOccupiedEdge()
    {
        var s = Base().Hand(0, brick: 1, lumber: 1).Build();
        Assert.False(Legal(s, ActionType.BuildRoad, Ring(Side.NE), out string reason));
        Assert.Contains("already has a road", reason);
    }

    // ---- Dev cards ----

    [Fact]
    public void BuyingADevCardDrawsFromTheDeck()
    {
        var s = Base().Hand(0, wool: 1, grain: 1, ore: 1).Build();
        var events = new List<GameEvent>();
        Do(s, ActionType.BuyDevCard, chance: new ScriptedChance().Draw(DevCardType.Monopoly), events: events);
        Assert.Equal(1, s.DevHand[0 * 5 + (int)DevCardType.Monopoly]);
        Assert.Equal(1, s.DevBoughtThisTurn[(int)DevCardType.Monopoly]);
        Assert.Equal(1, s.DevDeck[(int)DevCardType.Monopoly]);
        Assert.Contains(new DevCardBought(0, DevCardType.Monopoly), events);
    }

    [Fact]
    public void CantBuyFromAnEmptyDeck()
    {
        var s = Base().Hand(0, wool: 1, grain: 1, ore: 1).DevCards(1, knight: 14, victoryPoint: 5, roadBuilding: 2, yearOfPlenty: 2, monopoly: 2).Build();
        Assert.False(Legal(s, ActionType.BuyDevCard, -1, out string reason));
        Assert.Contains("deck is empty", reason);
    }

    // ---- Turn flow ----

    [Fact]
    public void EndTurnPassesToTheNextSeatsPreRoll()
    {
        var s = Base().Hand(0, wool: 1, grain: 1, ore: 1).Build();
        s.TurnNumber = 7;
        Do(s, ActionType.BuyDevCard, chance: new ScriptedChance().Draw(DevCardType.Knight));
        var events = new List<GameEvent>();
        Do(s, ActionType.EndTurn, events: events);

        Assert.Equal(Phase.PreRoll, s.Phase);
        Assert.Equal(1, s.CurrentPlayer);
        Assert.Equal(8, s.TurnNumber);
        Assert.False(s.HasRolled);
        Assert.All(s.DevBoughtThisTurn, n => Assert.Equal(0, n));
        Assert.Equal(1, s.DevHand[0 * 5 + (int)DevCardType.Knight]); // still owned, now playable
        Assert.Contains(new TurnEnded(0), events);
    }

    [Fact]
    public void SeatThreeWrapsToSeatZero()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 3).Build();
        Do(s, ActionType.EndTurn);
        Assert.Equal(0, s.CurrentPlayer);
    }

    [Fact]
    public void CantRollTwice()
    {
        var s = Base().Build();
        Assert.False(Legal(s, ActionType.RollDice, -1, out string reason));
        Assert.Contains("already rolled", reason);
    }

    [Fact]
    public void TurnCapEndsTheGameAsADraw()
    {
        var s = new StateBuilder(TestBoards.Standard, new GameSettings { MaxTurns = 10 }).Phase(Phase.Main, current: 1).Build();
        s.TurnNumber = 10;
        var events = new List<GameEvent>();
        Do(s, ActionType.EndTurn, events: events);
        Assert.Equal(Phase.GameOver, s.Phase);
        Assert.Equal(-1, s.Winner);
        Assert.Contains(new GameEnded(-1), events);
        Assert.Equal(-1, Rules.ActingSeat(s));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.RollDice, 2), out string reason));
        Assert.Contains("game is over", reason);
    }

    // ---- Longest Road (basic; the full FAQ set comes with step 11) ----

    [Fact]
    public void FifthRoadClaimsLongestRoad()
    {
        var s = Base().Roads(0, Ring(Side.E), Ring(Side.SE), Ring(Side.SW)).Hand(0, brick: 1, lumber: 1).Build();
        var events = new List<GameEvent>();
        Do(s, ActionType.BuildRoad, Ring(Side.W), events: events);
        Assert.Equal(5, s.RoadLength[0]);
        Assert.Equal(0, s.LongestRoadOwner);
        Assert.Equal(3, s.PublicVP[0]);
        Assert.Contains(new AwardChanged(Award.LongestRoad, -1, 0), events);
    }

    [Fact]
    public void SettlementThatCutsTheHoldersRoadTakesTheAwardAway()
    {
        // Seat 0: 5 roads around the center hex from N to NW. Seat 1 builds on S, splitting it into 3 + 2.
        int below = Vertex(-1, 2, Corner.N);
        int seat1Home = Vertex(0, 1, Corner.S);
        var s = Base()
            .Roads(0, Ring(Side.E), Ring(Side.SE), Ring(Side.SW), Ring(Side.W))
            .Settlement(1, seat1Home)
            .Roads(1, EdgeBetween(seat1Home, below), EdgeBetween(below, CenterS))
            .Hand(1, brick: 1, lumber: 1, wool: 1, grain: 1)
            .Phase(Phase.Main, current: 1)
            .Build();
        Assert.Equal(0, s.LongestRoadOwner);

        var events = new List<GameEvent>();
        Do(s, ActionType.BuildSettlement, CenterS, events: events);
        Assert.Equal(3, s.RoadLength[0]);
        Assert.Equal(-1, s.LongestRoadOwner);
        Assert.Equal(1, s.PublicVP[0]);
        Assert.Contains(new AwardChanged(Award.LongestRoad, 0, -1), events);
    }

    // ---- Whole games ----

    [Fact]
    public void RandomGamesStayValidAndLegalListMatchesIsLegal()
    {
        var legal = new List<GameAction>();
        for (ulong seed = 0; seed < 60; seed++)
        {
            var rng = new Rng(seed);
            var chance = new RngChance(rng);
            var s = new GameState(BoardGenerator.Balanced(rng), new GameSettings { MaxTurns = 200 });
            int actions = 0;
            while (s.Phase != Phase.GameOver)
            {
                Rules.GetLegalActions(s, legal);
                Assert.NotEmpty(legal);
                if (actions % 25 == 0)
                    AssertListMatchesIsLegal(s, legal);

                Rules.ApplyChecked(s, legal[rng.NextInt(legal.Count)], chance);
                actions++;
                var errors = StateValidator.Check(s);
                Assert.True(errors.Count == 0, $"seed {seed}, action {actions}: {string.Join(" | ", errors)}");
            }
            Assert.Equal(201, s.TurnNumber);
        }
    }

    private static void AssertListMatchesIsLegal(GameState s, List<GameAction> legal)
    {
        var listed = legal.ToHashSet();
        int seat = Rules.ActingSeat(s);
        var candidates = new List<GameAction>
        {
            new(ActionType.RollDice, seat), new(ActionType.BuyDevCard, seat), new(ActionType.EndTurn, seat),
        };
        for (int e = 0; e < EdgeCount; e++)
            candidates.Add(new(ActionType.BuildRoad, seat, e));
        for (int v = 0; v < VertexCount; v++)
        {
            candidates.Add(new(ActionType.BuildSettlement, seat, v));
            candidates.Add(new(ActionType.BuildCity, seat, v));
        }
        foreach (var a in candidates)
            Assert.True(listed.Contains(a) == Rules.IsLegal(s, a, out string reason), $"{a} listed={listed.Contains(a)} but IsLegal says: {reason}");
    }
}
