using Catan.Core;

namespace Catan.AI;

/// <summary>
/// Creates bots from short specs used by the Sim and the game: random, smart, smart-fast, a weights file path, or
/// search[:iterations][:weights.json]. A search bot made here thinks a fixed number of iterations per decision on one thread
/// (matches run many games in parallel); it uses the weights' folder's calibration.json when there is one.
/// </summary>
public static class Bots
{
    public static IPlayerAgent Create(string spec, ulong seed)
    {
        if (spec == "search" || spec.StartsWith("search:", StringComparison.Ordinal))
            return CreateSearch(spec, seed);
        return spec switch
        {
            "random" => new RandomBot(seed),
            "smart" => new SmartBot(new BotWeights(), SmartBotSettings.Play, seed),
            "smart-fast" => new SmartBot(new BotWeights(), SmartBotSettings.Training, seed, "SmartBot(fast)"),
            _ when spec.EndsWith(".json", StringComparison.OrdinalIgnoreCase) =>
                new SmartBot(BotWeights.Load(spec), SmartBotSettings.Play, seed, $"SmartBot({Path.GetFileNameWithoutExtension(spec)})"),
            _ => throw new ArgumentException($"Unknown bot '{spec}' (use random, smart, smart-fast, a weights .json file, or search[:iterations][:weights.json])."),
        };
    }

    /// <summary>
    /// search[:iterations][:weights.json][:key=value,...] with keys mode (paired|tree), cut (cutoff turns), conf (paired
    /// confidence), root (root moves), inner (inner moves), c (tree exploration).
    /// </summary>
    private static SearchBot CreateSearch(string spec, ulong seed)
    {
        var parts = spec.Split(':');
        int iterations = parts.Length > 1 && int.TryParse(parts[1], out int n) && n > 0 ? n : 1000;
        string? weightsPath = parts.Skip(2).FirstOrDefault(p => !p.Contains('='));
        var settings = new SearchSettings { Iterations = iterations, Threads = 1 };
        foreach (var option in parts.Skip(2).Where(p => p.Contains('=')).SelectMany(p => p.Split(',')))
        {
            var (key, value) = (option.Split('=')[0], option.Split('=')[1]);
            settings = key switch
            {
                "mode" => settings with { Mode = value == "tree" ? SearchMode.Tree : SearchMode.Paired },
                "cut" => settings with { CutoffTurns = int.Parse(value) },
                "conf" => settings with { Confidence = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                "root" => settings with { RootMoves = int.Parse(value) },
                "inner" => settings with { InnerMoves = int.Parse(value) },
                "gain" => settings with { MinGain = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                "c" => settings with { Exploration = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                _ => throw new ArgumentException($"Unknown search option '{key}'."),
            };
        }
        var weights = weightsPath is null ? new BotWeights() : BotWeights.Load(weightsPath);
        var calibration = weightsPath is null ? WinModel.Default
            : WinModel.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(weightsPath)) ?? ".", "calibration.json"));
        return new SearchBot(weights, calibration, settings, seed, $"SearchBot({iterations})");
    }
}
