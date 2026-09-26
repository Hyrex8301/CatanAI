using System.Text.Json;

namespace Catan.UI;

/// <summary>Where a score came from.</summary>
public enum PracticeKind { Placement, Position, GameReview }

/// <summary>One practice result: a 0–100 score, how many plays it covered and how many were the bots' pick.</summary>
public sealed record PracticeResult(DateTime At, PracticeKind Kind, double Score, int Plays, int BestPicks);

/// <summary>Recent form in one kind of practice: the last score, the average of the last few and of the few before them.</summary>
public sealed record PracticeSummary(int Count, double Last, double RecentAverage, double? PreviousAverage, double Best, IReadOnlyList<double> Recent);

/// <summary>
/// Your practice scores over time (placement practice, position practice and game reviews), kept in one JSON file so the
/// Play screen can show how you're doing. A missing or damaged file starts an empty history.
/// </summary>
public sealed class PracticeHistory
{
    /// <summary>Scores averaged for "recent" form (and the same number before them for the trend).</summary>
    public const int Window = 10;

    /// <summary>Oldest results are dropped past this many.</summary>
    public const int MaxResults = 2000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly List<PracticeResult> _results;

    public PracticeHistory(string path)
    {
        Path = path;
        _results = Load(path);
    }

    public string Path { get; }

    public IReadOnlyList<PracticeResult> Results => _results;

    public void Add(PracticeResult result)
    {
        _results.Add(result);
        if (_results.Count > MaxResults)
            _results.RemoveRange(0, _results.Count - MaxResults);
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(_results, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save a score shouldn't stop the game; it's still counted for this session.
        }
    }

    /// <summary>Recent form for one kind, or null before the first result.</summary>
    public PracticeSummary? Summary(PracticeKind kind, int chart = 20)
    {
        var scores = _results.Where(r => r.Kind == kind).Select(r => r.Score).ToList();
        if (scores.Count == 0)
            return null;
        var recent = scores.TakeLast(Window).ToList();
        var before = scores.SkipLast(Window).TakeLast(Window).ToList();
        return new PracticeSummary(scores.Count, scores[^1], recent.Average(), before.Count == 0 ? null : before.Average(), scores.Max(),
            scores.TakeLast(chart).ToList());
    }

    /// <summary>"Last 82 · recent average 78 (up 6) · best 97 · 14 played", or null before the first result.</summary>
    public string? Describe(PracticeKind kind)
    {
        if (Summary(kind) is not { } s)
            return null;
        string trend = s.PreviousAverage is { } before
            ? Math.Round(s.RecentAverage - before, MidpointRounding.AwayFromZero) is var d && d == 0 ? " (steady)" : d > 0 ? $" (up {d:0})" : $" (down {-d:0})"
            : "";
        return $"Last {s.Last:0} · recent average {s.RecentAverage:0}{trend} · best {s.Best:0} · {s.Count} played";
    }

    private static List<PracticeResult> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<PracticeResult>>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
        return new();
    }
}
