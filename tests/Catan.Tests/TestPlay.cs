using Catan.Core;

namespace Catan.Tests;

/// <summary>Shared helpers for tests that play random games.</summary>
public static class TestPlay
{
    /// <summary>
    /// A random legal action from any seat that may act. Half the time during Main, an opponent with open trades to answer
    /// acts instead (accept, decline, withdraw its counter, or 1 in 4 a random counter). Otherwise the acting seat plays:
    /// uniform from its legal list, a random discard in Discard (never listed), or in Main a random new offer (5%) or
    /// edit (2%), which are never listed either.
    /// </summary>
    public static GameAction RandomAction(GameState s, List<GameAction> legal, Rng rng)
    {
        if (s.Phase == Phase.Main && rng.NextInt(2) == 0)
        {
            Span<bool> canAct = stackalloc bool[GameConstants.PlayerCount];
            int count = Rules.OptionalSeats(s, canAct);
            if (count > 0)
            {
                int pick = rng.NextInt(count), seat = 0;
                for (int i = 0; ; seat++)
                    if (canAct[seat] && i++ == pick)
                        break;
                if (rng.NextInt(4) == 0 && Rules.RandomCounterOffer(s, seat, rng) is { } counter)
                    return counter;
                Rules.GetLegalActions(s, seat, legal);
                Assert.NotEmpty(legal);
                return legal[rng.NextInt(legal.Count)];
            }
        }

        Rules.GetLegalActions(s, legal);
        if (s.Phase == Phase.Discard)
        {
            Assert.Empty(legal);
            return Rules.RandomDiscard(s, Rules.ActingSeat(s), rng);
        }
        Assert.NotEmpty(legal);
        if (s.Phase == Phase.Main)
        {
            int roll = rng.NextInt(100);
            if (roll < 5 && Rules.RandomTradeOffer(s, rng) is { } offer)
                return offer;
            if (roll < 7 && Rules.RandomEditOffer(s, rng) is { } edit)
                return edit;
        }
        return legal[rng.NextInt(legal.Count)];
    }

    /// <summary>
    /// For every seat, every candidate action (all types and targets the tests know about) is listed exactly when IsLegal
    /// accepts it, and everything listed is legal.
    /// </summary>
    public static void AssertListMatchesIsLegal(GameState s)
    {
        if (s.Phase == Phase.Discard)
            return; // discards aren't enumerated
        var legal = new List<GameAction>();
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
        {
            Rules.GetLegalActions(s, seat, legal);
            var listed = legal.ToHashSet();
            foreach (var a in Candidates(seat))
                Assert.True(listed.Contains(a) == Rules.IsLegal(s, a, out string reason),
                    $"{a} listed={listed.Contains(a)} but IsLegal says: {(reason == "" ? "legal" : reason)}");
            foreach (var a in legal)
                Assert.True(Rules.IsLegal(s, a, out string reason), $"{a} is listed but IsLegal says: {reason}");
        }
    }

    private static List<GameAction> Candidates(int seat)
    {
        var candidates = new List<GameAction>
        {
            new(ActionType.RollDice, seat), new(ActionType.BuyDevCard, seat), new(ActionType.EndTurn, seat),
            new(ActionType.PlayKnight, seat), new(ActionType.PlayRoadBuilding, seat),
        };
        for (int r = 0; r < GameConstants.ResourceCount; r++)
        {
            candidates.Add(new(ActionType.PlayMonopoly, seat, r));
            for (int r2 = r; r2 < GameConstants.ResourceCount; r2++)
                candidates.Add(new(ActionType.PlayYearOfPlenty, seat, Get: ResourceSet.Of((Resource)r) + ResourceSet.Of((Resource)r2)));
        }
        for (int e = 0; e < Topology.EdgeCount; e++)
            candidates.Add(new(ActionType.BuildRoad, seat, e));
        for (int v = 0; v < Topology.VertexCount; v++)
        {
            candidates.Add(new(ActionType.BuildSettlement, seat, v));
            candidates.Add(new(ActionType.BuildCity, seat, v));
        }
        for (int h = 0; h < Topology.HexCount; h++)
            for (int victim = -1; victim < GameConstants.PlayerCount; victim++)
                candidates.Add(new(ActionType.MoveRobber, seat, h, victim));
        for (int give = 0; give < GameConstants.ResourceCount; give++)
            for (int count = 1; count <= 4; count++)
                for (int get = 0; get < GameConstants.ResourceCount; get++)
                    candidates.Add(new(ActionType.BankTrade, seat,
                        Give: ResourceSet.Of((Resource)give, count), Get: ResourceSet.Of((Resource)get)));
        for (int slot = 0; slot < GameConstants.OfferSlots; slot++)
        {
            candidates.Add(new(ActionType.AcceptOffer, seat, slot));
            candidates.Add(new(ActionType.CancelOffer, seat, slot));
            for (int partner = -1; partner < GameConstants.PlayerCount; partner++)
            {
                candidates.Add(new(ActionType.DeclineOffer, seat, slot, partner));
                if (partner >= 0)
                    candidates.Add(new(ActionType.ConfirmTrade, seat, slot, partner));
            }
        }
        return candidates;
    }
}
