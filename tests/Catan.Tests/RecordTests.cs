using System.Runtime.CompilerServices;
using System.Text.Json;
using Catan.Core;

namespace Catan.Tests;

/// <summary>The brief's Determinism row: records, replay and hashes all agree.</summary>
public class RecordTests
{
    private static async Task<GameRunner> Played(ulong seed, GameSettings? settings = null)
    {
        var runner = RandomTestAgent.Game(seed, settings: settings);
        await runner.RunAsync();
        return runner;
    }

    [Fact]
    public async Task SameSeedGivesTheSameFinalHash()
    {
        for (ulong seed = 0; seed < 5; seed++)
            Assert.Equal((await Played(seed)).State.ComputeHash(), (await Played(seed)).State.ComputeHash());
    }

    [Fact]
    public async Task RecordReplayAndHashAgreeThroughJson()
    {
        for (ulong seed = 0; seed < 40; seed++)
        {
            var runner = await Played(seed);
            var json = runner.ToRecord(seed).ToJson();
            var result = GameRecord.FromJson(json).Replay(validate: true);

            Assert.True(result.Ok, $"seed {seed}: {result.Error}");
            Assert.True(result.HashMatches);
            Assert.Equal(runner.Actions.Count, result.ActionsApplied);
            Assert.Equal(runner.State.ComputeHash(), result.State!.ComputeHash());
            Assert.Equal(runner.Log.All, result.Log.All); // same events, in the same order
        }
    }

    [Fact]
    public async Task StoppingAtActionKGivesThePositionAfterKActions()
    {
        // Record the hash after every action while playing, then replay prefixes.
        var runner = RandomTestAgent.Game(5);
        var hashes = new List<ulong> { runner.State.ComputeHash() };
        runner.ActionApplied += (_, _) => hashes.Add(runner.State.ComputeHash());
        await runner.RunAsync();
        var record = runner.ToRecord();
        Assert.Equal(record.Actions.Count + 1, hashes.Count);

        foreach (int k in new[] { 0, 1, 8, 16, 17, 100, record.Actions.Count / 2, record.Actions.Count })
        {
            var result = record.Replay(stopAfter: k);
            Assert.True(result.Ok, result.Error);
            Assert.Equal(k, result.ActionsApplied);
            Assert.Equal(hashes[k], result.State!.ComputeHash());
        }
    }

    [Fact]
    public async Task JsonHasTheBriefsShape()
    {
        var runner = await Played(1);
        var json = runner.ToRecord(12345).ToJson(indented: false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(12345UL, root.GetProperty("seed").GetUInt64());
        Assert.Equal(10, root.GetProperty("settings").GetProperty("vpToWin").GetInt32());
        Assert.False(root.GetProperty("settings").GetProperty("friendlyRobber").GetBoolean());
        Assert.Equal(19, root.GetProperty("board").GetProperty("hexes").GetArrayLength());
        Assert.Equal(9, root.GetProperty("board").GetProperty("harbors").GetArrayLength());
        Assert.Equal(4, root.GetProperty("players").GetArrayLength());
        Assert.Matches("^[0-9a-f]{16}$", root.GetProperty("finalHash").GetString()!);

        var first = root.GetProperty("actions")[0];
        Assert.Equal("BuildSettlement", first.GetProperty("type").GetString());
        Assert.Equal(0, first.GetProperty("seat").GetInt32());
        Assert.True(first.TryGetProperty("target", out _));
        Assert.False(first.TryGetProperty("target2", out _)); // -1 is omitted
        Assert.False(first.TryGetProperty("give", out _));    // empty sets are omitted

        var kinds = root.GetProperty("chance").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ToHashSet();
        Assert.Contains("Dice", kinds);
        var dice = root.GetProperty("chance").EnumerateArray().First(c => c.GetProperty("kind").GetString() == "Dice");
        Assert.InRange(dice.GetProperty("d1").GetInt32(), 1, 6);
    }

    [Fact]
    public void ActionsWithCardsRoundTrip()
    {
        var record = new GameRecord
        {
            Board = TestBoards.Standard.ToLayout(),
            Actions = new[]
            {
                new GameAction(ActionType.CounterOffer, 2, 3, Give: new ResourceSet(1, 0, 0, 0, 2), Get: ResourceSet.Of(Resource.Wool)),
                new GameAction(ActionType.ConfirmTrade, 0, 4, 1),
                new GameAction(ActionType.Discard, 1, Give: new ResourceSet(0, 4, 0, 0, 0)),
            },
            Chance = new[] { new ChanceOutcome(ChanceKind.Dice, 6, 1), new ChanceOutcome(ChanceKind.Steal, Value: 0), new ChanceOutcome(ChanceKind.Draw, Value: 4) },
        };
        var back = GameRecord.FromJson(record.ToJson());
        Assert.Equal(record.Actions, back.Actions);
        Assert.Equal(record.Chance, back.Chance);
    }

    // ---- Replay reports what went wrong ----

    [Fact]
    public async Task TamperedRecordsReportTheFirstProblem()
    {
        var record = (await Played(2)).ToRecord();

        // A changed action: the first settlement moved onto the second one's spot.
        var actions = record.Actions.ToArray();
        actions[2] = actions[2] with { Target = actions[0].Target };
        var illegal = new GameRecord { Board = record.Board, Settings = record.Settings, Actions = actions, Chance = record.Chance, FinalHash = record.FinalHash }.Replay();
        Assert.False(illegal.Ok);
        Assert.Equal(2, illegal.ActionsApplied);
        Assert.Contains("Action 2", illegal.Error);

        // A wrong final hash.
        var wrongHash = new GameRecord { Board = record.Board, Settings = record.Settings, Actions = record.Actions, Chance = record.Chance, FinalHash = "0000000000000000" }.Replay();
        Assert.False(wrongHash.Ok);
        Assert.False(wrongHash.HashMatches);
        Assert.Contains("differs", wrongHash.Error);

        // Missing dice outcomes.
        var noDice = new GameRecord { Board = record.Board, Settings = record.Settings, Actions = record.Actions, Chance = record.Chance.Where(c => c.Kind != ChanceKind.Dice).ToArray() }.Replay();
        Assert.False(noDice.Ok);
        Assert.Contains("no more dice roll", noDice.Error);

        // Unknown format.
        Assert.Contains("format", new GameRecord { FormatVersion = 99 }.Replay().Error);
    }

    [Fact]
    public void ReplayChanceReadsEachKindInOrderRegardlessOfInterleaving()
    {
        var outcomes = new[]
        {
            new ChanceOutcome(ChanceKind.Draw, Value: 1), new ChanceOutcome(ChanceKind.Dice, 2, 3),
            new ChanceOutcome(ChanceKind.Draw, Value: 0), new ChanceOutcome(ChanceKind.Dice, 6, 6),
        };
        var chance = new ReplayChance(outcomes);
        int[] deck = { 14, 5, 2, 2, 2 };
        Assert.Equal((2, 3), chance.RollDice());
        Assert.Equal((6, 6), chance.RollDice());
        Assert.Equal(1, chance.DrawDevCard(deck));
        Assert.Equal(0, chance.DrawDevCard(deck));
        Assert.Equal(0, chance.Remaining);
        Assert.Throws<ReplayException>(() => chance.RollDice());
    }

    [Fact]
    public void ReplayChanceRejectsImpossibleOutcomes()
    {
        Assert.Throws<ReplayException>(() => new ReplayChance(new[] { new ChanceOutcome(ChanceKind.Steal, Value: 2) }).PickStolenCard(new[] { 1, 1, 0, 1, 1 }));
        Assert.Throws<ReplayException>(() => new ReplayChance(new[] { new ChanceOutcome(ChanceKind.Draw, Value: 4) }).DrawDevCard(new[] { 1, 1, 1, 1, 0 }));
        Assert.Throws<ReplayException>(() => new ReplayChance(new[] { new ChanceOutcome(ChanceKind.Dice, 0, 7) }).RollDice());
    }

    [Fact]
    public void OnlyNewGamesCanBeRecorded()
    {
        var mid = new StateBuilder(TestBoards.Standard).Phase(Phase.Main).Build();
        var runner = new GameRunner(mid, Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomTestAgent((ulong)i)).ToArray(), new RngChance(1));
        Assert.Throws<InvalidOperationException>(() => runner.ToRecord());
    }

    // ---- Saved records: golden games and fixed Sim failures ----

    /// <summary>Every record in tests/Catan.Tests/Records must replay cleanly to its final hash, forever.</summary>
    [Theory]
    [MemberData(nameof(SavedRecords))]
    public void SavedRecordStillReplays(string file)
    {
        var record = GameRecord.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Records", file)));
        var result = record.Replay(validate: true);
        Assert.True(result.Ok, $"{file}: {result.Error}");
        Assert.True(result.HashMatches, $"{file}: no final hash to compare");
    }

    public static IEnumerable<object[]> SavedRecords() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Records"), "*.json", SearchOption.AllDirectories)
            .Select(f => new object[] { Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "Records"), f) })
            .OrderBy(f => (string)f[0]);

    /// <summary>
    /// Writes the golden records into the source tree. Does nothing unless CATAN_WRITE_GOLDEN=1, so it only runs on purpose:
    /// <c>$env:CATAN_WRITE_GOLDEN=1; dotnet test --filter WriteGoldenRecords</c>. Never regenerate them to make a failing
    /// replay pass: a failure means the engine changed how old games play.
    /// </summary>
    [Fact]
    public async Task WriteGoldenRecords()
    {
        if (Environment.GetEnvironmentVariable("CATAN_WRITE_GOLDEN") != "1")
            return;
        string dir = Path.Combine(SourceDir(), "Records", "golden");
        Directory.CreateDirectory(dir);
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            var runner = await Played(seed);
            File.WriteAllText(Path.Combine(dir, $"random-seed-{seed}.json"), runner.ToRecord(seed).ToJson());
        }
    }

    private static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
