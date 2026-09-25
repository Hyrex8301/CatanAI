using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catan.Core;

/// <summary>
/// A saved game: everything needed to rebuild it exactly. The full board layout is stored (not just the seed) so old saves
/// keep loading if the generator changes. Replaying applies the actions in order with a <see cref="ReplayChance"/> over the
/// recorded outcomes, and must end at <see cref="FinalHash"/>. Stopping at action k gives the position after k actions.
/// </summary>
public sealed class GameRecord
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>The seed the game was generated from, if any (informational; replay doesn't need it).</summary>
    public ulong? Seed { get; init; }

    public GameSettings Settings { get; init; } = GameSettings.Default;
    public BoardLayout Board { get; init; } = new(Array.Empty<HexLayout>(), Array.Empty<HarborLayout>());
    public IReadOnlyList<string> Players { get; init; } = Array.Empty<string>();
    public IReadOnlyList<GameAction> Actions { get; init; } = Array.Empty<GameAction>();
    public IReadOnlyList<ChanceOutcome> Chance { get; init; } = Array.Empty<ChanceOutcome>();

    /// <summary><see cref="GameState.ComputeHash"/> after the last action, as 16 lowercase hex digits.</summary>
    public string? FinalHash { get; init; }

    /// <summary>
    /// The game's table talk (chat and deals) as the game screen saved it, if any. Replay doesn't use it: talk never changes
    /// the rules, only what players chose, and their choices are the recorded actions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TableTalk { get; set; }

    /// <summary>
    /// The saved position the game started from, for games that didn't start from a new board (null otherwise). Replaying
    /// starts here instead of from an empty board; <see cref="Board"/> and <see cref="Settings"/> match it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Position? Start { get; init; }

    public static string HashText(ulong hash) => hash.ToString("x16");

    // ---- JSON ----

    public static readonly JsonSerializerOptions JsonOptions = CreateOptions(indented: false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(indented: true);

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions(BoardJson.Options) { WriteIndented = indented };
        options.Converters.Add(new GameActionConverter());
        options.Converters.Add(new ChanceOutcomeConverter());
        return options;
    }

    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, indented ? IndentedOptions : JsonOptions);

    public static GameRecord FromJson(string json) =>
        JsonSerializer.Deserialize<GameRecord>(json, JsonOptions) ?? throw new ArgumentException("Game record JSON is empty.");

    // ---- Replay ----

    /// <summary>
    /// Rebuilds the game from the board and settings and applies the recorded actions with the recorded outcomes.
    /// Stops at the first problem (unknown format, illegal action, bad outcome, or a validation failure when
    /// <paramref name="validate"/> is set) and reports it. With <paramref name="stopAfter"/>, stops after that many actions.
    /// </summary>
    public ReplayResult Replay(int? stopAfter = null, bool validate = false)
    {
        var log = new EventLog();
        if (FormatVersion != CurrentFormatVersion)
            return new ReplayResult(null, log, 0, $"Unsupported record format {FormatVersion}.", null);

        GameState state;
        try
        {
            state = Start?.ToState() ?? new GameState(Core.Board.FromLayout(Board), Settings);
        }
        catch (ArgumentException ex)
        {
            return new ReplayResult(null, log, 0, $"Bad {(Start is null ? "board" : "starting position")}: {ex.Message}", null);
        }
        if (Start is not null)
            log.Add(PositionStarted.Of(state, Start.HandsKnown));

        var chance = new ReplayChance(Chance);
        var events = new List<GameEvent>();
        int count = Math.Min(stopAfter ?? Actions.Count, Actions.Count);
        for (int i = 0; i < count; i++)
        {
            var action = Actions[i];
            if (!Rules.IsLegal(state, action, out string reason))
                return new ReplayResult(state, log, i, $"Action {i} ({action.Type} by seat {action.Seat}) is illegal: {reason}", null);
            events.Clear();
            try
            {
                Rules.Apply(state, action, chance, events);
            }
            catch (ReplayException ex)
            {
                return new ReplayResult(state, log, i, $"Action {i} ({action.Type} by seat {action.Seat}): {ex.Message}", null);
            }
            log.AddRange(events);
            if (validate)
            {
                var errors = StateValidator.Check(state);
                if (errors.Count > 0)
                    return new ReplayResult(state, log, i + 1, $"State invalid after action {i} ({action.Type}): {string.Join(" | ", errors)}", null);
            }
        }

        bool? hashMatches = count == Actions.Count && FinalHash is not null ? HashText(state.ComputeHash()) == FinalHash : null;
        string? error = hashMatches == false ? $"Final hash {HashText(state.ComputeHash())} differs from the recorded {FinalHash}." : null;
        return new ReplayResult(state, log, count, error, hashMatches);
    }

    // ---- Converters: compact actions and chance outcomes ----

    /// <summary>{"type":"BuildRoad","seat":0,"target":12}; target / target2 omitted when -1, give / get as [b,l,w,g,o] when non-empty.</summary>
    private sealed class GameActionConverter : JsonConverter<GameAction>
    {
        public override void Write(Utf8JsonWriter w, GameAction a, JsonSerializerOptions options)
        {
            w.WriteStartObject();
            w.WriteString("type", a.Type.ToString());
            w.WriteNumber("seat", a.Seat);
            if (a.Target != -1) w.WriteNumber("target", a.Target);
            if (a.Target2 != -1) w.WriteNumber("target2", a.Target2);
            WriteSet(w, "give", a.Give);
            WriteSet(w, "get", a.Get);
            w.WriteEndObject();
        }

        private static void WriteSet(Utf8JsonWriter w, string name, ResourceSet set)
        {
            if (set == default)
                return;
            w.WriteStartArray(name);
            for (int r = 0; r < GameConstants.ResourceCount; r++)
                w.WriteNumberValue(set[r]);
            w.WriteEndArray();
        }

        public override GameAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var element = JsonElement.ParseValue(ref reader);
            var type = Enum.Parse<ActionType>(element.GetProperty("type").GetString()!);
            int Int(string name, int fallback) => element.TryGetProperty(name, out var p) ? p.GetInt32() : fallback;
            ResourceSet Set(string name)
            {
                if (!element.TryGetProperty(name, out var p))
                    return default;
                var counts = p.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                return counts.Length == GameConstants.ResourceCount ? ResourceSet.From(counts) : throw new JsonException($"'{name}' needs 5 counts.");
            }
            return new GameAction(type, Int("seat", -1), Int("target", -1), Int("target2", -1), Set("give"), Set("get"));
        }
    }

    /// <summary>{"kind":"Dice","d1":3,"d2":4}, {"kind":"Steal","value":2}, {"kind":"Draw","value":0}.</summary>
    private sealed class ChanceOutcomeConverter : JsonConverter<ChanceOutcome>
    {
        public override void Write(Utf8JsonWriter w, ChanceOutcome o, JsonSerializerOptions options)
        {
            w.WriteStartObject();
            w.WriteString("kind", o.Kind.ToString());
            if (o.Kind == ChanceKind.Dice)
            {
                w.WriteNumber("d1", o.D1);
                w.WriteNumber("d2", o.D2);
            }
            else
                w.WriteNumber("value", o.Value);
            w.WriteEndObject();
        }

        public override ChanceOutcome Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var element = JsonElement.ParseValue(ref reader);
            var kind = Enum.Parse<ChanceKind>(element.GetProperty("kind").GetString()!);
            int Int(string name) => element.TryGetProperty(name, out var p) ? p.GetInt32() : 0;
            return new ChanceOutcome(kind, Int("d1"), Int("d2"), Int("value"));
        }
    }
}

/// <summary>
/// The outcome of a replay. <see cref="Error"/> is null when every requested action applied cleanly and (for a full replay)
/// the hash matched. <see cref="ActionsApplied"/> is where it stopped. <see cref="HashMatches"/> is null when not compared.
/// </summary>
public sealed record ReplayResult(GameState? State, EventLog Log, int ActionsApplied, string? Error, bool? HashMatches)
{
    public bool Ok => Error is null;
}
