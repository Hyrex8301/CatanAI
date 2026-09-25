using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>
/// How bots keep their word: a move that would break one of their promises loses <see cref="BreakMarginPoints"/> victory
/// points' worth from its score, so it is only played when it beats the best honest move by more than that.
/// </summary>
public static class PromiseKeeping
{
    /// <summary>What a break must gain over the best honest move, in victory points (scaled by the weights' value of a point).</summary>
    public const double BreakMarginPoints = 1.1;

    /// <summary>
    /// Lowers the score of each move in <paramref name="legal"/> that would break a promise of the deciding seat.
    /// <paramref name="samples"/> is how many samples the scores were summed over (the margin applies per sample);
    /// <paramref name="pointValue"/> is what the scores' weights give a victory point.
    /// Returns true if any move was penalized.
    /// </summary>
    public static bool Penalize(DealBook? deals, PlayerView view, IReadOnlyList<GameAction> legal, double[] scores, int samples = 1,
        double pointValue = 18)
    {
        if (deals is null)
            return false;
        bool any = false;
        for (int i = 0; i < legal.Count; i++)
            if (deals.WouldBreak(view, legal[i]) is not null)
            {
                scores[i] -= BreakMarginPoints * pointValue * Math.Max(1, samples);
                any = true;
            }
        return any;
    }
}
