using System.Collections.Concurrent;
using System.Diagnostics;
using Catan.AI;
using Catan.Core;

// Catan.Sim: finds engine bugs by playing many random games.
//   random --games 10000 --seed 1 [--validate] [--pure] [--out failures]
//   replay --file failures/seed-4211.json
//   bench --seconds 10 [--seed 1] [--pure]
return Sim.Main(args);

static class Sim
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();
        var options = args[0] == "talk" ? null! : Options.Parse(args.Skip(1));
        try
        {
            return args[0] switch
            {
                "random" => RandomGames(options),
                "replay" => Replay(options),
                "bench" => Bench(options),
                "match" => Match(options),
                "ladder" => LadderCommand(options),
                "calibrate" => Calibrate(options),
                "think" => Think(options),
                "train" => Train(options),
                "deal" => Deal(options),
                "talk" => Talk(args.Skip(1)),
                _ => Usage(),
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            Catan.Sim commands:
              random --games N --seed S [--validate] [--pure] [--out DIR] [--save DIR]
                                                                           play N random games (game i uses seed S+i);
                                                                           failures go to --out, --save keeps every record
              replay --file PATH                                           re-run a saved game and report the first problem
              bench --seconds N [--seed S] [--pure]                        games/s and actions/s without validation
              match --a BOT --b BOT [--games N] [--seed S] [--layout 1v3|2v2] [--threads T] [--validate] [--deals all|a]
                                                                           A vs B with rotated seats; A's win rate and 95% CI
                    BOT: random | smart | smart-fast | path/to/weights.json (smart-fast: training settings)
              ladder --bots BOT,BOT,... [--games N] [--seed S] [--threads T]  every pair plays 2v2; pairwise win rates and ratings
              calibrate [--weights W.json] [--games N] [--out F.json]      fit evaluation values to win chances from self-play
              think [--weights W.json] [--ms M] [--positions N]            search iterations a thinking time buys (all threads)
              deal [--seed S] [--weights W] [--out position.json]           a Position practice position
              talk "wheat nb?" ["don't block me" ...]                 how the chat reads each line (you are blue, talking to orange)
              train --out DIR [--hours H | --minutes M] [--generations G] [--from weights.json] [--threads T] [--seed S]
                                                                           self-play training; resumes if DIR has a checkpoint;
                                                                           Ctrl+C stops after the current generation
            """);
        return 2;
    }

    // ---- random ----

    private sealed record GameResult(ulong Seed, int Actions, int Turns, int Winner, string? Error);

    private static int RandomGames(Options o)
    {
        int games = o.Int("games", 1000);
        ulong baseSeed = o.ULong("seed", 1);
        bool validate = o.Flag("validate"), pure = o.Flag("pure");
        string outDir = o.String("out", "failures");
        string? saveDir = o.Flag("save") ? o.String("save", "records") : null;

        var results = new GameResult[games];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, games, i => results[i] = PlayOne(baseSeed + (ulong)i, validate, pure, outDir, saveDir));
        sw.Stop();

        var failed = results.Where(r => r.Error is not null).ToList();
        Report(results, sw.Elapsed);
        Console.WriteLine($"violations: {failed.Count}");
        foreach (var f in failed.Take(20))
            Console.WriteLine($"  seed {f.Seed}: {FirstLine(f.Error!)}  -> {Path.Combine(outDir, $"seed-{f.Seed}.json")}");
        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// One game. Any rules violation or illegal choice is saved as a replayable record plus a note with the error.
    /// With <paramref name="saveDir"/>, every finished game's record is saved there too (e.g. to add a fixed seed as a regression test).
    /// </summary>
    private static GameResult PlayOne(ulong seed, bool validate, bool pure, string outDir, string? saveDir)
    {
        var runner = CreateGame(seed, validate, pure);
        try
        {
            runner.RunAsync().GetAwaiter().GetResult();
            if (saveDir is not null)
            {
                Directory.CreateDirectory(saveDir);
                File.WriteAllText(Path.Combine(saveDir, $"seed-{seed}{(pure ? "-pure" : "")}.json"), runner.ToRecord(seed).ToJson());
            }
            return new GameResult(seed, runner.Actions.Count, runner.State.TurnNumber, runner.State.Winner, null);
        }
        catch (Exception ex) when (ex is StateViolationException or InvalidOperationException or ArgumentException)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, $"seed-{seed}.json"), runner.ToRecord(seed).ToJson());
            File.WriteAllText(Path.Combine(outDir, $"seed-{seed}.txt"), ex.ToString());
            return new GameResult(seed, runner.Actions.Count, runner.State.TurnNumber, runner.State.Winner, ex.Message);
        }
    }

    /// <summary>Game <paramref name="seed"/>: a balanced board from the seed, four RandomBots and dice seeded from it.</summary>
    public static GameRunner CreateGame(ulong seed, bool validate, bool pure)
    {
        var state = new GameState(BoardGenerator.Balanced(new Rng(seed)));
        var agents = Enumerable.Range(0, GameConstants.PlayerCount)
            .Select(i => (IPlayerAgent)new RandomBot(Mix(seed, (ulong)i + 1), pure))
            .ToArray();
        return new GameRunner(state, agents, new RngChance(Mix(seed, 99)), validate);
    }

    private static ulong Mix(ulong seed, ulong stream) => (seed + stream) * 0x9E3779B97F4A7C15UL ^ stream;

    private static void Report(IReadOnlyCollection<GameResult> results, TimeSpan elapsed)
    {
        int n = results.Count;
        long actions = results.Sum(r => (long)r.Actions);
        double seconds = Math.Max(elapsed.TotalSeconds, 1e-9);
        int draws = results.Count(r => r.Error is null && r.Winner < 0);
        var decided = results.Where(r => r.Error is null && r.Winner >= 0).ToList();
        string Pct(double part, double whole) => whole == 0 ? "-" : $"{100 * part / whole:F1}%";

        Console.WriteLine($"games {n} | {n / seconds:F0} games/s | {actions / seconds:F0} actions/s | " +
                          $"avg turns {results.Average(r => (double)r.Turns):F1} | turn-cap draws {Pct(draws, n)}");
        Console.WriteLine("wins by seat: " + string.Join(" ", Enumerable.Range(0, 4).Select(s => Pct(decided.Count(r => r.Winner == s), decided.Count))));
    }

    // ---- match ----

    private static int Match(Options o)
    {
        string a = o.String("a", "smart"), b = o.String("b", "random");
        int games = o.Int("games", 400);
        ulong baseSeed = o.ULong("seed", 1);
        bool twoVsTwo = o.String("layout", "1v3") == "2v2";
        bool validate = o.Flag("validate");
        string? deals = o.Flag("deals") ? o.String("deals", "all") : null;
        long dealsMade = 0, promisesBroken = 0;
        int threads = o.Int("threads", Math.Max(1, Environment.ProcessorCount - 2));

        var results = new (int WinnerIsA, int Winner, int Turns, int Actions)[games];
        var sw = Stopwatch.StartNew();
        int done = 0;
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            ulong seed = baseSeed + (ulong)i;
            // Rotate which seats A plays so turn order evens out.
            var isA = new bool[GameConstants.PlayerCount];
            for (int k = 0; k < (twoVsTwo ? 2 : 1); k++)
                isA[(i + k * 2) % GameConstants.PlayerCount] = true;
            var agents = Enumerable.Range(0, GameConstants.PlayerCount)
                .Select(seat => Bots.Create(isA[seat] ? a : b, Mix(seed, (ulong)seat + 11)))
                .ToArray();
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))), agents, new RngChance(Mix(seed, 99)), validate);
            Catan.AI.Talk.TableTalk? talk = null;
            if (deals is not null)
            {
                talk = new Catan.AI.Talk.TableTalk(new[] { "red", "blue", "orange", "white" });
                for (int seat = 0; seat < agents.Length; seat++)
                {
                    bool talks = deals == "all" || isA[seat];
                    if (agents[seat] is SmartBot smart)
                    {
                        smart.Table = talks ? talk : null;
                        smart.Deals = talks ? null : talk.Deals;
                    }
                    else if (agents[seat] is SearchBot search)
                    {
                        search.Table = talks ? talk : null;
                        search.Deals = talks ? null : talk.Deals;
                    }
                }
                runner.ActionApplied += (action, _) => Interlocked.Add(ref promisesBroken, talk!.OnAction(runner.State, action).Count);
            }
            runner.RunAsync().GetAwaiter().GetResult();
            if (talk is not null)
                Interlocked.Add(ref dealsMade, talk.Lines.Count(l => l.Seat < 0 && l.Text.StartsWith("Deal:")));
            int winner = runner.State.Winner;
            results[i] = (winner >= 0 && isA[winner] ? 1 : 0, winner, runner.State.TurnNumber, runner.Actions.Count);
            int n = Interlocked.Increment(ref done);
            if (n % Math.Max(1, games / 10) == 0)
                Console.Error.Write($"\r  {n}/{games} games...");
        });
        sw.Stop();
        Console.Error.WriteLine();

        int decided = results.Count(r => r.Winner >= 0);
        double p = (double)results.Sum(r => r.WinnerIsA) / games;
        double ci = 1.96 * Math.Sqrt(p * (1 - p) / games);
        double fair = twoVsTwo ? 0.5 : 0.25;
        Console.WriteLine($"{a} vs {b} ({(twoVsTwo ? "2v2" : "1v3")}): {games} games, {games / sw.Elapsed.TotalSeconds:F1} games/s, " +
                          $"avg turns {results.Average(r => r.Turns):F0}, draws {games - decided}");
        Console.WriteLine($"{a} wins {100 * p:F1}% ± {100 * ci:F1}% (equal strength would be {100 * fair:F0}%)");
        if (deals is not null)
            Console.WriteLine($"table talk ({deals}): {dealsMade} deals ({(double)dealsMade / games:F2} per game), {promisesBroken} promises broken");
        return 0;
    }

    // ---- ladder ----

    private static int LadderCommand(Options o)
    {
        var bots = o.String("bots", "random,smart-fast,smart").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int games = o.Int("games", 400);
        ulong seed = o.ULong("seed", 1);
        int threads = o.Int("threads", Math.Max(1, Environment.ProcessorCount - 2));
        var sw = Stopwatch.StartNew();
        var results = new List<PairResult>();
        for (int i = 0; i < bots.Length; i++)
            for (int j = i + 1; j < bots.Length; j++)
            {
                var r = Ladder.Play(bots[i], bots[j], games, seed, threads);
                results.Add(r);
                Console.WriteLine($"{bots[i]} vs {bots[j]}: {100 * r.AShare:F1}% ± {100 * r.Interval:F1}% ({games} games, 2v2)");
            }
        Console.WriteLine();
        Console.WriteLine("Ratings (Elo scale, first bot = 1000):");
        foreach (var (bot, rating) in Ladder.Ratings(bots, results).OrderByDescending(p => p.Value))
            Console.WriteLine($"  {rating,7:F0}  {bot}");
        Console.WriteLine($"({sw.Elapsed.TotalSeconds:F0} s)");
        return 0;
    }

    // ---- calibrate ----

    private static int Calibrate(Options o)
    {
        var weights = o.Flag("weights") ? BotWeights.Load(o.String("weights", "")) : new BotWeights();
        int games = o.Int("games", 1000);
        int threads = o.Int("threads", Math.Max(1, Environment.ProcessorCount - 2));
        ulong seed = o.ULong("seed", 1);
        var sw = Stopwatch.StartNew();
        var train = WinModel.Collect(weights, games, seed, threads);
        var test = WinModel.Collect(weights, Math.Max(100, games / 4), seed + 50_000_000, threads);
        var model = WinModel.Fit(train);
        Console.WriteLine($"{train.Count:N0} training positions, {test.Count:N0} held out ({sw.Elapsed.TotalSeconds:F0} s)");
        Console.WriteLine($"fitted: k = {model.A:G4} + {model.B:G4} × leader VP");
        Console.WriteLine($"log-likelihood per position: fitted {model.LogLikelihood(test):F4}, default {WinModel.Default.LogLikelihood(test):F4}, blind guess {Math.Log(0.25):F4}");
        var values = train.SelectMany(s => s.Values).ToArray();
        Console.WriteLine($"values: mean {values.Average():F1}, min {values.Min():F1}, max {values.Max():F1}");

        // Reliability: predicted chance in bins against how often those seats actually won.
        var bins = new (double Predicted, int Won, int Count)[10];
        var p = new double[GameConstants.PlayerCount];
        foreach (var s in test)
        {
            model.Chances(s.Values, s.LeaderVp, p);
            for (int seat = 0; seat < p.Length; seat++)
            {
                int b = Math.Min(9, (int)(p[seat] * 10));
                bins[b] = (bins[b].Predicted + p[seat], bins[b].Won + (s.Winner == seat ? 1 : 0), bins[b].Count + 1);
            }
        }
        Console.WriteLine("predicted  actual   positions");
        foreach (var (predicted, won, count) in bins.Where(b => b.Count > 0))
            Console.WriteLine($"  {100 * predicted / count,5:F1}%  {100.0 * won / count,5:F1}%  {count,8:N0}");

        if (o.Flag("out"))
        {
            File.WriteAllText(o.String("out", ""), model.ToJson());
            Console.WriteLine($"saved {o.String("out", "")}");
        }
        return 0;
    }

    // ---- think ----

    /// <summary>How many search iterations a thinking time buys: SearchBot on real mid-game decisions, all threads.</summary>
    private static int Think(Options o)
    {
        var weights = o.Flag("weights") ? BotWeights.Load(o.String("weights", "")) : new BotWeights();
        int ms = o.Int("ms", 1000), positions = o.Int("positions", 10);
        int threads = o.Int("threads", Math.Max(1, Environment.ProcessorCount - 2));
        SearchBot NewBot() => new(weights, WinModel.Default, new SearchSettings { ThinkMs = ms, Threads = threads }, 7); // one per game: its tracker follows one game
        var legal = new List<GameAction>();
        var counts = new List<int>();
        for (ulong seed = 1; counts.Count < positions; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new SmartBot(weights, SmartBotSettings.Training, seed + (ulong)i)).ToArray(), new RngChance(seed));
            for (int i = 0; i < 80 + (int)(seed * 17 % 200) && !runner.IsOver; i++)
                runner.StepAsync().GetAwaiter().GetResult();
            int seat = Rules.ActingSeat(runner.State);
            Rules.GetLegalActions(runner.State, seat, legal);
            if (runner.IsOver || runner.State.Phase == Phase.Discard || legal.Count < 3)
                continue;
            var bot = NewBot();
            var sw = Stopwatch.StartNew();
            bot.Decide(PlayerView.From(runner.State, seat, runner.Log), legal);
            if (bot.LastIterations > 0)
            {
                counts.Add(bot.LastIterations);
                Console.WriteLine($"  {runner.State.Phase,-12} {legal.Count,3} moves: {bot.LastIterations,6:N0} iterations in {sw.ElapsedMilliseconds} ms");
            }
        }
        Console.WriteLine($"{ms} ms on {threads} threads: {counts.Average():N0} iterations per decision on average ({counts.Average() / threads:N0} per thread)");
        return 0;
    }

    // ---- talk ----

    /// <summary>
    /// Reads each argument as a chat line said by blue to orange on the board of game seed 1, and prints what it understood.
    /// Lists a few of that board's spots first, so there are real numbers to try.
    /// </summary>
    private static int Talk(IEnumerable<string> lines)
    {
        string[] names = { "red", "blue", "orange", "white" };
        var board = BoardGenerator.Balanced(new Rng(1));
        var spots = Enumerable.Range(0, Topology.VertexCount).Select(v => Catan.AI.Talk.Spots.Name(board, v))
            .Where(n => n.Split(' ').Length == 3 && Catan.AI.Talk.Spots.Find(board, n.Split(' ').Select(int.Parse).ToList()).Count == 1).Distinct().Take(8);
        Console.WriteLine($"Some spots on this board: {string.Join(", ", spots)}");
        foreach (string line in lines)
        {
            var reading = Catan.AI.Talk.PhraseReader.Read(line, 1, names, 2, board);
            Console.WriteLine($"{line,-45} -> {reading.Describe(names, 1, board)}");
        }
        return 0;
    }

    // ---- deal ----

    /// <summary>Deals a Position practice position (the same seed deals the same one) and writes it as a position file.</summary>
    private static int Deal(Options o)
    {
        var weights = o.Flag("weights") ? BotWeights.Load(o.String("weights", "")) : new BotWeights();
        var position = PositionDealer.Deal(weights, o.ULong("seed", 1));
        string file = o.String("out", "position.json");
        File.WriteAllText(file, position.ToJson());
        Console.WriteLine($"seat {position.Seat}: {position.Description} -> {file}");
        return 0;
    }

    // ---- train ----

    private static int Train(Options o)
    {
        string outDir = o.String("out", "training/run");
        TimeSpan duration = o.Flag("minutes") ? TimeSpan.FromMinutes(o.Int("minutes", 10)) : TimeSpan.FromHours(o.Int("hours", 8));
        var options = new TrainerOptions
        {
            OutDir = outDir,
            Duration = duration,
            MaxGenerations = o.Flag("generations") ? o.Int("generations", 1) : null,
            Threads = o.Int("threads", Math.Max(1, Environment.ProcessorCount - 2)),
            Seed = o.ULong("seed", 1),
        };
        var start = o.Flag("from") ? BotWeights.Load(o.String("from", "")) : new BotWeights();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // don't kill the process: finish the generation and checkpoint
            cts.Cancel();
            Console.WriteLine("Stopping after this generation...");
        };

        Console.WriteLine($"Training into {outDir} for up to {duration.TotalHours:F1} h on {options.Threads} threads. Ctrl+C to stop safely.");
        KeepAwake(true);
        try
        {
            new Trainer(options, line => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}")).Run(start, cts.Token);
        }
        finally
        {
            KeepAwake(false);
        }
        return 0;
    }

    /// <summary>
    /// On Windows, asks the system not to sleep while training runs (the display may still turn off). The request ends when
    /// training stops or the process exits; no power settings are changed.
    /// </summary>
    private static void KeepAwake(bool on)
    {
        if (!OperatingSystem.IsWindows())
            return;
        const uint Continuous = 0x80000000, SystemRequired = 0x00000001;
        SetThreadExecutionState(on ? Continuous | SystemRequired : Continuous);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    // ---- replay ----

    private static int Replay(Options o)
    {
        string file = o.String("file", "");
        if (file == "")
            throw new ArgumentException("replay needs --file PATH");
        var record = GameRecord.FromJson(File.ReadAllText(file));
        var result = record.Replay(validate: true);
        string players = string.Join(", ", record.Players);
        Console.WriteLine($"{file}: seed {record.Seed?.ToString() ?? "-"}, {record.Actions.Count} actions, players {players}");
        if (!result.Ok)
        {
            Console.WriteLine($"FAILED after {result.ActionsApplied} actions: {result.Error}");
            return 1;
        }
        var s = result.State!;
        string outcome = s.Phase != Phase.GameOver ? $"stopped in {s.Phase}" : s.Winner >= 0 ? $"seat {s.Winner} won" : "draw at the turn cap";
        string hash = result.HashMatches == true ? "final hash matches" : "no final hash to compare";
        Console.WriteLine($"OK: replayed {result.ActionsApplied} actions, turn {s.TurnNumber}, {outcome}, {hash} ({GameRecord.HashText(s.ComputeHash())})");
        return 0;
    }

    // ---- bench ----

    private static int Bench(Options o)
    {
        double seconds = o.Int("seconds", 10);
        long nextSeed = (long)o.ULong("seed", 1);
        bool pure = o.Flag("pure");
        var results = new ConcurrentBag<GameResult>();
        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Run(() =>
        {
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                ulong seed = (ulong)Interlocked.Increment(ref nextSeed) - 1;
                var runner = CreateGame(seed, validate: false, pure);
                runner.RunAsync().GetAwaiter().GetResult();
                results.Add(new GameResult(seed, runner.Actions.Count, runner.State.TurnNumber, runner.State.Winner, null));
            }
        })).ToArray();
        Task.WaitAll(workers);
        sw.Stop();

        Console.WriteLine($"bench: {Environment.ProcessorCount} threads, {sw.Elapsed.TotalSeconds:F1} s, no validation{(pure ? ", pure random" : "")}");
        Report(results.ToArray(), sw.Elapsed);
        return 0;
    }

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();

    /// <summary>--key value pairs and bare --flags.</summary>
    private sealed class Options
    {
        private readonly Dictionary<string, string?> _values = new();

        public static Options Parse(IEnumerable<string> args)
        {
            var o = new Options();
            var list = args.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].StartsWith("--"))
                    throw new ArgumentException($"Unexpected argument '{list[i]}'.");
                string key = list[i][2..];
                string? value = i + 1 < list.Count && !list[i + 1].StartsWith("--") ? list[++i] : null;
                o._values[key] = value;
            }
            return o;
        }

        public bool Flag(string key) => _values.ContainsKey(key);

        public string String(string key, string fallback) => _values.TryGetValue(key, out var v) && v is not null ? v : fallback;

        public int Int(string key, int fallback) =>
            _values.TryGetValue(key, out var v) ? int.TryParse(v, out int n) && n > 0 ? n : throw new ArgumentException($"--{key} needs a positive number.") : fallback;

        public ulong ULong(string key, ulong fallback) =>
            _values.TryGetValue(key, out var v) ? ulong.TryParse(v, out ulong n) ? n : throw new ArgumentException($"--{key} needs a number.") : fallback;
    }
}
