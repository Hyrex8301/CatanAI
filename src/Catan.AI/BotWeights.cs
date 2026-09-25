using System.Text.Json;

namespace Catan.AI;

/// <summary>
/// The weights of <see cref="Evaluator"/>'s features, by name. Stored as JSON so trained bots are just different files.
/// Missing names fall back to the defaults, so older weight files keep working when features are added.
/// </summary>
public sealed class BotWeights
{
    /// <summary>Hand-set starting weights; training tunes them.</summary>
    public static readonly IReadOnlyDictionary<string, double> Defaults = new Dictionary<string, double>
    {
        ["vp"] = 10,
        ["prod_brick"] = 30, ["prod_lumber"] = 30, ["prod_wool"] = 22, ["prod_grain"] = 28, ["prod_ore"] = 28,
        ["diversity"] = 2,
        ["city_combo"] = 20, ["road_combo"] = 15, ["dev_combo"] = 10,
        ["harbor_2to1"] = 15, ["harbor_3to1"] = 2,
        ["hand_total"] = 1.2, ["hand_over7"] = -1.5,
        ["can_road"] = 0.5, ["can_settlement"] = 3, ["can_city"] = 3, ["can_dev"] = 1,
        ["settle_spots"] = 1.5, ["best_spot_pips"] = 0.6,
        ["road_length"] = 0.4, ["longest_road_gap"] = 1,
        ["knights"] = 1, ["army_gap"] = 1,
        ["dev_cards"] = 2.5,
        ["robber_blocked"] = -15,
        ["opp_max"] = 0.5, ["opp_mean"] = 0.25,
        // Cards still missing for a city, settlement or dev card (the full cost when it can't be built at all). 0 here, so
        // weight files from before these features behave exactly as they did.
        ["city_missing"] = 0, ["settlement_missing"] = 0, ["dev_missing"] = 0,
        // Road ends that lead nowhere (no free spot there or one road further); leader threat (how much extra the opponent
        // with the most points counts, from 5 points to one from winning) and help for the others (how much less they count).
        ["dead_roads"] = 0, ["leader_threat"] = 0, ["last_help"] = 0,
        // Holding a knight while blocked; our lead in points (0-5); extra weight on opponents going for the same award.
        ["knight_blocked"] = 0, ["vp_lead"] = 0, ["rival"] = 0,
        // Out of settlements (all 5 on the board; only a city frees one): a flag, ore + wheat production, and cards still
        // missing for a city, each only while stuck. Opponents who are stuck are worth blocking and not feeding ore or wheat.
        ["settles_stuck"] = 0, ["stuck_city_combo"] = 0, ["stuck_city_missing"] = 0,
        // Being the obvious robber target: the most production (cards per roll) one robber placement could shut off, and
        // the lead in points everyone can see (hidden VP cards keep you under the radar).
        ["robber_magnet"] = 0, ["public_lead"] = 0,
        // Unplayed Monopoly / Year of Plenty / Road Building (so holding one for the right moment can be worth more than
        // dev_cards alone says); how many different dice numbers our buildings touch; a 3:1 port scaled by our production
        // (worth little while we produce little); and our strongest single plan (cities, roads or dev cards).
        ["monopoly_held"] = 0, ["yop_held"] = 0, ["rb_held"] = 0, ["number_diversity"] = 0, ["harbor_3to1_prod"] = 0, ["strategy_focus"] = 0,
    };

    /// <summary>
    /// The feature names in a fixed order (the training vector's layout). Explicit, not taken from the dictionary, whose
    /// enumeration order isn't guaranteed; the five prod_* entries must stay adjacent in resource order.
    /// </summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "vp",
        "prod_brick", "prod_lumber", "prod_wool", "prod_grain", "prod_ore",
        "diversity", "city_combo", "road_combo", "dev_combo", "harbor_2to1", "harbor_3to1",
        "hand_total", "hand_over7", "can_road", "can_settlement", "can_city", "can_dev",
        "settle_spots", "best_spot_pips", "road_length", "longest_road_gap", "knights", "army_gap",
        "dev_cards", "robber_blocked", "opp_max", "opp_mean",
        "city_missing", "settlement_missing", "dev_missing",
        "dead_roads", "leader_threat", "last_help",
        "knight_blocked", "vp_lead", "rival",
        "settles_stuck", "stuck_city_combo", "stuck_city_missing",
        "robber_magnet", "public_lead",
        "monopoly_held", "yop_held", "rb_held", "number_diversity", "harbor_3to1_prod", "strategy_focus",
    };

    private readonly double[] _values;

    public BotWeights() : this(Defaults) { }

    public BotWeights(IReadOnlyDictionary<string, double> values)
    {
        _values = Names.Select(n => values.TryGetValue(n, out double v) ? v : Defaults[n]).ToArray();
    }

    private BotWeights(double[] values) => _values = values;

    public double this[int index] => _values[index];

    public double this[string name] => _values[IndexOf(name)];

    public static int IndexOf(string name)
    {
        for (int i = 0; i < Names.Count; i++)
            if (Names[i] == name)
                return i;
        throw new ArgumentException($"Unknown weight '{name}'.");
    }

    public double[] ToVector() => (double[])_values.Clone();

    public static BotWeights FromVector(IReadOnlyList<double> vector)
    {
        if (vector.Count != Names.Count)
            throw new ArgumentException($"Expected {Names.Count} weights, got {vector.Count}.");
        return new BotWeights(vector.ToArray());
    }

    public string ToJson() => JsonSerializer.Serialize(Names.Select((n, i) => (n, _values[i])).ToDictionary(p => p.n, p => Math.Round(p.Item2, 4)),
        new JsonSerializerOptions { WriteIndented = true });

    public static BotWeights FromJson(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? throw new ArgumentException("Weights JSON is empty."));

    public static BotWeights Load(string path) => FromJson(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, ToJson());
}
