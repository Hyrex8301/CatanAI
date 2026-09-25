using Catan.Core;

namespace Catan.UI;

public enum SeatColor : byte { Red, Blue, Orange, White }

/// <summary>What the player picks on the new-game screen.</summary>
public sealed record GameOptions
{
    /// <summary>Master seed for board, seats, colors, dice and bots; null picks one at random.</summary>
    public ulong? Seed { get; init; }

    /// <summary>A fixed board: the same seed always deals the same board (null = a new random board each game).</summary>
    public ulong? BoardSeed { get; init; }

    public int VpToWin { get; init; } = 10;
    public bool FriendlyRobber { get; init; }

    /// <summary>Pause after each bot action so the human can follow along.</summary>
    public double BotDelaySeconds { get; init; } = 0.5;

    /// <summary>How long a bot's trade offer waits for the human's answer before it's skipped.</summary>
    public double ResponseWindowSeconds { get; init; } = 20;

    /// <summary>Placement practice: rate each placement as you make it (false: all at the end of the opening).</summary>
    public bool PracticeFeedbackEach { get; init; } = true;

    /// <summary>Your colour; null = a random one each game.</summary>
    public SeatColor? PreferredColor { get; init; }

    /// <summary>Play in full screen (F11 or the corner button toggles it; remembered between sessions).</summary>
    public bool Fullscreen { get; init; }

    /// <summary>Sound effects volume, 0 (off) to 1.</summary>
    public double Volume { get; init; } = 0.4;

    /// <summary>Developer: draw every seat's hand and dev cards.</summary>
    public bool ShowAllHands { get; init; }

    /// <summary>The settings as saved between sessions (the seed isn't saved: every game gets a new one).</summary>
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this with { Seed = null });

    /// <summary>Saved settings, kept within sensible limits; defaults if the file is missing or damaged.</summary>
    public static GameOptions FromJson(string? json)
    {
        try
        {
            var o = json is null ? null : System.Text.Json.JsonSerializer.Deserialize<GameOptions>(json);
            if (o is null)
                return new GameOptions();
            return o with
            {
                Seed = null,
                VpToWin = Math.Clamp(o.VpToWin, 3, 20),
                BotDelaySeconds = Math.Clamp(o.BotDelaySeconds, 0, 3),
                ResponseWindowSeconds = Math.Clamp(o.ResponseWindowSeconds, 5, 120),
                Volume = Math.Clamp(o.Volume, 0, 1),
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return new GameOptions();
        }
    }

    /// <summary>Real games don't need the simulation turn cap, and allow 10 offers and edits per turn.</summary>
    public GameSettings ToSettings() => new() { VpToWin = VpToWin, FriendlyRobber = FriendlyRobber, MaxTurns = 10_000, MaxOffersPerTurn = 10 };
}

/// <summary>
/// Everything random about starting a game, derived from one master seed so a game can always be recreated:
/// the human's seat, each seat's color, and separate seeds for the board, the dice and the bots.
/// </summary>
public sealed record GameSetup(ulong Seed, int HumanSeat, IReadOnlyList<SeatColor> Colors, ulong BoardSeed, ulong ChanceSeed, ulong BotSeed)
{
    public static GameSetup Create(ulong seed)
    {
        var rng = new Rng(seed, stream: 7);
        var colors = new[] { SeatColor.Red, SeatColor.Blue, SeatColor.Orange, SeatColor.White };
        rng.Shuffle<SeatColor>(colors);
        int humanSeat = rng.NextInt(GameConstants.PlayerCount);
        return new GameSetup(seed, humanSeat, colors, Next(rng), Next(rng), Next(rng));
    }

    public SeatColor HumanColor => Colors[HumanSeat];

    /// <summary>
    /// A random game seed that gives the human <paramref name="preferred"/> (any seed when null). Everything about a game
    /// still follows from its seed alone, so saved games load exactly as they were played.
    /// </summary>
    public static ulong SeedFor(SeatColor? preferred, Func<ulong> random) => SeedFor(preferred, null, random);

    /// <summary>A random game seed giving the human <paramref name="preferred"/> (any when null) and seat <paramref name="seat"/> (any when null).</summary>
    public static ulong SeedFor(SeatColor? preferred, int? seat, Func<ulong> random)
    {
        for (int attempt = 0; ; attempt++)
        {
            ulong seed = random();
            var setup = Create(seed);
            bool colourOk = preferred is not { } color || setup.HumanColor == color;
            bool seatOk = seat is not { } s || setup.HumanSeat == s;
            if ((colourOk && seatOk) || attempt >= 2000)
                return seed;
        }
    }

    private static ulong Next(Rng rng) => ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
}
