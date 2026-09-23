using Catan.Core;

namespace Catan.UI;

/// <summary>Where a flying card starts or ends: a hex, a player's row (or your hand), or the bank.</summary>
public enum SpotKind { Hex, Seat, Bank }

public readonly record struct Spot(SpotKind Kind, int Id = -1)
{
    public static Spot Hex(int hex) => new(SpotKind.Hex, hex);
    public static Spot Seat(int seat) => new(SpotKind.Seat, seat);
    public static readonly Spot Bank = new(SpotKind.Bank);
}

/// <summary>One thing to animate.</summary>
public abstract record Cue;

/// <summary>The hexes showing this number light up (a roll).</summary>
public sealed record FlashNumber(int Number) : Cue;

/// <summary>A card flies from one spot to another. Resource -1 = face down (the viewer may not see which).</summary>
public sealed record FlyCard(int Resource, Spot From, Spot To) : Cue;

/// <summary>A new piece pops onto the board.</summary>
public sealed record PopPiece(PieceType Piece, int Target) : Cue;

/// <summary>The robber slides to a hex.</summary>
public sealed record SlideRobber(int Hex) : Cue;

/// <summary>A short message about something that happened to you (or a big moment).</summary>
public sealed record Toast(string Text) : Cue;

/// <summary>
/// Turns the viewer's redacted events, in order, into animation cues: rolls flash their hexes and send cards from the
/// producing hexes to each player, steals and trades send cards between players, builds pop, the robber slides, and events
/// that concern you raise a toast ("Blue stole a sheep from you"). Stateful: it remembers the last roll, the last
/// settlement each player placed (setup production comes from its hexes) and a just-played Year of Plenty (from the bank).
/// </summary>
public sealed class AnimationCues
{
    private readonly GameText _text;
    private readonly int _viewer;
    private readonly int[] _lastSettlement = { -1, -1, -1, -1 };
    private int _lastRoll;
    private bool _yearOfPlenty;

    public AnimationCues(GameText text, int viewer)
    {
        _text = text;
        _viewer = viewer;
    }

    /// <summary>The cues for one event. <paramref name="v"/> is the viewer's view after the event's action.</summary>
    public IEnumerable<Cue> For(GameEvent e, PlayerView v)
    {
        switch (e)
        {
            case DiceRolled d:
                _lastRoll = d.Total;
                if (d.Total != 7)
                    yield return new FlashNumber(d.Total);
                else if (d.Seat != _viewer)
                    yield return new Toast($"{_text.Seat(d.Seat)} rolled a 7: the robber moves");
                break;

            case ResourcesProduced p:
                var from = _yearOfPlenty ? null : Sources(v, p.Seat, p.Gained);
                _yearOfPlenty = false;
                for (int r = 0; r < GameConstants.ResourceCount; r++)
                    for (int k = 0; k < p.Gained[r]; k++)
                        yield return new FlyCard(r, from?[r] is { } hex ? Spot.Hex(hex) : Spot.Bank, Spot.Seat(p.Seat));
                break;

            case Built b:
                if (b.Piece == PieceType.Settlement)
                    _lastSettlement[b.Seat] = b.Target;
                yield return new PopPiece(b.Piece, b.Target);
                break;

            case RobberMoved r:
                yield return new SlideRobber(r.Hex);
                break;

            case CardStolen c:
                yield return new FlyCard(c.Resource, Spot.Seat(c.Victim), Spot.Seat(c.Thief));
                if (c.Victim == _viewer)
                    yield return new Toast($"{_text.Seat(c.Thief)} stole {Card(c.Resource)} from you");
                else if (c.Thief == _viewer)
                    yield return new Toast($"You stole {Card(c.Resource)} from {_text.Seat(c.Victim)}");
                break;

            case MonopolyTaken m:
                for (int k = 0; k < Math.Min(m.Count, 8); k++)
                    yield return new FlyCard(m.Resource, Spot.Seat(m.Victim), Spot.Seat(m.Seat));
                if (m.Victim == _viewer && m.Count > 0)
                    yield return new Toast($"{_text.Seat(m.Seat)} took your {m.Count} {GameText.Resource(m.Resource)} (Monopoly)");
                break;

            case Discarded d:
                foreach (var cue in Cards(d.Cards, Spot.Seat(d.Seat), Spot.Bank))
                    yield return cue;
                break;

            case BankTraded b:
                foreach (var cue in Cards(b.Gave, Spot.Seat(b.Seat), Spot.Bank).Concat(Cards(b.Got, Spot.Bank, Spot.Seat(b.Seat))))
                    yield return cue;
                break;

            case TradeDone t:
                foreach (var cue in Cards(t.Gave, Spot.Seat(t.Seat), Spot.Seat(t.Partner)).Concat(Cards(t.Got, Spot.Seat(t.Partner), Spot.Seat(t.Seat))))
                    yield return cue;
                if (t.Seat == _viewer || t.Partner == _viewer)
                {
                    bool mine = t.Seat == _viewer;
                    var gave = mine ? t.Gave : t.Got;
                    var got = mine ? t.Got : t.Gave;
                    yield return new Toast($"Traded with {_text.Seat(mine ? t.Partner : t.Seat)}: you gave {GameText.Cards(gave)}, got {GameText.Cards(got)}");
                }
                break;

            case DevCardPlayed d:
                _yearOfPlenty = d.Type == DevCardType.YearOfPlenty;
                if (d.Seat != _viewer)
                    yield return new Toast($"{_text.Seat(d.Seat)} played {GameText.DevCard(d.Type)}");
                break;

            case AwardChanged a:
                string award = a.Award == Award.LongestRoad ? "Longest Road" : "Largest Army";
                if (a.To == _viewer)
                    yield return new Toast($"You got {award}!");
                else if (a.From == _viewer)
                    yield return new Toast(a.To >= 0 ? $"{_text.Seat(a.To)} took {award} from you" : $"You lost {award}");
                else if (a.To >= 0)
                    yield return new Toast($"{_text.Seat(a.To)} got {award}");
                break;

            case GameEnded g:
                yield return new Toast(g.Winner == _viewer ? "You won the game!" : g.Winner >= 0 ? $"{_text.Seat(g.Winner)} won the game" : "The game ended in a draw");
                break;
        }
    }

    private static string Card(int resource) => resource >= 0 ? $"a {GameText.Resource(resource)}" : "a card";

    private static IEnumerable<Cue> Cards(ResourceSet cards, Spot from, Spot to)
    {
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            for (int k = 0; k < cards[r]; k++)
                yield return new FlyCard(r, from, to);
    }

    /// <summary>
    /// For each resource, a hex the seat's cards came from: after a roll, a hex with that number and resource next to one of
    /// their buildings (not under the robber); in setup, a hex of that resource next to the settlement they just placed.
    /// </summary>
    private int?[] Sources(PlayerView v, int seat, ResourceSet gained)
    {
        var sources = new int?[GameConstants.ResourceCount];
        bool setup = v.Phase is Phase.SetupRoad or Phase.SetupSettlement || _lastRoll == 0;
        for (int h = 0; h < Topology.HexCount; h++)
        {
            int r = v.Board.ResourceAt(h);
            if (r < 0 || gained[r] == 0 || sources[r] is not null)
                continue;
            bool touches = false;
            for (int c = 0; c < 6; c++)
            {
                int vertex = Topology.HexVertices[h, c];
                touches |= setup ? vertex == _lastSettlement[seat] : v.VertexOwner[vertex] == seat;
            }
            if (touches && (setup || (v.Board.NumberAt(h) == _lastRoll && h != v.RobberHex)))
                sources[r] = h;
        }
        return sources;
    }
}
