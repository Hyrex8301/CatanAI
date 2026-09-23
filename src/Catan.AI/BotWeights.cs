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
        ["harbor_2to1"] = 15, ["harbor_3to1"] = 2,
        ["hand_total"] = 1.2, ["hand_over7"] = -1.5,
        ["can_road"] = 0.5, ["can_settlement"] = 3, ["can_city"] = 3, ["can_dev"] = 1,
        ["settle_spots"] = 1.5, ["best_spot_pips"] = 0.6,
        ["road_length"] = 0.4, ["longest_road_gap"] = 1,
        ["knights"] = 1, ["army_gap"] = 1,
        ["dev_cards"] = 2.5,
        ["robber_blocked"] = -15,
        ["opp_max"] = 0.5, ["opp_mean"] = 0.25,
    };

    /// <summary>
    /// The feature names in a fixed order (the training vector's layout). Explicit, not taken from the dictionary, whose
    /// enumeration order isn't guaranteed; the five prod_* entries must stay adjacent in resource order.
    /// </summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "vp",
        "prod_brick", "prod_lumber", "prod_wool", "prod_grain", "prod_ore",
        "diversity", "harbor_2to1", "harbor_3to1",
        "hand_total", "hand_over7", "can_road", "can_settlement", "can_city", "can_dev",
        "settle_spots", "best_spot_pips", "road_length", "longest_road_gap", "knights", "army_gap",
        "dev_cards", "robber_blocked", "opp_max", "opp_mean",
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
