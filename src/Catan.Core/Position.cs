using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catan.Core;

/// <summary>
/// A game position with no history: the board layout, the settings and every field of the <see cref="GameState"/>
/// (buildings, roads, hands, dev cards in hand and played, bank, dev deck, robber, awards, knights, points, phase, whose
/// turn, open trades). Written as JSON. The fields are read and written by reflection, so a field added to GameState is
/// saved without anyone remembering to (the round-trip tests compare hashes, which cover every field). Loading checks the
/// state with <see cref="StateValidator"/>. Optional: whose seat the person plays, and a title, description and kind for
/// practice scenarios.
/// </summary>
public sealed class Position
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public BoardLayout Board { get; init; } = new(Array.Empty<HexLayout>(), Array.Empty<HarborLayout>());
    public GameSettings Settings { get; init; } = GameSettings.Default;

    /// <summary>Every GameState field by name.</summary>
    public Dictionary<string, JsonElement> State { get; init; } = new();

    /// <summary>The seat the person plays from this position (null: any).</summary>
    public int? Seat { get; init; }

    /// <summary>For practice scenarios: a short title, what to think about, and whether it grades one decision or plays out.</summary>
    public string? Title { get; init; }
    public string? Description { get; init; }
    public ScenarioKind? Kind { get; init; }

    /// <summary>For scenarios: every hand is known to everyone at the start (a puzzle); otherwise bots know only hand sizes.</summary>
    public bool HandsKnown { get; init; }

    private static readonly FieldInfo[] Fields = typeof(GameState)
        .GetFields(BindingFlags.Instance | BindingFlags.Public)
        .Where(f => f.Name is not (nameof(GameState.Board) or nameof(GameState.Settings)))
        .OrderBy(f => f.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly JsonSerializerOptions Options = CreateOptions(indented: false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(indented: true);

    private static JsonSerializerOptions CreateOptions(bool indented) => new(BoardJson.Options) { WriteIndented = indented };

    /// <summary>The position of <paramref name="s"/> (a snapshot: later changes to the state don't affect it).</summary>
    public static Position From(GameState s, int? seat = null) => new()
    {
        Board = s.Board.ToLayout(),
        Settings = s.Settings,
        State = Fields.ToDictionary(f => f.Name, f => JsonSerializer.SerializeToElement(f.GetValue(s), f.FieldType, Options)),
        Seat = seat,
    };

    /// <summary>A new GameState at this position. Throws <see cref="ArgumentException"/> if it's damaged or breaks the rules.</summary>
    public GameState ToState()
    {
        if (FormatVersion != CurrentFormatVersion)
            throw new ArgumentException($"Unsupported position format {FormatVersion}.");
        var s = new GameState(Core.Board.FromLayout(Board), Settings);
        foreach (var field in Fields)
        {
            if (!State.TryGetValue(field.Name, out var element))
                throw new ArgumentException($"The position has no {field.Name}.");
            object? value = element.Deserialize(field.FieldType, Options);
            if (field.FieldType.IsArray)
            {
                var into = (Array)field.GetValue(s)!;
                var from = (Array)(value ?? throw new ArgumentException($"{field.Name} is empty."));
                if (from.Length != into.Length)
                    throw new ArgumentException($"{field.Name} has {from.Length} entries, expected {into.Length}.");
                Array.Copy(from, into, into.Length);
            }
            else
                field.SetValue(s, value);
        }
        var errors = StateValidator.Check(s);
        if (errors.Count > 0)
            throw new ArgumentException($"The position breaks the rules: {errors[0]}");
        return s;
    }

    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, indented ? IndentedOptions : Options);

    public static Position FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Position>(json, Options) ?? throw new ArgumentException("The position file is empty.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"The position file can't be read: {ex.Message}");
        }
    }
}

/// <summary>What a practice scenario asks: one decision graded against every choice, or a position to play to the end.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScenarioKind { Decision, PlayOut }
