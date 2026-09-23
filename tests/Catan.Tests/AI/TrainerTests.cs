using Catan.AI;

namespace Catan.Tests.AI;

public class TrainerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "catan-trainer-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Tiny settings so tests run in seconds.</summary>
    private TrainerOptions Small(string name, int generations) => new()
    {
        OutDir = Path.Combine(_root, name),
        MaxGenerations = generations,
        Pairs = 2,
        GamesPerVariation = 8,
        ChallengeGames = 24,
        ReportEvery = 2,
        Threads = 4,
        Bot = SmartBotSettings.Training with { Depth = 1 },
    };

    [Fact]
    public void AResumedRunMatchesAnUninterruptedOne()
    {
        var straight = new Trainer(Small("straight", 4), _ => { }).Run(new BotWeights());

        var first = Small("resumed", 2);
        new Trainer(first, _ => { }).Run(new BotWeights());
        var resumed = new Trainer(first with { MaxGenerations = 4 }, _ => { }).Run(new BotWeights());

        Assert.Equal(4, resumed.Generation);
        Assert.Equal(straight.Mean, resumed.Mean);
        Assert.Equal(straight.Champion, resumed.Champion);
        Assert.Equal(straight.GamesPlayed, resumed.GamesPlayed);
    }

    [Fact]
    public void WritesCheckpointBestWeightsAndProgress()
    {
        var o = Small("files", 2);
        var trainer = new Trainer(o, _ => { });
        trainer.Run(new BotWeights());
        Assert.True(File.Exists(trainer.StatePath));
        Assert.NotNull(BotWeights.Load(trainer.BestPath));
        var lines = File.ReadAllLines(trainer.ProgressPath);
        Assert.Equal(3, lines.Length); // header + 2 generations
        Assert.EndsWith(",", lines[1]);  // no vs-start report on generation 1
        Assert.NotEqual(',', lines[2][^1]); // generation 2 has one (ReportEvery = 2)
    }

    [Fact]
    public void WinRateSeesAClearlyBetterBot()
    {
        // Weights that treat victory points as bad lose badly to the defaults.
        var bad = new BotWeights().ToVector();
        bad[BotWeights.IndexOf("vp")] = -10;
        var trainer = new Trainer(Small("winrate", 1), _ => { });
        double goodVsBad = trainer.WinRate(new BotWeights().ToVector(), new[] { bad }, 40, 1);
        Assert.True(goodVsBad > 0.6, $"defaults won only {goodVsBad:P0} against VP-hating weights");
    }

    [Fact]
    public void TrainingMovesAwayFromBadWeights()
    {
        var bad = new BotWeights().ToVector();
        bad[BotWeights.IndexOf("vp")] = -10;
        var o = Small("improve", 12) with { Pairs = 4, GamesPerVariation = 16, Sigma = 0.3, LearningRate = 1.0 };
        var state = new Trainer(o, _ => { }).Run(BotWeights.FromVector(bad));
        Assert.True(state.Mean[BotWeights.IndexOf("vp")] > -10, $"vp weight went {state.Mean[BotWeights.IndexOf("vp")]:F2}");
    }
}
