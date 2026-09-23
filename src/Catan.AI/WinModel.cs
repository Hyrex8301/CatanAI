using System.Text.Json;
using Catan.Core;

namespace Catan.AI;

/// <summary>One position from a self-play game: every seat's evaluation value, the leader's points, and who went on to win.</summary>
public sealed record WinSample(double[] Values, int LeaderVp, int Winner);

/// <summary>
/// Turns the evaluator's per-seat values into each seat's chance of winning, which the search averages. A softmax over the
/// four values with a sharpness that grows with the leader's points (a lead means more late in the game):
/// P(seat i wins) = exp(k·v_i) / Σ exp(k·v_j), k = A + B·leaderVp. A and B are fitted by maximum likelihood from
/// self-play positions (<see cref="Fit"/>) and saved next to the weights. A finished game is 1 for the winner.
/// </summary>
public sealed record WinModel(double A, double B)
{
    /// <summary>The fit from 3,000 self-play games with the first trained weights (game/bots/calibration.json), used when no file is found.</summary>
    public static readonly WinModel Default = new(0.014, 0.0024);

    public double Sharpness(int leaderVp) => Math.Max(1e-4, A + B * leaderVp);

    /// <summary>Win chances for every seat from their values (written into <paramref name="chances"/>, summing to 1).</summary>
    public void Chances(ReadOnlySpan<double> values, int leaderVp, Span<double> chances)
    {
        double k = Sharpness(leaderVp), max = double.MinValue, total = 0;
        for (int i = 0; i < values.Length; i++)
            max = Math.Max(max, values[i]);
        for (int i = 0; i < values.Length; i++)
            total += chances[i] = Math.Exp(k * (values[i] - max));
        for (int i = 0; i < values.Length; i++)
            chances[i] /= total;
    }

    /// <summary>Win chances in a real position: evaluator values, or certainty once the game is over.</summary>
    public void Chances(GameState s, Evaluator eval, Span<double> chances)
    {
        if (s.Phase == Phase.GameOver)
        {
            for (int i = 0; i < GameConstants.PlayerCount; i++)
                chances[i] = s.Winner < 0 ? 0.25 : i == s.Winner ? 1 : 0;
            return;
        }
        Span<double> values = stackalloc double[GameConstants.PlayerCount];
        eval.Values(s, values);
        Chances(values, LeaderVp(s), chances);
    }

    public static int LeaderVp(GameState s)
    {
        int best = 0;
        for (int i = 0; i < GameConstants.PlayerCount; i++)
            best = Math.Max(best, s.TotalVP(i));
        return best;
    }

    /// <summary>Average log-likelihood of the actual winners (higher is better; a blind guess scores log 0.25).</summary>
    public double LogLikelihood(IReadOnlyList<WinSample> samples)
    {
        Span<double> p = stackalloc double[GameConstants.PlayerCount];
        double total = 0;
        foreach (var s in samples)
        {
            Chances(s.Values, s.LeaderVp, p);
            total += Math.Log(Math.Max(p[s.Winner], 1e-12));
        }
        return total / Math.Max(1, samples.Count);
    }

    /// <summary>The A and B that make the samples' winners most likely (Newton's method from <see cref="Default"/>).</summary>
    public static WinModel Fit(IReadOnlyList<WinSample> samples, int iterations = 50)
    {
        double a = Default.A, b = Default.B;
        Span<double> p = stackalloc double[GameConstants.PlayerCount];
        for (int it = 0; it < iterations; it++)
        {
            // For one sample, d logL / dk = v_winner − E[v] and d² logL / dk² = −Var[v]; k = A + B·L, so by the chain rule
            // the gradient is g·(1, L) and the Hessian −Var·[[1, L], [L, L²]].
            double ga = 0, gb = 0, haa = 0, hab = 0, hbb = 0;
            var model = new WinModel(a, b);
            foreach (var s in samples)
            {
                model.Chances(s.Values, s.LeaderVp, p);
                double mean = 0, meanSq = 0;
                for (int i = 0; i < p.Length; i++)
                {
                    mean += p[i] * s.Values[i];
                    meanSq += p[i] * s.Values[i] * s.Values[i];
                }
                double g = s.Values[s.Winner] - mean, variance = Math.Max(meanSq - mean * mean, 0);
                int l = s.LeaderVp;
                ga += g;
                gb += g * l;
                haa += variance;
                hab += variance * l;
                hbb += variance * l * l;
            }
            double det = haa * hbb - hab * hab;
            if (det <= 1e-12)
                break;
            // Newton step (solve H·d = g for the 2×2 curvature), halved until the likelihood improves.
            double da = (hbb * ga - hab * gb) / det, db = (haa * gb - hab * ga) / det;
            double before = model.LogLikelihood(samples);
            for (double scale = 1; scale > 1e-3; scale /= 2)
            {
                var next = new WinModel(a + scale * da, Math.Max(0, b + scale * db));
                if (next.LogLikelihood(samples) >= before)
                {
                    (a, b) = (next.A, next.B);
                    break;
                }
            }
            if (Math.Abs(da) + Math.Abs(db) < 1e-9)
                break;
        }
        return new WinModel(a, b);
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static WinModel FromJson(string json) => JsonSerializer.Deserialize<WinModel>(json) ?? Default;

    public static WinModel Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : Default;

    /// <summary>
    /// Self-play positions for fitting: <paramref name="games"/> games between four SmartBots with the given weights, one
    /// sample at the end of every turn. Draws are left out. Results don't depend on the thread count.
    /// </summary>
    public static List<WinSample> Collect(BotWeights weights, int games, ulong seed, int threads, SmartBotSettings? settings = null)
    {
        var perGame = new List<WinSample>[games];
        var eval = new Evaluator(weights);
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
        {
            ulong gameSeed = seed + (ulong)g * 7919;
            var agents = Enumerable.Range(0, GameConstants.PlayerCount)
                .Select(seat => (IPlayerAgent)new SmartBot(weights, settings ?? SmartBotSettings.Training, gameSeed * 31 + (ulong)seat)).ToArray();
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(gameSeed))), agents, new RngChance(gameSeed ^ 0xCA11));
            var positions = new List<(double[] Values, int Leader)>();
            runner.ActionApplied += (_, events) =>
            {
                if (!events.Any(e => e is TurnEnded) || runner.State.Phase == Phase.GameOver)
                    return;
                var values = new double[GameConstants.PlayerCount];
                eval.Values(runner.State, values);
                positions.Add((values, LeaderVp(runner.State)));
            };
            runner.RunAsync().GetAwaiter().GetResult();
            int winner = runner.State.Winner;
            perGame[g] = winner < 0 ? new() : positions.Select(p => new WinSample(p.Values, p.Leader, winner)).ToList();
        });
        return perGame.SelectMany(p => p).ToList();
    }
}
