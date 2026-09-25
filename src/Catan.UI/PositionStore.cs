using Catan.Core;

namespace Catan.UI;

/// <summary>A saved position on disk: its file, when it was saved, and the position.</summary>
public sealed record SavedPosition(string Path, DateTime SavedAt, Position Position)
{
    public string Name => Position.Title ?? System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Saved positions (<see cref="Position"/> JSON, full state with no history) in one folder, saved from any moment of any
/// game ("Save position" in the game's menu) and played from under Play → Position practice.
/// </summary>
public sealed class PositionStore
{
    public PositionStore(string folder) => Folder = folder;

    public string Folder { get; }

    /// <summary>A new position file, e.g. "position-2026-09-25-1530"; a number is added if that name is taken.</summary>
    public string Save(Position position, DateTime now)
    {
        Directory.CreateDirectory(Folder);
        string name = $"position-{now:yyyy-MM-dd-HHmm}";
        string path = System.IO.Path.Combine(Folder, name + ".json");
        for (int n = 2; File.Exists(path); n++)
            path = System.IO.Path.Combine(Folder, $"{name}-{n}.json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, position.ToJson());
        File.Move(temp, path, overwrite: true); // never leave a half-written file behind
        return path;
    }

    /// <summary>Every readable position, newest first. Files that aren't valid positions are skipped.</summary>
    public IReadOnlyList<SavedPosition> List()
    {
        if (!Directory.Exists(Folder))
            return Array.Empty<SavedPosition>();
        var found = new List<SavedPosition>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var position = Position.FromJson(File.ReadAllText(path));
                position.ToState(); // only list positions that load
                found.Add(new SavedPosition(path, File.GetLastWriteTimeUtc(path), position));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // Damaged or not a position: skip it.
            }
        }
        return found.OrderByDescending(p => p.SavedAt).ToList();
    }
}
