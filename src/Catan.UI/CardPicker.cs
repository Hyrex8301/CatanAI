using Catan.Core;

namespace Catan.UI;

/// <summary>
/// The model behind a −/+ card chooser (discards, trade offers, counters): a count per resource, each capped by a maximum,
/// with an optional cap on the total.
/// </summary>
public sealed class CardPicker
{
    private readonly int[] _counts = new int[GameConstants.ResourceCount];
    private readonly int[] _max = new int[GameConstants.ResourceCount];

    public CardPicker(int maxEach = 19, int? totalCap = null)
    {
        Array.Fill(_max, maxEach);
        TotalCap = totalCap;
    }

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    public int? TotalCap { get; private set; }

    public int this[int resource] => _counts[resource];

    public int Max(int resource) => _max[resource];

    public int Total => _counts.Sum();

    public ResourceSet Cards => ResourceSet.From(_counts);

    public bool CanAdd(int resource) => _counts[resource] < _max[resource] && (TotalCap is null || Total < TotalCap);

    public bool CanRemove(int resource) => _counts[resource] > 0;

    public void Add(int resource)
    {
        if (!CanAdd(resource))
            return;
        _counts[resource]++;
        Changed?.Invoke();
    }

    public void Remove(int resource)
    {
        if (!CanRemove(resource))
            return;
        _counts[resource]--;
        Changed?.Invoke();
    }

    /// <summary>New limits (e.g. the hand changed); counts above a new maximum are reduced to it.</summary>
    public void SetLimits(ReadOnlySpan<int> maxEach, int? totalCap = null)
    {
        for (int r = 0; r < _max.Length; r++)
        {
            _max[r] = maxEach[r];
            _counts[r] = Math.Min(_counts[r], _max[r]);
        }
        TotalCap = totalCap;
        while (TotalCap is { } cap && Total > cap)
            _counts[Array.FindLastIndex(_counts, c => c > 0)]--;
        Changed?.Invoke();
    }

    /// <summary>Loads counts (e.g. an offer being edited), within the current limits.</summary>
    public void Set(ResourceSet cards)
    {
        for (int r = 0; r < _counts.Length; r++)
            _counts[r] = Math.Clamp(cards[r], 0, _max[r]);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Array.Clear(_counts);
        Changed?.Invoke();
    }
}
