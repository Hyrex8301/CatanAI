using System.Collections;
using System.Reflection;
using System.Text;
using Catan.Core;

namespace Catan.Tests;

public class RedactionTests
{
    [Fact]
    public void StolenResourceIsHiddenFromEveryoneButThiefAndVictim()
    {
        var e = new CardStolen(1, 2, (int)Resource.Ore);
        Assert.Same(e, e.RedactFor(1));
        Assert.Same(e, e.RedactFor(2));
        Assert.Equal(new CardStolen(1, 2, -1), e.RedactFor(0));
        Assert.Equal(new CardStolen(1, 2, -1), e.RedactFor(3));
    }

    [Fact]
    public void BoughtCardTypeIsHiddenFromEveryoneButTheBuyer()
    {
        var e = new DevCardBought(3, DevCardType.VictoryPoint);
        Assert.Same(e, e.RedactFor(3));
        Assert.Equal(new DevCardBought(3, null), e.RedactFor(0));
    }

    [Fact]
    public void EverythingElseIsPublic()
    {
        GameEvent[] events =
        {
            new DiceRolled(0, 3, 4), new ResourcesProduced(1, ResourceSet.Of(Resource.Ore)), new Built(2, PieceType.City, 5),
            new Discarded(3, ResourceSet.Of(Resource.Wool, 4)), new RobberMoved(0, 4), new DevCardPlayed(1, DevCardType.Monopoly),
            new MonopolyTaken(1, 2, 3, 4), new BankTraded(0, ResourceSet.Of(Resource.Ore, 4), ResourceSet.Of(Resource.Brick)),
            new TradeDone(0, 1, ResourceSet.Of(Resource.Ore), ResourceSet.Of(Resource.Wool)), new AwardChanged(Award.LongestRoad, -1, 2),
            new TurnEnded(1), new GameEnded(2),
        };
        foreach (var e in events)
            for (int viewer = 0; viewer < 4; viewer++)
                Assert.Same(e, e.RedactFor(viewer));
    }

    [Fact]
    public void EventLogKeepsARedactedCopyPerSeatAndSnapshotsDontChange()
    {
        var log = new EventLog();
        log.Add(new CardStolen(1, 2, (int)Resource.Brick));
        var seat0Before = log.For(0);
        log.Add(new DevCardBought(0, DevCardType.Knight));

        Assert.Single(seat0Before);
        Assert.Equal(new CardStolen(1, 2, -1), seat0Before[0]);
        Assert.Equal(new CardStolen(1, 2, 0), log.For(1)[0]);
        Assert.Equal(new DevCardBought(0, DevCardType.Knight), log.For(0)[1]);
        Assert.Equal(new DevCardBought(0, null), log.For(2)[1]);
        Assert.Equal(2, log.All.Count);
    }
}

public class PlayerViewTests
{
    /// <summary>
    /// Two positions that differ only in hidden information between seats 1 and 2: who holds which cards (same totals, same bank),
    /// who holds which dev cards (same deck), and what was stolen and bought.
    /// </summary>
    private static (GameState State, List<GameEvent> Log) Position(bool swapped)
    {
        int a = swapped ? 2 : 1, b = swapped ? 1 : 2;
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(1, Topology.Vertex(0, 0, Corner.N))
            .Settlement(2, Topology.Vertex(0, 2, Corner.S))
            .Hand(0, grain: 1)
            .Hand(a, brick: 2, wool: 1)
            .Hand(b, ore: 2, lumber: 1)
            .DevCards(a, knight: 1)
            .DevCards(b, victoryPoint: 1)
            .Phase(Phase.Main, current: 3)
            .Build();
        var log = new List<GameEvent>
        {
            new CardStolen(1, 2, swapped ? (int)Resource.Ore : (int)Resource.Brick),
            new DevCardBought(2, swapped ? DevCardType.Knight : DevCardType.VictoryPoint),
            new DiceRolled(3, 2, 2),
        };
        return (s, log);
    }

    [Fact]
    public void AViewRevealsNothingAboutOtherSeatsHiddenCards()
    {
        var (s1, log1) = Position(swapped: false);
        var (s2, log2) = Position(swapped: true);
        Assert.NotEqual(s1.ComputeHash(), s2.ComputeHash()); // the hidden information really differs

        foreach (int outsider in new[] { 0, 3 })
            Assert.Equal(Dump(PlayerView.From(s1, outsider, log1)), Dump(PlayerView.From(s2, outsider, log2)));
        foreach (int insider in new[] { 1, 2 })
            Assert.NotEqual(Dump(PlayerView.From(s1, insider, log1)), Dump(PlayerView.From(s2, insider, log2)));
    }

    [Fact]
    public void AViewHoldsNoReferenceToTheStateOrItsArrays()
    {
        var (s, log) = Position(swapped: false);
        var view = PlayerView.From(s, 0, log);
        var stateObjects = typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.GetValue(s)).Where(v => v is Array).Append(s).ToList();

        foreach (var reachable in Reachable(view))
            Assert.DoesNotContain(stateObjects, o => ReferenceEquals(o, reachable));
    }

    [Fact]
    public void AViewShowsCountsOfOthersAndDetailsOfItsOwn()
    {
        var (s, log) = Position(swapped: false);
        var v = PlayerView.From(s, 1, log);
        Assert.Equal(new[] { 1, 3, 3, 0 }, v.HandSizes);
        Assert.Equal(new[] { 0, 1, 1, 0 }, v.DevCardCounts);
        Assert.Equal(new ResourceSet(2, 0, 1, 0, 0), v.HandSet);
        Assert.Equal(1, v.DevHand[(int)DevCardType.Knight]);
        Assert.Equal(23, v.DevDeckSize);
        Assert.Equal(3, v.ActingSeat);
        Assert.Equal(new CardStolen(1, 2, (int)Resource.Brick), v.Events[0]);

        var v2 = PlayerView.From(s, 2, log);
        Assert.Equal(2, v2.TotalVP); // settlement + its hidden VP card
        Assert.Equal(1, v2.PublicVP[2]);
    }

    [Fact]
    public void ChangingAViewLeavesTheStateAlone()
    {
        var (s, log) = Position(swapped: false);
        ulong before = s.ComputeHash();
        var v = PlayerView.From(s, 1, log);
        v.Hand[0] = 99;
        v.VertexOwner[0] = 3;
        v.Offers[0] = new TradeOffer(true, 0, -1, default, default, 0);
        Assert.Equal(before, s.ComputeHash());
    }

    /// <summary>Everything a view exposes, as text (Board is shared and public, so it's compared by reference only).</summary>
    private static string Dump(PlayerView v)
    {
        var sb = new StringBuilder();
        foreach (var f in typeof(PlayerView).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = f.GetValue(v);
            sb.Append(f.Name).Append('=');
            switch (value)
            {
                case Board: sb.Append("<board>"); break;
                case string str: sb.Append(str); break;
                case IEnumerable seq: sb.Append(string.Join(",", seq.Cast<object>())); break;
                default: sb.Append(value); break;
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Every object reachable from the view's fields, arrays and event lists (not descending into Board or Settings).</summary>
    private static IEnumerable<object> Reachable(PlayerView v)
    {
        foreach (var f in typeof(PlayerView).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = f.GetValue(v);
            if (value is null || value is Board || value is GameSettings || value.GetType().IsValueType)
                continue;
            yield return value;
            if (value is IEnumerable seq and not Array)
                foreach (var item in seq)
                    if (item is not null)
                        yield return item;
        }
    }
}

public class GameRunnerTests
{
    /// <summary>
    /// Plays random legal moves from its view only: random discards, 5% new offers in Main, and answers open trades
    /// half the time (a quarter of those as counters).
    /// </summary>
    private sealed class RandomTestAgent : IPlayerAgent
    {
        private readonly Rng _rng;

        public RandomTestAgent(ulong seed) => _rng = new Rng(seed);

        public string Name => "RandomTestAgent";

        public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
        {
            if (view.Phase == Phase.Discard)
                return Task.FromResult(Rules.RandomDiscard(view, _rng));
            if (view.Phase == Phase.Main && _rng.NextInt(20) == 0 && Rules.RandomTradeOffer(view, _rng) is { } offer)
                return Task.FromResult(offer);
            return Task.FromResult(legal[_rng.NextInt(legal.Count)]);
        }

        public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct)
        {
            if (_rng.NextInt(2) == 0)
                return Task.FromResult<GameAction?>(null);
            if (_rng.NextInt(4) == 0 && Rules.RandomCounterOffer(view, _rng) is { } counter)
                return Task.FromResult<GameAction?>(counter);
            return Task.FromResult<GameAction?>(legal[_rng.NextInt(legal.Count)]);
        }
    }

    /// <summary>Delegates to functions, for scripted scenarios.</summary>
    private sealed class FuncAgent : IPlayerAgent
    {
        public Func<PlayerView, IReadOnlyList<GameAction>, GameAction> Decide = (_, legal) => legal[^1];
        public Func<PlayerView, IReadOnlyList<GameAction>, GameAction?> Respond = (_, _) => null;

        public string Name { get; init; } = "FuncAgent";

        public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
            Task.FromResult(Decide(view, legal));

        public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
            Task.FromResult(Respond(view, legal));
    }

    private static GameRunner RandomGame(ulong seed, bool validate)
    {
        var rng = new Rng(seed);
        var state = new GameState(BoardGenerator.Balanced(rng));
        var agents = Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomTestAgent(seed * 10 + (ulong)i)).ToArray();
        return new GameRunner(state, agents, new RngChance(seed + 1000), validate);
    }

    [Fact]
    public async Task RandomAgentsPlayWholeGamesThroughTheRunner()
    {
        int trades = 0;
        for (ulong seed = 0; seed < 30; seed++)
        {
            var runner = RandomGame(seed, validate: true);
            await runner.RunAsync();
            Assert.Equal(Phase.GameOver, runner.State.Phase);
            Assert.Contains(runner.Log.All, e => e is GameEnded);
            trades += runner.Log.All.Count(e => e is TradeDone);
        }
        Assert.True(trades > 10, $"only {trades} player trades");
    }

    [Fact]
    public async Task SameSeedsGiveTheSameGame()
    {
        var a = RandomGame(7, validate: false);
        var b = RandomGame(7, validate: false);
        await a.RunAsync();
        await b.RunAsync();
        Assert.Equal(a.Actions, b.Actions);
        Assert.Equal(a.State.ComputeHash(), b.State.ComputeHash());
    }

    /// <summary>Seat 0 (current, Main) holds 3 ore; everyone else holds 2 wool, so all of them can accept.</summary>
    private static GameState OfferTable() => new StateBuilder(TestBoards.Standard)
        .Hand(0, ore: 3).Hand(1, wool: 2).Hand(2, wool: 2).Hand(3, wool: 2)
        .Phase(Phase.Main)
        .Build();

    [Fact]
    public async Task OpponentsAnswerFromTheSameSnapshotSoNobodySeesAnotherAnswerFirst()
    {
        var seen = new List<(int Seat, int[] Responses)>();
        var current = new FuncAgent { Name = "Current" };
        int step = 0;
        current.Decide = (view, legal) => step++ == 0
            ? new GameAction(ActionType.OfferTrade, 0, Give: ResourceSet.Of(Resource.Ore), Get: ResourceSet.Of(Resource.Wool))
            : legal.First(a => a.Type == ActionType.EndTurn);

        IPlayerAgent Responder(int seat) => new FuncAgent
        {
            Name = $"Responder{seat}",
            Respond = (view, legal) =>
            {
                seen.Add((view.Seat, Enumerable.Range(0, 4).Select(p => view.Offers[0].ResponseOf(p)).ToArray()));
                return legal.FirstOrDefault(a => a.Type == ActionType.AcceptOffer) is { Type: ActionType.AcceptOffer } accept ? accept : null;
            },
        };

        var runner = new GameRunner(OfferTable(), new[] { current, Responder(1), Responder(2), Responder(3) }, new RngChance(1), validate: true);
        await runner.StepAsync(); // seat 0 offers
        await runner.StepAsync(); // opponents answer together, then seat 0 decides again

        Assert.Equal(new[] { 1, 2, 3 }, seen.Select(x => x.Seat).Order());
        Assert.All(seen, x => Assert.All(x.Responses, r => Assert.Equal(TradeOffer.NoResponse, r)));
        var accepts = runner.Actions.Where(a => a.Type == ActionType.AcceptOffer).Select(a => a.Seat).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, accepts); // applied in turn order after the current player
    }

    [Fact]
    public async Task APassingOpponentIsOnlyAskedAgainAfterTheGameMovesOn()
    {
        var askedAt = new List<int>();
        GameRunner? runner = null;
        var current = new FuncAgent();
        int step = 0;
        current.Decide = (view, legal) => step++ switch
        {
            0 => new GameAction(ActionType.OfferTrade, 0, Give: ResourceSet.Of(Resource.Ore), Get: ResourceSet.Of(Resource.Wool)),
            1 => new GameAction(ActionType.OfferTrade, 0, Give: ResourceSet.Of(Resource.Ore, 2), Get: ResourceSet.Of(Resource.Wool)),
            _ => legal.First(a => a.Type == ActionType.EndTurn),
        };
        var passer = new FuncAgent { Respond = (_, _) => { askedAt.Add(runner!.Actions.Count); return null; } };
        var silent = new FuncAgent();
        runner = new GameRunner(OfferTable(), new IPlayerAgent[] { current, passer, silent, silent }, new RngChance(1));

        for (int i = 0; i < 3; i++)
            await runner.StepAsync();
        Assert.Equal(new[] { 1, 2 }, askedAt); // once after each offer, never twice at the same point
    }

    [Fact]
    public async Task AnIllegalChoiceIsRejectedWithTheAgentsName()
    {
        var cheat = new FuncAgent { Name = "Cheater", Decide = (_, _) => new GameAction(ActionType.BuildCity, 0, 0) };
        var runner = new GameRunner(OfferTable(), new IPlayerAgent[] { cheat, new FuncAgent(), new FuncAgent(), new FuncAgent() }, new RngChance(1));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StepAsync());
        Assert.Contains("Cheater", ex.Message);
        Assert.Contains("illegal BuildCity", ex.Message);
    }

    [Fact]
    public async Task AgentsOnlyEverSeeTheirOwnRedactedLog()
    {
        var views = new List<PlayerView>();
        var agents = Enumerable.Range(0, 4).Select(i =>
        {
            var inner = new RandomTestAgent((ulong)i);
            return (IPlayerAgent)new FuncAgent
            {
                Decide = (v, legal) => { views.Add(v); return inner.DecideAsync(v, legal, default).Result; },
                Respond = (v, legal) => inner.RespondAsync(v, legal, default).Result,
            };
        }).ToArray();
        var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(3))), agents, new RngChance(4));
        await runner.RunAsync();

        int hiddenSteals = 0;
        foreach (var v in views)
            foreach (var e in v.Events)
            {
                if (e is CardStolen c && c.Thief != v.Seat && c.Victim != v.Seat)
                {
                    Assert.Equal(-1, c.Resource);
                    hiddenSteals++;
                }
                if (e is DevCardBought d && d.Seat != v.Seat)
                    Assert.Null(d.Type);
            }
        Assert.True(hiddenSteals > 0);
    }
}
