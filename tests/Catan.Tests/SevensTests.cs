using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

public class SevensTests
{
    private const int Desert = 9; // TestBoards.Standard

    private static void Do(GameState s, GameAction a, IChance? chance = null, List<GameEvent>? events = null)
    {
        Rules.ApplyChecked(s, a, chance ?? new ScriptedChance(), events);
        var errors = StateValidator.Check(s);
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
    }

    private static GameAction Discard(int seat, int brick = 0, int lumber = 0, int wool = 0, int grain = 0, int ore = 0) =>
        new(ActionType.Discard, seat, Give: new ResourceSet(brick, lumber, wool, grain, ore));

    private static GameAction Rob(int seat, int hex, int victim = -1) => new(ActionType.MoveRobber, seat, hex, victim);

    // ---- Discards ----

    /// <summary>Seat 1 is about to roll. Hands: seat 0 has 8 (owes 4), seat 1 has 7 (owes 0), seat 2 has 9 (owes 4), seat 3 has 15 (owes 7).</summary>
    private static GameState BigHands() => new StateBuilder(TestBoards.Standard)
        .Hand(0, ore: 8)
        .Hand(1, grain: 7)
        .Hand(2, wool: 9)
        .Hand(3, brick: 15)
        .Phase(Phase.PreRoll, current: 1)
        .Build();

    [Fact]
    public void OnlyHandsOverSevenOweHalfRoundedDown()
    {
        var s = BigHands();
        Do(s, new GameAction(ActionType.RollDice, 1), new ScriptedChance().Roll(7));
        Assert.Equal(Phase.Discard, s.Phase);
        Assert.Equal(new[] { 4, 0, 4, 7 }, s.DiscardOwed);
    }

    [Fact]
    public void DiscardsGoLowestSeatFirstThenTheCurrentPlayerMovesTheRobber()
    {
        var s = BigHands();
        Do(s, new GameAction(ActionType.RollDice, 1), new ScriptedChance().Roll(7));

        Assert.Equal(0, Rules.ActingSeat(s));
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Empty(legal); // discards are built by the agent, not enumerated

        var events = new List<GameEvent>();
        Do(s, Discard(0, ore: 4), events: events);
        Assert.Contains(new Discarded(0, new ResourceSet(0, 0, 0, 0, 4)), events);
        Assert.Equal(2, Rules.ActingSeat(s));
        Do(s, Discard(2, wool: 4));
        Assert.Equal(3, Rules.ActingSeat(s));
        Do(s, Discard(3, brick: 7));

        Assert.Equal(Phase.MoveRobber, s.Phase);
        Assert.Equal(1, Rules.ActingSeat(s));
        Assert.Equal(new[] { 4, 7, 5, 8 }, Enumerable.Range(0, 4).Select(s.HandSize));
        Assert.Equal(19 - 8 + 4, s.Bank[(int)Resource.Ore]);
    }

    [Fact]
    public void DiscardMustBeExactlyTheOwedCountFromCardsYouHold()
    {
        var s = BigHands();
        Do(s, new GameAction(ActionType.RollDice, 1), new ScriptedChance().Roll(7));

        Assert.False(Rules.IsLegal(s, Discard(0, ore: 3), out string tooFew));
        Assert.Contains("exactly 4", tooFew);
        Assert.False(Rules.IsLegal(s, Discard(0, ore: 5), out _));
        Assert.False(Rules.IsLegal(s, Discard(0, brick: 4), out string notHeld));
        Assert.Contains("cards you hold", notHeld);
        Assert.False(Rules.IsLegal(s, Discard(0, brick: -1, ore: 5), out string negative));
        Assert.Contains("negative", negative);
        Assert.False(Rules.IsLegal(s, Discard(2, wool: 4), out string early));
        Assert.Contains("seat 0's turn", early);
        Assert.False(Rules.IsLegal(s, Rob(0, 0), out _));
    }

    [Fact]
    public void NobodyOverSevenGoesStraightToTheRobber()
    {
        var s = new StateBuilder(TestBoards.Standard).Hand(0, ore: 7).Phase(Phase.PreRoll).Build();
        Do(s, new GameAction(ActionType.RollDice, 0), new ScriptedChance().Roll(7));
        Assert.Equal(Phase.MoveRobber, s.Phase);
        Assert.All(s.DiscardOwed, n => Assert.Equal(0, n));
    }

    [Fact]
    public void RandomDiscardIsAlwaysLegal()
    {
        var rng = new Rng(21);
        for (int trial = 0; trial < 300; trial++)
        {
            int[] hand = Enumerable.Range(0, 5).Select(_ => rng.NextInt(5)).ToArray();
            if (hand.Sum() <= 7)
                hand[rng.NextInt(5)] += 8 - hand.Sum() + rng.NextInt(4);
            var s = new StateBuilder(TestBoards.Standard).Hand(0, ResourceSet.From(hand)).Phase(Phase.PreRoll).Build();
            Do(s, new GameAction(ActionType.RollDice, 0), new ScriptedChance().Roll(7));

            var discard = Rules.RandomDiscard(s, 0, rng);
            Assert.True(Rules.IsLegal(s, discard, out string reason), reason);
            Assert.Equal(hand.Sum() / 2, discard.Give.Total);
        }
    }

    // ---- Moving the robber and stealing ----

    /// <summary>Hex 0 (Hills 6): seat 1 on N with cards, seat 2 on SE with no cards, seat 0 (moving the robber) on SW.</summary>
    private static StateBuilder RobberSpot(bool hasRolled = true, GameSettings? settings = null) =>
        new StateBuilder(TestBoards.Standard, settings)
            .Settlement(1, Vertex(0, -2, Corner.N))
            .Settlement(2, Vertex(0, -2, Corner.SE))
            .Settlement(0, Vertex(0, -2, Corner.SW))
            .Hand(1, grain: 2, ore: 1)
            .Hand(0, brick: 1)
            .Phase(Phase.MoveRobber, current: 0, hasRolled: hasRolled);

    [Fact]
    public void RobberMustMoveToAnotherHex()
    {
        var s = RobberSpot().Build();
        Assert.False(Rules.IsLegal(s, Rob(0, Desert), out string reason));
        Assert.Contains("different hex", reason);
        Assert.False(Rules.IsLegal(s, Rob(0, 19), out _));
    }

    [Fact]
    public void OnlyOpponentsOnTheHexWithCardsCanBeRobbed()
    {
        var s = RobberSpot().Build();
        Assert.True(Rules.IsLegal(s, Rob(0, 0, victim: 1), out _));
        Assert.False(Rules.IsLegal(s, Rob(0, 0, victim: 2), out _));  // no cards
        Assert.False(Rules.IsLegal(s, Rob(0, 0, victim: 0), out _));  // yourself
        Assert.False(Rules.IsLegal(s, Rob(0, 0, victim: 3), out _));  // not on the hex
        Assert.False(Rules.IsLegal(s, Rob(0, 0), out string mustSteal));
        Assert.Contains("Choose an opponent", mustSteal);
    }

    [Fact]
    public void EmptyHexIsALegalNonSteal()
    {
        var s = RobberSpot().Build();
        Assert.True(Rules.IsLegal(s, Rob(0, 18), out _));
        Assert.False(Rules.IsLegal(s, Rob(0, 18, victim: 1), out _));

        var events = new List<GameEvent>();
        Do(s, Rob(0, 18), events: events);
        Assert.Equal(18, s.RobberHex);
        Assert.Equal(new[] { new RobberMoved(0, 18) }, events.Cast<GameEvent>());
        Assert.Equal(Phase.Main, s.Phase);
    }

    [Fact]
    public void StealTakesOneCardOfTheChosenKind()
    {
        var s = RobberSpot().Build();
        var events = new List<GameEvent>();
        Do(s, Rob(0, 0, victim: 1), new ScriptedChance().Steal(Resource.Grain), events);
        Assert.Equal(0, s.RobberHex);
        Assert.Equal(new ResourceSet(1, 0, 0, 1, 0), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(new ResourceSet(0, 0, 0, 1, 1), ResourceSet.From(s.HandOf(1)));
        Assert.Contains(new CardStolen(0, 1, (int)Resource.Grain), events);
    }

    [Fact]
    public void RobberReturnsToPreRollWhenTheDiceHaventBeenRolled()
    {
        // Knight before rolling (step 10) moves the robber with HasRolled false.
        var s = RobberSpot(hasRolled: false).Build();
        Do(s, Rob(0, 18));
        Assert.Equal(Phase.PreRoll, s.Phase);
    }

    [Fact]
    public void LegalListHasOneMovePerHexOrVictim()
    {
        var s = RobberSpot().Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(18, legal.Count); // 18 other hexes; hex 0 has one eligible victim
        Assert.Contains(Rob(0, 0, victim: 1), legal);
        TestPlay.AssertListMatchesIsLegal(s);
    }

    // ---- Friendly robber ----

    [Fact]
    public void FriendlyRobberProtectsPlayersWithTwoOrFewerPoints()
    {
        var friendly = new GameSettings { FriendlyRobber = true };
        var s = RobberSpot(settings: friendly).Build();
        Assert.False(Rules.IsLegal(s, Rob(0, 0, victim: 1), out string reason));
        Assert.Contains("Friendly robber", reason);
        Assert.True(Rules.IsLegal(s, Rob(0, 18), out _));

        // Seats 1 and 2 at 3 points each: hex 0 is fair game again.
        s = RobberSpot(settings: friendly)
            .Settlement(1, Vertex(2, -2, Corner.S)).Settlement(1, Vertex(-2, 2, Corner.S))
            .Settlement(2, Vertex(0, 2, Corner.S)).Settlement(2, Vertex(-2, 1, Corner.N))
            .Build();
        Assert.Equal(3, s.PublicVP[1]);
        Assert.True(Rules.IsLegal(s, Rob(0, 0, victim: 1), out _));
    }

    [Fact]
    public void FriendlyRobberAllowsEverythingWhenEveryHexIsProtected()
    {
        // Six settlements (two per opponent, 2 VP each) that together touch all 18 non-desert hexes.
        var cover = FindExactCover();
        var builder = new StateBuilder(TestBoards.Standard, new GameSettings { FriendlyRobber = true })
            .Phase(Phase.MoveRobber, current: 0);
        for (int i = 0; i < cover.Count; i++)
            builder.Settlement(1 + i / 2, cover[i]);
        var s = builder.Build();

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(18, legal.Count);
        Assert.True(Rules.IsLegal(s, Rob(0, 0), out _));
        TestPlay.AssertListMatchesIsLegal(s);

        // Turn the rule off for comparison: nothing changes, since no one has cards to steal.
        s = new StateBuilder(TestBoards.Standard).Phase(Phase.MoveRobber).Build();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(18, legal.Count);
    }

    /// <summary>Backtracking search for 6 vertices obeying the distance rule whose hexes exactly cover the 18 non-desert hexes.</summary>
    private static List<int> FindExactCover()
    {
        var candidates = Enumerable.Range(0, VertexCount)
            .Where(v => Enumerable.Range(0, 3).All(i => VertexHexes[v, i] >= 0 && VertexHexes[v, i] != Desert))
            .ToList();
        var chosen = new List<int>();
        var covered = new bool[HexCount];
        covered[Desert] = true;

        bool Search()
        {
            int next = Array.IndexOf(covered, false);
            if (next < 0)
                return true;
            foreach (int v in candidates)
            {
                var hexes = Enumerable.Range(0, 3).Select(i => VertexHexes[v, i]).ToArray();
                if (!hexes.Contains(next) || hexes.Any(h => covered[h]))
                    continue;
                if (chosen.Any(c => c == v || Enumerable.Range(0, 3).Any(i => VertexNeighbors[v, i] == c)))
                    continue;
                chosen.Add(v);
                foreach (int h in hexes) covered[h] = true;
                if (Search())
                    return true;
                foreach (int h in hexes) covered[h] = false;
                chosen.RemoveAt(chosen.Count - 1);
            }
            return false;
        }

        Assert.True(Search(), "no exact cover found");
        Assert.Equal(6, chosen.Count);
        return chosen;
    }

    // ---- Whole flow ----

    [Fact]
    public void RollSevenDiscardMoveStealThenMain()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(1, Vertex(0, -2, Corner.N))
            .Hand(0, lumber: 9)
            .Hand(1, grain: 2)
            .Phase(Phase.PreRoll, current: 0)
            .Build();

        Do(s, new GameAction(ActionType.RollDice, 0), new ScriptedChance().Roll(7));
        Assert.Equal(Phase.Discard, s.Phase);
        Do(s, Discard(0, lumber: 4));
        Assert.Equal(Phase.MoveRobber, s.Phase);
        Do(s, Rob(0, 0, victim: 1), new ScriptedChance().Steal(Resource.Grain));
        Assert.Equal(Phase.Main, s.Phase);
        Assert.Equal(new ResourceSet(0, 5, 0, 1, 0), ResourceSet.From(s.HandOf(0)));
    }
}
