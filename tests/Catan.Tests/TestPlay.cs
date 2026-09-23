using Catan.Core;

namespace Catan.Tests;

/// <summary>Shared helpers for tests that play random games.</summary>
public static class TestPlay
{
    /// <summary>A random legal action: uniform from the legal list, or a random discard when the list is empty in Discard.</summary>
    public static GameAction RandomAction(GameState s, List<GameAction> legal, Rng rng)
    {
        Rules.GetLegalActions(s, legal);
        if (s.Phase == Phase.Discard)
        {
            Assert.Empty(legal);
            return Rules.RandomDiscard(s, Rules.ActingSeat(s), rng);
        }
        Assert.NotEmpty(legal);
        return legal[rng.NextInt(legal.Count)];
    }

    /// <summary>Every candidate action (all types and targets the tests know about) is listed exactly when IsLegal accepts it.</summary>
    public static void AssertListMatchesIsLegal(GameState s, List<GameAction> legal)
    {
        if (s.Phase == Phase.Discard)
            return; // discards aren't enumerated
        var listed = legal.ToHashSet();
        int seat = Rules.ActingSeat(s);
        var candidates = new List<GameAction>
        {
            new(ActionType.RollDice, seat), new(ActionType.BuyDevCard, seat), new(ActionType.EndTurn, seat),
        };
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

        foreach (var a in candidates)
            Assert.True(listed.Contains(a) == Rules.IsLegal(s, a, out string reason),
                $"{a} listed={listed.Contains(a)} but IsLegal says: {(reason == "" ? "legal" : reason)}");
        foreach (var a in legal)
            Assert.True(Rules.IsLegal(s, a, out string reason), $"{a} is listed but IsLegal says: {reason}");
    }
}
