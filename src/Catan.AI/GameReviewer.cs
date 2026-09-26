using Catan.Core;

namespace Catan.AI;

/// <summary>One decision you made: what you saw, what you could do, and what you did.</summary>
public sealed record Decision(PlayerView View, IReadOnlyList<GameAction> Legal, GameAction Move);

/// <summary>One of your plays after the game: its grade, the bots' pick, and what it cost you in victory points.</summary>
public sealed record ReviewedPlay(int Round, Phase Phase, MoveRating Yours, MoveRating Best, int Choices, double CostVp);

/// <summary>A whole game's plays, graded.</summary>
public sealed record GameReview(IReadOnlyList<ReviewedPlay> Plays)
{
    /// <summary>Plays costing less than this (in victory points) count as fine: the bots can't tell them apart.</summary>
    public const double MistakeVp = 0.1;

    /// <summary>Average rating of your plays (0–100).</summary>
    public double Score => Plays.Count == 0 ? 0 : Plays.Average(p => p.Yours.Rating);

    public int BestPicks => Plays.Count(p => p.Yours.Rank == 1 || p.CostVp < MistakeVp);

    public double TotalCostVp => Plays.Sum(p => p.CostVp);

    /// <summary>The plays that cost the most, worst first.</summary>
    public IEnumerable<ReviewedPlay> Mistakes => Plays.Where(p => p.CostVp >= MistakeVp).OrderByDescending(p => p.CostVp);
}

/// <summary>
/// Game review: grades every decision you made in a game with <see cref="DecisionCoach"/>, from exactly the view you had at
/// the time (never what you couldn't see), and measures each play by how much worse the bots think it was than their pick,
/// in victory points. Runs after the game, on all threads.
/// </summary>
public static class GameReviewer
{
    /// <summary>
    /// A decision the coach can rank fairly: a real choice (two or more moves) of a move from the legal list, and not trade
    /// bookkeeping on your own offers (withdraw, confirm, turn an acceptance down), which only follows the trade you already
    /// chose. Answering someone else's offer is graded.
    /// </summary>
    public static bool IsGradable(Decision d) =>
        d.Legal.Count > 1 && d.Legal.Contains(d.Move)
        && d.Move.Type is not (ActionType.CancelOffer or ActionType.ConfirmTrade)
        && !(d.Move.Type == ActionType.DeclineOffer && d.View.CurrentPlayer == d.View.Seat);

    public static GameReview Review(BotWeights weights, IReadOnlyList<Decision> decisions, IReadOnlyList<string> names, int samples = 12)
    {
        var plays = new ReviewedPlay?[decisions.Count];
        double vp = Math.Max(1e-9, weights["vp"]);
        Parallel.For(0, decisions.Count, i =>
        {
            var d = decisions[i];
            if (!IsGradable(d))
                return; // nothing to decide, trade bookkeeping, or a move the coach can't rank (offers aren't in the legal list)
            var grade = new DecisionCoach(weights).Grade(d.View, d.Legal, names, samples, seed: (ulong)i + 1);
            var yours = grade.Of(d.Move);
            var best = grade.Moves[0];
            plays[i] = new ReviewedPlay(d.View.TurnNumber / GameConstants.PlayerCount + 1, d.View.Phase, yours, best, grade.Moves.Count,
                Math.Max(0, (best.Score - yours.Score) / vp));
        });
        return new GameReview(plays.Where(p => p is not null).Select(p => p!).ToList());
    }
}
