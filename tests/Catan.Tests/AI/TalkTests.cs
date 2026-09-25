using Catan.AI;
using Catan.AI.Talk;
using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests.AI;

public class TalkTests
{
    private static readonly string[] Names = { "red", "blue", "orange", "white" };
    private const int Blue = 1, Orange = 2, White = 3;

    private static Reading Read(string text, int speaker = Blue, int target = Orange) => PhraseReader.Read(text, speaker, Names, target);

    private static PromiseTerm Nb => new(PromiseKind.NoBlock);
    private static PromiseTerm Ns => new(PromiseKind.NoSteal);

    [Theory]
    [InlineData("wheat nb?")]
    [InlineData("wheat nb")]
    [InlineData("Wheat NB?")]
    [InlineData("nb for wheat")]
    [InlineData("I won't block you if you give me a wheat")]
    [InlineData("i wont put the robber on you for a wheat")]
    public void NonBlockForAWheat(string text)
    {
        var r = Read(text);
        Assert.Equal(TalkKind.Proposal, r.Kind);
        Assert.Equal(new[] { Nb }, r.SpeakerPromises);
        Assert.Empty(r.TargetPromises);
        Assert.Equal(ResourceSet.Of(Resource.Grain), r.SpeakerGets);
        Assert.Equal(0, r.SpeakerGives.Total);
        Assert.Equal(Orange, r.Target);
    }

    [Fact]
    public void ShorthandCombinations()
    {
        Assert.Equal(new[] { Nb, Ns }, Read("wheat nb ns").SpeakerPromises);
        Assert.Equal(new[] { Ns }, Read("ore ns?").SpeakerPromises);
        Assert.Equal(new ResourceSet(0, 0, 0, 0, 2), Read("2 ore nb").SpeakerGets);
        Assert.Equal(new[] { Nb, Ns }, Read("I won't rob you for a sheep").SpeakerPromises); // "rob" = both
    }

    [Fact]
    public void NonBlockForNonBlockIsASwap()
    {
        var r = Read("nb for nb");
        Assert.Equal(new[] { Nb }, r.SpeakerPromises);
        Assert.Equal(new[] { Nb }, r.TargetPromises);
        Assert.Equal(0, r.SpeakerGets.Total + r.SpeakerGives.Total);
    }

    [Fact]
    public void RequestsAskTheTargetAndThePriceIsPaidByTheSpeaker()
    {
        var r = Read("don't block me and I'll give you an ore");
        Assert.Empty(r.SpeakerPromises);
        Assert.Equal(new[] { Nb }, r.TargetPromises);
        Assert.Equal(ResourceSet.Of(Resource.Ore), r.SpeakerGives);

        var nbMe = Read("nb me? wheat");
        Assert.Equal(new[] { Nb }, nbMe.TargetPromises);
        Assert.Equal(ResourceSet.Of(Resource.Grain), nbMe.SpeakerGives);
    }

    [Fact]
    public void TradeWithAPromiseOnTop()
    {
        var r = Read("wheat for sheep nb");
        Assert.Equal(ResourceSet.Of(Resource.Grain), r.SpeakerGives);
        Assert.Equal(ResourceSet.Of(Resource.Wool), r.SpeakerGets);
        Assert.Equal(new[] { Nb }, r.SpeakerPromises);
    }

    [Fact]
    public void NamesAndLengths()
    {
        Assert.Equal(White, Read("white, wheat nb?").Target);
        Assert.Equal(-1, Read("anyone wheat nb?").Target);
        Assert.Equal(2, Read("nb for 2 turns for a wheat").Length);   // the next 2 robber moves
        Assert.Equal(2, Read("nb for two knights for a wheat").Length);
        Assert.Null(Read("wheat nb").Length);                           // the next robber move
        Assert.Equal("You won't block orange the next 2 times you move the robber, if orange gives a wheat (for a card of your choice)",
            Read("nb for 2 turns for a wheat").Describe(Names, Blue));
    }

    [Fact]
    public void SpotsAreNamedByTheirNumbers()
    {
        var board = TestBoards.Standard;
        // A corner whose three numbers no other corner has.
        int spot = Enumerable.Range(0, VertexCount).First(v => Spots.NumbersAt(board, v).Count == 3 && Spots.Find(board, Spots.NumbersAt(board, v)).Count == 1);
        var numbers = Spots.NumbersAt(board, spot);
        string name = string.Join(" ", numbers);

        foreach (string said in new[] { name, string.Join("/", numbers), string.Join(" ", numbers.AsEnumerable().Reverse()) })
        {
            var r = PhraseReader.Read($"i wont take {said} for a brick", Blue, Names, Orange, board);
            Assert.Equal(new[] { new PromiseTerm(PromiseKind.NoBuild, spot) }, r.SpeakerPromises);
            Assert.Null(r.Length); // the rest of the game
            Assert.Equal(ResourceSet.Of(Resource.Brick), r.SpeakerGets);
        }
        Assert.Equal($"You won't take the {name} spot from orange for the rest of the game, if orange gives a brick (for a card of your choice)",
            PhraseReader.Read($"i wont take {name} for a brick", Blue, Names, Orange, board).Describe(Names, Blue, board));

        var request = PhraseReader.Read($"dont take my {name} and ill give you a sheep", Blue, Names, Orange, board);
        Assert.Equal(new[] { new PromiseTerm(PromiseKind.NoBuild, spot) }, request.TargetPromises);
        Assert.Equal(ResourceSet.Of(Resource.Wool), request.SpeakerGives);

        var nowhere = PhraseReader.Read("i wont take 12 12 12", Blue, Names, Orange, board);
        Assert.Equal(TalkKind.Chatter, nowhere.Kind);
        Assert.Contains("no 12 12 12 spot", nowhere.Describe(Names, Blue, board));

        Assert.Equal(ResourceSet.Of(Resource.Ore, 2), PhraseReader.Read("2 ore nb", Blue, Names, Orange, board).SpeakerGets); // a count, not a spot

        // Numbers several corners share: only the open ones count.
        var shared = Enumerable.Range(0, VertexCount).Select(v => Spots.NumbersAt(board, v)).First(n => n.Count >= 2 && Spots.Find(board, n).Count > 1);
        var corners = Spots.Find(board, shared);
        string sharedName = string.Join(" ", shared);
        Assert.Equal(TalkKind.Chatter, PhraseReader.Read($"i wont take {sharedName}", Blue, Names, Orange, board).Kind);
        var narrowed = PhraseReader.Read($"i wont take {sharedName}", Blue, Names, Orange, board, v => v == corners[1]);
        Assert.Equal(new[] { new PromiseTerm(PromiseKind.NoBuild, corners[1]) }, narrowed.SpeakerPromises);
    }

    [Theory]
    [InlineData("deal", TalkKind.Accept)]
    [InlineData("OK!", TalkKind.Accept)]
    [InlineData("sure", TalkKind.Accept)]
    [InlineData("nah", TalkKind.Refuse)]
    [InlineData("no thanks", TalkKind.Refuse)]
    [InlineData("gg", TalkKind.Chatter)]
    [InlineData("anyone have wheat?", TalkKind.Chatter)]
    [InlineData("lol red is winning", TalkKind.Chatter)]
    public void AnswersAndSmallTalk(string text, TalkKind kind) => Assert.Equal(kind, Read(text).Kind);

    [Fact]
    public void DescriptionsReadNaturally()
    {
        Assert.Equal("You won't block orange the next time you move the robber, if orange gives a wheat (for a card of your choice)",
            Read("wheat nb?").Describe(Names, Blue));
        Assert.Equal("Blue won't block you the next time they move the robber, if you give a wheat (for a card of blue's choice)",
            Read("wheat nb?").Describe(Names, Orange));
    }

    // ---- Deal book ----

    private static GameState Board() =>
        new StateBuilder(TestBoards.Standard)
            .Settlement(Orange, Vertex(0, -2, Corner.N))
            .Settlement(White, Vertex(0, 0, Corner.N))
            .Phase(Phase.MoveRobber, current: Blue)
            .Build();

    [Fact]
    public void BlockingOrStealingBreaksTheRightPromise()
    {
        var s = Board();
        var book = new DealBook();
        book.Add(Blue, Orange, Nb, s.TurnNumber);
        book.Add(Blue, White, Ns, s.TurnNumber);

        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1)));       // Orange's hex
        Assert.Null(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 9, -1)));          // desert
        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 9, White)));    // stealing from White
        Assert.Null(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Orange, 0, -1)));        // Orange promised nothing
    }

    [Fact]
    public void PromisesRunOutAndBreakersAreTrustedLessForAWhile()
    {
        var s = Board();
        var book = new DealBook();
        int spot = Vertex(0, 0, Corner.S);
        book.Add(Blue, Orange, new PromiseTerm(PromiseKind.NoBuild, spot), s.TurnNumber, length: 1);
        s.TurnNumber += 4;
        Assert.Null(book.WouldBreak(s, new GameAction(ActionType.BuildSettlement, Blue, spot))); // a spot for 1 turn: expired

        book.Add(Blue, Orange, Nb, s.TurnNumber);
        Assert.Equal(1, book.Trust(Blue, s.TurnNumber));
        Assert.Single(book.Record(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1)));
        Assert.Equal(0.5, book.Trust(Blue, s.TurnNumber));
        Assert.True(book.Trust(Blue, s.TurnNumber + 8) > 0.5);
        Assert.Equal(1, book.Trust(Blue, s.TurnNumber + 40));
        Assert.Empty(book.Record(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1))); // a broken promise no longer binds
    }

    [Fact]
    public void AnNbWithNoLengthCoversOnlyTheNextRobberMove()
    {
        var s = Board();
        var book = new DealBook();
        book.Add(Blue, Orange, Nb, s.TurnNumber);
        s.TurnNumber += 40; // no time limit
        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1)));

        Assert.Empty(book.Record(s, new GameAction(ActionType.MoveRobber, Blue, 9, -1))); // kept: the desert
        Assert.Null(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1))); // used up
        Assert.Equal(1, book.Trust(Blue, s.TurnNumber));
    }

    [Fact]
    public void ALengthOnNbCountsRobberMoves()
    {
        var s = Board();
        var book = new DealBook();
        book.Add(Blue, Orange, Nb, s.TurnNumber, length: 2);
        Assert.Empty(book.Record(s, new GameAction(ActionType.MoveRobber, Blue, 9, -1)));
        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1))); // one move left
        Assert.Empty(book.Record(s, new GameAction(ActionType.MoveRobber, Blue, 9, -1)));
        Assert.Null(book.WouldBreak(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1)));    // both used
    }

    [Fact]
    public void SettlingOnOrNextToAPromisedSpotBreaksIt()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: Blue).Build();
        var book = new DealBook();
        int spot = Vertex(0, 0, Corner.S);
        book.Add(Blue, Orange, new PromiseTerm(PromiseKind.NoBuild, spot), s.TurnNumber);
        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.BuildSettlement, Blue, spot)));
        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.BuildSettlement, Blue, VertexNeighbors[spot, 0])));
        Assert.Null(book.WouldBreak(s, new GameAction(ActionType.BuildSettlement, Blue, Vertex(0, -2, Corner.N))));
        s.TurnNumber += 400; // no length said: it holds for the rest of the game
        Assert.NotNull(book.WouldBreak(s, new GameAction(ActionType.BuildSettlement, Blue, spot)));
    }

    [Fact]
    public void TheBookSurvivesSaving()
    {
        var book = new DealBook();
        book.Add(Blue, Orange, Nb, 12);
        book.Add(White, Blue, new PromiseTerm(PromiseKind.NoBuild, 7), 13, length: 2);
        var back = DealBook.FromJson(book.ToJson());
        Assert.Equal(book.Promises, back.Promises);
    }
}

public class PromiseKeepingTests
{
    private static readonly string[] Names = { "red", "blue", "orange", "white" };
    private const int Blue = 1, Orange = 2, White = 3;

    private static GameState RobberDecision() =>
        new StateBuilder(TestBoards.Standard)
            .Settlement(Blue, Vertex(0, 2, Corner.N))
            .Settlement(Orange, Vertex(0, -2, Corner.N)).City(Orange, Vertex(1, -2, Corner.N))
            .Settlement(White, Vertex(0, 0, Corner.N))
            .Hand(Orange, brick: 2, grain: 2).Hand(White, ore: 1)
            .Phase(Phase.MoveRobber, current: Blue)
            .Build();

    private static GameAction Decide(IPlayerAgent bot, GameState s)
    {
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, Blue, legal);
        return bot.DecideAsync(PlayerView.From(s, Blue), legal, default).Result;
    }

    [Fact]
    public void BotsDontBlockSomeoneTheyPromised()
    {
        var weights = new BotWeights();
        var s = RobberDecision();

        foreach (var bot in new IPlayerAgent[]
        {
            new SmartBot(weights, SmartBotSettings.Play, 3),
            new SearchBot(weights, WinModel.Default, new SearchSettings { Iterations = 200, Threads = 1 }, 3),
        })
        {
            var book = new DealBook();
            foreach (int other in new[] { Orange, White })
            {
                book.Add(Blue, other, new PromiseTerm(PromiseKind.NoBlock), s.TurnNumber);
                book.Add(Blue, other, new PromiseTerm(PromiseKind.NoSteal), s.TurnNumber);
            }
            if (bot is SmartBot smart) smart.Deals = book; else ((SearchBot)bot).Deals = book;
            var kept = Decide(bot, s);
            Assert.Null(book.WouldBreak(s, kept));
        }
    }

    [Fact]
    public void ABreakHasToBeWorthMoreThanTheMargin()
    {
        var s = RobberDecision();
        var view = PlayerView.From(s, Blue);
        var book = new DealBook();
        book.Add(Blue, Orange, new PromiseTerm(PromiseKind.NoBlock), s.TurnNumber);
        var legal = new[] { new GameAction(ActionType.MoveRobber, Blue, 0, -1), new GameAction(ActionType.MoveRobber, Blue, 9, -1) };

        var small = new[] { 15.0, 5.0 };   // breaking gains 10: not worth it
        PromiseKeeping.Penalize(book, view, legal, small);
        Assert.True(small[1] > small[0]);

        var big = new[] { 40.0, 5.0 };     // breaking gains 35: worth it
        PromiseKeeping.Penalize(book, view, legal, big);
        Assert.True(big[0] > big[1]);
    }

    [Fact]
    public void BrokenPromisesAreAnnounced()
    {
        var s = RobberDecision();
        var talk = new TableTalk(Names);
        talk.Deals.Add(Blue, Orange, new PromiseTerm(PromiseKind.NoBlock), s.TurnNumber);
        Assert.Empty(talk.OnAction(s, new GameAction(ActionType.MoveRobber, Blue, 9, -1)) );
        talk.Deals.Add(Blue, Orange, new PromiseTerm(PromiseKind.NoBlock), s.TurnNumber);
        Assert.Single(talk.OnAction(s, new GameAction(ActionType.MoveRobber, Blue, 0, -1)));
        Assert.Equal("Blue broke their promise to orange (not to block them).", talk.Lines.Single().Text);
        Assert.Equal(-1, talk.Lines.Single().Seat);
    }

    [Fact]
    public void BotsInAWholeGameRarelyBreakTheirWord()
    {
        // Every seat promises everyone nb and ns for its next ten robber moves; count how many robber moves break one.
        int moves = 0, breaks = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var talk = new TableTalk(Names);
            var bots = Enumerable.Range(0, 4).Select(i => new SmartBot(new BotWeights(), SmartBotSettings.Training, seed * 10 + (ulong)i) { Deals = talk.Deals }).ToArray();
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    if (a != b)
                    {
                        talk.Deals.Add(a, b, new PromiseTerm(PromiseKind.NoBlock), 0, length: 10);
                        talk.Deals.Add(a, b, new PromiseTerm(PromiseKind.NoSteal), 0, length: 10);
                    }
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))), bots, new RngChance(seed));
            runner.ActionApplied += (action, _) =>
            {
                if (action.Type == ActionType.MoveRobber)
                    moves++;
                breaks += talk.OnAction(runner.State, action).Count > 0 ? 1 : 0;
            };
            runner.RunAsync().GetAwaiter().GetResult();
        }
        Assert.True(moves > 0);
        Assert.True(breaks <= moves / 4, $"{breaks} of {moves} robber moves broke a promise");
    }

    private static bool TouchesSeat(GameState s, int hex, int seat) =>
        Enumerable.Range(0, 6).Any(c => s.VertexOwner[HexVertices[hex, c]] == seat);
}

public class DealTests
{
    private static readonly BotWeights Bundled = BotWeights.Load(Path.Combine(RepoRoot(), "game", "bots", "best.json"));

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    private static readonly string[] Names = { "red", "blue", "orange", "white" };
    private const int Red = 0, Blue = 1, Orange = 2, White = 3;
    private static PromiseTerm Nb => new(PromiseKind.NoBlock);

    [Fact]
    public void WhatBotsSayReadsBackAsTheDealTheyMean()
    {
        var sell = PhraseReader.Read("orange, wheat for wood nb?", Blue, Names, -1);
        Assert.Equal(Orange, sell.Target);
        Assert.Equal(new[] { Nb }, sell.SpeakerPromises);
        Assert.Equal(ResourceSet.Of(Resource.Grain), sell.SpeakerGives);
        Assert.Equal(ResourceSet.Of(Resource.Lumber), sell.SpeakerGets);

        var buy = PhraseReader.Read("orange, wheat for wood, nb me?", Blue, Names, -1);
        Assert.Empty(buy.SpeakerPromises);
        Assert.Equal(new[] { Nb }, buy.TargetPromises);
    }

    private static GameState Main(int current = Blue) =>
        new StateBuilder(TestBoards.Standard)
            .Settlement(Blue, Vertex(0, 2, Corner.N)).Settlement(Orange, Vertex(0, -2, Corner.N)).Settlement(White, Vertex(0, 0, Corner.N))
            .Hand(Blue, grain: 2, ore: 1).Hand(Orange, lumber: 2, wool: 1).Hand(White, lumber: 1)
            .Phase(Phase.Main, current: current)
            .Build();

    private static void Apply(GameState s, TableTalk talk, GameAction a)
    {
        Rules.ApplyChecked(s, a, new RngChance(1));
        talk.OnAction(s, a);
    }

    [Fact]
    public void PromisesRideOnTheTradeAndStartWhenItIsDoneWithTheRightPartner()
    {
        var s = Main();
        var talk = new TableTalk(Names);
        talk.Propose(PhraseReader.Read("orange, wheat for wood nb?", Blue, Names, -1), s.TurnNumber, s.CurrentPlayer);
        var offer = new GameAction(ActionType.OfferTrade, Blue, Give: ResourceSet.Of(Resource.Grain), Get: ResourceSet.Of(Resource.Lumber));
        Apply(s, talk, offer);
        Assert.NotNull(talk.DealOn(0));
        Apply(s, talk, new GameAction(ActionType.AcceptOffer, White, 0));
        Apply(s, talk, new GameAction(ActionType.AcceptOffer, Orange, 0));
        Assert.Empty(talk.Deals.Promises);

        Apply(s, talk, new GameAction(ActionType.ConfirmTrade, Blue, 0, Orange));
        var promise = Assert.Single(talk.Deals.Promises);
        Assert.Equal((Blue, Orange, PromiseKind.NoBlock), (promise.From, promise.To, promise.Term.Kind));
        Assert.Equal("Deal: blue won't block orange.", talk.Lines.Last().Text);
    }

    [Fact]
    public void ATradeWithSomeoneElseCarriesNoPromise()
    {
        var s = Main();
        var talk = new TableTalk(Names);
        talk.Propose(PhraseReader.Read("orange, wheat for wood nb?", Blue, Names, -1), s.TurnNumber, s.CurrentPlayer);
        Apply(s, talk, new GameAction(ActionType.OfferTrade, Blue, Give: ResourceSet.Of(Resource.Grain), Get: ResourceSet.Of(Resource.Lumber)));
        Apply(s, talk, new GameAction(ActionType.AcceptOffer, White, 0));
        Apply(s, talk, new GameAction(ActionType.ConfirmTrade, Blue, 0, White));
        Assert.Empty(talk.Deals.Promises);
    }

    [Fact]
    public void APromiseOnlySwapIsRecordedWhenTheOtherSaysDeal()
    {
        var s = Main();
        var talk = new TableTalk(Names);
        talk.Hear(Orange, "nb for nb", s, Blue);
        Assert.Empty(talk.Deals.Promises);
        talk.Hear(Blue, "deal", s, Orange);
        Assert.Equal(2, talk.Deals.Promises.Count);
        Assert.Contains(talk.Deals.Promises, p => p.From == Orange && p.To == Blue);
        Assert.Contains(talk.Deals.Promises, p => p.From == Blue && p.To == Orange);
    }

    [Fact]
    public void ABotAnswersADealSaidToItAndMakesTheOffer()
    {
        var s = Main(current: Orange);
        var talk = new TableTalk(Names);
        talk.Hear(Blue, "dont block me and ill give you an ore", s, Orange);
        var bot = new SmartBot(new BotWeights(), SmartBotSettings.Play, 5) { Table = talk };
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, Orange, legal);
        var move = bot.DecideAsync(PlayerView.From(s, Orange), legal, default).Result;

        var answer = talk.Lines.Last(l => l.Seat == Orange).Text;
        Assert.Contains(answer, new[] { "deal", "no thanks" });
        if (answer == "deal")
        {
            Assert.Equal(ActionType.OfferTrade, move.Type);
            Assert.Equal(ResourceSet.Of(Resource.Ore), move.Get);
            Assert.True(Rules.IsLegal(s, move, out _));
        }
        Assert.Empty(talk.OpenProposalsFor(Orange, s.TurnNumber));
    }

    [Fact]
    public void NoNewDealsOnceSomeoneHasFivePoints()
    {
        var s = Main(current: Orange);
        s.PublicVP[White] = 5;
        var talk = new TableTalk(Names);
        talk.Hear(Blue, "dont block me and ill give you an ore", s, Orange);
        var bot = new SmartBot(new BotWeights(), SmartBotSettings.Play, 5) { Table = talk };
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, Orange, legal);
        bot.DecideAsync(PlayerView.From(s, Orange), legal, default).Wait();
        Assert.Equal("too late for deals", talk.Lines.Last(l => l.Seat == Orange).Text);
    }

    [Fact]
    public void WholeGamesWithDealsStayLegal()
    {
        // The game's trained weights (the plain defaults judge deals differently and may make none).
        int deals = 0;
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var talk = new TableTalk(Names);
            var bots = Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(Bundled, SmartBotSettings.Play, seed * 10 + (ulong)i) { Table = talk }).ToArray();
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))), bots, new RngChance(seed), validate: true);
            runner.ActionApplied += (a, _) => talk.OnAction(runner.State, a);
            runner.RunAsync().GetAwaiter().GetResult();
            Assert.True(runner.IsOver);
            deals += talk.Lines.Count(l => l.Text.StartsWith("Deal:"));
            Assert.All(talk.Lines.Where(l => l.Text.StartsWith("Deal:")), l => Assert.True(l.Turn < 60, "deals should come early"));
        }
        Assert.True(deals > 0, "bots made no deals at all");
    }
}
