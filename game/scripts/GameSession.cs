using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>Carries choices from the menu to the game scene, and knows where saves live.</summary>
public static class GameSession
{
    public static GameOptions Options { get; set; } = new();

    /// <summary>When set, the game scene continues this saved game instead of starting a new one.</summary>
    public static GameRecord? Resume { get; set; }

    /// <summary>Saves go in Godot's per-user data folder (on Windows: %APPDATA%\Godot\app_userdata\CatanAI\saves).</summary>
    public static SaveStore Store { get; } = new(ProjectSettings.GlobalizePath("user://saves"));
}
