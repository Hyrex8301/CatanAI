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

    // ---- Player trades ----
    //
    // Modeled on colonist.io. During Main the current player may have up to 10 offers open at once; every offer goes to all
    // opponents. New offers and edits count toward MaxOffersPerTurn; an edit resets that offer's responses. Opponents are
    // never waited on: at any time, in any order, each may Accept, Decline or Counter each open offer (once per version).
    // A counter is a proposal from that opponent to the current player (one open counter per opponent; a new one replaces it).
    // The current player confirms with any opponent who accepted, rejects an acceptance, cancels an offer, or accepts or
    // declines a counter. Accepting a counter trades immediately. All open trades close at the end of the turn.

    /// <summary>Accept, Decline, Counter or Cancel: the actions an opponent may take on open trades outside its turn.</summary>
    public static bool IsTradeResponse(ActionType type) =>
        type is ActionType.AcceptOffer or ActionType.DeclineOffer or ActionType.CounterOffer or ActionType.CancelOffer;

    /// <summary>
    /// Seats other than the acting seat that may act right now (opponents who can respond to open trades).
    /// The game never waits for them; runners ask them alongside the acting seat.
    /// </summary>
    public static int OptionalSeats(GameState s, Span<bool> canAct)
    {
        canAct.Slice(0, Seats).Clear();
        int count = 0;
        if (s.Phase != Phase.Main)
            return 0;
        for (int seat = 0; seat < Seats; seat++)
        {
            if (seat == s.CurrentPlayer)
                continue;
            foreach (var o in s.Offers)
                if (o.IsActive && ((!o.IsCounter && o.ResponseOf(seat) == TradeOffer.NoResponse) || (o.IsCounter && o.From == seat)))
                {
                    canAct[seat] = true;
                    count++;
                    break;
                }
        }
        return count;
    }

    private static bool ValidTerms(ResourceSet give, ResourceSet get, out string reason)
    {
        for (int r = 0; r < R; r++)
        {
            if (give[r] < 0 || get[r] < 0)
                return Fail("A trade can't have negative counts.", out reason);
            if (give[r] > 0 && get[r] > 0)
                return Fail($"{(Resource)r} can't be on both sides of a trade.", out reason);
        }
        if (give.Total == 0 || get.Total == 0)
            return Fail("Both sides must give at least one card (no gifts).", out reason);
        return Pass(out reason);
    }

    private static int OpenOwnOffers(GameState s)
    {
        int count = 0;
        foreach (var o in s.Offers)
            if (o.IsActive && !o.IsCounter)
                count++;
        return count;
    }

    private static int FreeOfferSlot(GameState s) => Array.FindIndex(s.Offers, o => !o.IsActive);

    private static int CounterSlotOf(GameState s, int seat) =>
        Array.FindIndex(s.Offers, o => o.IsActive && o.IsCounter && o.From == seat);

    private static bool IsOpenOffer(GameState s, int slot, out TradeOffer offer, out string reason)
    {
        if (slot is < 0 or >= GameConstants.OfferSlots || !s.Offers[slot].IsActive)
        {
            offer = default;
            return Fail($"There's no open trade in slot {slot}.", out reason);
        }
        offer = s.Offers[slot];
        return Pass(out reason);
    }

    /// <summary>Both sides still hold their cards: <paramref name="a"/> gives <paramref name="aGives"/>, <paramref name="b"/> gives <paramref name="bGives"/>.</summary>
    private static bool CanSwap(GameState s, int a, ResourceSet aGives, int b, ResourceSet bGives) =>
        aGives.FitsIn(s.HandOf(a)) && bGives.FitsIn(s.HandOf(b));

    // ---- Legal lists ----

    /// <summary>The current player's enumerable trade actions in Main (new offers and edits are built by the agent).</summary>
    private static void CurrentPlayerTradeActions(GameState s, int seat, List<GameAction> buffer)
    {
        for (int slot = 0; slot < GameConstants.OfferSlots; slot++)
        {
            var o = s.Offers[slot];
            if (!o.IsActive)
                continue;
            if (o.IsCounter)
            {
                if (CanSwap(s, seat, o.Get, o.From, o.Give))
                    buffer.Add(new GameAction(ActionType.AcceptOffer, seat, slot));
                buffer.Add(new GameAction(ActionType.DeclineOffer, seat, slot));
                continue;
            }
            for (int partner = 0; partner < Seats; partner++)
                if (o.ResponseOf(partner) == TradeOffer.Accepted)
                {
                    if (CanSwap(s, seat, o.Give, partner, o.Get))
                        buffer.Add(new GameAction(ActionType.ConfirmTrade, seat, slot, partner));
                    buffer.Add(new GameAction(ActionType.DeclineOffer, seat, slot, partner));
                }
            buffer.Add(new GameAction(ActionType.CancelOffer, seat, slot));
        }
    }

    /// <summary>An opponent's enumerable responses (counters are built by the agent).</summary>
    private static void ResponderTradeActions(GameState s, int seat, List<GameAction> buffer)
    {
        for (int slot = 0; slot < GameConstants.OfferSlots; slot++)
        {
            var o = s.Offers[slot];
            if (!o.IsActive)
                continue;
            if (!o.IsCounter && o.ResponseOf(seat) == TradeOffer.NoResponse)
            {
                if (o.Get.FitsIn(s.HandOf(seat)))
                    buffer.Add(new GameAction(ActionType.AcceptOffer, seat, slot));
                buffer.Add(new GameAction(ActionType.DeclineOffer, seat, slot));
            }
            else if (o.IsCounter && o.From == seat)
                buffer.Add(new GameAction(ActionType.CancelOffer, seat, slot));
        }
    }

    // ---- Legality: current player ----

    private static bool IsLegalCurrentPlayerTrade(GameState s, GameAction a, out string reason)
    {
        int seat = a.Seat;
        switch (a.Type)
        {
            case ActionType.OfferTrade:
                if (s.OffersThisTurn >= s.Settings.MaxOffersPerTurn)
                    return Fail($"You've used all {s.Settings.MaxOffersPerTurn} trade offers and edits this turn.", out reason);
                if (OpenOwnOffers(s) >= GameConstants.MaxOpenOffers)
                    return Fail($"You can have at most {GameConstants.MaxOpenOffers} offers open at once.", out reason);
                if (!ValidTerms(a.Give, a.Get, out reason))
                    return false;
                if (!a.Give.FitsIn(s.HandOf(seat)))
                    return Fail("You don't have the cards you're offering.", out reason);
                return Pass(out reason);

            case ActionType.EditOffer:
                if (!IsOpenOffer(s, a.Target, out var edited, out reason))
                    return false;
                if (edited.IsCounter)
                    return Fail("You can only edit your own offers.", out reason);
                if (s.OffersThisTurn >= s.Settings.MaxOffersPerTurn)
                    return Fail($"You've used all {s.Settings.MaxOffersPerTurn} trade offers and edits this turn.", out reason);
                if (!ValidTerms(a.Give, a.Get, out reason))
                    return false;
                if (!a.Give.FitsIn(s.HandOf(seat)))
                    return Fail("You don't have the cards you're offering.", out reason);
                return Pass(out reason);

            case ActionType.ConfirmTrade:
                if (!IsOpenOffer(s, a.Target, out var confirmed, out reason))
                    return false;
                if (confirmed.IsCounter)
                    return Fail("Accept a counter-offer instead of confirming it.", out reason);
                if (a.Target2 is < 0 or >= Seats || a.Target2 == seat || confirmed.ResponseOf(a.Target2) != TradeOffer.Accepted)
                    return Fail("You can only confirm with a player who accepted.", out reason);
                if (!CanSwap(s, seat, confirmed.Give, a.Target2, confirmed.Get))
                    return Fail("One of you no longer has the cards for this trade.", out reason);
                return Pass(out reason);

            case ActionType.AcceptOffer:
                if (!IsOpenOffer(s, a.Target, out var counter, out reason))
                    return false;
                if (a.Target2 != -1)
                    return Fail("Accepting a counter-offer takes no partner (it's from its maker).", out reason);
                if (!counter.IsCounter)
                    return Fail("Confirm your own offer with a player who accepted it.", out reason);
                if (!CanSwap(s, seat, counter.Get, counter.From, counter.Give))
                    return Fail("One of you no longer has the cards for this trade.", out reason);
                return Pass(out reason);

            case ActionType.DeclineOffer:
                if (!IsOpenOffer(s, a.Target, out var declined, out reason))
                    return false;
                if (declined.IsCounter)
                    return a.Target2 == -1 ? Pass(out reason) : Fail("Declining a counter-offer takes no partner.", out reason);
                if (a.Target2 is < 0 or >= Seats || declined.ResponseOf(a.Target2) != TradeOffer.Accepted)
                    return Fail("Choose a player who accepted to turn down.", out reason);
                return Pass(out reason);

            case ActionType.CancelOffer:
                if (!IsOpenOffer(s, a.Target, out var cancelled, out reason))
                    return false;
                if (a.Target2 != -1)
                    return Fail("Cancelling an offer takes no partner.", out reason);
                if (cancelled.IsCounter)
                    return Fail("Decline a counter-offer instead of cancelling it.", out reason);
                return Pass(out reason);

            default: // CounterOffer
                return Fail("Only other players can make counter-offers.", out reason);
        }
    }

    // ---- Legality: opponents ----

    private static bool IsLegalResponderTrade(GameState s, GameAction a, out string reason)
    {
        int seat = a.Seat;
        if (!IsOpenOffer(s, a.Target, out var o, out reason))
            return false;
        if (a.Target2 != -1)
            return Fail("Answering an offer takes no partner.", out reason);

        if (a.Type == ActionType.CancelOffer)
            return o.IsCounter && o.From == seat ? Pass(out reason) : Fail("You can only withdraw your own counter-offer.", out reason);
        if (o.IsCounter)
            return Fail("Only the current player can answer a counter-offer.", out reason);
        if (o.ResponseOf(seat) != TradeOffer.NoResponse)
            return Fail("You've already answered this offer.", out reason);

        switch (a.Type)
        {
            case ActionType.AcceptOffer:
                return o.Get.FitsIn(s.HandOf(seat)) ? Pass(out reason) : Fail("You don't have the cards this trade asks for.", out reason);
            case ActionType.DeclineOffer:
                return Pass(out reason);
            default: // CounterOffer: Give / Get are from the countering player's side
                if (!ValidTerms(a.Give, a.Get, out reason))
                    return false;
                if (!a.Give.FitsIn(s.HandOf(seat)))
                    return Fail("You don't have the cards you're offering.", out reason);
                return Pass(out reason);
        }
    }

    // ---- Apply ----

    private static void ApplyPlayerTrade(GameState s, GameAction a, List<GameEvent>? events)
    {
        int seat = a.Seat;
        bool current = seat == s.CurrentPlayer;
        switch (a.Type)
        {
            case ActionType.OfferTrade:
            {
                int slot = FreeOfferSlot(s);
                s.Offers[slot] = new TradeOffer(true, seat, -1, a.Give, a.Get, 0);
                s.OffersThisTurn++;
                events?.Add(new TradeOffered(seat, slot, a.Give, a.Get));
                break;
            }
            case ActionType.EditOffer:
                s.Offers[a.Target] = s.Offers[a.Target] with { Give = a.Give, Get = a.Get, Responses = 0 };
                s.OffersThisTurn++;
                events?.Add(new TradeEdited(seat, a.Target, a.Give, a.Get));
                break;

            case ActionType.CounterOffer:
            {
                s.Offers[a.Target] = s.Offers[a.Target].WithResponse(seat, TradeOffer.Countered);
                int slot = CounterSlotOf(s, seat);
                if (slot < 0)
                    slot = FreeOfferSlot(s);
                s.Offers[slot] = new TradeOffer(true, seat, a.Target, a.Give, a.Get, 0);
                events?.Add(new TradeCountered(seat, slot, a.Target, a.Give, a.Get));
                break;
            }
            case ActionType.AcceptOffer when current: // accept a counter: trade now
            {
                var counter = s.Offers[a.Target];
                Swap(s, counter.From, counter.Give, seat, counter.Get);
                s.Offers[a.Target] = default;
                events?.Add(new TradeDone(seat, counter.From, counter.Get, counter.Give));
                break;
            }
            case ActionType.AcceptOffer:
                s.Offers[a.Target] = s.Offers[a.Target].WithResponse(seat, TradeOffer.Accepted);
                events?.Add(new TradeReplied(seat, a.Target, true));
                break;

            case ActionType.DeclineOffer when current:
            {
                var o = s.Offers[a.Target];
                if (o.IsCounter)
                {
                    s.Offers[a.Target] = default;
                    events?.Add(new TradeRejected(seat, a.Target, o.From));
                }
                else
                {
                    s.Offers[a.Target] = o.WithResponse(a.Target2, TradeOffer.Declined);
                    events?.Add(new TradeRejected(seat, a.Target, a.Target2));
                }
                break;
            }
            case ActionType.DeclineOffer:
                s.Offers[a.Target] = s.Offers[a.Target].WithResponse(seat, TradeOffer.Declined);
                events?.Add(new TradeReplied(seat, a.Target, false));
                break;

            case ActionType.ConfirmTrade:
            {
                var o = s.Offers[a.Target];
                Swap(s, seat, o.Give, a.Target2, o.Get);
                s.Offers[a.Target] = default;
                events?.Add(new TradeDone(seat, a.Target2, o.Give, o.Get));
                break;
            }
            case ActionType.CancelOffer:
                s.Offers[a.Target] = default;
                events?.Add(new TradeCancelled(seat, a.Target));
                break;
        }
    }

    /// <summary><paramref name="a"/> gives <paramref name="aGives"/> to <paramref name="b"/>, who gives <paramref name="bGives"/> back.</summary>
    private static void Swap(GameState s, int a, ResourceSet aGives, int b, ResourceSet bGives)
    {
        for (int r = 0; r < R; r++)
        {
            s.Hand[a * R + r] += bGives[r] - aGives[r];
            s.Hand[b * R + r] += aGives[r] - bGives[r];
        }
    }

    // ---- Random builders for bots and tests (offers, edits and counters are never enumerated) ----

    /// <summary>A random new offer from the current player, or null if it can't make one.</summary>
    public static GameAction? RandomTradeOffer(GameState s, Rng rng)
    {
        int seat = s.CurrentPlayer;
        if (s.Phase != Phase.Main || s.OffersThisTurn >= s.Settings.MaxOffersPerTurn || OpenOwnOffers(s) >= GameConstants.MaxOpenOffers)
            return null;
        return RandomTerms(s, seat, rng, out var give, out var get)
            ? new GameAction(ActionType.OfferTrade, seat, Give: give, Get: get)
            : null;
    }

    /// <summary>A random edit of one of the current player's open offers, or null if there is none to edit.</summary>
    public static GameAction? RandomEditOffer(GameState s, Rng rng)
    {
        int seat = s.CurrentPlayer;
        if (s.Phase != Phase.Main || s.OffersThisTurn >= s.Settings.MaxOffersPerTurn)
            return null;
        int slot = RandomSlot(s, rng, o => !o.IsCounter);
        return slot >= 0 && RandomTerms(s, seat, rng, out var give, out var get)
            ? new GameAction(ActionType.EditOffer, seat, slot, Give: give, Get: get)
            : null;
    }

    /// <summary>A random counter by <paramref name="seat"/> to an open offer it hasn't answered, or null.</summary>
    public static GameAction? RandomCounterOffer(GameState s, int seat, Rng rng)
    {
        if (s.Phase != Phase.Main || seat == s.CurrentPlayer)
            return null;
        int slot = RandomSlot(s, rng, o => !o.IsCounter && o.ResponseOf(seat) == TradeOffer.NoResponse);
        return slot >= 0 && RandomTerms(s, seat, rng, out var give, out var get)
            ? new GameAction(ActionType.CounterOffer, seat, slot, Give: give, Get: get)
            : null;
    }

    private static int RandomSlot(GameState s, Rng rng, Func<TradeOffer, bool> match)
    {
        Span<int> slots = stackalloc int[GameConstants.OfferSlots];
        int n = 0;
        for (int slot = 0; slot < GameConstants.OfferSlots; slot++)
            if (s.Offers[slot].IsActive && match(s.Offers[slot]))
                slots[n++] = slot;
        return n == 0 ? -1 : slots[rng.NextInt(n)];
    }

    /// <summary>1-2 of a resource the seat holds for 1-2 of another resource.</summary>
    private static bool RandomTerms(GameState s, int seat, Rng rng, out ResourceSet give, out ResourceSet get)
    {
        give = get = default;
        if (s.HandSize(seat) == 0)
            return false;
        int g;
        do g = rng.NextInt(R); while (s.Hand[seat * R + g] == 0);
        int w = (g + 1 + rng.NextInt(R - 1)) % R;
        give = ResourceSet.Of((Resource)g, 1 + rng.NextInt(Math.Min(2, s.Hand[seat * R + g])));
        get = ResourceSet.Of((Resource)w, 1 + rng.NextInt(2));
        return true;
    }
}
