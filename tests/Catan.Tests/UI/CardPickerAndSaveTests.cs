using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class CardPickerTests
{
    [Fact]
    public void CountsStayWithinEachMaximum()
    {
        var picker = new CardPicker();
        picker.SetLimits(new[] { 2, 0, 1, 0, 0 });
        picker.Add(0); picker.Add(0); picker.Add(0);
        picker.Add(1);
        picker.Add(2);
        Assert.Equal(new ResourceSet(2, 0, 1, 0, 0), picker.Cards);
        Assert.False(picker.CanAdd(0));
        picker.Remove(0);
        picker.Remove(3); // nothing to remove
        Assert.Equal(2, picker.Total);
    }

    [Fact]
    public void TotalCapLimitsTheWholePick()
    {
        // A discard of 4 from a hand of 3 brick, 5 ore.
        var picker = new CardPicker();
        picker.SetLimits(new[] { 3, 0, 0, 0, 5 }, totalCap: 4);
        for (int i = 0; i < 3; i++) picker.Add(0);
        for (int i = 0; i < 3; i++) picker.Add(4);
        Assert.Equal(4, picker.Total);
        Assert.False(picker.CanAdd(4));
        Assert.Equal(new ResourceSet(3, 0, 0, 0, 1), picker.Cards);
    }

    [Fact]
    public void NewLimitsTrimTheCounts()
    {
        var picker = new CardPicker();
        picker.Set(new ResourceSet(3, 0, 2, 0, 0));
        int changes = 0;
        picker.Changed += () => changes++;
        picker.SetLimits(new[] { 1, 5, 5, 5, 5 }, totalCap: 2);
        Assert.Equal(new ResourceSet(1, 0, 1, 0, 0), picker.Cards);
        Assert.Equal(1, changes);
        picker.Clear();
        Assert.Equal(0, picker.Total);
    }
}

public class SaveStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "catan-saves-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static async Task<GameRecord> SomeGame(ulong seed)
    {
        var runner = RandomTestAgent.Game(seed);
        while (runner.Actions.Count < 40)
            await runner.StepAsync();
        return runner.ToRecord(seed);
    }

    [Fact]
    public async Task SavesListNewestFirstAndLoadBack()
    {
        var store = new SaveStore(_folder);
        Assert.Empty(store.List());
        Assert.False(store.HasAutosave);

        var first = await SomeGame(1);
        var second = await SomeGame(2);
        var now = new DateTime(2026, 9, 22, 21, 30, 0);
        string a = store.Save(first, now);
        string b = store.Save(second, now); // same minute: gets a suffix
        store.Autosave(second);
        File.SetLastWriteTimeUtc(a, now.AddMinutes(-5));

        Assert.NotEqual(a, b);
        Assert.True(store.HasAutosave);
        var saves = store.List();
        Assert.Equal(3, saves.Count);
        Assert.Equal(a, saves[^1].Path);
        Assert.Equal(first.FinalHash, store.Load(a).FinalHash);
        Assert.Equal(1UL, saves.Single(s => s.Path == a).Record.Seed);
    }

    [Fact]
    public async Task OtherFilesAreIgnored()
    {
        var store = new SaveStore(_folder);
        store.Save(await SomeGame(3), DateTime.Now);
        File.WriteAllText(Path.Combine(_folder, "notes.json"), "{ not a record");
        Assert.Single(store.List());
    }
}
