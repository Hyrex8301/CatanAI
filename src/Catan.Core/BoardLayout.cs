using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catan.Core;

/// <summary>Explicit board layout: the JSON shape for fixtures, practice positions and saved games. Harbor spots are 0-8.</summary>
public sealed record BoardLayout(IReadOnlyList<HexLayout> Hexes, IReadOnlyList<HarborLayout> Harbors);

/// <summary>Number is null (omitted in JSON) on the desert.</summary>
public sealed record HexLayout(int Q, int R, Terrain Terrain, int? Number = null);

public sealed record HarborLayout(int Spot, HarborType Type);

public static class BoardJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static string Serialize(Board board, bool indented = false) =>
        JsonSerializer.Serialize(board.ToLayout(), indented ? new JsonSerializerOptions(Options) { WriteIndented = true } : Options);

    public static Board Deserialize(string json) =>
        Board.FromLayout(JsonSerializer.Deserialize<BoardLayout>(json, Options) ?? throw new ArgumentException("Board JSON is empty."));
}
