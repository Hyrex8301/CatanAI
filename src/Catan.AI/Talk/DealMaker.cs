using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>
/// A bot's side of table talk on its own turn (Main phase, the current player). First it answers deals said to it in the
/// chat: yes when the cards plus the promises beat doing nothing, and then it makes the trade offer that carries them.
/// Otherwise, at most once per turn and only while nobody has <see cref="TableTalk.DealsUntilVp"/> points, it looks for one
/// deal worth making: selling a non-block ("sheep for wheat nb?": we promise not to block them on our next robber move, they
/// take a swap they would otherwise refuse) or buying one ("sheep for wheat, nb me?"). A deal is proposed only when the
/// promise itself matters: it prevents at least <see cref="MinPromisePoints"/> victory points of expected damage, the deal only happens
/// because of it (the plain swap is one the partner would refuse, or we would), both sides should gain, and there is no
/// promise already in force between the two.
/// </summary>
public static class DealMaker
{
    /// <summary>Smallest expected damage a promise must prevent to be worth a deal, in victory points.</summary>
    public const double MinPromisePoints = 0.17;

    /// <summary>How much more than <see cref="Trading.MinGain"/> the other side should gain, so proposals are likely taken.</summary>
    public const double PartnerMargin = 0.5;

    private const int R = GameConstants.ResourceCount;
    private static readonly PromiseTerm[] Nb = { new(PromiseKind.NoBlock) };

    /// <summary>A move for the current player from table talk, or null. <paramref name="lastDealTurn"/> limits proposals to one per turn.</summary>
    public static GameAction? Act(PlayerView view, HandTracker tracker, Evaluator eval, TableTalk talk, Rng rng, ref int lastDealTurn)
    {
        int me = view.Seat;
        if (view.Phase != Phase.Main || view.CurrentPlayer != me || view.OffersThisTurn >= view.Settings.MaxOffersPerTurn)
            return null;
        foreach (var o in view.Offers)
            if (o.IsActive)
                return null; // let open trades settle first

        foreach (var proposal in talk.OpenProposalsFor(me, view.TurnNumber))
            if (AnswerProposal(view, tracker, eval, talk, rng, proposal) is { } move)
                return move;

        if (lastDealTurn == view.TurnNumber || !TableTalk.DealsOpen(view))
            return null;
        lastDealTurn = view.TurnNumber; // look once per turn, found or not
        return Propose(view, tracker, eval, talk, rng);
    }

    /// <summary>Says yes (and makes the offer) or no to a deal someone said to us.</summary>
    private static GameAction? AnswerProposal(PlayerView view, HandTracker tracker, Evaluator eval, TableTalk talk, Rng rng, ChatProposal proposal)
    {
        int me = view.Seat, turn = view.TurnNumber;
        var r = proposal.Reading;
        int partner = r.Speaker;
        if (!TableTalk.DealsOpen(view))
        {
            talk.Answer(me, false, turn, me);
            talk.Say(me, "too late for deals", turn);
            return null;
        }

        var s = Determinizer.Build(view, tracker, rng);
        // From our side: we give what they want, get what they give, keep their asks, receive their promises.
        var give = r.SpeakerGets;
        var get = r.SpeakerGives;
        double promises = Value(s, eval, talk, partner, me, r.SpeakerPromises) - Cost(s, eval, talk, me, partner, r.TargetPromises);

        double best = double.NegativeInfinity;
        ResourceSet bestGive = give, bestGet = get;
        if (give.Total == 0 && get.Total == 0)
            best = promises; // a straight swap of promises
        else
            foreach (var (g, t) in Fill(give, get, view.Hand))
            {
                if (!g.FitsIn(s.HandOf(me)))
                    continue;
                double gain = Trading.ScoreAfterSwap(s, me, partner, g, t, eval) - eval.Score(s, me) + promises;
                if (gain > best)
                    (best, bestGive, bestGet) = (gain, g, t);
            }

        bool yes = best > Trading.MinGain;
        talk.Answer(me, yes, turn, me);
        talk.Say(me, yes ? "deal" : "no thanks", turn);
        if (!yes || (bestGive.Total == 0 && bestGet.Total == 0))
            return null;
        var offer = new GameAction(ActionType.OfferTrade, me, Give: bestGive, Get: bestGet);
        return Rules.IsLegal(Determinizer.Build(view, tracker, rng), offer, out _) ? offer : null;
    }

    /// <summary>The best deal to propose this turn, if any: posts it in the chat and returns the offer that carries it.</summary>
    private static GameAction? Propose(PlayerView view, HandTracker tracker, Evaluator eval, TableTalk talk, Rng rng)
    {
        int me = view.Seat, turn = view.TurnNumber;
        var s = Determinizer.Build(view, tracker, rng);
        double myNow = eval.Score(s, me);
        double minPromise = MinPromisePoints * eval.Weights["vp"];
        double bestGain = Trading.MinGain;
        (int Partner, int Give, int Get, bool Sell)? best = null;

        for (int partner = 0; partner < GameConstants.PlayerCount; partner++)
        {
            if (partner == me)
                continue;
            if (talk.Deals.MadeBy(me, turn).Any(p => p.To == partner) || talk.Deals.MadeBy(partner, turn).Any(p => p.To == me))
                continue; // already a deal between us
            double theirNow = eval.Score(s, partner);
            var sell = DealValues.Price(Nb, DealValues.Threat(s, eval, me, partner, Kept(talk, me, turn)), talk.Deals.Trust(me, turn));
            var buy = DealValues.Price(Nb, DealValues.Threat(s, eval, partner, me, Kept(talk, partner, turn)), talk.Deals.Trust(partner, turn));
            for (int g = 0; g < R; g++)
            {
                if (view.Hand[g] == 0)
                    continue;
                for (int t = 0; t < R; t++)
                {
                    if (t == g || s.Hand[partner * R + t] == 0)
                        continue;
                    var give = ResourceSet.Of((Resource)g);
                    var get = ResourceSet.Of((Resource)t);
                    double mySwap = Trading.ScoreAfterSwap(s, me, partner, give, get, eval) - myNow;
                    double theirSwap = Trading.ScoreAfterSwap(s, partner, me, get, give, eval) - theirNow;
                    // Only deals the promise makes happen: they'd refuse the plain swap (sell), or we would (buy).
                    if (sell.ValueToVictim >= minPromise && theirSwap <= Trading.MinGain)
                        Consider(mySwap - sell.CostToPromiser, theirSwap + sell.ValueToVictim, partner, g, t, true);
                    if (buy.ValueToVictim >= minPromise && mySwap <= Trading.MinGain)
                        Consider(mySwap + buy.ValueToVictim, theirSwap - buy.CostToPromiser, partner, g, t, false);
                }
            }
        }
        if (best is not { } deal)
            return null;

        var offer = new GameAction(ActionType.OfferTrade, me, Give: ResourceSet.Of((Resource)deal.Give), Get: ResourceSet.Of((Resource)deal.Get));
        talk.AttachToNextOffer(new DealTerms(me, deal.Partner, deal.Sell ? Nb : Array.Empty<PromiseTerm>(), deal.Sell ? Array.Empty<PromiseTerm>() : Nb,
            offer.Give, offer.Get, null));
        string cards = $"{ResourceNames.Of(deal.Give)} for {ResourceNames.Of(deal.Get)}";
        talk.Say(me, deal.Sell ? $"{talk.Names[deal.Partner]}, {cards} nb?" : $"{talk.Names[deal.Partner]}, {cards}, nb me?", turn);
        return offer;

        void Consider(double mine, double theirs, int partner, int g, int t, bool isSell)
        {
            if (mine > bestGain && theirs > Trading.MinGain + PartnerMargin)
                (bestGain, best) = (mine, (partner, g, t, isSell));
        }
    }

    /// <summary>What <paramref name="terms"/> promised by <paramref name="promiser"/> are worth to <paramref name="victim"/>, discounted by trust.</summary>
    public static double Value(GameState s, Evaluator eval, TableTalk talk, int promiser, int victim, IReadOnlyList<PromiseTerm> terms) =>
        terms.Count == 0 ? 0 : DealValues.Price(terms, DealValues.Threat(s, eval, promiser, victim, Kept(talk, promiser, s.TurnNumber)),
            talk.Deals.Trust(promiser, s.TurnNumber)).ValueToVictim;

    /// <summary>What promising <paramref name="terms"/> to <paramref name="victim"/> costs <paramref name="promiser"/>.</summary>
    public static double Cost(GameState s, Evaluator eval, TableTalk talk, int promiser, int victim, IReadOnlyList<PromiseTerm> terms) =>
        terms.Count == 0 ? 0 : DealValues.Price(terms, DealValues.Threat(s, eval, promiser, victim, Kept(talk, promiser, s.TurnNumber))).CostToPromiser;

    /// <summary>Players <paramref name="promiser"/> has promised not to block (promises still in force).</summary>
    public static List<int> Kept(TableTalk talk, int promiser, int turn) =>
        talk.Deals.MadeBy(promiser, turn).Where(p => p.Term.Kind == PromiseKind.NoBlock).Select(p => p.To).Distinct().ToList();

    /// <summary>
    /// The trades a proposal allows: its cards as named, with a side left open ("wheat nb?" names only what we get) filled
    /// with any one card we have.
    /// </summary>
    private static IEnumerable<(ResourceSet Give, ResourceSet Get)> Fill(ResourceSet give, ResourceSet get, IReadOnlyList<int> hand)
    {
        if (give.Total > 0 && get.Total > 0)
        {
            yield return (give, get);
            yield break;
        }
        for (int r = 0; r < R; r++)
        {
            var one = ResourceSet.Of((Resource)r);
            if (give.Total == 0 && hand[r] > 0 && get[r] == 0)
                yield return (one, get);
            else if (get.Total == 0 && give[r] == 0)
                yield return (give, one);
        }
    }
}
