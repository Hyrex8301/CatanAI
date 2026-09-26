using Catan.UI;

namespace Catan.Tests.UI;

public class PracticeHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "catan-history-" + Guid.NewGuid().ToString("N"));
    private string File => Path.Combine(_dir, "practice_history.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static PracticeResult Result(PracticeKind kind, double score) => new(new DateTime(2026, 9, 25, 12, 0, 0), kind, score, 5, 3);

    [Fact]
    public void ResultsAreSavedAndLoadedAgain()
    {
        var history = new PracticeHistory(File);
        history.Add(Result(PracticeKind.Placement, 81));
        history.Add(Result(PracticeKind.GameReview, 64.5));

        var loaded = new PracticeHistory(File);
        Assert.Equal(history.Results, loaded.Results);
        Assert.Contains("\"Placement\"", System.IO.File.ReadAllText(File)); // kinds are written by name
    }

    [Fact]
    public void NoFileOrADamagedOneStartsEmpty()
    {
        Assert.Empty(new PracticeHistory(File).Results);
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{ not json");
        var history = new PracticeHistory(File);
        Assert.Empty(history.Results);
        Assert.Null(history.Summary(PracticeKind.Position));
        Assert.Null(history.Describe(PracticeKind.Position));
    }

    [Fact]
    public void SummaryComparesTheLastTenWithTheTenBefore()
    {
        var history = new PracticeHistory(File);
        for (int i = 0; i < 10; i++)
            history.Add(Result(PracticeKind.Position, 60));
        for (int i = 0; i < 10; i++)
            history.Add(Result(PracticeKind.Position, 70 + i)); // 70..79, average 74.5
        history.Add(Result(PracticeKind.Placement, 99)); // other kinds don't count

        var s = history.Summary(PracticeKind.Position)!;
        Assert.Equal(20, s.Count);
        Assert.Equal(79, s.Last);
        Assert.Equal(74.5, s.RecentAverage, 6);
        Assert.Equal(60, s.PreviousAverage!.Value, 6);
        Assert.Equal(79, s.Best);
        Assert.Equal(20, s.Recent.Count);
        Assert.Equal("Last 79 · recent average 75 (up 15) · best 79 · 20 played", history.Describe(PracticeKind.Position));
    }

    [Fact]
    public void AFirstScoreHasNoTrend()
    {
        var history = new PracticeHistory(File);
        history.Add(Result(PracticeKind.GameReview, 88));
        Assert.Null(history.Summary(PracticeKind.GameReview)!.PreviousAverage);
        Assert.Equal("Last 88 · recent average 88 · best 88 · 1 played", history.Describe(PracticeKind.GameReview));
    }

    [Fact]
    public void OldestResultsAreDroppedPastTheLimit()
    {
        var history = new PracticeHistory(File);
        for (int i = 0; i < PracticeHistory.MaxResults + 5; i++)
            history.Add(Result(PracticeKind.Placement, i % 100));
        Assert.Equal(PracticeHistory.MaxResults, history.Results.Count);
        Assert.Equal(5 % 100, history.Results[0].Score);
    }
}
