using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>
/// What the promiser's next robber move threatens the victim with, in evaluation points: how likely the promiser is to
/// hit the victim (<see cref="Chance"/>), what a block and a steal would cost the victim, and what the promiser gives up
/// by keeping the robber off the victim (<see cref="PromiserCost"/>).
/// </summary>
public readonly record struct RobberThreat(double Chance, double BlockDamage, double StealDamage, double PromiserCost);

/// <summary>
/// Prices promises with the evaluation. The promiser's robber options are scored from the promiser's side (robber on each
/// hex); the chance they pick the victim is a softmax over the best option against each opponent (and against nobody).
/// A promise is worth the expected damage it prevents to the one protected (scaled by how far they trust the promiser),
/// and costs the promiser what they lose by leaving the victim alone.
/// </summary>
public static class DealValues
{
    /// <summary>How sharply a promiser prefers its best robber target, in victory points (smaller = more certain).</summary>
    public const double TemperaturePoints = 0.45;

    /// <summary>What a promise costs when keeping it would be impossible (every hex hits someone promised).</summary>
    public const double ForcedBreakCost = 1000;

    /// <summary>
    /// Share of a block's evaluated damage that is real: the evaluation scores the robber as if it stayed, but it usually
    /// moves on within a round or two. Tuned by Sim matches (deal-makers against bots that don't deal).
    /// </summary>
    public const double BlockShare = 0.35;

    /// <summary>
    /// The threat of <paramref name="promiser"/>'s next robber move to <paramref name="victim"/>. Hexes next to seats in
    /// <paramref name="kept"/> (players the promiser already promised not to block) are left out: the promiser keeps its
    /// word. If every hex that avoids the victim is ruled out, the promise would force a break: its cost is
    /// <see cref="ForcedBreakCost"/>.
    /// </summary>
    public static RobberThreat Threat(GameState s, Evaluator eval, int promiser, int victim, IReadOnlyCollection<int>? kept = null)
    {
        const int Seats = GameConstants.PlayerCount;
        Span<double> bestFor = stackalloc double[Seats + 1]; // per victim seat, plus [Seats] = hits nobody
        bestFor.Fill(double.NegativeInfinity);
        double best = double.NegativeInfinity, bestAvoiding = double.NegativeInfinity;
        int bestHexOnVictim = -1;
        double bestOnVictim = double.NegativeInfinity;
        int saved = s.RobberHex;
        for (int h = 0; h < Topology.HexCount; h++)
        {
            if (h == saved)
                continue;
            if (kept is not null && kept.Any(k => k != victim && Touches(s, h, k)))
                continue;
            s.RobberHex = h;
            double v = eval.Score(s, promiser);
            s.RobberHex = saved;
            best = Math.Max(best, v);
            bool hitsAnyone = false, hitsVictim = false;
            for (int seat = 0; seat < Seats; seat++)
                if (seat != promiser && Touches(s, h, seat))
                {
                    hitsAnyone = true;
                    bestFor[seat] = Math.Max(bestFor[seat], v);
                    hitsVictim |= seat == victim;
                }
            if (!hitsAnyone)
                bestFor[Seats] = Math.Max(bestFor[Seats], v);
            if (hitsVictim && v > bestOnVictim)
                (bestOnVictim, bestHexOnVictim) = (v, h);
            if (!hitsVictim)
                bestAvoiding = Math.Max(bestAvoiding, v);
        }

        // Chance the promiser picks the victim: softmax over its best option against each opponent and against nobody.
        double total = 0, victimWeight = 0, temperature = TemperaturePoints * eval.Weights["vp"];
        for (int i = 0; i <= Seats; i++)
        {
            if (double.IsNegativeInfinity(bestFor[i]))
                continue;
            double w = Math.Exp((bestFor[i] - best) / temperature);
            total += w;
            if (i == victim)
                victimWeight = w;
        }
        double chance = total > 0 ? victimWeight / total : 0;

        double before = eval.Score(s, victim);
        double block = 0;
        if (bestHexOnVictim >= 0)
        {
            s.RobberHex = bestHexOnVictim;
            block = Math.Max(0, before - eval.Score(s, victim));
            s.RobberHex = saved;
        }
        double steal = StealDamage(s, eval, victim, before);
        double cost = double.IsNegativeInfinity(bestAvoiding) ? ForcedBreakCost : Math.Max(0, best - bestAvoiding);
        return new RobberThreat(chance, block, steal, cost);
    }

    /// <summary>
    /// What <paramref name="terms"/> (from the promiser to the victim) are worth to the victim, and what they cost the
    /// promiser, given the threat. The victim discounts by <paramref name="trust"/> in the promiser. Spot promises aren't
    /// priced yet (0).
    /// </summary>
    public static (double ValueToVictim, double CostToPromiser) Price(IReadOnlyList<PromiseTerm> terms, RobberThreat t, double trust = 1)
    {
        double value = 0, cost = 0;
        foreach (var term in terms)
            switch (term.Kind)
            {
                case PromiseKind.NoBlock:
                    value += t.Chance * t.BlockDamage * BlockShare;
                    cost += t.PromiserCost * BlockShare;
                    break;
                case PromiseKind.NoSteal:
                    value += t.Chance * t.StealDamage;
                    cost += t.Chance * t.StealDamage * 0.5; // roughly what the stolen card was worth to the thief
                    break;
            }
        return (value * trust, cost);
    }

    /// <summary>The victim's expected loss from one random card stolen from its hand.</summary>
    private static double StealDamage(GameState s, Evaluator eval, int victim, double before)
    {
        const int R = GameConstants.ResourceCount;
        int hand = s.HandSize(victim);
        if (hand == 0)
            return 0;
        double damage = 0;
        for (int r = 0; r < R; r++)
        {
            int count = s.Hand[victim * R + r];
            if (count == 0)
                continue;
            s.Hand[victim * R + r]--;
            damage += (double)count / hand * Math.Max(0, before - eval.Score(s, victim));
            s.Hand[victim * R + r]++;
        }
        return damage;
    }

    private static bool Touches(GameState s, int hex, int seat)
    {
        for (int c = 0; c < 6; c++)
            if (s.VertexOwner[Topology.HexVertices[hex, c]] == seat)
                return true;
        return false;
    }
}
