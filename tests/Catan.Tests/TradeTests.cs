using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

/// <summary>
/// Trades on TestBoards.Standard. Harbor spot 0 (generic 3:1) is on hex (2,0)'s E side; spot 1 (brick 2:1) is on hex (1,1)'s SE side.
/// Player trades follow the colonist.io model: several open offers, every offer to all opponents, opponents answer at any time
/// in any order (accept, decline or counter), and the current player confirms, rejects or cancels.
/// </summary>
public class TradeTests
{
    private static readonly int GenericHarbor = Vertex(2, 0, Corner.NE);
    private static readonly int BrickHarbor = Vertex(1, 1, Corner.S);
    private static readonly int Inland = Vertex(0, 0, Corner.N);

    private static void Do(GameState s, GameAction a, List<GameEvent>? events = null)
    {
        Rules.ApplyChecked(s, a, new ScriptedChance(), events);
        var errors = StateValidator.Check(s);
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
    }

    private static GameAction Bank(int seat, Resource give, int count, Resource get) =>
        new(ActionType.BankTrade, seat, Give: ResourceSet.Of(give, count), Get: ResourceSet.Of(get));

    private static int[] Ratios(GameState s, int seat)
    {
        var ratios = new int[5];
        Rules.TradeRatios(s, seat, ratios);
        return ratios;
    }

    // ---- Bank trades ----

    [Fact]
    public void FourToOneWithoutAHarbor()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, Inland).Hand(0, ore: 4).Phase(Phase.Main).Build();
        Assert.Equal(new[] { 4, 4, 4, 4, 4 }, Ratios(s, 0));
        Assert.False(Rules.IsLegal(s, Bank(0, Resource.Ore, 3, Resource.Wool), out string reason));
        Assert.Contains("4:1", reason);

        var events = new List<GameEvent>();
        Do(s, Bank(0, Resource.Ore, 4, Resource.Wool), events);
        Assert.Equal(new ResourceSet(0, 0, 1, 0, 0), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(19, s.Bank[(int)Resource.Ore]);
        Assert.Contains(new BankTraded(0, ResourceSet.Of(Resource.Ore, 4), ResourceSet.Of(Resource.Wool)), events);
    }

    [Fact]
    public void GenericHarborGivesThreeToOne()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, GenericHarbor).Hand(0, grain: 3).Phase(Phase.Main).Build();
        Assert.Equal(new[] { 3, 3, 3, 3, 3 }, Ratios(s, 0));
        Do(s, Bank(0, Resource.Grain, 3, Resource.Brick));
        Assert.Equal(1, s.Hand[(int)Resource.Brick]);
    }

    [Fact]
    public void ResourceHarborGivesTwoToOneForThatResourceOnly()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, BrickHarbor).Hand(0, brick: 2, ore: 2).Phase(Phase.Main).Build();
        Assert.Equal(new[] { 2, 4, 4, 4, 4 }, Ratios(s, 0));
        Assert.True(Rules.IsLegal(s, Bank(0, Resource.Brick, 2, Resource.Ore), out _));
        Assert.False(Rules.IsLegal(s, Bank(0, Resource.Ore, 2, Resource.Brick), out _));
    }

    [Fact]
    public void BothHarborsCombine()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, BrickHarbor).City(0, GenericHarbor).Phase(Phase.Main).Build();
        Assert.Equal(new[] { 2, 3, 3, 3, 3 }, Ratios(s, 0));
    }

    [Fact]
    public void HarborNeedsYourOwnBuildingOnIt()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(1, GenericHarbor).Settlement(0, Inland).Phase(Phase.Main).Build();
        Assert.Equal(new[] { 4, 4, 4, 4, 4 }, Ratios(s, 0));
        Assert.Equal(new[] { 3, 3, 3, 3, 3 }, Ratios(s, 1));
    }

    [Fact]
    public void BankTradeShapeIsChecked()
    {
        var s = new StateBuilder(TestBoards.Standard).Hand(0, brick: 4, ore: 4).Hand(1, wool: 19).Phase(Phase.Main).Build();
        Assert.False(Rules.IsLegal(s, Bank(0, Resource.Ore, 4, Resource.Ore), out string self));
        Assert.Contains("for itself", self);
        Assert.False(Rules.IsLegal(s, Bank(0, Resource.Ore, 4, Resource.Wool), out string empty));
        Assert.Contains("no Wool left", empty);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BankTrade, 0,
            Give: new ResourceSet(2, 0, 0, 0, 2), Get: ResourceSet.Of(Resource.Grain)), out string mixed));
        Assert.Contains("single resource", mixed);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.BankTrade, 0,
            Give: ResourceSet.Of(Resource.Ore, 4), Get: ResourceSet.Of(Resource.Grain, 2)), out string two));
        Assert.Contains("exactly 1", two);
    }

    [Fact]
    public void BankTradesAreListed()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, BrickHarbor).Hand(0, brick: 2, ore: 4).Phase(Phase.Main).Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(8, legal.Count(a => a.Type == ActionType.BankTrade)); // brick 2:1 and ore 4:1, each for 4 other resources
        TestPlay.AssertListMatchesIsLegal(s);
    }

    // ---- Player trades ----

    private static readonly ResourceSet OneOre = ResourceSet.Of(Resource.Ore);
    private static readonly ResourceSet OneWool = ResourceSet.Of(Resource.Wool);
    private static readonly ResourceSet TwoWool = ResourceSet.Of(Resource.Wool, 2);
    private static readonly ResourceSet OneGrain = ResourceSet.Of(Resource.Grain);

    private static GameAction Offer(ResourceSet give, ResourceSet get, int seat = 0) => new(ActionType.OfferTrade, seat, Give: give, Get: get);
    private static GameAction Edit(int slot, ResourceSet give, ResourceSet get) => new(ActionType.EditOffer, 0, slot, Give: give, Get: get);
    private static GameAction Counter(int seat, int slot, ResourceSet give, ResourceSet get) => new(ActionType.CounterOffer, seat, slot, Give: give, Get: get);
    private static GameAction Accept(int seat, int slot) => new(ActionType.AcceptOffer, seat, slot);
    private static GameAction Decline(int seat, int slot, int partner = -1) => new(ActionType.DeclineOffer, seat, slot, partner);
    private static GameAction Confirm(int slot, int partner) => new(ActionType.ConfirmTrade, 0, slot, partner);
    private static GameAction Cancel(int seat, int slot) => new(ActionType.CancelOffer, seat, slot);

    /// <summary>Seat 0 (current, Main) has 3 ore; seat 1 has 1 wool; seat 2 has 3 wool; seat 3 has 2 wool and 2 grain.</summary>
    private static GameState TradeTable(GameSettings? settings = null) => new StateBuilder(TestBoards.Standard, settings)
        .Hand(0, ore: 3).Hand(1, wool: 1).Hand(2, wool: 3).Hand(3, wool: 2, grain: 2)
        .Phase(Phase.Main)
        .Build();

    private static int[] Optional(GameState s)
    {
        var canAct = new bool[4];
        Rules.OptionalSeats(s, canAct);
        return Enumerable.Range(0, 4).Where(i => canAct[i]).ToArray();
    }

    [Fact]
    public void AnOfferGoesToEveryOpponentAtOnceAndNobodyIsWaitedOn()
    {
        var s = TradeTable();
        var events = new List<GameEvent>();
        Do(s, Offer(OneOre, TwoWool), events);

        Assert.Equal(Phase.Main, s.Phase);
        Assert.Equal(0, Rules.ActingSeat(s));            // the game still waits only on the current player
        Assert.Equal(new[] { 1, 2, 3 }, Optional(s));    // every opponent may answer right now
        Assert.Contains(new TradeOffered(0, 0, OneOre, TwoWool), events);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, 1, legal);
        Assert.Equal(new[] { Decline(1, 0) }, legal);    // seat 1 can't pay 2 wool
        Rules.GetLegalActions(s, 2, legal);
        Assert.Equal(new[] { Accept(2, 0), Decline(2, 0) }, legal);
        TestPlay.AssertListMatchesIsLegal(s);
    }

    [Fact]
    public void OpponentsAnswerInAnyOrderAndTheCurrentPlayerCanConfirmRightAway()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        Do(s, Accept(3, 0));                              // seat 3 answers first; seats 1 and 2 haven't answered
        Assert.True(Rules.IsLegal(s, Confirm(0, 3), out _));

        Do(s, Accept(2, 0));
        Do(s, Decline(1, 0));
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Contains(Confirm(0, 2), legal);
        Assert.Contains(Confirm(0, 3), legal);

        var events = new List<GameEvent>();
        Do(s, Confirm(0, 3), events);                    // the current player picks who to trade with
        Assert.Equal(new ResourceSet(0, 0, 2, 0, 2), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(new ResourceSet(0, 0, 0, 2, 1), ResourceSet.From(s.HandOf(3)));
        Assert.False(s.Offers[0].IsActive);
        Assert.Contains(new TradeDone(0, 3, OneOre, TwoWool), events);
    }

    [Fact]
    public void OpponentsCanAnswerEachVersionOnceAndMustHoldTheCards()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        Assert.False(Rules.IsLegal(s, Accept(1, 0), out string cantPay));
        Assert.Contains("don't have the cards", cantPay);
        Do(s, Decline(1, 0));
        Assert.False(Rules.IsLegal(s, Decline(1, 0), out string twice));
        Assert.Contains("already answered", twice);
        Assert.False(Rules.IsLegal(s, Accept(1, 5), out string noSlot));
        Assert.Contains("no open trade", noSlot);
    }

    [Fact]
    public void TheCurrentPlayerCanTurnDownAnAcceptance()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        Do(s, Accept(2, 0));
        var events = new List<GameEvent>();
        Do(s, Decline(0, 0, partner: 2), events);
        Assert.False(Rules.IsLegal(s, Confirm(0, 2), out string reason));
        Assert.Contains("who accepted", reason);
        Assert.Contains(new TradeRejected(0, 0, 2), events);
        Assert.True(s.Offers[0].IsActive); // still open to the others
    }

    [Fact]
    public void SeveralOffersCanBeOpenAtOnce()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        Do(s, Offer(OneOre, OneGrain));
        Do(s, Accept(3, 1));                              // seat 3 takes the second offer only
        Do(s, Decline(3, 0));
        Assert.Equal(2, s.OffersThisTurn);
        Assert.True(Rules.IsLegal(s, Confirm(1, 3), out _));
        Assert.False(Rules.IsLegal(s, Confirm(0, 3), out _));

        Do(s, Confirm(1, 3));
        Assert.True(s.Offers[0].IsActive);                // the other offer stays open
        Assert.Equal(new[] { 1, 2 }, Optional(s));
    }

    [Fact]
    public void TenOffersAndEditsPerTurn()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        for (int i = 0; i < 9; i++)
            Do(s, Edit(0, OneOre, i % 2 == 0 ? OneWool : TwoWool));
        Assert.Equal(10, s.OffersThisTurn);
        Assert.False(Rules.IsLegal(s, Offer(OneOre, OneGrain), out string offer));
        Assert.Contains("all 10", offer);
        Assert.False(Rules.IsLegal(s, Edit(0, OneOre, OneGrain), out string edit));
        Assert.Contains("all 10", edit);

        Do(s, new GameAction(ActionType.EndTurn, 0));
        Assert.Equal(0, s.OffersThisTurn);
        Assert.All(s.Offers, o => Assert.False(o.IsActive)); // open trades close at the end of the turn
    }

    [Fact]
    public void AtMostTenOffersOpenAtOnce()
    {
        var s = TradeTable(new GameSettings { MaxOffersPerTurn = 20 });
        for (int i = 0; i < 10; i++)
            Do(s, Offer(OneOre, OneWool));
        Assert.False(Rules.IsLegal(s, Offer(OneOre, OneWool), out string reason));
        Assert.Contains("at most 10 offers open", reason);
        Do(s, Cancel(0, 4));
        Assert.True(Rules.IsLegal(s, Offer(OneOre, OneWool), out _));
    }

    [Fact]
    public void EditingResetsResponses()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        Do(s, Accept(2, 0));
        var events = new List<GameEvent>();
        Do(s, Edit(0, OneOre, OneWool), events);
        Assert.Equal(TradeOffer.NoResponse, s.Offers[0].ResponseOf(2));
        Assert.False(Rules.IsLegal(s, Confirm(0, 2), out _));
        Assert.Contains(new TradeEdited(0, 0, OneOre, OneWool), events);

        Do(s, Accept(1, 0)); // seat 1 can afford the new terms
        Assert.True(Rules.IsLegal(s, Confirm(0, 1), out _));
    }

    [Fact]
    public void ACounterOfferGoesToTheCurrentPlayerWhoCanAcceptIt()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        var events = new List<GameEvent>();
        Do(s, Counter(3, 0, OneWool, OneOre), events);   // seat 3: "1 wool for your 1 ore instead"
        var counter = s.Offers[1];
        Assert.True(counter.IsCounter);
        Assert.Equal(3, counter.From);
        Assert.Equal(TradeOffer.Countered, s.Offers[0].ResponseOf(3));
        Assert.Contains(new TradeCountered(3, 1, 0, OneWool, OneOre), events);

        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Contains(Accept(0, 1), legal);
        Assert.Contains(Decline(0, 1), legal);

        events.Clear();
        Do(s, Accept(0, 1), events);                     // accepting a counter trades immediately
        Assert.Equal(new ResourceSet(0, 0, 1, 0, 2), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(new ResourceSet(0, 0, 1, 2, 1), ResourceSet.From(s.HandOf(3)));
        Assert.False(s.Offers[1].IsActive);
        Assert.Contains(new TradeDone(0, 3, OneOre, OneWool), events);
    }

    [Fact]
    public void CounterRules()
    {
        var s = TradeTable();
        Do(s, Offer(OneOre, TwoWool));
        Assert.False(Rules.IsLegal(s, Counter(0, 0, OneOre, OneWool), out string self));
        Assert.Contains("Only other players", self);
        Assert.False(Rules.IsLegal(s, Counter(1, 0, OneGrain, OneOre), out string cantPay));
        Assert.Contains("don't have the cards", cantPay);

        Do(s, Counter(3, 0, OneWool, OneOre));
        Assert.False(Rules.IsLegal(s, Counter(3, 0, OneGrain, OneOre), out string twice));
        Assert.Contains("already answered", twice);          // once per version
        Assert.False(Rules.IsLegal(s, Accept(2, 1), out string notYours));
        Assert.Contains("Only the current player", notYours); // only the current player answers counters

        // After an edit seat 3 may counter again; its new counter replaces the old one.
        Do(s, Edit(0, OneOre, OneWool));
        Do(s, Counter(3, 0, OneGrain, OneOre));
        Assert.Single(s.Offers, o => o.IsActive && o.IsCounter);
        Assert.Equal(OneGrain, s.Offers[1].Give);

        // Seat 3 can withdraw it; the current player can decline a counter.
        Do(s, Cancel(3, 1));
        Do(s, Counter(2, 0, OneWool, OneOre));
        int slot = Array.FindIndex(s.Offers, o => o.IsActive && o.IsCounter);
        Do(s, Decline(0, slot));
        Assert.DoesNotContain(s.Offers, o => o.IsActive && o.IsCounter);
    }

    [Fact]
    public void ConfirmNeedsBothSidesToStillHoldTheCards()
    {
        var s = TradeTable();
        Do(s, Offer(ResourceSet.Of(Resource.Ore, 3), OneWool));
        Do(s, Accept(1, 0));
        Do(s, Offer(OneOre, OneGrain));
        Do(s, Accept(3, 1));
        Do(s, Confirm(1, 3));                               // seat 0 now has only 2 ore
        Assert.False(Rules.IsLegal(s, Confirm(0, 1), out string reason));
        Assert.Contains("no longer has the cards", reason);
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.DoesNotContain(Confirm(0, 1), legal);
        Assert.Contains(Decline(0, 0, partner: 1), legal);
    }

    [Fact]
    public void NoGiftsAndNoResourceOnBothSides()
    {
        var s = TradeTable();
        Assert.False(Rules.IsLegal(s, Offer(OneOre, default), out string gift));
        Assert.Contains("no gifts", gift);
        Assert.False(Rules.IsLegal(s, Offer(default, TwoWool), out _));
        Assert.False(Rules.IsLegal(s, Offer(OneOre, new ResourceSet(0, 0, 1, 0, 1)), out string both));
        Assert.Contains("both sides", both);
        Assert.False(Rules.IsLegal(s, Offer(ResourceSet.Of(Resource.Ore, 4), TwoWool), out string cantPay));
        Assert.Contains("don't have the cards", cantPay);
    }

    [Fact]
    public void OnlyTheCurrentPlayerOffersAndOnlyInMain()
    {
        var s = TradeTable();
        Assert.False(Rules.IsLegal(s, Offer(OneWool, OneOre, seat: 2), out string notYou));
        Assert.Contains("seat 0's turn", notYou);

        s = new StateBuilder(TestBoards.Standard).Hand(0, ore: 4).Phase(Phase.PreRoll).Build();
        Assert.False(Rules.IsLegal(s, Offer(OneOre, OneWool), out string preRoll));
        Assert.Contains("Roll the dice first", preRoll);
        Assert.False(Rules.IsLegal(s, Bank(0, Resource.Ore, 4, Resource.Wool), out _));
    }

    [Fact]
    public void AnswersOnlyDuringMain()
    {
        var s = new StateBuilder(TestBoards.Standard).Hand(0, ore: 3).Hand(2, wool: 3).DevCards(0, knight: 1).Phase(Phase.Main).Build();
        Do(s, Offer(OneOre, TwoWool));
        Do(s, new GameAction(ActionType.PlayKnight, 0));   // now in MoveRobber
        Assert.False(Rules.IsLegal(s, Accept(2, 0), out _));
        Assert.Empty(Optional(s));
        Do(s, new GameAction(ActionType.MoveRobber, 0, 18));
        Assert.True(Rules.IsLegal(s, Accept(2, 0), out _)); // back in Main, the offer is still open
    }

    [Fact]
    public void RandomBuildersAreAlwaysLegal()
    {
        var rng = new Rng(4);
        for (int trial = 0; trial < 300; trial++)
        {
            var s = new StateBuilder(TestBoards.Standard)
                .Hand(0, ResourceSet.From(Enumerable.Range(0, 5).Select(_ => rng.NextInt(3)).ToArray()))
                .Hand(1, ResourceSet.From(Enumerable.Range(0, 5).Select(_ => rng.NextInt(3)).ToArray()))
                .Phase(Phase.Main).Build();
            if (Rules.RandomTradeOffer(s, rng) is not { } offer)
            {
                Assert.Equal(0, s.HandSize(0));
                continue;
            }
            Assert.True(Rules.IsLegal(s, offer, out string reason), reason);
            Rules.Apply(s, offer, new ScriptedChance());
            if (Rules.RandomEditOffer(s, rng) is { } edit)
                Assert.True(Rules.IsLegal(s, edit, out reason), reason);
            if (Rules.RandomCounterOffer(s, 1, rng) is { } counter)
                Assert.True(Rules.IsLegal(s, counter, out reason), reason);
            else
                Assert.Equal(0, s.HandSize(1));
        }
    }

    [Fact]
    public void RandomGamesWithTradesStayValid()
    {
        var legal = new List<GameAction>();
        int trades = 0, counterTrades = 0, bankTrades = 0;
        for (ulong seed = 0; seed < 60; seed++)
        {
            var rng = new Rng(seed);
            var chance = new RngChance(rng);
            var s = new GameState(BoardGenerator.Balanced(rng), new GameSettings { MaxTurns = 200 });
            var events = new List<GameEvent>();
            int actions = 0;
            while (s.Phase != Phase.GameOver)
            {
                var action = TestPlay.RandomAction(s, legal, rng);
                if (actions++ % 10 == 0 && s.Offers.Any(o => o.IsActive))
                    TestPlay.AssertListMatchesIsLegal(s);
                bool acceptingCounter = action.Type == ActionType.AcceptOffer && action.Seat == s.CurrentPlayer;
                events.Clear();
                Rules.ApplyChecked(s, action, chance, events);
                int done = events.Count(e => e is TradeDone);
                trades += done;
                if (acceptingCounter)
                    counterTrades += done;
                bankTrades += events.Count(e => e is BankTraded);
                var errors = StateValidator.Check(s);
                Assert.True(errors.Count == 0, $"seed {seed}: {string.Join(" | ", errors)}");
            }
        }
        // Counts depend on the random boards and moves; these floors only make sure trading really happens.
        Assert.True(trades > 25, $"only {trades} player trades completed");
        Assert.True(counterTrades > 5, $"only {counterTrades} counter-offers were accepted");
        Assert.True(bankTrades > 20, $"only {bankTrades} bank trades");
    }
}
