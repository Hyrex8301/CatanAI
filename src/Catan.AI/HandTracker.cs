using Catan.Core;

namespace Catan.AI;

/// <summary>Everyone's resource hands at once: one possible world.</summary>
public readonly record struct Hands(ResourceSet S0, ResourceSet S1, ResourceSet S2, ResourceSet S3)
{
    public ResourceSet this[int seat] => seat switch { 0 => S0, 1 => S1, 2 => S2, _ => S3 };

    public Hands With(int seat, ResourceSet hand) => seat switch
    {
        0 => this with { S0 = hand },
        1 => this with { S1 = hand },
        2 => this with { S2 = hand },
        _ => this with { S3 = hand },
    };

    public bool AnyNegative()
    {
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            for (int r = 0; r < GameConstants.ResourceCount; r++)
                if (this[seat][r] < 0)
                    return true;
        return false;
    }
}

/// <summary>
/// Tracks every seat's possible resource hands from one seat's redacted event log. Nearly everything in Catan is public
/// (production, builds, trades, discards, Monopoly counts); the only unknowns are steals between two other seats. The tracker
/// keeps every combination of hands consistent with what the viewer has seen, with its exact probability: an unknown steal
/// splits each world by the resource the victim could have lost (weighted by how many of it they held), and later events
/// remove worlds that turn out impossible (e.g. someone builds a city they couldn't have afforded).
/// It uses only the redacted log, never hidden information.
/// </summary>
public sealed class HandTracker
{
    /// <summary>Most worlds kept; beyond this the least likely are dropped (rarely reached in practice).</summary>
    public const int MaxWorlds = 4096;

    private const int R = GameConstants.ResourceCount;

    private Dictionary<Hands, double> _worlds = new() { [default] = 1.0 };
    private int _processed;
    private bool _setupOver;
    private int _freeRoadSeat = -1, _freeRoads;

    public HandTracker(int viewer) => Viewer = viewer;

    public int Viewer { get; }

    public int WorldCount => _worlds.Count;

    /// <summary>The possible worlds and their probabilities (summing to 1).</summary>
    public IReadOnlyDictionary<Hands, double> Worlds => _worlds;

    /// <summary>Reads any events added to the log since the last call.</summary>
    public void Update(IReadOnlyList<GameEvent> log)
    {
        for (; _processed < log.Count; _processed++)
            Apply(log[_processed]);
    }

    /// <summary>
    /// Reads the view's new events. If the tracked worlds stop fitting what happens (possible after a game starts from a
    /// saved position, whose hands are only sampled, or after very unlikely worlds were dropped to stay small), it starts
    /// over from what the view shows now: hand sizes, the bank and our own hand, like a player re-reading the table.
    /// </summary>
    public void Update(PlayerView view)
    {
        try
        {
            Update(view.Events);
        }
        catch (InvalidOperationException)
        {
            Resyncs++;
            var hands = new ResourceSet[GameConstants.PlayerCount];
            hands[Viewer] = ResourceSet.From(view.Hand);
            _freeRoads = 0;
            StartFrom(new PositionStarted((int[])view.HandSizes.Clone(), (int[])view.Bank.Clone(), hands, false,
                view.Phase is Phase.SetupSettlement or Phase.SetupRoad));
            _processed = view.Events.Count;
        }
    }

    /// <summary>How many times the tracker had to start over from the table (<see cref="Update(PlayerView)"/>).</summary>
    public int Resyncs { get; private set; }

    /// <summary>Fewest of each resource <paramref name="seat"/> can hold.</summary>
    public ResourceSet Min(int seat) => Bound(seat, min: true);

    /// <summary>Most of each resource <paramref name="seat"/> can hold.</summary>
    public ResourceSet Max(int seat) => Bound(seat, min: false);

    public double Expected(int seat, int resource) => _worlds.Sum(w => w.Key[seat][resource] * w.Value);

    public bool IsPossible(Hands hands) => _worlds.ContainsKey(hands);

    /// <summary>One world drawn by probability.</summary>
    public Hands Sample(Rng rng)
    {
        double pick = rng.NextUInt() / (double)uint.MaxValue, total = 0;
        Hands last = default;
        foreach (var (hands, weight) in _worlds)
        {
            total += weight;
            last = hands;
            if (pick <= total)
                return hands;
        }
        return last;
    }

    private ResourceSet Bound(int seat, bool min)
    {
        Span<int> bound = stackalloc int[R];
        bound.Fill(min ? int.MaxValue : int.MinValue);
        foreach (var hands in _worlds.Keys)
            for (int r = 0; r < R; r++)
                bound[r] = min ? Math.Min(bound[r], hands[seat][r]) : Math.Max(bound[r], hands[seat][r]);
        return ResourceSet.From(bound);
    }

    // ---- Events ----

    private void Apply(GameEvent e)
    {
        // Road Building's free roads are the Built(Road) events right after the card is played (awards may interleave).
        if (_freeRoads > 0 && e is not (Built { Piece: PieceType.Road } or AwardChanged or GameEnded))
            _freeRoads = 0;

        switch (e)
        {
            case DiceRolled:
                _setupOver = true;
                break;
            case ResourcesProduced p:
                Map(h => h.With(p.Seat, h[p.Seat] + p.Gained));
                break;
            case Built b:
                if (!_setupOver)
                    break; // setup pieces are free
                if (b.Piece == PieceType.Road && _freeRoads > 0 && b.Seat == _freeRoadSeat)
                {
                    _freeRoads--;
                    break;
                }
                var cost = b.Piece switch { PieceType.Road => Costs.Road, PieceType.Settlement => Costs.Settlement, _ => Costs.City };
                Map(h => h.With(b.Seat, h[b.Seat] - cost));
                break;
            case DevCardBought d:
                Map(h => h.With(d.Seat, h[d.Seat] - Costs.DevCard));
                break;
            case DevCardPlayed { Type: DevCardType.RoadBuilding } d:
                _freeRoadSeat = d.Seat;
                _freeRoads = Rules.RoadBuildingRoads;
                break;
            case Discarded d:
                Map(h => h.With(d.Seat, h[d.Seat] - d.Cards));
                break;
            case PositionStarted p:
                StartFrom(p);
                break;
            case BankTraded t:
                Map(h => h.With(t.Seat, h[t.Seat] - t.Gave + t.Got));
                break;
            case TradeDone t:
                Map(h => h.With(t.Seat, h[t.Seat] - t.Gave + t.Got).With(t.Partner, h[t.Partner] + t.Gave - t.Got));
                break;
            case MonopolyTaken m:
                var taken = ResourceSet.Of((Resource)m.Resource, m.Count);
                Filter(h => h[m.Victim][m.Resource] == m.Count);
                Map(h => h.With(m.Victim, h[m.Victim] - taken).With(m.Seat, h[m.Seat] + taken));
                break;
            case CardStolen { Resource: >= 0 } c:
                var card = ResourceSet.Of((Resource)c.Resource);
                Map(h => h.With(c.Victim, h[c.Victim] - card).With(c.Thief, h[c.Thief] + card));
                break;
            case CardStolen c:
                Branch(c.Thief, c.Victim);
                break;
        }
    }

    /// <summary>
    /// A game that starts from a saved position: our own hand is known exactly; the others' cards are the ones not in the
    /// bank or our hand, dealt out by hand size. Worlds come from many random deals (weighted by how often each comes up), so
    /// it's as if we sat down at the table knowing only what everyone holds in total. A scenario may show every hand.
    /// </summary>
    private void StartFrom(PositionStarted p)
    {
        _setupOver = !p.InSetup;
        var hands = new ResourceSet[GameConstants.PlayerCount];
        if (p.HandsKnown)
        {
            Commit(new Dictionary<Hands, double> { [new Hands(p.Hands[0], p.Hands[1], p.Hands[2], p.Hands[3])] = 1 });
            return;
        }
        var pool = new List<int>();
        for (int r = 0; r < R; r++)
            for (int k = 0; k < Costs.BankPerResource - p.Bank[r] - p.Hands[Viewer][r]; k++)
                pool.Add(r);
        var rng = new Rng((ulong)(Viewer + 17), stream: 5);
        var next = new Dictionary<Hands, double>();
        var cards = pool.ToArray();
        for (int deal = 0; deal < StartDeals; deal++)
        {
            rng.Shuffle<int>(cards);
            int at = 0;
            for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
            {
                if (seat == Viewer)
                {
                    hands[seat] = p.Hands[seat];
                    continue;
                }
                var counts = new int[R];
                for (int k = 0; k < p.HandSizes[seat] && at < cards.Length; k++)
                    counts[cards[at++]]++;
                hands[seat] = ResourceSet.From(counts);
            }
            Add(next, new Hands(hands[0], hands[1], hands[2], hands[3]), 1);
        }
        Commit(next);
    }

    /// <summary>Random deals used to start tracking from a saved position.</summary>
    private const int StartDeals = 3000;

    /// <summary>Applies a deterministic change to every world, dropping worlds where someone would hold a negative count.</summary>
    private void Map(Func<Hands, Hands> change)
    {
        var next = new Dictionary<Hands, double>(_worlds.Count);
        foreach (var (hands, weight) in _worlds)
        {
            var changed = change(hands);
            if (!changed.AnyNegative())
                Add(next, changed, weight);
        }
        Commit(next);
    }

    private void Filter(Func<Hands, bool> keep)
    {
        var next = new Dictionary<Hands, double>(_worlds.Count);
        foreach (var (hands, weight) in _worlds)
            if (keep(hands))
                next[hands] = weight;
        Commit(next);
    }

    /// <summary>An unknown steal: each world splits by the resource taken, weighted by the victim's counts.</summary>
    private void Branch(int thief, int victim)
    {
        var next = new Dictionary<Hands, double>(_worlds.Count * 3);
        foreach (var (hands, weight) in _worlds)
        {
            var hand = hands[victim];
            int total = hand.Total;
            if (total == 0)
                continue; // impossible: a steal needs a card
            for (int r = 0; r < R; r++)
            {
                if (hand[r] == 0)
                    continue;
                var card = ResourceSet.Of((Resource)r);
                Add(next, hands.With(victim, hand - card).With(thief, hands[thief] + card), weight * hand[r] / total);
            }
        }
        Commit(next);
    }

    private static void Add(Dictionary<Hands, double> worlds, Hands hands, double weight) =>
        worlds[hands] = worlds.TryGetValue(hands, out double w) ? w + weight : weight;

    private void Commit(Dictionary<Hands, double> next)
    {
        if (next.Count == 0)
            throw new InvalidOperationException($"HandTracker for seat {Viewer}: no world fits the events (event {_processed}).");
        if (next.Count > MaxWorlds)
            next = next.OrderByDescending(w => w.Value).Take(MaxWorlds).ToDictionary(w => w.Key, w => w.Value);
        double sum = next.Values.Sum();
        foreach (var key in next.Keys.ToList())
            next[key] /= sum;
        _worlds = next;
    }
}
