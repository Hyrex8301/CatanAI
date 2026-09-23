using Catan.Core;

namespace Catan.UI;

/// <summary>A saved game on disk: its file, when it was saved, and its record.</summary>
public sealed record SavedGame(string Path, DateTime SavedAt, GameRecord Record)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Saved games as <see cref="GameRecord"/> JSON files in one folder: named saves plus a single autosave.
/// A record carries its game seed, which restores the seats and colors (<see cref="GameSetup.Create"/>).
/// </summary>
public sealed class SaveStore
{
    public const string AutosaveName = "autosave";

    public SaveStore(string folder) => Folder = folder;

    public string Folder { get; }

    public string AutosavePath => System.IO.Path.Combine(Folder, AutosaveName + ".json");

    public bool HasAutosave => File.Exists(AutosavePath);

    public string Autosave(GameRecord record) => Write(AutosavePath, record);

    /// <summary>A new named save, e.g. "save-2026-09-22-2130"; a number is added if that name is taken.</summary>
    public string Save(GameRecord record, DateTime now)
    {
        string name = $"save-{now:yyyy-MM-dd-HHmm}";
        string path = System.IO.Path.Combine(Folder, name + ".json");
        for (int n = 2; File.Exists(path); n++)
            path = System.IO.Path.Combine(Folder, $"{name}-{n}.json");
        return Write(path, record);
    }

    /// <summary>Every readable save, newest first. Files that aren't valid records are skipped.</summary>
    public IReadOnlyList<SavedGame> List()
    {
        if (!Directory.Exists(Folder))
            return Array.Empty<SavedGame>();
        var saves = new List<SavedGame>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                saves.Add(new SavedGame(path, File.GetLastWriteTimeUtc(path), GameRecord.FromJson(File.ReadAllText(path))));
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or IOException)
            {
                // Not a game record; ignore it.
            }
        }
        return saves.OrderByDescending(s => s.SavedAt).ToList();
    }

    public GameRecord Load(string path) => GameRecord.FromJson(File.ReadAllText(path));

    private string Write(string path, GameRecord record)
    {
        Directory.CreateDirectory(Folder);
        string temp = path + ".tmp";
        File.WriteAllText(temp, record.ToJson());
        File.Move(temp, path, overwrite: true); // never leave a half-written save behind
        return path;
    }
}
