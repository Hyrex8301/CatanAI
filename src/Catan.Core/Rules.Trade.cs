namespace Catan.Core;

public static partial class Rules
{
    private const int Seats = GameConstants.PlayerCount;

    // ---- Bank trades ----

    /// <summary>
    /// The seat's bank-trade ratio per resource: 4:1 always, 3:1 with a building on a generic harbor,
    /// 2:1 with a building on that resource's harbor.
    /// </summary>
    public static void TradeRatios(GameState s, int seat, Span<int> ratios)
    {
        ratios.Slice(0, R).Fill(4);
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            int spot = Topology.VertexHarbor[v];
            if (spot < 0 || s.VertexOwner[v] != seat)
                continue;
            var type = s.Board.HarborTypeAt(spot);
            if (type == HarborType.Generic)
            {
                for (int r = 0; r < R; r++)
                    ratios[r] = Math.Min(ratios[r], 3);
            }
            else
                ratios[(int)type] = 2;
        }
    }

    private static void BankTradeActions(GameState s, int seat, List<GameAction> buffer)
    {
        Span<int> ratios = stackalloc int[R];
        TradeRatios(s, seat, ratios);
        for (int give = 0; give < R; give++)
        {
            if (s.Hand[seat * R + give] < ratios[give])
                continue;
            for (int get = 0; get < R; get++)
                if (get != give && s.Bank[get] > 0)
                    buffer.Add(new GameAction(ActionType.BankTrade, seat,
                        Give: ResourceSet.Of((Resource)give, ratios[give]), Get: ResourceSet.Of((Resource)get)));
        }
    }

    private static bool IsLegalBankTrade(GameState s, GameAction a, out string reason)
    {
        int give = SingleResource(a.Give), get = SingleResource(a.Get);
        if (give < 0)
            return Fail("A bank trade gives cards of a single resource.", out reason);
        if (get < 0 || a.Get.Total != 1)
            return Fail("A bank trade gets exactly 1 card.", out reason);
        if (give == get)
            return Fail("You can't trade a resource for itself.", out reason);

        Span<int> ratios = stackalloc int[R];
        TradeRatios(s, a.Seat, ratios);
        if (a.Give.Total != ratios[give])
            return Fail($"You trade {(Resource)give} with the bank at {ratios[give]}:1.", out reason);
        if (!a.Give.FitsIn(s.HandOf(a.Seat)))
            return Fail("You don't have those cards.", out reason);
        if (s.Bank[get] == 0)
            return Fail($"The bank has no {(Resource)get} left.", out reason);
        return Pass(out reason);
    }

    private static void ApplyBankTrade(GameState s, GameAction a, List<GameEvent>? events)
    {
        Pay(s, a.Seat, a.Give);
        GiveFromBank(s, a.Seat, a.Get);
        events?.Add(new BankTraded(a.Seat, a.Give, a.Get));
    }

    /// <summary>The resource index if exactly one resource has a positive count and none is negative; else -1.</summary>
    private static int SingleResource(ResourceSet set)
    {
        int found = -1;
        for (int r = 0; r < R; r++)
        {
            if (set[r] < 0)
                return -1;
            if (set[r] > 0)
            {
                if (found >= 0)
                    return -1;
                found = r;
            }
        }
        return found;
    }

    // ---- Player trades: offer ----

    /// <summary>
    /// A random valid offer from the current player (1-2 of a resource it holds for 1-2 of another, to a random non-empty
    /// set of opponents), or null if it can't offer. Offers aren't in the legal list; bots build them.
    /// </summary>
    public static GameAction? RandomTradeOffer(GameState s, Rng rng)
    {
        int seat = s.CurrentPlayer;
        if (s.Phase != Phase.Main || s.OffersThisTurn >= s.Settings.MaxOffersPerTurn || s.HandSize(seat) == 0)
            return null;

        int give;
        do give = rng.NextInt(R); while (s.Hand[seat * R + give] == 0);
        int get = (give + 1 + rng.NextInt(R - 1)) % R;
        int giveCount = 1 + rng.NextInt(Math.Min(2, s.Hand[seat * R + give]));
        int getCount = 1 + rng.NextInt(2);

        int mask = 0;
        while (mask == 0)
            for (int other = 0; other < Seats; other++)
                if (other != seat && rng.NextInt(2) == 1)
                    mask |= 1 << other;

        return new GameAction(ActionType.OfferTrade, seat, mask,
            Give: ResourceSet.Of((Resource)give, giveCount), Get: ResourceSet.Of((Resource)get, getCount));
    }

    private static bool IsLegalOfferTrade(GameState s, GameAction a, out string reason)
    {
        if (s.OffersThisTurn >= s.Settings.MaxOffersPerTurn)
            return Fail($"You've made the maximum of {s.Settings.MaxOffersPerTurn} trade offers this turn.", out reason);
        int mask = a.Target;
        if (mask <= 0 || mask >= 1 << Seats || (mask & (1 << a.Seat)) != 0)
            return Fail("Offer the trade to at least one other player.", out reason);
        for (int r = 0; r < R; r++)
        {
            if (a.Give[r] < 0 || a.Get[r] < 0)
                return Fail("A trade can't have negative counts.", out reason);
            if (a.Give[r] > 0 && a.Get[r] > 0)
                return Fail($"{(Resource)r} can't be on both sides of a trade.", out reason);
        }
        if (a.Give.Total == 0 || a.Get.Total == 0)
            return Fail("Both sides must give at least one card (no gifts).", out reason);
        if (!a.Give.FitsIn(s.HandOf(a.Seat)))
            return Fail("You don't have the cards you're offering.", out reason);
        return Pass(out reason);
    }

    private static void ApplyOfferTrade(GameState s, GameAction a, List<GameEvent>? events)
    {
        s.Offer = new TradeOffer(true, a.Give, a.Get, a.Target);
        for (int seat = 0; seat < Seats; seat++)
            s.OfferReply[seat] = (sbyte)((a.Target & (1 << seat)) != 0 ? -1 : 0);
        s.OffersThisTurn++;
        s.Phase = Phase.TradeReply;
        events?.Add(new TradeOffered(a.Seat, a.Give, a.Get, a.Target));
    }

    // ---- Player trades: replies ----

    /// <summary>Offered seats reply one at a time in turn order after the current player.</summary>
    private static int NextReplier(GameState s)
    {
        for (int i = 1; i < Seats; i++)
        {
            int seat = (s.CurrentPlayer + i) % Seats;
            if (s.OfferReply[seat] == -1)
                return seat;
        }
        return -1;
    }

    private static void TradeReplyActions(GameState s, int seat, List<GameAction> buffer)
    {
        if (s.Offer.Get.FitsIn(s.HandOf(seat)))
            buffer.Add(new GameAction(ActionType.AcceptOffer, seat));
        buffer.Add(new GameAction(ActionType.DeclineOffer, seat));
    }

    private static bool IsLegalTradeReply(GameState s, GameAction a, out string reason)
    {
        if (a.Type == ActionType.DeclineOffer)
            return Pass(out reason);
        if (a.Type != ActionType.AcceptOffer)
            return Fail("Accept or decline the trade offer.", out reason);
        if (!s.Offer.Get.FitsIn(s.HandOf(a.Seat)))
            return Fail("You don't have the cards this trade asks for.", out reason);
        return Pass(out reason);
    }

    private static void ApplyTradeReply(GameState s, GameAction a, List<GameEvent>? events)
    {
        bool accepted = a.Type == ActionType.AcceptOffer;
        s.OfferReply[a.Seat] = (sbyte)(accepted ? 1 : 0);
        events?.Add(new TradeReplied(a.Seat, accepted));
        if (NextReplier(s) < 0)
            s.Phase = Phase.TradeConfirm;
    }

    // ---- Player trades: confirm or cancel ----

    private static void TradeConfirmActions(GameState s, int seat, List<GameAction> buffer)
    {
        for (int partner = 0; partner < Seats; partner++)
            if (s.OfferReply[partner] == 1)
                buffer.Add(new GameAction(ActionType.ConfirmTrade, seat, partner));
        buffer.Add(new GameAction(ActionType.CancelOffer, seat));
    }

    private static bool IsLegalTradeConfirm(GameState s, GameAction a, out string reason)
    {
        if (a.Type == ActionType.CancelOffer)
            return Pass(out reason);
        if (a.Type != ActionType.ConfirmTrade)
            return Fail("Confirm the trade with a player who accepted, or cancel it.", out reason);
        if (a.Target is < 0 or >= Seats || s.OfferReply[a.Target] != 1 || a.Target == a.Seat)
            return Fail("You can only confirm with a player who accepted.", out reason);
        return Pass(out reason);
    }

    private static void ApplyConfirmTrade(GameState s, GameAction a, List<GameEvent>? events)
    {
        var offer = s.Offer;
        int partner = a.Target;
        for (int r = 0; r < R; r++)
        {
            s.Hand[a.Seat * R + r] += offer.Get[r] - offer.Give[r];
            s.Hand[partner * R + r] += offer.Give[r] - offer.Get[r];
        }
        events?.Add(new TradeDone(a.Seat, partner, offer.Give, offer.Get));
        ClearOffer(s);
    }

    private static void ApplyCancelOffer(GameState s, GameAction a, List<GameEvent>? events)
    {
        events?.Add(new TradeCancelled(a.Seat));
        ClearOffer(s);
    }

    private static void ClearOffer(GameState s)
    {
        s.Offer = default;
        Array.Fill(s.OfferReply, (sbyte)-1);
        s.Phase = Phase.Main;
    }
}
