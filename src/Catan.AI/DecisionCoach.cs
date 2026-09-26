using Catan.Core;
using Catan.AI.Talk;

namespace Catan.AI;

/// <summary>One legal move as the coach sees it.</summary>
/// <param name="Rating">0-100 among the choices: 100 for the best, 0 for the worst.</param>
/// <param name="Reasons">What the move does, in plain words: your gains, then what it costs the others.</param>
public sealed record MoveRating(GameAction Move, string Description, double Score, double Rating, int Rank, IReadOnlyList<string> Reasons);

/// <summary>Every choice in a position, best first; <see cref="CloseCall"/> when the top two are nearly equal.</summary>
public sealed record DecisionGrade(IReadOnlyList<MoveRating> Moves, bool CloseCall)
{
    public MoveRating Of(GameAction move) => Moves.First(m => m.Move == move);
}

/// <summary>
/// Grades a decision the way the bots judge positions (for position practice and a future coach mode; no UI). Every legal
/// move is scored by SmartBot's planner at its play settings (your moves this turn after it, dice and draws at their exact
/// odds), averaged over many samples of the hidden cards from your view only. Answers to a trade offer are scored by the
/// position after the swap against declining, since the swap only happens later. The reasons compare the evaluation's
/// features before and after the move: your gains, and what it costs each opponent. When the top two moves are within a
/// fraction of a victory point, it says it's a close call rather than pretending one is clearly right.
/// </summary>
public sealed class DecisionCoach
{
    /// <summary>Two moves closer than this share of a victory point are a close call.</summary>
    public const double CloseCallPoints = 0.1;

    private readonly BotWeights _weights;
    private readonly Evaluator _eval;
    private readonly Planner _planner;

    public DecisionCoach(BotWeights weights)
    {
        _weights = weights;
        _eval = new Evaluator(weights);
        _planner = new Planner(_eval, SmartBotSettings.Play);
    }

    public DecisionGrade Grade(PlayerView view, IReadOnlyList<GameAction> legal, IReadOnlyList<string> names, int samples = 12, ulong seed = 1)
    {
        int me = view.Seat;
        var tracker = new HandTracker(me);
        tracker.Update(view);
        var rng = new Rng(seed);
        // Before the roll only a knight is worth playing (the bots' rule): progress cards go last, with that reason.
        var waitForRoll = view.Phase == Phase.PreRoll ? legal.Where(DevTiming.IsProgressCard).ToHashSet() : new HashSet<GameAction>();
        var scores = new double[legal.Count];
        bool answering = view.CurrentPlayer != me;
        GameState? sample = null;
        for (int k = 0; k < samples; k++)
        {
            var s = Determinizer.Build(view, tracker, rng);
            sample ??= s.Clone();
            if (answering)
                for (int i = 0; i < legal.Count; i++)
                    scores[i] += AnswerScore(s, me, legal[i]);
            else
                _planner.ScoreActions(s, me, legal, scores);
        }

        double floor = scores.DefaultIfEmpty(0).Min() - Math.Abs(scores.DefaultIfEmpty(0).Min()) * 0.01 - 1;
        for (int i = 0; i < legal.Count; i++)
            if (waitForRoll.Contains(legal[i]))
                scores[i] = floor;
        double best = scores.DefaultIfEmpty(0).Max(), worst = scores.DefaultIfEmpty(0).Min();
        var order = Enumerable.Range(0, legal.Count).OrderByDescending(i => scores[i]).ThenBy(i => i).ToList();
        var moves = order.Select((i, rank) => new MoveRating(legal[i], Describe(sample!, legal[i], names, me), scores[i] / samples,
            best > worst ? Math.Clamp(100 * (scores[i] - worst) / (best - worst), 0, 100) : 100, rank + 1, Reasons(sample!, legal[i], me, names))).ToList();
        bool close = moves.Count > 1 && moves[0].Score - moves[1].Score < CloseCallPoints * _weights["vp"];
        return new DecisionGrade(moves, close);
    }

    /// <summary>An answer to an offer: accepting is worth the position after the swap; declining (or anything else) the position now.</summary>
    private double AnswerScore(GameState s, int me, GameAction a)
    {
        if (a.Type == ActionType.AcceptOffer && s.Offers[a.Target] is { IsActive: true, IsCounter: false } offer)
            return Trading.ScoreAfterSwap(s, me, offer.From, offer.Get, offer.Give, _eval);
        return _eval.Score(s, me);
    }

    // ---- Reasons ----

    private IReadOnlyList<string> Reasons(GameState s, GameAction a, int me, IReadOnlyList<string> names)
    {
        if (s.Phase == Phase.PreRoll && DevTiming.IsProgressCard(a))
            return new[] { "wait until after the roll: nothing is lost by waiting, and you'll know what the dice brought" };
        switch (a.Type)
        {
            case ActionType.RollDice:
                return new[] { "rolls now: what happens depends on the dice" };
            case ActionType.BuyDevCard:
                return new[] { "a dev card: a knight, points or progress, depending on the draw" };
            case ActionType.EndTurn:
                return new[] { "keeps your cards for later" };
            case ActionType.DeclineOffer when s.Offers[a.Target] is { IsActive: true } declined
                                              && s.PublicVP[declined.From] >= s.Settings.VpToWin - 1:
                return new[] { $"keeps {Name(names, declined.From)} from getting the cards to finish: they are one point from winning" };
        }
        GameState after;
        if (a.Type == ActionType.AcceptOffer && s.Offers[a.Target] is { IsActive: true, IsCounter: false } offer)
        {
            after = s.Clone();
            Trading.ShowCards(after, offer.From, offer.Get);
            for (int r = 0; r < GameConstants.ResourceCount; r++)
            {
                after.Hand[me * GameConstants.ResourceCount + r] += offer.Give[r] - offer.Get[r];
                after.Hand[offer.From * GameConstants.ResourceCount + r] += offer.Get[r] - offer.Give[r];
            }
        }
        else
        {
            after = s.Clone();
            var chance = new FixedChance();
            if (a.Type == ActionType.MoveRobber && a.Target2 >= 0)
                chance.Steal = Enumerable.Range(0, GameConstants.ResourceCount)
                    .OrderByDescending(r => s.Hand[a.Target2 * GameConstants.ResourceCount + r]).First(); // their most plentiful card
            Rules.Apply(after, a, chance);
        }

        var reasons = new List<string>();
        var before = AllFeatures(s);
        var then = AllFeatures(after);
        int n = BotWeights.Names.Count;
        // Your gains, biggest first.
        var gains = new Dictionary<string, double>();
        for (int f = 0; f < n; f++)
            if (Phrase(BotWeights.Names[f]) is { } phrase)
            {
                double gain = _weights[f] * (then[me * n + f] - before[me * n + f]);
                if (gain > 0.5)
                    gains[phrase] = gains.GetValueOrDefault(phrase) + gain;
            }
        int vpGain = after.TotalVP(me) - s.TotalVP(me);
        if (vpGain > 0 && after.LongestRoadOwner == s.LongestRoadOwner && after.LargestArmyOwner == s.LargestArmyOwner)
            reasons.Add(vpGain == 1 ? "a victory point" : $"{vpGain} victory points");
        gains.Remove("a victory point");
        reasons.AddRange(gains.OrderByDescending(g => g.Value).Take(2).Select(g => g.Key));

        // What it does to the others: costs them something (good for you) or helps them (bad for you).
        for (int o = 0; o < GameConstants.PlayerCount; o++)
        {
            if (o == me)
                continue;
            var losses = new Dictionary<string, double>();
            var helps = new Dictionary<string, double>();
            for (int f = 0; f < n; f++)
            {
                double change = _weights[f] * (then[o * n + f] - before[o * n + f]);
                if (change < -0.5 && OpponentPhrase(BotWeights.Names[f]) is { } loss)
                    losses[loss] = losses.GetValueOrDefault(loss) - change;
                if (change > 0.5 && OpponentGainPhrase(BotWeights.Names[f]) is { } gain)
                    helps[gain] = helps.GetValueOrDefault(gain) + change;
            }
            string near = after.PublicVP[o] >= after.Settings.VpToWin - 1 && after.Winner < 0 ? $" ({Name(names, o)} is one point from winning)" : "";
            if (helps.Count > 0)
                reasons.Add($"helps {Name(names, o)}: {helps.OrderByDescending(l => l.Value).First().Key}{near}");
            else if (losses.Count > 0)
                reasons.Add($"{Name(names, o)}: {losses.OrderByDescending(l => l.Value).First().Key}");
        }
        // Awards change hands: say so plainly.
        if (after.LongestRoadOwner == me && s.LongestRoadOwner != me)
        {
            reasons.Remove("closer to Longest Road");
            reasons.Insert(0, "takes Longest Road: 2 points");
        }
        if (after.LargestArmyOwner == me && s.LargestArmyOwner != me)
            reasons.Insert(0, "takes Largest Army: 2 points");
        if (reasons.Count == 0)
            reasons.Add(gains.Count == 0 ? "changes little right now" : "small gains");
        return reasons.Take(4).ToList();
    }

    private static double[] AllFeatures(GameState s)
    {
        var all = new double[GameConstants.PlayerCount * BotWeights.Names.Count];
        Evaluator.Features(s, all);
        return all;
    }

    private static readonly string[] ProdNames = { "prod_brick", "prod_lumber", "prod_wool", "prod_grain", "prod_ore" };

    private static string? Phrase(string feature) => feature switch
    {
        "vp" => "a victory point",
        "prod_brick" or "prod_lumber" or "prod_wool" or "prod_grain" or "prod_ore" => $"more {ResourceNames.Of(Array.IndexOf(ProdNames, feature))} production",
        "diversity" or "number_diversity" => "more variety in what you produce",
        "city_combo" or "stuck_city_combo" => "wheat + ore for cities",
        "road_combo" => "wood + brick for roads",
        "dev_combo" => "sheep + wheat + ore for dev cards",
        "harbor_2to1" or "harbor_3to1" or "harbor_3to1_prod" => "a harbor to trade through",
        "can_road" or "can_settlement" or "can_city" or "can_dev" => "cards for your next build",
        "city_missing" or "settlement_missing" or "dev_missing" => "closer to your next build",
        "settle_spots" or "best_spot_pips" or "prospect_pips" or "prospects" => "better spots to expand to",
        "road_length" or "longest_road_gap" => "closer to Longest Road",
        "knights" or "army_gap" => "closer to Largest Army",
        "robber_blocked" or "knight_blocked" => "gets the robber off your tiles",
        "strategy_focus" => "fits your plan",
        "vp_turns" => "closer to your next point",
        "road_early" => "a longer road toward new spots",
        "army_reach" => "closer to Largest Army",
        "port_count" => "a port for trading",
        _ => null,
    };

    private static string? OpponentGainPhrase(string feature) => feature switch
    {
        "vp" => "a victory point",
        "prod_brick" or "prod_lumber" or "prod_wool" or "prod_grain" or "prod_ore" => $"more {ResourceNames.Of(Array.IndexOf(ProdNames, feature))} production",
        "hand_total" => "a card",
        "city_combo" or "city_missing" or "can_city" or "stuck_city_combo" or "stuck_city_missing" => "closer to a city",
        "settlement_missing" or "can_settlement" => "closer to a settlement",
        "dev_missing" or "can_dev" => "closer to a dev card",
        "longest_road_gap" or "road_length" => "gains on Longest Road",
        "army_gap" => "gains on Largest Army",
        _ => null,
    };

    private static string? OpponentPhrase(string feature) => feature switch
    {
        "vp" => "loses a victory point",
        "prod_brick" or "prod_lumber" or "prod_wool" or "prod_grain" or "prod_ore" => $"loses {ResourceNames.Of(Array.IndexOf(ProdNames, feature))} production",
        "hand_total" => "loses a card",
        "city_combo" or "city_missing" or "stuck_city_combo" or "stuck_city_missing" => "further from a city",
        "settlement_missing" or "can_settlement" => "further from a settlement",
        "dev_missing" or "can_dev" => "further from a dev card",
        "longest_road_gap" or "road_length" => "loses ground on Longest Road",
        "army_gap" => "loses ground on Largest Army",
        "prospect_pips" or "prospects" or "settle_spots" or "best_spot_pips" => "loses a spot to expand to",
        _ => null,
    };

    // ---- Describing moves ----

    public static string Describe(GameState s, GameAction a, IReadOnlyList<string> names, int me)
    {
        var board = s.Board;
        string Hex(int h) => board.ResourceAt(h) is var r and >= 0 ? $"the {board.NumberAt(h)} {ResourceNames.Of(r)}" : "the desert";
        string Cards(ResourceSet set) => string.Join(" + ", Enumerable.Range(0, GameConstants.ResourceCount).Where(r => set[r] > 0)
            .Select(r => set[r] == 1 ? ResourceNames.Of(r) : $"{set[r]} {ResourceNames.Of(r)}"));
        return a.Type switch
        {
            ActionType.RollDice => "Roll the dice",
            ActionType.EndTurn => "End your turn",
            ActionType.BuyDevCard => "Buy a dev card",
            ActionType.BuildRoad => $"Build a road toward the {Spots.Name(board, Topology.EdgeVertices[a.Target, 1])} corner",
            ActionType.BuildSettlement => $"Settle on the {Spots.Name(board, a.Target)} spot",
            ActionType.BuildCity => $"City on the {Spots.Name(board, a.Target)} spot",
            ActionType.PlayKnight => "Play a knight",
            ActionType.PlayRoadBuilding => "Play Road Building",
            ActionType.PlayYearOfPlenty => $"Year of Plenty: {Cards(a.Get)}",
            ActionType.PlayMonopoly => $"Monopoly on {ResourceNames.Of(a.Target)}",
            ActionType.MoveRobber => a.Target2 >= 0 ? $"Robber to {Hex(a.Target)}, steal from {Name(names, a.Target2)}" : $"Robber to {Hex(a.Target)}",
            ActionType.BankTrade => $"Trade {Cards(a.Give)} to the bank for {Cards(a.Get)}",
            ActionType.AcceptOffer when s.Offers[a.Target] is { IsActive: true } o =>
                $"Accept {Name(names, o.From)}'s offer: your {Cards(o.Get)} for their {Cards(o.Give)}",
            ActionType.DeclineOffer when s.Offers[a.Target] is { IsActive: true } o => $"Decline {Name(names, o.From)}'s offer",
            ActionType.ConfirmTrade => $"Trade with {Name(names, a.Target2)}",
            ActionType.CancelOffer => "Withdraw your offer",
            ActionType.Discard => $"Discard {Cards(a.Give)}",
            _ => a.Type.ToString(),
        };
    }

    private static string Name(IReadOnlyList<string> names, int seat) => seat >= 0 && seat < names.Count ? names[seat] : $"seat {seat + 1}";
}
