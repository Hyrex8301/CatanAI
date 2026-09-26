using Catan.AI;
using Catan.Core;
using static Catan.Core.Topology;

namespace Catan.Tests.AI;

public class EvaluatorTests
{
    private static double[] FeaturesOf(GameState s, int seat)
    {
        int n = BotWeights.Names.Count;
        var all = new double[4 * n];
        Evaluator.Features(s, all);
        return all.Skip(seat * n).Take(n).ToArray();
    }

    private static double F(GameState s, int seat, string name) => FeaturesOf(s, seat)[BotWeights.IndexOf(name)];

    [Fact]
    public void ProductionCountsPipsPerResourceAndCitiesDouble()
    {
        // Hex 0 at (0,-2) is Hills 6 (5 pips): a settlement on it produces 5/36 brick per roll, a city 10/36.
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, -2, Corner.N)).City(1, Vertex(0, -2, Corner.S)).Build();
        Assert.Equal(5 / 36.0, F(s, 0, "prod_brick"), 9);
        Assert.True(F(s, 1, "prod_brick") >= 10 / 36.0 - 1e-9);
        Assert.Equal(0, F(s, 2, "prod_brick"));
    }

    [Fact]
    public void RobberMovesProductionToBlocked()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, -2, Corner.N)).Robber(0).Build();
        Assert.Equal(0, F(s, 0, "prod_brick"));
        Assert.Equal(5 / 36.0, F(s, 0, "robber_blocked"), 9);
    }

    [Fact]
    public void HandAffordabilityAndSevenRisk()
    {
        var s = new StateBuilder(TestBoards.Standard).Hand(0, brick: 1, lumber: 1, wool: 1, grain: 3, ore: 3).Build();
        Assert.Equal(9, F(s, 0, "hand_total"));
        Assert.Equal(2, F(s, 0, "hand_over7"));
        Assert.Equal(1, F(s, 0, "can_settlement"));
        Assert.Equal(1, F(s, 0, "can_city"));
        Assert.Equal(1, F(s, 0, "can_dev"));
    }

    [Fact]
    public void MissingCardsCountWhatEachBuildStillNeeds()
    {
        // Seat 0 has a settlement to upgrade and a spot its road reaches (see the test below); seat 1 has neither.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E))
            .Hand(0, brick: 1, grain: 1, ore: 1)
            .Build();
        Assert.Equal(3, F(s, 0, "city_missing"));       // 1 grain + 2 ore
        Assert.Equal(2, F(s, 0, "settlement_missing")); // lumber + wool
        Assert.Equal(1, F(s, 0, "dev_missing"));        // wool
        Assert.Equal(5, F(s, 1, "city_missing"));       // nothing to upgrade: the full cost
        Assert.Equal(4, F(s, 1, "settlement_missing")); // no spot: the full cost
        Assert.Equal(3, F(s, 1, "dev_missing"));
    }

    [Fact]
    public void SettlementSpotsFollowRoadsAndTheDistanceRule()
    {
        // Seat 0: settlement at the center's N, roads N -> NE -> SE: SE is a legal spot (NE is too close).
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E))
            .Build();
        Assert.Equal(1, F(s, 0, "settle_spots"));
        Assert.True(F(s, 0, "best_spot_pips") > 0);
        Assert.Equal(0, F(s, 1, "settle_spots"));
    }

    [Fact]
    public void ARoadIntoSomeoneElsesBuildingIsDead()
    {
        // Seat 0: settlement at the center's N and roads N -> NE -> SE. Open, SE is a free spot: not dead.
        var open = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E))
            .Build();
        Assert.Equal(0, F(open, 0, "dead_roads"));

        // Seat 1 builds right where the road ends: nowhere left to go.
        var blocked = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E))
            .Settlement(1, Vertex(0, 0, Corner.SE))
            .Build();
        Assert.Equal(1, F(blocked, 0, "dead_roads"));
    }

    [Fact]
    public void TheLeaderCountsExtraOnlyFromFivePoints()
    {
        var calm = new BotWeights();
        var wary = BotWeights.FromJson("""{ "leader_threat": 1, "last_help": 0.5 }""");
        var s = new StateBuilder(TestBoards.Standard).Settlement(1, Vertex(0, -2, Corner.N)).Settlement(2, Vertex(0, 0, Corner.N)).Build();

        s.PublicVP[1] = 5;
        Assert.Equal(new Evaluator(calm).Score(s, 0), new Evaluator(wary).Score(s, 0));

        s.PublicVP[1] = 8;
        Assert.True(new Evaluator(wary).Score(s, 0) < new Evaluator(calm).Score(s, 0));
    }

    [Fact]
    public void HoldingAKnightWhileBlockedAndLeadingInPoints()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, -2, Corner.N))
            .DevCards(0, knight: 1)
            .Robber(0)
            .Build();
        Assert.Equal(1, F(s, 0, "knight_blocked"));
        Assert.Equal(0, F(new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, -2, Corner.N)).DevCards(0, knight: 1).Build(), 0, "knight_blocked"));

        s.PublicVP[0] = 6;
        s.PublicVP[1] = 3;
        Assert.Equal(3, F(s, 0, "vp_lead"));
        Assert.Equal(0, F(s, 1, "vp_lead"));
    }

    [Fact]
    public void OutOfSettlementsCountsOnlyWhileStuck()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, -2, Corner.N)).Hand(0, grain: 1).Build();
        Assert.Equal(0, F(s, 0, "settles_stuck"));
        Assert.Equal(0, F(s, 0, "stuck_city_missing"));

        s.SettlementsLeft[0] = 0; // all five on the board
        Assert.Equal(1, F(s, 0, "settles_stuck"));
        Assert.Equal(F(s, 0, "city_missing"), F(s, 0, "stuck_city_missing"));
        Assert.Equal(4, F(s, 0, "stuck_city_missing")); // 1 wheat + 3 ore
        Assert.Equal(F(s, 0, "city_combo"), F(s, 0, "stuck_city_combo"));
    }

    [Fact]
    public void TheBiggestRobberTargetAndTheVisibleLead()
    {
        // A city on hex 0 (a 6: 5 pips) makes it the worst place for the robber to land: 10 pips.
        var s = new StateBuilder(TestBoards.Standard).City(0, Vertex(0, -2, Corner.N)).Settlement(1, Vertex(0, 0, Corner.N)).Build();
        Assert.Equal(10 / 36.0, F(s, 0, "robber_magnet"), 9);
        s.RobberHex = 0; // standing there already: it has to move elsewhere
        Assert.True(F(s, 0, "robber_magnet") < 10 / 36.0);

        s.PublicVP[0] = 5;
        s.PublicVP[1] = 2;
        s.DevHand[1 * GameConstants.DevCardTypeCount + (int)DevCardType.VictoryPoint] = 3; // hidden: doesn't count
        Assert.Equal(3, F(s, 0, "public_lead"));
        Assert.Equal(0, F(s, 1, "public_lead"));
    }

    [Fact]
    public void DevsHeldNumbersPortsAndFocus()
    {
        // Seat 0 on hex 0 (a 6) and the centre hex, with a Monopoly and a Road Building in hand.
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, -2, Corner.N)).Settlement(0, Vertex(0, 0, Corner.N))
            .DevCards(0, monopoly: 1, roadBuilding: 2)
            .Build();
        Assert.Equal(1, F(s, 0, "monopoly_held"));
        Assert.Equal(0, F(s, 0, "yop_held"));
        Assert.Equal(2, F(s, 0, "rb_held"));
        int distinct = new[] { Vertex(0, -2, Corner.N), Vertex(0, 0, Corner.N) }
            .SelectMany(v => Enumerable.Range(0, 3).Select(i => VertexHexes[v, i]))
            .Where(h => h >= 0 && s.Board.NumberAt(h) > 0).Select(h => s.Board.NumberAt(h)).Distinct().Count();
        Assert.Equal(distinct, F(s, 0, "number_diversity"));
        double production = new[] { "prod_brick", "prod_lumber", "prod_wool", "prod_grain", "prod_ore" }.Sum(n => F(s, 0, n));
        Assert.Equal(F(s, 0, "harbor_3to1") * production, F(s, 0, "harbor_3to1_prod"), 9); // a 3:1 port, scaled by production
        double focus = new[] { F(s, 0, "city_combo"), F(s, 0, "road_combo"), F(s, 0, "dev_combo") }.Max();
        Assert.Equal(focus, F(s, 0, "strategy_focus"));
    }

    [Fact]
    public void ARoadPointedAtAnOpenSpotMakesItAProspect()
    {
        // A settlement alone: every spot is at least two roads away (the first corner is too close to settle).
        var bare = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, 0, Corner.N)).Build();
        Assert.Equal(0, F(bare, 0, "prospects"));

        // Road N -> NE: the spot at SE is one more road away, so it is a prospect worth its pips.
        var pointed = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, 0, Corner.N)).Road(0, Edge(0, 0, Side.NE)).Build();
        Assert.True(F(pointed, 0, "prospects") >= 1);
        int se = Vertex(0, 0, Corner.SE);
        int sePips = Enumerable.Range(0, 3).Select(i => VertexHexes[se, i]).Where(h => h >= 0).Sum(h => pointed.Board.PipsAt(h));
        Assert.True(F(pointed, 0, "prospect_pips") >= sePips);
        Assert.Equal(0, F(pointed, 1, "prospects")); // nobody else is near
    }

    [Fact]
    public void ATiedRaceGoesToWhoeverPlaysSooner()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 2).Build();
        Assert.Equal(2, Evaluator.RaceWinner(s, new[] { 3, 2, 2, 3 }));  // seat 2 plays now
        Assert.Equal(3, Evaluator.RaceWinner(s, new[] { 1, 3, 3, 1 }));  // after 2 comes 3, then 0
        Assert.Equal(1, Evaluator.RaceWinner(s, new[] { 2, 1, 2, 2 }));  // closer beats sooner
        Assert.Equal(-1, Evaluator.RaceWinner(s, new[] { 3, 3, 3, 3 })); // nobody within two roads
    }

    [Fact]
    public void InTheOpeningTheBestOpenSpotsAreLikelyTaken()
    {
        var s = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, 0, Corner.N)).Phase(Phase.SetupRoad, current: 0).Build();
        var rank = new int[VertexCount];
        var left = new int[4];
        Assert.True(Evaluator.OpeningRanks(s, rank, left));
        Assert.Equal(new[] { 1, 2, 2, 2 }, left);
        int best = Enumerable.Range(0, VertexCount).First(v => rank[v] == 0);
        int bestPips = Enumerable.Range(0, 3).Select(i => VertexHexes[best, i]).Where(h => h >= 0).Sum(h => s.Board.PipsAt(h));
        Assert.All(Enumerable.Range(0, VertexCount).Where(v => rank[v] != int.MaxValue),
            v => Assert.True(Enumerable.Range(0, 3).Select(i => VertexHexes[v, i]).Where(h => h >= 0).Sum(h => s.Board.PipsAt(h)) <= bestPips));
        s.Phase = Phase.Main;
        Assert.False(Evaluator.OpeningRanks(s, rank, left)); // only in the opening
    }

    [Fact]
    public void RivalsForTheSameAwardCountExtra()
    {
        var rivalWeights = new Evaluator(BotWeights.FromJson("""{ "rival": 1 }"""));
        var plain = new Evaluator(new BotWeights());
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N)).Settlement(1, Vertex(0, -2, Corner.N))
            .DevCards(0, knight: 2).DevCards(1, knight: 2)
            .Build();
        Assert.True(rivalWeights.Score(s, 0) < plain.Score(s, 0)); // seat 1 builds an army too

        var calm = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N)).Settlement(1, Vertex(0, -2, Corner.N))
            .DevCards(0, knight: 2)
            .Build();
        Assert.Equal(plain.Score(calm, 0), rivalWeights.Score(calm, 0)); // nobody else is
    }

    [Fact]
    public void WinningAndLosingDominate()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main).Build();
        s.Phase = Phase.GameOver;
        s.Winner = 2;
        var eval = new Evaluator(new BotWeights());
        Assert.Equal(Evaluator.WinScore, eval.Score(s, 2));
        Assert.Equal(-Evaluator.WinScore, eval.Score(s, 0));
    }

    [Fact]
    public void MoreIsBetter()
    {
        // With default weights, an extra settlement on a good hex beats not having it.
        var eval = new Evaluator(new BotWeights());
        var without = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, 0, Corner.N)).Build();
        var with = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, 0, Corner.N)).Settlement(0, Vertex(0, -2, Corner.N)).Build();
        Assert.True(eval.Score(with, 0) > eval.Score(without, 0));
        Assert.True(eval.Score(with, 1) < eval.Score(without, 1)); // and it's worse for an opponent
    }

    [Fact]
    public void NamesAndDefaultsAgree()
    {
        Assert.Equal(BotWeights.Defaults.Keys.Order(), BotWeights.Names.Order());
        Assert.Equal(new[] { "prod_brick", "prod_lumber", "prod_wool", "prod_grain", "prod_ore" },
            BotWeights.Names.Skip(BotWeights.IndexOf("prod_brick")).Take(5));
    }

    [Fact]
    public void WeightsRoundTripAndFillMissingNames()
    {
        var w = BotWeights.FromVector(BotWeights.Names.Select((_, i) => (double)i).ToArray());
        var back = BotWeights.FromJson(w.ToJson());
        Assert.Equal(w.ToVector(), back.ToVector());

        var partial = BotWeights.FromJson("{ \"vp\": 42 }");
        Assert.Equal(42, partial["vp"]);
        Assert.Equal(BotWeights.Defaults["dev_cards"], partial["dev_cards"]);
    }

    /// <summary>
    /// The game loads game/bots/best.json. Missing names would silently fall back to defaults and unknown ones would be
    /// ignored, so after a feature is added or renamed this fails until the bundled weights are retrained or updated.
    /// </summary>
    [Fact]
    public void BundledGameWeightsLoadAndNameEveryWeight()
    {
        string path = Path.Combine(RepoRoot(), "game", "bots", "best.json");
        Assert.True(File.Exists(path), "game/bots/best.json is missing");
        var names = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path))!.Keys;
        Assert.Equal(BotWeights.Names.OrderBy(n => n), names.OrderBy(n => n));
        Assert.All(BotWeights.Load(path).ToVector(), v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void RoundsForCountsTheSlowestMissingCard()
    {
        // A city: 2 grain + 3 ore. Holding 2 grain and 1 ore, making ore at 9 pips (one card a round): 2 rounds.
        var pips = new[] { 0, 0, 0, 5, 9 };
        Assert.Equal(2, Evaluator.RoundsFor(Costs.City, new[] { 0, 0, 0, 2, 1 }, pips, 4), 6);
        // No ore production: 3 ore by trading 4:1 from 18 pips of other cards (half a card a round): 6 rounds.
        Assert.Equal(6, Evaluator.RoundsFor(Costs.City, new[] { 0, 0, 0, 2, 0 }, new[] { 9, 0, 0, 9, 0 }, 4), 6);
        // A 3:1 port makes it faster; nothing produced at all hits the cap; a covered cost is 0.
        Assert.Equal(4.5, Evaluator.RoundsFor(Costs.City, new[] { 0, 0, 0, 2, 0 }, new[] { 9, 0, 0, 9, 0 }, 3), 6);
        Assert.Equal(Evaluator.VpTurnsCap, Evaluator.RoundsFor(Costs.City, new int[5], new int[5], 4));
        Assert.Equal(0, Evaluator.RoundsFor(Costs.City, new[] { 0, 0, 0, 2, 3 }, new int[5], 4));
    }

    [Fact]
    public void VpTurnsFallsAsTheNextPointGetsCloser()
    {
        // Seat 0 has an open spot (roads N -> NE -> SE of the center) and a settlement to upgrade.
        StateBuilder Base() => new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E));
        double empty = F(Base().Build(), 0, "vp_turns");
        double almost = F(Base().Hand(0, brick: 1, lumber: 1, wool: 1).Build(), 0, "vp_turns");
        double ready = F(Base().Hand(0, brick: 1, lumber: 1, wool: 1, grain: 1).Build(), 0, "vp_turns");
        Assert.True(empty > almost, $"{empty} > {almost}");
        Assert.True(almost > ready, $"{almost} > {ready}");
        Assert.Equal(0, ready);
        // Spending the settlement's brick and wood on a road pushes the point further away.
        double afterRoad = F(Base().Hand(0, wool: 1, grain: 1).Build(), 0, "vp_turns");
        Assert.True(afterRoad > ready);
        // Nothing to build toward (no buildings at all): the cap.
        Assert.Equal(Evaluator.VpTurnsCap, F(new StateBuilder(TestBoards.Standard).Build(), 1, "vp_turns"));
    }

    [Fact]
    public void RoadEarlyStopsCountingAtFive()
    {
        var ring = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Vertex(0, 0, Corner.N))
            .Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E), Edge(0, 0, Side.SE), Edge(0, 0, Side.SW), Edge(0, 0, Side.W), Edge(0, 0, Side.NW))
            .Build();
        Assert.Equal(6, F(ring, 0, "road_length"));
        Assert.Equal(5, F(ring, 0, "road_early"));
        var short2 = new StateBuilder(TestBoards.Standard).Settlement(0, Vertex(0, 0, Corner.N)).Roads(0, Edge(0, 0, Side.NE), Edge(0, 0, Side.E)).Build();
        Assert.Equal(2, F(short2, 0, "road_early"));
    }

    [Fact]
    public void KnightsHeldCountsOnlyKnightsInHand()
    {
        var s = new StateBuilder(TestBoards.Standard).DevCards(0, knight: 2, monopoly: 1, victoryPoint: 1).KnightsPlayed(0, 3).Build();
        Assert.Equal(2, F(s, 0, "knights_held"));
        Assert.Equal(0, F(s, 1, "knights_held"));
    }

    [Fact]
    public void ArmyReachCountsKnightsStillToPlayAfterTheOnesInHand()
    {
        // Seat 0 played 1 and holds 1; seat 1 played 2: taking the army needs 3 played, so 2 more, one already in hand.
        var s = new StateBuilder(TestBoards.Standard).KnightsPlayed(0, 1).DevCards(0, knight: 1).KnightsPlayed(1, 2).Build();
        Assert.Equal(1, F(s, 0, "army_reach"));
        Assert.Equal(1, F(s, 1, "army_reach")); // needs a 3rd, holds none
        var holder = new StateBuilder(TestBoards.Standard).KnightsPlayed(0, 3).LargestArmyOwner(0).KnightsPlayed(1, 2).Build();
        Assert.Equal(0, F(holder, 0, "army_reach"));
        Assert.Equal(2, F(holder, 1, "army_reach")); // must pass 3: two more
    }

    [Fact]
    public void PortCountCountsEachHarborOnce()
    {
        var s = new StateBuilder(TestBoards.Standard)
            .Settlement(0, Topology.HarborVertices[0, 0])
            .Settlement(0, Topology.HarborVertices[3, 0])
            .Build();
        Assert.Equal(2, F(s, 0, "port_count"));
        Assert.Equal(0, F(s, 1, "port_count"));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
