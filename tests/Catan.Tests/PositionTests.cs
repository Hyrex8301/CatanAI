using Catan.AI;
using Catan.Core;

namespace Catan.Tests;

public class PositionTests
{
    /// <summary>Positions from random games at every step: every phase shows up (setup, pre-roll, discard, robber, main with trades...).</summary>
    private static IEnumerable<GameState> Positions()
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 10 + (ulong)i)).ToArray(), new RngChance(seed));
            yield return runner.State.Clone();
            for (int i = 0; i < 2000 && !runner.IsOver; i++)
            {
                runner.StepAsync().GetAwaiter().GetResult();
                if (i % 7 == 0)
                    yield return runner.State.Clone();
            }
            yield return runner.State.Clone(); // the end, too
        }
    }

    [Fact]
    public void EveryPositionRoundTripsExactly()
    {
        var phases = new HashSet<Phase>();
        foreach (var s in Positions())
        {
            phases.Add(s.Phase);
            var back = Position.FromJson(Position.From(s, seat: 2).ToJson()).ToState();
            Assert.Equal(s.ComputeHash(), back.ComputeHash());
            Assert.Equal(BoardJson.Serialize(s.Board), BoardJson.Serialize(back.Board));
            Assert.Equal(s.Settings, back.Settings);
        }
        foreach (var phase in new[] { Phase.SetupSettlement, Phase.SetupRoad, Phase.PreRoll, Phase.Main, Phase.MoveRobber, Phase.Discard, Phase.GameOver })
            Assert.Contains(phase, phases);
    }

    [Fact]
    public void TheSeatAndScenarioDetailsAreKept()
    {
        var s = new GameState(BoardGenerator.Balanced(new Rng(1)));
        var p = Position.FromJson(Position.From(s, seat: 3).ToJson());
        Assert.Equal(3, p.Seat);
        Assert.Null(p.Title);
    }

    [Fact]
    public void ADamagedPositionIsRefused()
    {
        var s = new GameState(BoardGenerator.Balanced(new Rng(1)));
        var p = Position.From(s);
        p.State["Bank"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { 99, 19, 19, 19, 19 });
        Assert.Throws<ArgumentException>(() => p.ToState()); // 99 bricks: the validator notices
        p.State.Remove("Hand");
        Assert.Throws<ArgumentException>(() => p.ToState());
        Assert.Throws<ArgumentException>(() => Position.FromJson("{ not json"));
    }
}

public class GamesFromPositionsTests
{
    private static GameState MidGame(ulong seed, int steps)
    {
        var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
            Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(new BotWeights(), SmartBotSettings.Training, seed + (ulong)i)).ToArray(),
            new RngChance(seed));
        for (int i = 0; i < steps && !runner.IsOver; i++)
            runner.StepAsync().GetAwaiter().GetResult();
        return runner.State.Clone();
    }

    private static IPlayerAgent[] Bots(ulong seed) =>
        Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(new BotWeights(), SmartBotSettings.Training, seed * 7 + (ulong)i)).ToArray();

    [Fact]
    public void AGameFromAPositionSavesLoadsAndReplays()
    {
        var start = Position.From(MidGame(3, 150), seat: 1);
        var runner = GameRunner.FromPosition(start, Bots(3), new RngChance(9), validate: true);
        for (int i = 0; i < 120 && !runner.IsOver; i++)
            runner.StepAsync().GetAwaiter().GetResult();

        var record = GameRecord.FromJson(runner.ToRecord(42).ToJson());
        Assert.NotNull(record.Start);
        var replay = record.Replay(validate: true);
        Assert.True(replay.Ok, replay.Error);
        Assert.Equal(runner.State.ComputeHash(), replay.State!.ComputeHash());

        var resumed = GameRunner.Resume(record, Bots(3), new RngChance(10), validate: true);
        Assert.Equal(runner.State.ComputeHash(), resumed.State.ComputeHash());
        Assert.IsType<PositionStarted>(resumed.Log.For(0)[0]);
    }

    [Theory]
    [InlineData(1UL, 60)]
    [InlineData(2UL, 200)]
    [InlineData(4UL, 400)]
    public void BotsPlayWholeGamesFromSavedPositions(ulong seed, int steps)
    {
        var runner = GameRunner.FromPosition(Position.From(MidGame(seed, steps)), Bots(seed), new RngChance(seed), validate: true);
        runner.RunAsync().GetAwaiter().GetResult();
        Assert.True(runner.IsOver);
    }

    [Fact]
    public void BotsSeeOnlyTheirOwnHandUnlessTheScenarioShowsAll()
    {
        var s = MidGame(5, 120);
        var hidden = (PositionStarted)GameRunner.FromPosition(Position.From(s), Bots(5), new RngChance(1)).Log.For(2)[0];
        Assert.Equal(ResourceSet.From(s.HandOf(2)), hidden.Hands[2]);
        Assert.All(new[] { 0, 1, 3 }, seat => Assert.Equal(default, hidden.Hands[seat]));
        Assert.Equal(Enumerable.Range(0, 4).Select(s.HandSize), hidden.HandSizes);

        var shown = new Position { Board = s.Board.ToLayout(), Settings = s.Settings, State = Position.From(s).State, HandsKnown = true };
        var all = (PositionStarted)GameRunner.FromPosition(shown, Bots(5), new RngChance(1)).Log.For(2)[0];
        Assert.Equal(ResourceSet.From(s.HandOf(0)), all.Hands[0]);
    }
}

public class TrackingFromPositionsTests
{
    [Fact]
    public void BotsKeepTrackingWhenSampledHandsStopFitting()
    {
        // Many positions, bots at their game settings: whatever the sampled starting hands, nobody's tracking breaks.
        for (ulong seed = 10; seed < 18; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(new BotWeights(), SmartBotSettings.Training, seed + (ulong)i)).ToArray(),
                new RngChance(seed));
            for (int i = 0; i < 150 + (int)(seed * 13 % 150) && !runner.IsOver; i++)
                runner.StepAsync().GetAwaiter().GetResult();
            var bots = Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(new BotWeights(), SmartBotSettings.Play, seed * 3 + (ulong)i)).ToArray();
            var fromHere = GameRunner.FromPosition(Position.From(runner.State), bots, new RngChance(seed + 1), validate: true);
            for (int i = 0; i < 300 && !fromHere.IsOver; i++)
                fromHere.StepAsync().GetAwaiter().GetResult();
        }
    }
}
