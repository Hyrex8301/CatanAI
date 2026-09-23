using System.Collections;

namespace Catan.Core;

/// <summary>
/// The full event log plus one redacted copy per seat, kept up to date as events are added, so building a view never
/// re-redacts the whole history. Logs only grow, so a prefix handed out earlier never changes.
/// </summary>
public sealed class EventLog
{
    private readonly List<GameEvent> _all = new();
    private readonly List<GameEvent>[] _bySeat = Enumerable.Range(0, GameConstants.PlayerCount).Select(_ => new List<GameEvent>()).ToArray();

    public int Count => _all.Count;

    /// <summary>Unredacted: for the runner, records and debugging, never for agents.</summary>
    public IReadOnlyList<GameEvent> All => _all;

    public void Add(GameEvent e)
    {
        _all.Add(e);
        for (int seat = 0; seat < _bySeat.Length; seat++)
            _bySeat[seat].Add(e.RedactFor(seat));
    }

    public void AddRange(IEnumerable<GameEvent> events)
    {
        foreach (var e in events)
            Add(e);
    }

    /// <summary>What <paramref name="seat"/> has seen so far: a read-only snapshot that later events don't change.</summary>
    public IReadOnlyList<GameEvent> For(int seat) => new Snapshot(_bySeat[seat], _bySeat[seat].Count);

    /// <summary>A fixed-length prefix of an append-only list.</summary>
    private sealed class Snapshot : IReadOnlyList<GameEvent>
    {
        private readonly List<GameEvent> _list;

        public Snapshot(List<GameEvent> list, int count)
        {
            _list = list;
            Count = count;
        }

        public int Count { get; }

        public GameEvent this[int index] =>
            (uint)index < (uint)Count ? _list[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<GameEvent> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return _list[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
