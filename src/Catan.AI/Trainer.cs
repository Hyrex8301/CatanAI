using System.Diagnostics;
using System.Text.Json;
using Catan.Core;

namespace Catan.AI;

/// <summary>Settings for a training run. Defaults are sized for a night on a 16-thread desktop.</summary>
public sealed record TrainerOptions
{
    /// <summary>Folder for checkpoints, weights and the progress log.</summary>
    public string OutDir { get; init; } = "training/run";

    public TimeSpan Duration { get; init; } = TimeSpan.FromHours(8);

    /// <summary>Stop after this many generations (for tests); null = until time runs out.</summary>
    public int? MaxGenerations { get; init; }

    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount - 2);
    public ulong Seed { get; init; } = 1;

    /// <summary>Antithetic pairs of variations per generation.</summary>
    public int Pairs { get; init; } = 8;

    /// <summary>Games each variation plays per generation.</summary>
    public int GamesPerVariation { get; init; } = 48;

    /// <summary>Games in a champion challenge.</summary>
    public int ChallengeGames { get; init; } = 480;

    /// <summary>Size of a variation, relative to each weight's scale.</summary>
    public double Sigma { get; init; } = 0.1;

    /// <summary>Step size of the update, relative to <see cref="Sigma"/>.</summary>
    public double LearningRate { get; init; } = 0.5;

    /// <summary>Past champions kept as opponents.</summary>
    public int PoolSize { get; init; } = 5;

    /// <summary>Measure the champion against the starting weights every this many generations.</summary>
    public int ReportEvery { get; init; } = 10;

    public SmartBotSettings Bot { get; init; } = SmartBotSettings.Training;
}

/// <summary>Everything needed to resume a run exactly.</summary>
public sealed class TrainerState
{
    public int Generation { get; set; }
    public double[] Mean { get; set; } = Array.Empty<double>();
    public double[] Start { get; set; } = Array.Empty<double>();
    public double[] Scale { get; set; } = Array.Empty<double>();
    public double[] Champion { get; set; } = Array.Empty<double>();
    public List<double[]> Pool { get; set; } = new();
    public int ChampionsCrowned { get; set; }
    public double Seconds { get; set; }
    public long GamesPlayed { get; set; }
}

/// <summary>
/// Self-play weight training by an evolution strategy. Each generation: make antithetic pairs of variations of the current
/// weights (scaled per weight), play each against the champion and past champions on shared seeds (same boards and dice for
/// both sides of a pair), and move the weights toward the better side of each pair. The moved weights then challenge the
/// champion in a bigger match and are crowned only if they win clearly. Checkpoints after every generation; a resumed run
/// continues exactly as if it had never stopped (all randomness comes from the run seed and generation number).
/// </summary>
public sealed class Trainer
{
    private readonly TrainerOptions _o;
    private readonly Action<string> _log;

    public Trainer(TrainerOptions options, Action<string>? log = null)
    {
        _o = options;
        _log = log ?? Console.WriteLine;
    }

    public string StatePath => Path.Combine(_o.OutDir, "state.json");
    public string BestPath => Path.Combine(_o.OutDir, "best.json");
    public string ProgressPath => Path.Combine(_o.OutDir, "progress.csv");

    /// <summary>Starts from <paramref name="start"/> (or resumes the checkpoint in the out folder) and trains until done or cancelled.</summary>
    public TrainerState Run(BotWeights start, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_o.OutDir);
        var state = File.Exists(StatePath) ? Load() : Begin(start);
        if (state.Generation > 0)
            _log($"Resuming at generation {state.Generation} ({state.GamesPlayed:N0} games so far).");

        var sw = Stopwatch.StartNew();
        double startSeconds = state.Seconds;
        while (!ct.IsCancellationRequested && sw.Elapsed < _o.Duration && (_o.MaxGenerations is null || state.Generation < _o.MaxGenerations))
        {
            var gen = Stopwatch.StartNew();
            double fitness = Step(state);
            state.Generation++;
            state.Seconds = startSeconds + sw.Elapsed.TotalSeconds;

            string line = $"gen {state.Generation}: fitness {100 * fitness:F1}%, champions {state.ChampionsCrowned}, " +
                          $"{gen.Elapsed.TotalSeconds:F1}s, {state.GamesPlayed:N0} games";
            if (state.Generation % _o.ReportEvery == 0)
            {
                double vsStart = WinRate(state.Champion, new[] { state.Start }, _o.ChallengeGames, Seed(state.Generation, 900_000));
                state.GamesPlayed += _o.ChallengeGames;
                line += $", champion vs start {100 * vsStart:F1}%";
                AppendProgress(state, fitness, vsStart);
            }
            else
                AppendProgress(state, fitness, null);
            Save(state);
            _log(line);
        }
        _log($"Stopped after {state.Generation} generations, {state.GamesPlayed:N0} games, {state.ChampionsCrowned} champions crowned. Best weights: {BestPath}");
        return state;
    }

    private TrainerState Begin(BotWeights start)
    {
        var mean = start.ToVector();
        var state = new TrainerState
        {
            Mean = mean,
            Start = (double[])mean.Clone(),
            Scale = mean.Select(w => Math.Max(Math.Abs(w), 0.5)).ToArray(),
            Champion = (double[])mean.Clone(),
        };
        File.WriteAllText(ProgressPath, "generation,seconds,games,fitness,champions,champion_vs_start\n");
        Save(state);
        return state;
    }

    /// <summary>One generation. Returns the mean fitness of the variations (win share against the pool).</summary>
    private double Step(TrainerState state)
    {
        int n = state.Mean.Length;
        var rng = new Rng(Seed(state.Generation, 1));
        var noise = new double[_o.Pairs][];
        for (int i = 0; i < _o.Pairs; i++)
            noise[i] = Enumerable.Range(0, n).Select(_ => Gaussian(rng)).ToArray();

        var opponents = state.Pool.Prepend(state.Champion).ToArray();
        var plus = new double[_o.Pairs];
        var minus = new double[_o.Pairs];
        for (int i = 0; i < _o.Pairs; i++)
        {
            ulong seed = Seed(state.Generation, 100 + (ulong)i); // shared by both sides of the pair
            plus[i] = WinRate(Perturb(state, noise[i], +1), opponents, _o.GamesPerVariation, seed);
            minus[i] = WinRate(Perturb(state, noise[i], -1), opponents, _o.GamesPerVariation, seed);
        }
        state.GamesPlayed += 2L * _o.Pairs * _o.GamesPerVariation;

        // Move toward the better side of each pair, in units of each weight's scale. The pair differences are normalized by
        // their spread (standard fitness shaping), so the step size doesn't depend on how noisy this generation's games were.
        var diff = new double[_o.Pairs];
        for (int i = 0; i < _o.Pairs; i++)
            diff[i] = plus[i] - minus[i];
        double spread = Math.Sqrt(diff.Select(d => d * d).Average());
        if (spread > 0)
            for (int j = 0; j < n; j++)
            {
                double g = 0;
                for (int i = 0; i < _o.Pairs; i++)
                    g += diff[i] / spread * noise[i][j];
                state.Mean[j] += _o.LearningRate * _o.Sigma * state.Scale[j] * g / _o.Pairs;
            }

        // Challenge: the moved weights against three copies of the champion. Crown only on a clear win.
        double challenge = WinRate(state.Mean, new[] { state.Champion }, _o.ChallengeGames, Seed(state.Generation, 500_000));
        state.GamesPlayed += _o.ChallengeGames;
        double bar = 0.25 + 1.96 * Math.Sqrt(0.25 * 0.75 / _o.ChallengeGames);
        if (challenge > bar)
        {
            state.Pool.Insert(0, state.Champion);
            if (state.Pool.Count > _o.PoolSize)
                state.Pool.RemoveAt(state.Pool.Count - 1);
            state.Champion = (double[])state.Mean.Clone();
            state.ChampionsCrowned++;
            _log($"  new champion: {100 * challenge:F1}% against the old one (needed {100 * bar:F1}%)");
        }
        return (plus.Sum() + minus.Sum()) / (2 * _o.Pairs);
    }

    private double[] Perturb(TrainerState state, double[] noise, int sign)
    {
        var w = new double[state.Mean.Length];
        for (int j = 0; j < w.Length; j++)
            w[j] = state.Mean[j] + sign * _o.Sigma * state.Scale[j] * noise[j];
        return w;
    }

    /// <summary>
    /// Share of games won by <paramref name="candidate"/> in one seat against three opponents drawn from
    /// <paramref name="opponents"/>. The candidate's seat rotates; game g uses seed + g, so results don't depend on threads.
    /// </summary>
    public double WinRate(double[] candidate, IReadOnlyList<double[]> opponents, int games, ulong seed)
    {
        var candidateWeights = BotWeights.FromVector(candidate);
        var opponentWeights = opponents.Select(BotWeights.FromVector).ToArray();
        int wins = 0;
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = _o.Threads }, g =>
        {
            ulong gameSeed = seed + (ulong)g * 7919;
            int mySeat = g % GameConstants.PlayerCount;
            var agents = new IPlayerAgent[GameConstants.PlayerCount];
            for (int seat = 0; seat < agents.Length; seat++)
            {
                var weights = seat == mySeat ? candidateWeights : opponentWeights[(int)((gameSeed + (ulong)seat) % (ulong)opponentWeights.Length)];
                agents[seat] = new SmartBot(weights, _o.Bot, gameSeed * 31 + (ulong)seat);
            }
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(gameSeed))), agents, new RngChance(gameSeed ^ 0xABCDEF));
            runner.RunAsync().GetAwaiter().GetResult();
            if (runner.State.Winner == mySeat)
                Interlocked.Increment(ref wins);
        });
        return (double)wins / games;
    }

    private ulong Seed(int generation, ulong stream) => _o.Seed * 1_000_003UL + (ulong)generation * 10_007UL + stream * 101UL;

    private static double Gaussian(Rng rng)
    {
        double u1 = (rng.NextUInt() + 1.0) / (uint.MaxValue + 2.0), u2 = (rng.NextUInt() + 1.0) / (uint.MaxValue + 2.0);
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    // ---- Files ----

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private TrainerState Load() => JsonSerializer.Deserialize<TrainerState>(File.ReadAllText(StatePath), Json)
                                   ?? throw new InvalidOperationException($"Can't read {StatePath}.");

    private void Save(TrainerState state)
    {
        WriteAtomic(StatePath, JsonSerializer.Serialize(state, Json));
        WriteAtomic(BestPath, BotWeights.FromVector(state.Champion).ToJson());
    }

    private void AppendProgress(TrainerState state, double fitness, double? vsStart) =>
        File.AppendAllText(ProgressPath, $"{state.Generation},{state.Seconds:F0},{state.GamesPlayed},{fitness:F4},{state.ChampionsCrowned}," +
                                         $"{(vsStart is { } v ? v.ToString("F4") : "")}\n");

    private static void WriteAtomic(string path, string text)
    {
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
