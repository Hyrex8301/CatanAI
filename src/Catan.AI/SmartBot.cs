using Catan.Core;

namespace Catan.AI;

/// <summary>How hard SmartBot thinks. Deeper and wider plans are stronger but slower.</summary>
public sealed record SmartBotSettings
{
    /// <summary>Own moves planned ahead within a turn (1 = just the next move).</summary>
    public int Depth { get; init; } = 3;

    /// <summary>Partial plans kept at each depth.</summary>
    public int Beam { get; init; } = 6;

    /// <summary>Hidden-card samples per decision; scores are averaged over them.</summary>
    public int Samples { get; init; } = 2;

    /// <summary>0 always plays the best move; above 0 sometimes plays near-best moves (variety for training).</summary>
    public double Temperature { get; init; }

    /// <summary>Make and answer player trades.</summary>
    public bool Trades { get; init; } = true;

    /// <summary>For games against people: deeper plans, more samples.</summary>
    public static readonly SmartBotSettings Play = new() { Depth = 3, Beam = 8, Samples = 3 };

    /// <summary>For bulk self-play: cheaper, so more games per night.</summary>
    public static readonly SmartBotSettings Training = new() { Depth = 2, Beam = 4, Samples = 1 };
}

/// <summary>
/// A bot that plays from its view only. For each decision it updates its <see cref="HandTracker"/>, samples the hidden cards
/// into a full state (<see cref="Determinizer"/>), and scores every legal move with a beam search over sequences of its own
/// moves this turn (<see cref="Planner"/>), with dice, dev card draws and steals weighted by their exact odds.
/// </summary>
public sealed class SmartBot : IPlayerAgent
{
    private readonly Rng _rng;
    private readonly Planner _planner;
    private HandTracker? _tracker;

    public SmartBot(BotWeights weights, SmartBotSettings? settings = null, ulong seed = 1, string? name = null)
    {
        Settings = settings ?? SmartBotSettings.Play;
        Evaluator = new Evaluator(weights);
        _planner = new Planner(Evaluator, Settings);
        _rng = new Rng(seed);
        Name = name ?? "SmartBot";
    }

    public string Name { get; }
    public SmartBotSettings Settings { get; }
    public Evaluator Evaluator { get; }

    /// <summary>The table's promises, when the game has table talk: moves that break ours are avoided unless worth a lot.</summary>
    public Talk.DealBook? Deals
    {
        get => _deals ?? Table?.Deals;
        set => _deals = value;
    }

    private Talk.DealBook? _deals;
    private int _lastDealTurn = -1;

    /// <summary>The game's table talk, if it has one: the bot makes and answers deals there and keeps its promises.</summary>
    public Talk.TableTalk? Table { get; set; }

    public Task<GameAction> DecideAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        Task.FromResult(Decide(view, legal));

    public Task<GameAction?> RespondAsync(PlayerView view, IReadOnlyList<GameAction> legal, CancellationToken ct) =>
        Task.FromResult(Respond(view, legal));

    private HandTracker Track(PlayerView view)
    {
        if (_tracker is null || _tracker.Viewer != view.Seat || view.Events.Count < _processedEvents)
            _tracker = new HandTracker(view.Seat);
        _tracker.Update(view.Events);
        _processedEvents = view.Events.Count;
        return _tracker;
    }

    private int _processedEvents;

    private GameAction Decide(PlayerView view, IReadOnlyList<GameAction> legal)
    {
        var tracker = Track(view);
        if (view.Phase == Phase.Discard)
            return Discard(view, tracker);
        if (Settings.Trades && Trading.SettleOpenTrades(view, tracker, Evaluator, legal, _rng, Table) is { } settle)
            return settle;
        if (Settings.Trades && Table is not null && Talk.DealMaker.Act(view, tracker, Evaluator, Table, _rng, ref _lastDealTurn) is { } dealMove)
            return dealMove;
        if (Settings.Trades && Trading.ProposeOffer(view, tracker, Evaluator, _rng) is { } offer)
            return offer;
        if (legal.Count == 1)
            return legal[0];

        var scores = new double[legal.Count];
        for (int sample = 0; sample < Settings.Samples; sample++)
        {
            var state = Determinizer.Build(view, tracker, _rng);
            _planner.ScoreActions(state, view.Seat, legal, scores);
        }
        Talk.PromiseKeeping.Penalize(Deals, view, legal, scores, Settings.Samples, Evaluator.Weights["vp"]);
        return legal[Choose(scores)];
    }

    private GameAction? Respond(PlayerView view, IReadOnlyList<GameAction> legal)
    {
        if (!Settings.Trades)
            return null;
        var tracker = Track(view);
        return Trading.Answer(view, tracker, Evaluator, legal, _rng, Table);
    }

    /// <summary>Best move, or with a temperature a softmax pick among the scores.</summary>
    private int Choose(double[] scores)
    {
        int best = 0;
        for (int i = 1; i < scores.Length; i++)
            if (scores[i] > scores[best])
                best = i;
        if (Settings.Temperature <= 0)
            return best;

        double total = 0;
        var weights = new double[scores.Length];
        for (int i = 0; i < scores.Length; i++)
            total += weights[i] = Math.Exp(Math.Max(-50, (scores[i] - scores[best]) / Settings.Temperature));
        double pick = _rng.NextUInt() / (double)uint.MaxValue * total;
        for (int i = 0; i < scores.Length; i++)
            if ((pick -= weights[i]) <= 0)
                return i;
        return best;
    }

    /// <summary>Discards one card at a time, each time the one whose loss hurts least.</summary>
    private GameAction Discard(PlayerView view, HandTracker tracker)
    {
        var state = Determinizer.Build(view, tracker, _rng);
        int me = view.Seat, owed = view.DiscardOwed[me];
        Span<int> discard = stackalloc int[GameConstants.ResourceCount];
        for (int k = 0; k < owed; k++)
        {
            int bestResource = -1;
            double bestScore = double.MinValue;
            for (int r = 0; r < GameConstants.ResourceCount; r++)
            {
                if (state.Hand[me * GameConstants.ResourceCount + r] == 0)
                    continue;
                state.Hand[me * GameConstants.ResourceCount + r]--;
                double score = Evaluator.Score(state, me);
                state.Hand[me * GameConstants.ResourceCount + r]++;
                if (score > bestScore)
                    (bestResource, bestScore) = (r, score);
            }
            state.Hand[me * GameConstants.ResourceCount + bestResource]--;
            discard[bestResource]++;
        }
        return new GameAction(ActionType.Discard, me, Give: ResourceSet.From(discard));
    }
}

/// <summary>
/// Scores a seat's legal moves by planning ahead within its turn: a beam search over sequences of its own moves.
/// Each prefix of a plan where the seat could stop (Main phase, or it's no longer its move) counts; states it must continue
/// from (moving the robber, placing free roads) don't. Dice, dev card draws and steals are chance: their exact expected
/// value ends a plan. A move's score is its best plan's value.
/// </summary>
public sealed class Planner
{
    private readonly Evaluator _eval;
    private readonly SmartBotSettings _settings;
    private readonly FixedChance _chance = new();
    private readonly List<GameAction> _legal = new();
    private readonly Stack<GameState> _pool = new();

    public Planner(Evaluator eval, SmartBotSettings settings)
    {
        _eval = eval;
        _settings = settings;
    }

    private readonly record struct Node(GameState State, int Root, double Value);

    /// <summary>Adds each root move's score (best plan value) into <paramref name="scores"/>.</summary>
    public void ScoreActions(GameState root, int me, IReadOnlyList<GameAction> actions, double[] scores)
    {
        var best = new double[actions.Count];
        var frontier = new List<Node>();

        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (!Rules.IsLegal(root, a, out _))
            {
                best[i] = _eval.Score(root, me) - 1; // legal for real, but not in this sample of hidden cards
                continue;
            }
            best[i] = Evaluate(root, a, me, i, frontier);
        }

        for (int depth = 2; depth <= _settings.Depth && frontier.Count > 0; depth++)
        {
            frontier.Sort((x, y) => y.Value.CompareTo(x.Value));
            var expand = frontier.Take(_settings.Beam).ToList();
            foreach (var node in frontier.Skip(_settings.Beam))
                Return(node.State);
            frontier = new List<Node>();

            foreach (var node in expand)
            {
                Rules.GetLegalActions(node.State, me, _legal);
                var moves = _legal.ToArray();
                foreach (var a in moves)
                {
                    if (a.Type == ActionType.EndTurn)
                        continue; // stopping here is already counted as the node's own value
                    best[node.Root] = Math.Max(best[node.Root], Evaluate(node.State, a, me, node.Root, frontier));
                }
                Return(node.State);
            }
        }
        foreach (var node in frontier)
            Return(node.State);

        for (int i = 0; i < scores.Length; i++)
            scores[i] += best[i];
    }

    /// <summary>
    /// The value of playing <paramref name="a"/> from <paramref name="s"/>. Chance moves return their expected value.
    /// Other moves are applied; if the seat still has the move afterwards, the new state joins the frontier.
    /// </summary>
    private double Evaluate(GameState s, GameAction a, int me, int root, List<Node> frontier)
    {
        if (IsChance(a))
            return Expected(s, a, me);

        var next = Rent(s);
        Rules.Apply(next, a, _chance);
        double value = StopValue(next, me);
        if (next.Phase != Phase.GameOver && Rules.ActingSeat(next) == me && next.Phase != Phase.PreRoll)
        {
            // Still our move: worth expanding. Unstoppable states (robber, free roads) get their best continuation instead.
            double rank = double.IsNaN(value) ? _eval.Score(next, me) : value;
            frontier.Add(new Node(next, root, rank));
        }
        else
            Return(next);
        return double.IsNaN(value) ? _eval.Score(s, me) - 0.5 : value;
    }

    /// <summary>The value of stopping at <paramref name="s"/>, or NaN if the seat can't stop there.</summary>
    private double StopValue(GameState s, int me)
    {
        if (s.Phase == Phase.GameOver || Rules.ActingSeat(s) != me)
            return _eval.Score(s, me);
        return s.Phase switch
        {
            Phase.Main => _eval.Score(s, me),
            Phase.PreRoll => Expected(s, new GameAction(ActionType.RollDice, me), me), // must roll next
            _ => double.NaN,
        };
    }

    private static bool IsChance(GameAction a) =>
        a.Type is ActionType.RollDice or ActionType.BuyDevCard || (a.Type == ActionType.MoveRobber && a.Target2 >= 0);

    // Probability of each dice total 2..12, and one pair of dice that makes it.
    private static readonly (int D1, int D2, double P)[] Rolls =
        Enumerable.Range(2, 11).Select(t => (Math.Max(1, t - 6), t - Math.Max(1, t - 6), (6 - Math.Abs(7 - t)) / 36.0)).ToArray();

    /// <summary>Exact expected value over the outcomes of a chance move.</summary>
    private double Expected(GameState s, GameAction a, int me)
    {
        double total = 0;
        switch (a.Type)
        {
            case ActionType.RollDice:
                foreach (var (d1, d2, p) in Rolls)
                {
                    _chance.SetDice(d1, d2);
                    total += p * AfterChance(s, a, me);
                }
                return total;

            case ActionType.BuyDevCard:
            {
                int deck = 0;
                for (int t = 0; t < GameConstants.DevCardTypeCount; t++)
                    deck += s.DevDeck[t];
                for (int t = 0; t < GameConstants.DevCardTypeCount; t++)
                    if (s.DevDeck[t] > 0)
                    {
                        _chance.Draw = t;
                        total += (double)s.DevDeck[t] / deck * AfterChance(s, a, me);
                    }
                return total;
            }

            default: // a steal
            {
                int victim = a.Target2, hand = s.HandSize(victim);
                for (int r = 0; r < GameConstants.ResourceCount; r++)
                {
                    int count = s.Hand[victim * GameConstants.ResourceCount + r];
                    if (count == 0)
                        continue;
                    _chance.Steal = r;
                    total += (double)count / hand * AfterChance(s, a, me);
                }
                return total;
            }
        }
    }

    /// <summary>Value right after a chance outcome. After our own 7, we also get to place the robber well.</summary>
    private double AfterChance(GameState s, GameAction a, int me)
    {
        var next = Rent(s);
        Rules.Apply(next, a, _chance);
        double value;
        if (next.Phase == Phase.MoveRobber && Rules.ActingSeat(next) == me)
        {
            // Our own 7: take the best robber placement, scored without its steal (a cheap, slightly pessimistic estimate).
            value = double.MinValue;
            Rules.GetLegalActions(next, me, _legal);
            foreach (var robber in _legal.ToArray())
            {
                var moved = Rent(next);
                moved.RobberHex = robber.Target;
                value = Math.Max(value, _eval.Score(moved, me));
                Return(moved);
            }
        }
        else
            value = _eval.Score(next, me);
        Return(next);
        return value;
    }

    private GameState Rent(GameState from)
    {
        var s = _pool.Count > 0 && ReferenceEquals(_pool.Peek().Board, from.Board) && Equals(_pool.Peek().Settings, from.Settings)
            ? _pool.Pop()
            : new GameState(from.Board, from.Settings);
        s.CopyFrom(from);
        return s;
    }

    private void Return(GameState s) => _pool.Push(s);
}

/// <summary>An <see cref="IChance"/> that returns preset outcomes, for enumerating chance exactly.</summary>
public sealed class FixedChance : IChance
{
    private int _d1 = 1, _d2 = 1;

    public int Steal { get; set; }
    public int Draw { get; set; }

    public void SetDice(int d1, int d2) => (_d1, _d2) = (d1, d2);

    public (int D1, int D2) RollDice() => (_d1, _d2);

    public int PickStolenCard(ReadOnlySpan<int> victimHand) => Steal;

    public int DrawDevCard(ReadOnlySpan<int> deckCounts) => Draw;
}
