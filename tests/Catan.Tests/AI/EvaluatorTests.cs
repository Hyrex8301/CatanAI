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

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
