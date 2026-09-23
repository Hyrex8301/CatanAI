using Catan.AI;

namespace Catan.Tests.AI;

public class WinModelTests
{
    [Fact]
    public void ChancesSumToOneAndFavorTheBiggerValue()
    {
        var p = new double[4];
        WinModel.Default.Chances(new double[] { 10, 20, 30, 40 }, 6, p);
        Assert.Equal(1, p.Sum(), 9);
        Assert.True(p[3] > p[2] && p[2] > p[1] && p[1] > p[0]);
        new WinModel(0.1, 0).Chances(new double[] { 5, 5, 5, 5 }, 3, p);
        Assert.All(p, x => Assert.Equal(0.25, x, 9));
    }

    [Fact]
    public void FittedModelPredictsHeldOutGamesBetterThanAGuessAndIsCalibrated()
    {
        // The trained weights the game ships (the hand-set defaults predict wins poorly on small samples).
        var weights = BotWeights.Load(Path.Combine(RepoRoot(), "game", "bots", "best.json"));
        var settings = SmartBotSettings.Training;
        var train = WinModel.Collect(weights, 200, 1, 8, settings);
        var test = WinModel.Collect(weights, 120, 900_000, 8, settings);
        var model = WinModel.Fit(train);

        double guess = Math.Log(0.25);
        Assert.True(model.LogLikelihood(test) > guess + 0.1, $"held-out log-likelihood {model.LogLikelihood(test):F3} vs guess {guess:F3}");
        Assert.True(model.LogLikelihood(train) >= WinModel.Default.LogLikelihood(train) - 1e-9); // fitting never makes it worse

        // Calibration: among held-out seats given a 50%+ chance, the actual win share is in the same ballpark.
        var p = new double[4];
        int confident = 0, won = 0;
        double predicted = 0;
        foreach (var s in test)
        {
            model.Chances(s.Values, s.LeaderVp, p);
            for (int seat = 0; seat < 4; seat++)
                if (p[seat] >= 0.5)
                {
                    confident++;
                    predicted += p[seat];
                    won += s.Winner == seat ? 1 : 0;
                }
        }
        Assert.True(confident > 30, $"only {confident} confident predictions");
        double actual = (double)won / confident, expected = predicted / confident;
        Assert.InRange(actual, expected - 0.15, expected + 0.15);
    }

    [Fact]
    public void SavesAndLoads()
    {
        var model = new WinModel(0.123, 0.045);
        Assert.Equal(model, WinModel.FromJson(model.ToJson()));
        Assert.Equal(WinModel.Default, WinModel.Load("no-such-file.json"));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
