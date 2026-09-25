using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>Carries choices from the menu to the game scene, and knows where saves and settings live.</summary>
public static class GameSession
{
    private static readonly string SettingsPath = ProjectSettings.GlobalizePath("user://settings.json");

    /// <summary>The player's settings, loaded from the last session (defaults the first time).</summary>
    public static GameOptions Options { get; set; } = GameOptions.FromJson(System.IO.File.Exists(SettingsPath) ? System.IO.File.ReadAllText(SettingsPath) : null);

    /// <summary>When set, the game scene continues this saved game instead of starting a new one.</summary>
    public static GameRecord? Resume { get; set; }

    /// <summary>Saves go in Godot's per-user data folder (on Windows: %APPDATA%\Godot\app_userdata\CatanAI\saves).</summary>
    public static SaveStore Store { get; } = new(ProjectSettings.GlobalizePath("user://saves"));

    public static void SaveSettings(GameOptions options)
    {
        Options = options;
        System.IO.File.WriteAllText(SettingsPath, options.ToJson());
    }

    /// <summary>Switches between full screen and a window, and remembers the choice.</summary>
    public static void ToggleFullscreen() => SetFullscreen(!Options.Fullscreen);

    public static void SetFullscreen(bool on)
    {
        DisplayServer.WindowSetMode(on ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        if (Options.Fullscreen != on)
            SaveSettings(Options with { Fullscreen = on });
    }

    /// <summary>F11 toggles full screen on every screen. Returns true if the event was F11.</summary>
    public static bool HandleFullscreenKey(InputEvent e)
    {
        if (e is not InputEventKey { Keycode: Key.F11, Pressed: true, Echo: false })
            return false;
        ToggleFullscreen();
        return true;
    }
}
