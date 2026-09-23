using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests;

/// <summary>
/// Trades on TestBoards.Standard. Harbor spot 0 (generic 3:1) is on hex (2,0)'s E side; spot 1 (brick 2:1) is on hex (1,1)'s SE side.
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

    private static GameAction Offer(int seat, int toMask, ResourceSet give, ResourceSet get) =>
        new(ActionType.OfferTrade, seat, toMask, Give: give, Get: get);

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
        TestPlay.AssertListMatchesIsLegal(s, legal);
    }

    [Fact]
    public void OnlyTheCurrentPlayerTradesAndOnlyInMain()
    {
        var s = new StateBuilder(TestBoards.Standard).Hand(0, ore: 4).Hand(1, ore: 4).Phase(Phase.PreRoll).Build();
        Assert.False(Rules.IsLegal(s, Bank(0, Resource.Ore, 4, Resource.Wool), out string preRoll));
        Assert.Contains("Roll the dice first", preRoll);
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0010, ResourceSet.Of(Resource.Ore), ResourceSet.Of(Resource.Wool)), out _));

        s = new StateBuilder(TestBoards.Standard).Hand(0, ore: 4).Hand(1, ore: 4).Phase(Phase.Main).Build();
        Assert.False(Rules.IsLegal(s, Bank(1, Resource.Ore, 4, Resource.Wool), out string notYou));
        Assert.Contains("seat 0's turn", notYou);
    }

    // ---- Player trades ----

    /// <summary>Seat 0 (current, Main) has 2 ore; seat 1 has 1 wool; seat 2 has 3 wool; seat 3 has nothing.</summary>
    private static GameState TradeTable() => new StateBuilder(TestBoards.Standard)
        .Hand(0, ore: 2).Hand(1, wool: 1).Hand(2, wool: 3)
        .Phase(Phase.Main)
        .Build();

    private static readonly ResourceSet OneOre = ResourceSet.Of(Resource.Ore);
    private static readonly ResourceSet TwoWool = ResourceSet.Of(Resource.Wool, 2);

    [Fact]
    public void FullTradeOfferReplyConfirm()
    {
        var s = TradeTable();
        var events = new List<GameEvent>();
        Do(s, Offer(0, 0b1110, OneOre, TwoWool), events);
        Assert.Equal(Phase.TradeReply, s.Phase);
        Assert.Equal(1, s.OffersThisTurn);

        // Replies go in turn order after seat 0. Seat 1 has only 1 wool: it can't accept.
        Assert.Equal(1, Rules.ActingSeat(s));
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(new[] { new GameAction(ActionType.DeclineOffer, 1) }, legal);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.AcceptOffer, 1), out string cantPay));
        Assert.Contains("don't have the cards", cantPay);
        Do(s, new GameAction(ActionType.DeclineOffer, 1), events);

        Assert.Equal(2, Rules.ActingSeat(s));
        Do(s, new GameAction(ActionType.AcceptOffer, 2), events);
        Assert.Equal(3, Rules.ActingSeat(s));
        Do(s, new GameAction(ActionType.DeclineOffer, 3), events);

        Assert.Equal(Phase.TradeConfirm, s.Phase);
        Assert.Equal(0, Rules.ActingSeat(s));
        Rules.GetLegalActions(s, legal);
        Assert.Equal(new[] { new GameAction(ActionType.ConfirmTrade, 0, 2), new GameAction(ActionType.CancelOffer, 0) }, legal);
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.ConfirmTrade, 0, 1), out string declined));
        Assert.Contains("who accepted", declined);

        Do(s, new GameAction(ActionType.ConfirmTrade, 0, 2), events);
        Assert.Equal(Phase.Main, s.Phase);
        Assert.Equal(new ResourceSet(0, 0, 2, 0, 1), ResourceSet.From(s.HandOf(0)));
        Assert.Equal(new ResourceSet(0, 0, 1, 0, 1), ResourceSet.From(s.HandOf(2)));
        Assert.False(s.Offer.IsActive);
        Assert.Contains(new TradeOffered(0, OneOre, TwoWool, 0b1110), events);
        Assert.Contains(new TradeReplied(2, true), events);
        Assert.Contains(new TradeDone(0, 2, OneOre, TwoWool), events);
    }

    [Fact]
    public void OnlyOfferedSeatsReply()
    {
        var s = TradeTable();
        Do(s, Offer(0, 0b0100, OneOre, TwoWool)); // only seat 2
        Assert.Equal(2, Rules.ActingSeat(s));
        Do(s, new GameAction(ActionType.AcceptOffer, 2));
        Assert.Equal(Phase.TradeConfirm, s.Phase);
    }

    [Fact]
    public void CancelLeavesHandsUntouched()
    {
        var s = TradeTable();
        Do(s, Offer(0, 0b0100, OneOre, TwoWool));
        Do(s, new GameAction(ActionType.AcceptOffer, 2));
        var events = new List<GameEvent>();
        Do(s, new GameAction(ActionType.CancelOffer, 0), events);
        Assert.Equal(Phase.Main, s.Phase);
        Assert.Equal(2, s.Hand[(int)Resource.Ore]);
        Assert.Equal(3, s.Hand[2 * 5 + (int)Resource.Wool]);
        Assert.Contains(new TradeCancelled(0), events);
    }

    [Fact]
    public void AllDeclinedLeavesOnlyCancel()
    {
        var s = TradeTable();
        Do(s, Offer(0, 0b0010, OneOre, ResourceSet.Of(Resource.Wool)));
        Do(s, new GameAction(ActionType.DeclineOffer, 1));
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(new[] { new GameAction(ActionType.CancelOffer, 0) }, legal);
    }

    [Fact]
    public void NoGiftsAndNoResourceOnBothSides()
    {
        var s = TradeTable();
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0010, OneOre, default), out string gift));
        Assert.Contains("no gifts", gift);
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0010, default, TwoWool), out _));
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0010, OneOre, new ResourceSet(0, 0, 1, 0, 1)), out string both));
        Assert.Contains("both sides", both);
    }

    [Fact]
    public void OfferMustBeAffordableAndGoToOthers()
    {
        var s = TradeTable();
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0010, ResourceSet.Of(Resource.Ore, 3), TwoWool), out string cantPay));
        Assert.Contains("don't have the cards", cantPay);
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0001, OneOre, TwoWool), out string self));
        Assert.Contains("other player", self);
        Assert.False(Rules.IsLegal(s, Offer(0, 0, OneOre, TwoWool), out _));
        Assert.False(Rules.IsLegal(s, Offer(0, 0b10000, OneOre, TwoWool), out _));
    }

    [Fact]
    public void OfferCapHolds()
    {
        var s = TradeTable(); // default cap: 3 offers per turn
        for (int i = 0; i < 3; i++)
        {
            Do(s, Offer(0, 0b0010, OneOre, TwoWool));
            Do(s, new GameAction(ActionType.DeclineOffer, 1));
            Do(s, new GameAction(ActionType.CancelOffer, 0));
        }
        Assert.False(Rules.IsLegal(s, Offer(0, 0b0010, OneOre, TwoWool), out string reason));
        Assert.Contains("maximum of 3", reason);

        Do(s, new GameAction(ActionType.EndTurn, 0));
        Assert.Equal(0, s.OffersThisTurn);
    }

    [Fact]
    public void NothingElseHappensWhileAnOfferIsOpen()
    {
        var s = TradeTable();
        Do(s, Offer(0, 0b0100, OneOre, TwoWool));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.EndTurn, 0), out _));
        Do(s, new GameAction(ActionType.AcceptOffer, 2));
        Assert.False(Rules.IsLegal(s, new GameAction(ActionType.EndTurn, 0), out string reason));
        Assert.Contains("Confirm the trade", reason);
    }

    [Fact]
    public void RandomOffersAreAlwaysLegal()
    {
        var rng = new Rng(4);
        for (int trial = 0; trial < 300; trial++)
        {
            var hand = ResourceSet.From(Enumerable.Range(0, 5).Select(_ => rng.NextInt(3)).ToArray());
            var s = new StateBuilder(TestBoards.Standard).Hand(0, hand).Phase(Phase.Main).Build();
            var offer = Rules.RandomTradeOffer(s, rng);
            if (hand.Total == 0)
                Assert.Null(offer);
            else
                Assert.True(Rules.IsLegal(s, offer!.Value, out string reason), reason);
        }
    }

    [Fact]
    public void RandomGamesWithTradesStayValid()
    {
        var legal = new List<GameAction>();
        int trades = 0, bankTrades = 0;
        for (ulong seed = 0; seed < 60; seed++)
        {
            var rng = new Rng(seed);
            var chance = new RngChance(rng);
            var s = new GameState(BoardGenerator.Balanced(rng), new GameSettings { MaxTurns = 200 });
            var events = new List<GameEvent>();
            while (s.Phase != Phase.GameOver)
            {
                var action = TestPlay.RandomAction(s, legal, rng);
                if (s.Phase is Phase.TradeReply or Phase.TradeConfirm || action.Type == ActionType.BankTrade)
                    TestPlay.AssertListMatchesIsLegal(s, legal);
                events.Clear();
                Rules.ApplyChecked(s, action, chance, events);
                trades += events.Count(e => e is TradeDone);
                bankTrades += events.Count(e => e is BankTraded);
                var errors = StateValidator.Check(s);
                Assert.True(errors.Count == 0, $"seed {seed}: {string.Join(" | ", errors)}");
            }
        }
        Assert.True(trades > 20, $"only {trades} player trades completed");
        Assert.True(bankTrades > 20, $"only {bankTrades} bank trades");
    }
}
