using System.Text.Json;
using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>What a promise rules out: the robber on the other player's tiles, stealing from them, or settling a spot.</summary>
public enum PromiseKind : byte { NoBlock, NoSteal, NoBuild }

/// <summary>One promise term: its kind, and for <see cref="PromiseKind.NoBuild"/> the vertex (else -1).</summary>
public readonly record struct PromiseTerm(PromiseKind Kind, int Vertex = -1);

/// <summary>
/// A promise from <see cref="From"/> to <see cref="To"/>. nb / ns count robber moves: the promise covers the promiser's next
/// <see cref="RobberMoves"/> robber moves (7s or knights; 1 unless a number was said) and each one uses a move up, kept or
/// not. A spot promise holds while the turn number is below <see cref="UntilTurn"/> (the rest of the game unless a length
/// was said). <see cref="BrokenAtTurn"/> is set when the promiser breaks it (it then no longer binds).
/// </summary>
public sealed record Promise(int From, int To, PromiseTerm Term, int MadeAtTurn, int UntilTurn, int RobberMoves = 0,
    int? BrokenAtTurn = null, int? UsedAtTurn = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRobberPromise => Term.Kind != PromiseKind.NoBuild;

    public bool InForce(int turn) => BrokenAtTurn is null && UsedAtTurn is null && (IsRobberPromise ? RobberMoves > 0 : turn < UntilTurn);
}

/// <summary>A broken promise: who broke it, to whom, and how.</summary>
public sealed record BrokenPromise(Promise Promise, GameAction Action);

/// <summary>
/// Every promise made in a game (table talk is public, so every bot may read it). Promises aren't rules: the book only
/// tells which moves would break one, notices when one is broken, and keeps each seat's record for trust. Safe to share
/// between threads (bots think on worker threads while the chat adds promises).
/// </summary>
public sealed class DealBook
{
    private readonly List<Promise> _promises = new();
    private readonly object _lock = new();

    /// <summary>A snapshot of every promise so far.</summary>
    public IReadOnlyList<Promise> Promises
    {
        get { lock (_lock) return _promises.ToList(); }
    }

    /// <summary>
    /// Records a promise from <paramref name="from"/> to <paramref name="to"/>. <paramref name="length"/> is the number said:
    /// for nb / ns the promiser's next robber moves (default 1), for a spot the promiser's turns (turn numbers count every
    /// seat's turns; default the rest of the game).
    /// </summary>
    public Promise Add(int from, int to, PromiseTerm term, int turnNow, int? length = null)
    {
        var p = term.Kind == PromiseKind.NoBuild
            ? new Promise(from, to, term, turnNow, length is { } n ? turnNow + Math.Max(1, n) * GameConstants.PlayerCount : int.MaxValue)
            : new Promise(from, to, term, turnNow, int.MaxValue, RobberMoves: Math.Max(1, length ?? 1));
        lock (_lock)
            _promises.Add(p);
        return p;
    }

    /// <summary>Promises <paramref name="from"/> has made that are still in force.</summary>
    public List<Promise> MadeBy(int from, int turn)
    {
        lock (_lock)
            return _promises.Where(p => p.From == from && p.InForce(turn)).ToList();
    }

    /// <summary>The promise <paramref name="action"/> would break, if any.</summary>
    public Promise? WouldBreak(GameState s, GameAction action) => WouldBreak(s.TurnNumber, v => s.VertexOwner[v], action);

    /// <summary>The promise <paramref name="action"/> would break, if any, from a player's view (promises are public).</summary>
    public Promise? WouldBreak(PlayerView view, GameAction action) => WouldBreak(view.TurnNumber, v => view.VertexOwner[v], action);

    private Promise? WouldBreak(int turn, Func<int, int> owner, GameAction action)
    {
        lock (_lock)
            foreach (var p in _promises)
                if (p.From == action.Seat && p.InForce(turn) && Breaks(owner, p, action))
                    return p;
        return null;
    }

    /// <summary>
    /// Marks the promises <paramref name="action"/> breaks and returns them (call just before or just after applying it:
    /// the checks only read buildings, which a robber move or a new settlement elsewhere doesn't change). Every robber move
    /// by the promiser also counts down their nb / ns promises it keeps.
    /// </summary>
    public List<BrokenPromise> Record(GameState s, GameAction action)
    {
        var broken = new List<BrokenPromise>();
        lock (_lock)
            for (int i = 0; i < _promises.Count; i++)
            {
                var p = _promises[i];
                if (p.From != action.Seat || !p.InForce(s.TurnNumber))
                    continue;
                if (Breaks(v => s.VertexOwner[v], p, action))
                {
                    _promises[i] = p with { BrokenAtTurn = s.TurnNumber };
                    broken.Add(new BrokenPromise(_promises[i], action));
                }
                else if (p.IsRobberPromise && action.Type == ActionType.MoveRobber)
                    _promises[i] = p.RobberMoves > 1 ? p with { RobberMoves = p.RobberMoves - 1 } : p with { RobberMoves = 0, UsedAtTurn = s.TurnNumber };
            }
        return broken;
    }

    /// <summary>
    /// How far others trust <paramref name="seat"/>'s word, 0..1: 1 with a clean record; each broken promise costs more the
    /// more recent it is (a break wears off over about five rounds).
    /// </summary>
    public double Trust(int seat, int turn)
    {
        double trust = 1;
        lock (_lock)
            foreach (var p in _promises)
                if (p.From == seat && p.BrokenAtTurn is { } at)
                {
                    double age = (turn - at) / (double)GameConstants.PlayerCount;
                    trust *= 0.5 + 0.5 * Math.Clamp(age / 5, 0, 1);
                }
        return trust;
    }

    private static bool Breaks(Func<int, int> owner, Promise p, GameAction a)
    {
        switch (p.Term.Kind)
        {
            case PromiseKind.NoBlock:
                return a.Type == ActionType.MoveRobber && TouchesSeat(owner, a.Target, p.To);
            case PromiseKind.NoSteal:
                return a.Type == ActionType.MoveRobber && a.Target2 == p.To;
            case PromiseKind.NoBuild:
                if (a.Type != ActionType.BuildSettlement)
                    return false;
                if (a.Target == p.Term.Vertex)
                    return true;
                for (int i = 0; i < 3; i++)
                    if (Topology.VertexNeighbors[a.Target, i] == p.Term.Vertex)
                        return true; // settling next to the spot takes it too (distance rule)
                return false;
            default:
                return false;
        }
    }

    private static bool TouchesSeat(Func<int, int> owner, int hex, int seat)
    {
        for (int c = 0; c < 6; c++)
            if (owner(Topology.HexVertices[hex, c]) == seat)
                return true;
        return false;
    }

    public string ToJson()
    {
        lock (_lock)
            return JsonSerializer.Serialize(_promises);
    }

    public static DealBook FromJson(string json)
    {
        var book = new DealBook();
        book._promises.AddRange(JsonSerializer.Deserialize<List<Promise>>(json) ?? new());
        return book;
    }
}
