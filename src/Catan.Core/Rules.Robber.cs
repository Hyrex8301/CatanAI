namespace Catan.Core;

public static partial class Rules
{
    public const int DiscardLimit = 7;

    // ---- Rolling a 7 ----

    /// <summary>Everyone holding more than 7 cards owes half, rounded down. Then discards (if any) and the robber.</summary>
    private static void StartSeven(GameState s)
    {
        bool anyOwed = false;
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
        {
            int size = s.HandSize(seat);
            s.DiscardOwed[seat] = size > DiscardLimit ? size / 2 : 0;
            anyOwed |= s.DiscardOwed[seat] > 0;
        }
        s.Phase = anyOwed ? Phase.Discard : Phase.MoveRobber;
    }

    /// <summary>During discards, the lowest seat that still owes cards acts.</summary>
    private static int LowestSeatOwingDiscard(GameState s)
    {
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            if (s.DiscardOwed[seat] > 0)
                return seat;
        return -1;
    }

    // ---- Discard ----

    /// <summary>
    /// A valid discard for <paramref name="seat"/>, choosing each owed card at random from its hand (weighted by counts).
    /// Discards are never enumerated in the legal list; agents build them, and this helper builds a random one.
    /// </summary>
    public static GameAction RandomDiscard(GameState s, int seat, Rng rng) => RandomDiscard(seat, s.HandOf(seat), s.DiscardOwed[seat], rng);

    /// <summary>A random valid discard for the view's own seat (agents only see views).</summary>
    public static GameAction RandomDiscard(PlayerView view, Rng rng) => RandomDiscard(view.Seat, view.Hand, view.DiscardOwed[view.Seat], rng);

    private static GameAction RandomDiscard(int seat, ReadOnlySpan<int> heldCards, int owed, Rng rng)
    {
        Span<int> hand = stackalloc int[R];
        heldCards.CopyTo(hand);
        int remaining = 0;
        foreach (int n in hand)
            remaining += n;
        Span<int> discard = stackalloc int[R];
        for (int k = 0; k < owed; k++)
        {
            int pick = rng.NextInt(remaining);
            int r = 0;
            while (pick >= hand[r])
                pick -= hand[r++];
            hand[r]--;
            discard[r]++;
            remaining--;
        }
        return new GameAction(ActionType.Discard, seat, Give: ResourceSet.From(discard));
    }

    private static bool IsLegalDiscard(GameState s, GameAction a, out string reason)
    {
        if (a.Type != ActionType.Discard)
            return Fail("Discard first: you hold more than 7 cards.", out reason);
        var give = a.Give;
        for (int r = 0; r < R; r++)
            if (give[r] < 0)
                return Fail("A discard can't have negative counts.", out reason);
        int owed = s.DiscardOwed[a.Seat];
        if (give.Total != owed)
            return Fail($"You must discard exactly {owed} cards (half your hand, rounded down).", out reason);
        if (!give.FitsIn(s.HandOf(a.Seat)))
            return Fail("You can only discard cards you hold.", out reason);
        reason = "";
        return true;
    }

    private static void ApplyDiscard(GameState s, GameAction a, List<GameEvent>? events)
    {
        Pay(s, a.Seat, a.Give);
        s.DiscardOwed[a.Seat] = 0;
        events?.Add(new Discarded(a.Seat, a.Give));
        if (LowestSeatOwingDiscard(s) < 0)
            s.Phase = Phase.MoveRobber;
    }

    // ---- Move the robber and steal ----

    private static void MoveRobberActions(GameState s, int seat, List<GameAction> buffer)
    {
        bool friendlyFallback = FriendlyRobberBlocksEverything(s, seat);
        for (int h = 0; h < Topology.HexCount; h++)
        {
            if (h == s.RobberHex || (!friendlyFallback && IsFriendlyBlocked(s, seat, h)))
                continue;
            bool anyVictim = false;
            for (int victim = 0; victim < GameConstants.PlayerCount; victim++)
                if (CanBeRobbed(s, seat, h, victim))
                {
                    buffer.Add(new GameAction(ActionType.MoveRobber, seat, h, victim));
                    anyVictim = true;
                }
            if (!anyVictim)
                buffer.Add(new GameAction(ActionType.MoveRobber, seat, h, -1));
        }
    }

    private static bool IsLegalMoveRobber(GameState s, GameAction a, out string reason)
    {
        if (a.Type != ActionType.MoveRobber)
            return Fail("Move the robber first.", out reason);
        int h = a.Target, seat = a.Seat;
        if (h is < 0 or >= Topology.HexCount)
            return Fail($"Hex {h} doesn't exist.", out reason);
        if (h == s.RobberHex)
            return Fail("The robber must move to a different hex.", out reason);
        if (IsFriendlyBlocked(s, seat, h) && !FriendlyRobberBlocksEverything(s, seat))
            return Fail("Friendly robber: that hex touches a player with 2 or fewer points.", out reason);

        bool anyVictim = false;
        for (int victim = 0; victim < GameConstants.PlayerCount; victim++)
            anyVictim |= CanBeRobbed(s, seat, h, victim);

        if (a.Target2 < 0)
            return anyVictim
                ? Fail("Choose an opponent on that hex to steal from.", out reason)
                : Pass(out reason);
        if (a.Target2 >= GameConstants.PlayerCount || !CanBeRobbed(s, seat, h, a.Target2))
            return Fail("You can only steal from an opponent with a building on that hex and at least one card.", out reason);
        return Pass(out reason);
    }

    private static void ApplyMoveRobber(GameState s, GameAction a, IChance chance, List<GameEvent>? events)
    {
        s.RobberHex = a.Target;
        events?.Add(new RobberMoved(a.Seat, a.Target));

        int victim = a.Target2;
        if (victim >= 0)
        {
            int r = chance.PickStolenCard(s.HandOf(victim));
            s.Hand[victim * R + r]--;
            s.Hand[a.Seat * R + r]++;
            events?.Add(new CardStolen(a.Seat, victim, r));
        }

        s.Phase = s.HasRolled ? Phase.Main : Phase.PreRoll;
    }

    /// <summary>An opponent with a building on the hex and at least one card.</summary>
    private static bool CanBeRobbed(GameState s, int thief, int hex, int victim)
    {
        if (victim == thief || s.HandSize(victim) == 0)
            return false;
        for (int c = 0; c < 6; c++)
            if (s.VertexOwner[Topology.HexVertices[hex, c]] == victim)
                return true;
        return false;
    }

    /// <summary>Friendly robber: a hex touching another player with 2 or fewer public VP is off-limits.</summary>
    private static bool IsFriendlyBlocked(GameState s, int seat, int hex)
    {
        if (!s.Settings.FriendlyRobber)
            return false;
        for (int c = 0; c < 6; c++)
        {
            int owner = s.VertexOwner[Topology.HexVertices[hex, c]];
            if (owner >= 0 && owner != seat && s.PublicVP[owner] <= 2)
                return true;
        }
        return false;
    }

    /// <summary>If the friendly-robber rule rules out every hex the robber could move to, all of them are allowed.</summary>
    private static bool FriendlyRobberBlocksEverything(GameState s, int seat)
    {
        if (!s.Settings.FriendlyRobber)
            return false;
        for (int h = 0; h < Topology.HexCount; h++)
            if (h != s.RobberHex && !IsFriendlyBlocked(s, seat, h))
                return false;
        return true;
    }

    private static bool Pass(out string reason)
    {
        reason = "";
        return true;
    }
}
