using Catan.Core;

namespace Catan.AI;

/// <summary>One pairing on the ladder: how often A beat B, and over how many games.</summary>
public sealed record PairResult(string A, string B, double AWins, double BWins, int Games)
{
    /// <summary>A's share of the decided games (draws count half to each).</summary>
    public double AShare => Games == 0 ? 0.5 : AWins / Games;

    /// <summary>95% interval half-width on <see cref="AShare"/>.</summary>
    public double Interval => Games == 0 ? 0.5 : 1.96 * Math.Sqrt(AShare * (1 - AShare) / Games);
}

/// <summary>
/// Measures bot strength. Every pair of bots plays head to head, two seats each (seats alternate so turn order evens out),
/// on the same boards and dice for every pairing; the pairwise results become ratings on the Elo scale (Bradley-Terry fit,
/// first bot anchored at 1000). A small prior keeps one-sided pairings (a smart bot always beating RandomBot) finite.
/// Results don't depend on the thread count.
/// </summary>
public static class Ladder
{
    /// <summary>Plays <paramref name="games"/> 2v2 games between two bot specs (see <see cref="Bots.Create"/>).</summary>
    public static PairResult Play(string a, string b, int games, ulong seed, int threads, Func<string, ulong, IPlayerAgent>? create = null)
    {
        create ??= Bots.Create;
        var result = new double[games];
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
        {
            ulong gameSeed = seed + (ulong)g * 7919;
            bool aEven = g % 2 == 0; // A sits in seats 0 and 2, or 1 and 3
            var agents = new IPlayerAgent[GameConstants.PlayerCount];
            for (int seat = 0; seat < agents.Length; seat++)
                agents[seat] = create((seat % 2 == 0) == aEven ? a : b, gameSeed * 31 + (ulong)seat);
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(gameSeed))), agents, new RngChance(gameSeed ^ 0x5EED));
            runner.RunAsync().GetAwaiter().GetResult();
            int w = runner.State.Winner;
            result[g] = w < 0 ? 0.5 : (w % 2 == 0) == aEven ? 1 : 0;
        });
        double aWins = result.Sum();
        return new PairResult(a, b, aWins, games - aWins, games);
    }

    /// <summary>Ratings on the Elo scale from pairwise results, the first bot at 1000.</summary>
    public static Dictionary<string, double> Ratings(IReadOnlyList<string> bots, IReadOnlyList<PairResult> results, double prior = 1)
    {
        int n = bots.Count;
        var index = bots.Select((b, i) => (b, i)).ToDictionary(p => p.b, p => p.i);
        var wins = new double[n, n];
        foreach (var r in results)
        {
            wins[index[r.A], index[r.B]] += r.AWins;
            wins[index[r.B], index[r.A]] += r.BWins;
        }
        // Minorization-maximization for Bradley-Terry strengths, with `prior` virtual wins each way per played pairing.
        var strength = Enumerable.Repeat(1.0, n).ToArray();
        for (int iteration = 0; iteration < 2000; iteration++)
        {
            var next = new double[n];
            for (int i = 0; i < n; i++)
            {
                double won = 0, denominator = 0;
                for (int j = 0; j < n; j++)
                {
                    double games = wins[i, j] + wins[j, i];
                    if (i == j || games == 0)
                        continue;
                    won += wins[i, j] + prior;
                    denominator += (games + 2 * prior) / (strength[i] + strength[j]);
                }
                next[i] = denominator > 0 ? won / denominator : strength[i];
            }
            double anchor = next[0];
            for (int i = 0; i < n; i++)
                strength[i] = next[i] / anchor;
        }
        return bots.Select((b, i) => (b, 1000 + 400 * Math.Log10(strength[i]))).ToDictionary(p => p.b, p => p.Item2);
    }
}
