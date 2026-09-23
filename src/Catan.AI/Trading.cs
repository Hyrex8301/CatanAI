using Catan.Core;

namespace Catan.AI;

/// <summary>
/// SmartBot's player-trade judgment. Offers and answers are scored with the same evaluation as everything else: a trade is
/// worth it if the position after it scores better for this seat. The score already subtracts the strongest opponent's
/// value, so a trade that helps the leader more than it helps us looks bad. Confirming accepted offers and answering
/// counters happen in the planner (they're in the legal list).
/// </summary>
public static class Trading
{
    /// <summary>Trades we propose per turn (the rules allow 10; bots don't spam).</summary>
    public const int OffersPerTurn = 2;

    /// <summary>Minimum score gain to propose or accept a trade.</summary>
    public const double MinGain = 1.0;

    private const int R = GameConstants.ResourceCount;

    /// <summary>On our Main turn with no offer open: the best 1-for-1 or 2-for-1 offer, if it clearly helps.</summary>
    public static GameAction? ProposeOffer(PlayerView view, HandTracker tracker, Evaluator eval, Rng rng)
    {
        int me = view.Seat;
        if (view.Phase != Phase.Main || view.CurrentPlayer != me || view.OffersThisTurn >= Math.Min(OffersPerTurn, view.Settings.MaxOffersPerTurn))
            return null;
        foreach (var o in view.Offers)
            if (o.IsActive && !o.IsCounter)
                return null; // one at a time: let answers come in first

        var state = Determinizer.Build(view, tracker, rng);
        double now = eval.Score(state, me);
        double bestGain = MinGain;
        GameAction? best = null;
        for (int give = 0; give < R; give++)
            for (int count = 1; count <= 2; count++)
            {
                if (view.Hand[give] < count)
                    continue;
                for (int get = 0; get < R; get++)
                {
                    if (get == give)
                        continue;
                    var gives = ResourceSet.Of((Resource)give, count);
                    var gets = ResourceSet.Of((Resource)get);
                    double gain = ScoreWithHand(state, me, gives, gets, eval) - now;
                    if (gain > bestGain)
                        (bestGain, best) = (gain, new GameAction(ActionType.OfferTrade, me, Give: gives, Get: gets));
                }
            }
        return best;
    }

    /// <summary>
    /// Answers open offers from the current player: accepts the one that helps us most (net of helping them), if any helps
    /// enough; otherwise declines the first one it hasn't answered. Null when there's nothing to answer.
    /// </summary>
    public static GameAction? Answer(PlayerView view, HandTracker tracker, Evaluator eval, IReadOnlyList<GameAction> legal, Rng rng)
    {
        int me = view.Seat;
        GameState? state = null;
        double now = 0, bestGain = MinGain;
        GameAction? accept = null, decline = null;

        foreach (var a in legal)
        {
            if (a.Type == ActionType.DeclineOffer)
                decline ??= a;
            if (a.Type != ActionType.AcceptOffer)
                continue;
            var offer = view.Offers[a.Target];
            state ??= Determinizer.Build(view, tracker, rng);
            if (now == 0)
                now = eval.Score(state, me);
            // Accepting: we give what they ask for (offer.Get) and receive what they give (offer.Give).
            double gain = ScoreAfterSwap(state, me, offer.From, offer.Get, offer.Give, eval) - now;
            if (gain > bestGain)
                (bestGain, accept) = (gain, a);
        }
        return accept ?? decline;
    }

    private static double ScoreWithHand(GameState s, int me, ResourceSet gives, ResourceSet gets, Evaluator eval)
    {
        Adjust(s, me, gets - gives);
        double score = eval.Score(s, me);
        Adjust(s, me, gives - gets);
        return score;
    }

    private static double ScoreAfterSwap(GameState s, int me, int partner, ResourceSet myGive, ResourceSet myGet, Evaluator eval)
    {
        if (!myGive.FitsIn(s.HandOf(me)) || !myGet.FitsIn(s.HandOf(partner)))
            return double.MinValue; // not possible in this sample of hidden cards
        Adjust(s, me, myGet - myGive);
        Adjust(s, partner, myGive - myGet);
        double score = eval.Score(s, me);
        Adjust(s, me, myGive - myGet);
        Adjust(s, partner, myGet - myGive);
        return score;
    }

    private static void Adjust(GameState s, int seat, ResourceSet delta)
    {
        for (int r = 0; r < R; r++)
            s.Hand[seat * R + r] += delta[r];
    }
}
