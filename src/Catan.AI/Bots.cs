using Catan.Core;

namespace Catan.AI;

/// <summary>Creates bots from short specs used by the Sim and the game: random, smart, smart-fast, or a weights file path.</summary>
public static class Bots
{
    public static IPlayerAgent Create(string spec, ulong seed) => spec switch
    {
        "random" => new RandomBot(seed),
        "smart" => new SmartBot(new BotWeights(), SmartBotSettings.Play, seed),
        "smart-fast" => new SmartBot(new BotWeights(), SmartBotSettings.Training, seed, "SmartBot(fast)"),
        _ when spec.EndsWith(".json", StringComparison.OrdinalIgnoreCase) =>
            new SmartBot(BotWeights.Load(spec), SmartBotSettings.Play, seed, $"SmartBot({Path.GetFileNameWithoutExtension(spec)})"),
        _ => throw new ArgumentException($"Unknown bot '{spec}' (use random, smart, smart-fast or a weights .json file)."),
    };
}
