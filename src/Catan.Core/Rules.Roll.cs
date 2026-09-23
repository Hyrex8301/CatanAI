namespace Catan.Core;

public static partial class Rules
{
    private static void PreRollActions(GameState s, int seat, List<GameAction> buffer)
    {
        buffer.Add(new GameAction(ActionType.RollDice, seat));
    }

    private static bool IsLegalPreRoll(GameState s, GameAction a, out string reason)
    {
        if (a.Type == ActionType.RollDice)
        {
            reason = "";
            return true;
        }
        return Fail("Roll the dice first.", out reason);
    }

    private static void ApplyRoll(GameState s, GameAction a, IChance chance, List<GameEvent>? events)
    {
        var (d1, d2) = chance.RollDice();
        int roll = d1 + d2;
        s.LastRoll = roll;
        s.HasRolled = true;
        events?.Add(new DiceRolled(a.Seat, d1, d2));

        if (roll == 7)
            StartSeven(s);
        else
        {
            Produce(s, roll, events);
            s.Phase = Phase.Main;
        }
    }

    /// <summary>
    /// Every hex numbered <paramref name="roll"/> without the robber pays 1 per adjacent settlement and 2 per city.
    /// Bank shortage, per resource: if the bank can't cover everyone owed it, nobody gets it, unless only one seat is owed it,
    /// in which case that seat gets whatever is left.
    /// </summary>
    private static void Produce(GameState s, int roll, List<GameEvent>? events)
    {
        const int seats = GameConstants.PlayerCount;
        Span<int> owed = stackalloc int[seats * R];
        foreach (int h in s.Board.HexesWithNumber(roll))
        {
            if (h == s.RobberHex)
                continue;
            int resource = s.Board.ResourceAt(h);
            for (int c = 0; c < 6; c++)
            {
                int v = Topology.HexVertices[h, c];
                int owner = s.VertexOwner[v];
                if (owner >= 0)
                    owed[owner * R + resource] += s.VertexLevel[v];
            }
        }

        Span<int> paid = stackalloc int[seats * R];
        for (int r = 0; r < R; r++)
        {
            int total = 0, claimants = 0;
            for (int seat = 0; seat < seats; seat++)
                if (owed[seat * R + r] > 0)
                {
                    total += owed[seat * R + r];
                    claimants++;
                }
            if (total == 0)
                continue;

            if (total <= s.Bank[r])
            {
                for (int seat = 0; seat < seats; seat++)
                    paid[seat * R + r] = owed[seat * R + r];
            }
            else if (claimants == 1)
            {
                for (int seat = 0; seat < seats; seat++)
                    if (owed[seat * R + r] > 0)
                        paid[seat * R + r] = s.Bank[r];
            }
            // else: shortage with several claimants, nobody gets this resource.
        }

        for (int seat = 0; seat < seats; seat++)
        {
            var cards = ResourceSet.From(paid.Slice(seat * R, R));
            if (cards.Total == 0)
                continue;
            GiveFromBank(s, seat, cards);
            events?.Add(new ResourcesProduced(seat, cards));
        }
    }
}
