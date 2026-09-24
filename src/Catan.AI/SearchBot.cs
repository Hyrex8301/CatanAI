using System.Diagnostics;
using Catan.Core;

namespace Catan.AI;

/// <summary>How SearchBot looks ahead.</summary>
public enum SearchMode
{
    /// <summary>
    /// Every sample (hidden cards and dice) is played out once for each root move, so moves are compared under identical luck;
    /// SmartBot's choice is kept unless another move is better with confidence.
    /// </summary>
    Paired,

    /// <summary>Information-set Monte Carlo tree search over everyone's moves (<see cref="SearchTree"/>).</summary>
    Tree,
}

/// <summary>How SearchBot searches. Thinking is limited by time in real games, or by a fixed number of iterations.</summary>
public sealed record SearchSettings
{
    public SearchMode Mode { get; init; } = SearchMode.Paired;

    /// <summary>Paired mode: how many standard errors a move must beat SmartBot's choice by to replace it.</summary>
    public double Confidence { get; init; } = 1.5;

    /// <summary>Thinking time per real decision (used when <see cref="Iterations"/> is null).</summary>
    public int ThinkMs { get; init; } = 1000;

    /// <summary>A fixed number of iterations per decision instead of a time limit (ladder matches, tests, determinism).</summary>
    public int? Iterations { get; init; }

    /// <summary>Threads searching in parallel, each with its own tree.</summary>
    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount - 2);

    /// <summary>Moves considered at the root: the best few by the Play planner.</summary>
    public int RootMoves { get; init; } = 6;

    /// <summary>Moves considered at deeper decisions: the best few by the cheap playout policy.</summary>
    public int InnerMoves { get; init; } = 3;

    /// <summary>Turns (any player's) searched past the current one before the position is scored.</summary>
    public int CutoffTurns { get; init; } = 8;

    /// <summary>UCB exploration constant (win chances are 0..1).</summary>
    public double Exploration { get; init; } = 0.5;

    /// <summary>Hidden-card samples the root planner averages over when picking the root moves.</summary>
    public int RootSamples { get; init; } = 3;
}

/// <summary>
/// The fast policy every player follows inside the search: SmartBot's planner looking one move ahead (no player trades),
/// greedy discards. Works on a full (sampled) state. Not thread-safe; each search thread has its own.
/// </summary>
public sealed class PlayoutPolicy
{
    private readonly Evaluator _eval;
    private readonly Planner _planner;
    private readonly List<GameAction> _legal = new();

    public PlayoutPolicy(Evaluator eval)
    {
        _eval = eval;
        _planner = new Planner(eval, new SmartBotSettings { Depth = 1, Beam = 1, Samples = 1, Trades = false });
    }

    /// <summary>Each legal move's one-move-ahead score for <paramref name="seat"/> (higher is better).</summary>
    public double[] Score(GameState s, int seat, IReadOnlyList<GameAction> legal)
    {
        var scores = new double[legal.Count];
        _planner.ScoreActions(s, seat, legal, scores);
        return scores;
    }

    /// <summary>The indices of the <paramref name="k"/> best moves, best first.</summary>
    public int[] Best(GameState s, int seat, IReadOnlyList<GameAction> legal, int k)
    {
        if (legal.Count <= 1)
            return legal.Count == 1 ? new[] { 0 } : Array.Empty<int>();
        var scores = Score(s, seat, legal);
        return Enumerable.Range(0, legal.Count).OrderByDescending(i => scores[i]).ThenBy(i => i).Take(k).ToArray();
    }

    /// <summary>Plays one move for whoever must act (greedy), with chance from <paramref name="chance"/>.</summary>
    public void Step(GameState s, IChance chance)
    {
        int seat = Rules.ActingSeat(s);
        if (s.Phase == Phase.Discard)
        {
            Rules.Apply(s, Discard(s, seat), chance);
            return;
        }
        Rules.GetLegalActions(s, seat, _legal);
        var move = _legal.Count == 1 ? _legal[0] : _legal[Best(s, seat, _legal, 1)[0]];
        Rules.Apply(s, move, chance);
    }

    /// <summary>Discards one card at a time, each time the one whose loss hurts least.</summary>
    public GameAction Discard(GameState s, int seat)
    {
        const int R = GameConstants.ResourceCount;
        Span<int> discard = stackalloc int[R];
        int owed = s.DiscardOwed[seat];
        for (int k = 0; k < owed; k++)
        {
            int best = -1;
            double bestScore = double.MinValue;
            for (int r = 0; r < R; r++)
            {
                if (s.Hand[seat * R + r] - discard[r] == 0)
                    continue;
                s.Hand[seat * R + r] -= discard[r] + 1;
                double score = _eval.Score(s, seat);
                s.Hand[seat * R + r] += discard[r] + 1;
                if (score > bestScore)
                    (best, bestScore) = (r, score);
            }
            discard[best]++;
        }
        return new GameAction(ActionType.Discard, seat, Give: ResourceSet.From(discard));
    }
}

/// <summary>
/// One thread's information-set Monte Carlo tree search. Every iteration samples the hidden cards afresh from the hand
/// tracker (<see cref="Determinizer"/>) and plays forward from the real position: down the tree (each decision's moves are
/// the best few by the root planner or the playout policy; the acting player picks by UCB on their own win chance, trying
/// each move once first), then with the playout policy for everyone until <see cref="SearchSettings.CutoffTurns"/> turns
/// have passed or the game ends. The position is scored as every seat's win chance (<see cref="WinModel"/>) and each tree
/// move is credited with the chance of the player who chose it. Dice and draws are sampled, so the tree is open-loop:
/// a node is a sequence of moves, and a move that isn't legal in an iteration's sample is simply not available there.
/// </summary>
public sealed class SearchTree
{
    private sealed class Node
    {
        public readonly Dictionary<GameAction, Node> Children = new();
        public int Visits, Available;
        public double Reward;
    }

    private readonly SearchSettings _settings;
    private readonly Evaluator _eval;
    private readonly WinModel _winModel;
    private readonly PlayoutPolicy _policy;
    private readonly Rng _rng;
    private readonly Node _root = new();
    private readonly List<GameAction> _legal = new();
    private readonly List<(Node Node, int Seat)> _path = new();
    private readonly double[] _chances = new double[GameConstants.PlayerCount];

    public SearchTree(Evaluator eval, WinModel winModel, SearchSettings settings, ulong seed)
    {
        _eval = eval;
        _winModel = winModel;
        _settings = settings;
        _policy = new PlayoutPolicy(eval);
        _rng = new Rng(seed);
    }

    public int Iterations { get; private set; }

    /// <summary>Visits and total reward per root move so far.</summary>
    public IEnumerable<(GameAction Move, int Visits, double Reward)> RootStats() =>
        _root.Children.Select(c => (c.Key, c.Value.Visits, c.Value.Reward));

    public void Iterate(PlayerView view, HandTracker tracker, IReadOnlyList<GameAction> rootMoves)
    {
        var s = Determinizer.Build(view, tracker, _rng);
        var chance = new RngChance(((ulong)_rng.NextUInt() << 32) | _rng.NextUInt());
        int cutoff = s.TurnNumber + _settings.CutoffTurns;
        var node = _root;
        _path.Clear();

        bool expanded = false;
        while (!expanded && s.Phase != Phase.GameOver && s.TurnNumber < cutoff)
        {
            int seat = Rules.ActingSeat(s);
            if (s.Phase == Phase.Discard)
            {
                Rules.Apply(s, _policy.Discard(s, seat), chance); // not branched on
                continue;
            }

            IReadOnlyList<GameAction> moves;
            if (node == _root)
            {
                moves = rootMoves.Where(a => Rules.IsLegal(s, a, out _)).ToList();
                if (moves.Count == 0)
                    break; // this sample makes none of the real moves legal (rare): skip the tree
            }
            else
            {
                Rules.GetLegalActions(s, seat, _legal);
                if (_legal.Count == 0)
                    break;
                moves = _policy.Best(s, seat, _legal, _settings.InnerMoves).Select(i => _legal[i]).ToList();
            }

            Node? child = null;
            GameAction pick = default;
            foreach (var m in moves)
                if (node.Children.TryGetValue(m, out var c))
                    c.Available++;
            foreach (var m in moves)
                if (!node.Children.ContainsKey(m))
                {
                    // Try each available move once before comparing them (best-first by the ordering above).
                    child = new Node { Available = 1 };
                    node.Children[m] = child;
                    pick = m;
                    expanded = true;
                    break;
                }
            if (child is null)
            {
                double best = double.MinValue;
                foreach (var m in moves)
                {
                    var c = node.Children[m];
                    double ucb = c.Reward / c.Visits + _settings.Exploration * Math.Sqrt(Math.Log(Math.Max(c.Available, 1)) / c.Visits);
                    if (ucb > best)
                        (best, pick, child) = (ucb, m, c);
                }
            }
            Rules.Apply(s, pick, chance);
            _path.Add((child!, seat));
            node = child!;
        }

        while (s.Phase != Phase.GameOver && s.TurnNumber < cutoff)
            _policy.Step(s, chance);
        _winModel.Chances(s, _eval, _chances);
        foreach (var (n, seat) in _path)
        {
            n.Visits++;
            n.Reward += _chances[seat];
        }
        Iterations++;
    }
}

/// <summary>
/// One thread's paired comparison of the root moves. Each sample draws hidden cards (<see cref="Determinizer"/>) and one
/// chance seed, then plays every root move out from that same sample with the playout policy until
/// <see cref="SearchSettings.CutoffTurns"/> turns have passed, and scores the bot's win chance. Because every move meets the
/// same hidden cards and the same dice (<see cref="StreamChance"/>), the differences between moves are far less noisy than
/// independent playouts. Statistics are kept relative to the first root move (SmartBot's choice).
/// </summary>
public sealed class PairedSearch
{
    private readonly SearchSettings _settings;
    private readonly Evaluator _eval;
    private readonly WinModel _winModel;
    private readonly PlayoutPolicy _policy;
    private readonly Rng _rng;
    private readonly double[] _chances = new double[GameConstants.PlayerCount];

    public PairedSearch(Evaluator eval, WinModel winModel, SearchSettings settings, int moves, ulong seed)
    {
        _eval = eval;
        _winModel = winModel;
        _settings = settings;
        _policy = new PlayoutPolicy(eval);
        _rng = new Rng(seed);
        DiffSum = new double[moves];
        DiffSq = new double[moves];
        Pairs = new int[moves];
    }

    /// <summary>Per root move: the sum and sum of squares of (its win chance − the first move's), over samples where both were legal.</summary>
    public double[] DiffSum { get; }
    public double[] DiffSq { get; }
    public int[] Pairs { get; }

    /// <summary>Playouts run so far (one per legal root move per sample).</summary>
    public int Playouts { get; private set; }

    public void Sample(PlayerView view, HandTracker tracker, IReadOnlyList<GameAction> rootMoves)
    {
        var start = Determinizer.Build(view, tracker, _rng);
        ulong chanceSeed = ((ulong)_rng.NextUInt() << 32) | _rng.NextUInt();
        int me = view.Seat, cutoff = start.TurnNumber + _settings.CutoffTurns;
        double baseline = double.NaN;
        for (int m = 0; m < rootMoves.Count; m++)
        {
            if (!Rules.IsLegal(start, rootMoves[m], out _))
                continue; // not legal with this sample's hidden cards
            var s = start.Clone();
            var chance = new StreamChance(chanceSeed);
            Rules.Apply(s, rootMoves[m], chance);
            while (s.Phase != Phase.GameOver && s.TurnNumber < cutoff)
                _policy.Step(s, chance);
            _winModel.Chances(s, _eval, _chances);
            Playouts++;
            double value = _chances[me];
            if (m == 0)
                baseline = value;
            else if (!double.IsNaN(baseline))
            {
                double d = value - baseline;
                DiffSum[m] += d;
                DiffSq[m] += d * d;
                Pairs[m]++;
            }
        }
    }
}

/// <summary>
/// The strongest bot: SmartBot's evaluation plus a multi-turn search (<see cref="SearchTree"/>) on every thread within a
/// time budget, or a fixed iteration count. Player trades (offers and answers) and discards use SmartBot's logic; decisions
/// with one legal move are instant. Plays from its view only: hidden cards come from its hand tracker.
/// </summary>
public sealed class SearchBot : IPlayerAgent
{
    private readonly SmartBot _smart;
    private readonly Planner _rootPlanner;
    private readonly WinModel _winModel;
    private readonly Rng _rng;
    private readonly ulong _seed;
    private HandTracker? _tracker;
    private int _processedEvents, _decisions;

    public SearchBot(BotWeights weights, WinModel winModel, SearchSettings? settings = null, ulong seed = 1, string? name = null)
    {
        Settings = settings ?? new SearchSettings();
        Evaluator = new Evaluator(weights);
        _smart = new SmartBot(weights, SmartBotSettings.Play, seed ^ 0x5EA7C4);
        _rootPlanner = new Planner(Evaluator, SmartBotSettings.Play);
        _winModel = winModel;
        _rng = new Rng(seed);
        _seed = seed;
        Name = name ?? "SearchBot";
    }

    public string Name { get; }
    public SearchSettings Settings { get; }
    public Evaluator Evaluator { get; }

    /// <summary>Iterations the last searched decision ran (all threads together).</summary>
    public int LastIterations { get; private set; }

    public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        Task.FromResult(Decide(view, legal, ct));

    public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        _smart.RespondAsync(view, legal, ct);

    private HandTracker Track(PlayerView view)
    {
        if (_tracker is null || _tracker.Viewer != view.Seat || view.Events.Count < _processedEvents)
            _tracker = new HandTracker(view.Seat);
        _tracker.Update(view.Events);
        _processedEvents = view.Events.Count;
        return _tracker;
    }

    public GameAction Decide(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct = default)
    {
        var tracker = Track(view);
        if (view.Phase == Phase.Discard)
            return _smart.DecideAsync(view, legal, ct).Result;
        if (Trading.ProposeOffer(view, tracker, Evaluator, _rng) is { } offer)
            return offer;
        if (legal.Count == 1)
            return legal[0];

        var rootMoves = RootMoves(view, legal, tracker);
        if (rootMoves.Count == 1)
            return rootMoves[0];
        return Search(view, tracker, rootMoves, ct);
    }

    /// <summary>The root's candidate moves: the Play planner's best few (averaged over a few samples), plus ending the turn.</summary>
    public IReadOnlyList<GameAction> RootMoves(PlayerView view, IReadOnlyList<GameAction> legal, HandTracker tracker)
    {
        var scores = new double[legal.Count];
        for (int sample = 0; sample < Settings.RootSamples; sample++)
            _rootPlanner.ScoreActions(Determinizer.Build(view, tracker, _rng), view.Seat, legal, scores);
        var moves = Enumerable.Range(0, legal.Count).OrderByDescending(i => scores[i]).ThenBy(i => i)
            .Take(Settings.RootMoves).Select(i => legal[i]).ToList();
        foreach (var a in legal)
            if (a.Type == ActionType.EndTurn && !moves.Contains(a))
                moves.Add(a);
        return moves;
    }

    private GameAction Search(PlayerView view, HandTracker tracker, IReadOnlyList<GameAction> rootMoves, CancellationToken ct) =>
        Settings.Mode == SearchMode.Paired ? SearchPaired(view, tracker, rootMoves, ct) : SearchTree(view, tracker, rootMoves, ct);

    /// <summary>
    /// Paired mode: samples on every thread until the budget is spent (the iteration budget counts playouts), then the move
    /// whose average gain over SmartBot's choice is largest, if that gain is at least <see cref="SearchSettings.Confidence"/>
    /// standard errors above zero; otherwise SmartBot's choice.
    /// </summary>
    private GameAction SearchPaired(PlayerView view, HandTracker tracker, IReadOnlyList<GameAction> rootMoves, CancellationToken ct)
    {
        int threads = Math.Max(1, Settings.Threads), moves = rootMoves.Count;
        ulong decisionSeed = _seed * 1_000_003UL + (ulong)_decisions++ * 7919UL;
        var searches = new PairedSearch[threads];
        var clock = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            var search = searches[t] = new PairedSearch(Evaluator, _winModel, Settings, moves, decisionSeed + (ulong)t * 104_729UL);
            if (Settings.Iterations is { } total)
            {
                int samples = Math.Max(1, total / moves / threads + (t < total / moves % threads ? 1 : 0));
                for (int i = 0; i < samples && !ct.IsCancellationRequested; i++)
                    search.Sample(view, tracker, rootMoves);
            }
            else
                while (clock.ElapsedMilliseconds < Settings.ThinkMs && !ct.IsCancellationRequested)
                    search.Sample(view, tracker, rootMoves);
        });
        LastIterations = searches.Sum(s => s.Playouts);

        int best = 0;
        double bestBound = 0;
        for (int m = 1; m < moves; m++)
        {
            double sum = searches.Sum(s => s.DiffSum[m]), sq = searches.Sum(s => s.DiffSq[m]);
            int n = searches.Sum(s => s.Pairs[m]);
            if (n < 2)
                continue;
            double mean = sum / n, variance = Math.Max(sq / n - mean * mean, 0) * n / (n - 1);
            double bound = mean - Settings.Confidence * Math.Sqrt(variance / n);
            if (bound > bestBound)
                (best, bestBound) = (m, bound);
        }
        return rootMoves[best];
    }

    private GameAction SearchTree(PlayerView view, HandTracker tracker, IReadOnlyList<GameAction> rootMoves, CancellationToken ct)
    {
        int threads = Math.Max(1, Settings.Threads);
        ulong decisionSeed = _seed * 1_000_003UL + (ulong)_decisions++ * 7919UL;
        var trees = new Catan.AI.SearchTree[threads];
        var deadline = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            // Threads share the tracker: sampling only reads it.
            var tree = trees[t] = new Catan.AI.SearchTree(Evaluator, _winModel, Settings, decisionSeed + (ulong)t * 104_729UL);
            var myTracker = tracker;
            if (Settings.Iterations is { } total)
            {
                int mine = total / threads + (t < total % threads ? 1 : 0);
                for (int i = 0; i < mine && !ct.IsCancellationRequested; i++)
                    tree.Iterate(view, myTracker, rootMoves);
            }
            else
                while (deadline.ElapsedMilliseconds < Settings.ThinkMs && !ct.IsCancellationRequested)
                    tree.Iterate(view, myTracker, rootMoves);
        });

        var visits = new Dictionary<GameAction, (int Visits, double Reward)>();
        foreach (var tree in trees)
            foreach (var (move, v, r) in tree.RootStats())
                visits[move] = visits.TryGetValue(move, out var sum) ? (sum.Visits + v, sum.Reward + r) : (v, r);
        LastIterations = trees.Sum(t => t.Iterations);
        if (visits.Count == 0)
            return rootMoves[0];
        return visits.OrderByDescending(p => p.Value.Visits).ThenByDescending(p => p.Value.Reward / Math.Max(1, p.Value.Visits))
            .ThenBy(p => rootMoves.ToList().IndexOf(p.Key)).First().Key;
    }
}
