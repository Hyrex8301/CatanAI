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

        var offeredThisTurn = OffersThisTurn(view);
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
                    if (offeredThisTurn.Contains((gives, gets)))
                        continue; // already asked this turn: the answer won't change
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
    public static GameAction? Answer(PlayerView view, HandTracker tracker, Evaluator eval, IReadOnlyList<GameAction> legal, Rng rng,
        Talk.TableTalk? talk = null)
    {
        int me = view.Seat;
        GameState? state = null;
        double now = 0, bestGain = MinGain;
        GameAction? accept = null, decline = null;
        bool acceptIsDeal = false;
        Talk.DealTerms? declinedDeal = null;

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
            // Promises riding on the offer (table talk): theirs to us are worth what they prevent, ours cost what we give up.
            var deal = talk?.DealOn(a.Target);
            if (deal is not null)
            {
                if (deal.Partner >= 0 && deal.Partner != me)
                    continue; // a deal meant for someone else
                gain += Talk.DealMaker.Value(state, eval, talk!, offer.From, me, deal.CurrentPromises)
                        - Talk.DealMaker.Cost(state, eval, talk!, me, offer.From, deal.PartnerPromises);
                declinedDeal ??= deal;
            }
            if (gain > bestGain)
                (bestGain, accept, acceptIsDeal) = (gain, a, deal is not null);
        }
        if (talk is not null && (acceptIsDeal || (accept is null && declinedDeal is not null)))
            talk.Say(me, acceptIsDeal ? "deal" : "no thanks", view.TurnNumber);
        return accept ?? decline;
    }

    /// <summary>
    /// On our Main turn, settles trades already on the table before anything else: a counter made to us is accepted if it
    /// helps enough and declined otherwise; our offer that someone accepted is confirmed with the partner it helps most (or
    /// cancelled if the trade no longer helps); our offer nobody accepted is withdrawn. Every answer round has finished by
    /// the time we're asked (the runner waits for the answers), so nothing we leave open would change. Null when there's
    /// nothing to settle.
    /// </summary>
    public static GameAction? SettleOpenTrades(PlayerView view, HandTracker tracker, Evaluator eval, IReadOnlyList<GameAction> legal, Rng rng,
        Talk.TableTalk? talk = null)
    {
        int me = view.Seat;
        if (view.Phase != Phase.Main || view.CurrentPlayer != me)
            return null;
        GameState? state = null;
        double now = 0;
        double Gain(int partner, ResourceSet myGive, ResourceSet myGet)
        {
            if (state is null)
            {
                state = Determinizer.Build(view, tracker, rng);
                now = eval.Score(state, me);
            }
            return ScoreAfterSwap(state, me, partner, myGive, myGet, eval) - now;
        }

        for (int slot = 0; slot < view.Offers.Length; slot++)
        {
            var o = view.Offers[slot];
            if (!o.IsActive || !o.IsCounter)
                continue;
            // A counter: they give o.Give and ask for o.Get.
            var accept = new GameAction(ActionType.AcceptOffer, me, slot);
            if (legal.Contains(accept) && Gain(o.From, o.Get, o.Give) > MinGain)
                return accept;
            return new GameAction(ActionType.DeclineOffer, me, slot);
        }

        for (int slot = 0; slot < view.Offers.Length; slot++)
        {
            var o = view.Offers[slot];
            if (!o.IsActive || o.IsCounter || o.From != me)
                continue;
            var deal = talk?.DealOn(slot);
            GameAction? best = null;
            // A deal we proposed is seen through unless it has clearly turned bad (a fresh sample of hidden cards moves
            // the numbers a little, and backing out after they said "deal" would be rude).
            double bestGain = deal is not null ? -MinGain : 0;
            for (int partner = 0; partner < GameConstants.PlayerCount; partner++)
            {
                var confirm = new GameAction(ActionType.ConfirmTrade, me, slot, partner);
                if (o.ResponseOf(partner) != TradeOffer.Accepted || !legal.Contains(confirm))
                    continue;
                if (deal is not null && deal.Partner >= 0 && deal.Partner != partner)
                    continue; // the deal was with someone else
                double gain = Gain(partner, o.Give, o.Get);
                if (deal is not null)
                    gain += Talk.DealMaker.Value(state!, eval, talk!, partner, me, deal.PartnerPromises)
                            - Talk.DealMaker.Cost(state!, eval, talk!, me, partner, deal.CurrentPromises);
                if (gain > bestGain)
                    (bestGain, best) = (gain, confirm);
            }
            return best ?? new GameAction(ActionType.CancelOffer, me, slot);
        }
        return null;
    }

    /// <summary>The (give, get) of every offer and edit we made this turn, from the log.</summary>
    private static HashSet<(ResourceSet Give, ResourceSet Get)> OffersThisTurn(PlayerView view)
    {
        var seen = new HashSet<(ResourceSet, ResourceSet)>();
        for (int i = view.Events.Count - 1; i >= 0; i--)
        {
            switch (view.Events[i])
            {
                case TurnEnded:
                    return seen;
                case TradeOffered o when o.Seat == view.Seat:
                    seen.Add((o.Give, o.Get));
                    break;
                case TradeEdited e when e.Seat == view.Seat:
                    seen.Add((e.Give, e.Get));
                    break;
            }
        }
        return seen;
    }

    private static double ScoreWithHand(GameState s, int me, ResourceSet gives, ResourceSet gets, Evaluator eval)
    {
        Adjust(s, me, gets - gives);
        double score = eval.Score(s, me);
        Adjust(s, me, gives - gets);
        return score;
    }

    /// <summary>
    /// Our score after giving <paramref name="myGive"/> for <paramref name="myGet"/>. Only called for cards the partner has
    /// shown they hold (they offered, countered or accepted) or that their sampled hand holds, so a sample that lacks them is
    /// corrected first (<see cref="ShowCards"/>). MinValue if we can't pay.
    /// </summary>
    internal static double ScoreAfterSwap(GameState s, int me, int partner, ResourceSet myGive, ResourceSet myGet, Evaluator eval)
    {
        if (!myGive.FitsIn(s.HandOf(me)))
            return double.MinValue;
        ShowCards(s, partner, myGet);
        Adjust(s, me, myGet - myGive);
        Adjust(s, partner, myGive - myGet);
        double score = eval.Score(s, me);
        Adjust(s, me, myGive - myGet);
        Adjust(s, partner, myGet - myGive);
        return score;
    }

    /// <summary>
    /// Makes a sampled hand hold <paramref name="cards"/>, which the player has shown they have: each missing card replaces
    /// one of the resource they hold most of (the hand size is public and stays the same).
    /// </summary>
    internal static void ShowCards(GameState s, int seat, ResourceSet cards)
    {
        for (int r = 0; r < R; r++)
            while (s.Hand[seat * R + r] < cards[r])
            {
                int most = -1;
                for (int x = 0; x < R; x++)
                    if (s.Hand[seat * R + x] > cards[x] && (most < 0 || s.Hand[seat * R + x] > s.Hand[seat * R + most]))
                        most = x;
                if (most >= 0)
                    s.Hand[seat * R + most]--;
                s.Hand[seat * R + r]++;
            }
    }

    private static void Adjust(GameState s, int seat, ResourceSet delta)
    {
        for (int r = 0; r < R; r++)
            s.Hand[seat * R + r] += delta[r];
    }
}
