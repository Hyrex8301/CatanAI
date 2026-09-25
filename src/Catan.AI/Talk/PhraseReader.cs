using System.Text;
using System.Text.RegularExpressions;
using Catan.Core;

namespace Catan.AI.Talk;

/// <summary>What a chat line is: a deal proposal, a yes or no to the last proposal, or just talk.</summary>
public enum TalkKind : byte { Proposal, Accept, Refuse, Chatter }

/// <summary>
/// What a chat line says, from the speaker's side. <see cref="Target"/> is who it's addressed to (-1 = anyone).
/// The speaker promises <see cref="SpeakerPromises"/> and asks the target for <see cref="TargetPromises"/>; the speaker
/// gives <see cref="SpeakerGives"/> and gets <see cref="SpeakerGets"/> (the cards move as an ordinary trade).
/// <see cref="Length"/> is the number said, if any: robber moves for nb / ns, the promiser's turns for a spot.
/// <see cref="Problem"/> explains a line that looked like a deal but couldn't be read (a spot that isn't on the board).
/// </summary>
public sealed record Reading(
    TalkKind Kind,
    int Speaker,
    int Target,
    IReadOnlyList<PromiseTerm> SpeakerPromises,
    IReadOnlyList<PromiseTerm> TargetPromises,
    ResourceSet SpeakerGives,
    ResourceSet SpeakerGets,
    int? Length,
    string? Problem = null)
{
    public bool HasPromises => SpeakerPromises.Count > 0 || TargetPromises.Count > 0;

    /// <summary>
    /// The reading in plain words, e.g. "Orange won't block you the next time they move the robber, if you give a wheat".
    /// <paramref name="board"/> names spots by their numbers.
    /// </summary>
    public string Describe(IReadOnlyList<string> names, int viewer, Board? board = null)
    {
        // Nobody named: with cards, the promise goes to whoever takes the trade; without, to anyone.
        bool withCards = SpeakerGives.Total > 0 || SpeakerGets.Total > 0;
        string Name(int seat) => seat < 0 ? (withCards ? "whoever takes the trade" : "anyone") : seat == viewer ? "you" : names[seat];
        string Verb(int seat, string you, string other) => seat == viewer ? you : other;
        string Whose(int seat) => seat == viewer ? "your" : $"{Name(seat)}'s";
        if (Problem is not null)
            return Problem;
        switch (Kind)
        {
            case TalkKind.Accept:
                return Capital($"{Name(Speaker)} {Verb(Speaker, "accept", "accepts")}");
            case TalkKind.Refuse:
                return Capital($"{Name(Speaker)} {Verb(Speaker, "say", "says")} no");
            case TalkKind.Chatter:
                return "No deal in that (try \"wheat nb?\" or \"I won't take 6 5 9 for a sheep\")";
        }
        var parts = new List<string>();
        if (SpeakerPromises.Count > 0)
            parts.Add($"{Name(Speaker)} won't {Terms(SpeakerPromises, board)} {Name(Target)}");
        if (TargetPromises.Count > 0)
            parts.Add($"{Name(Target)} won't {Terms(TargetPromises, board)} {Name(Speaker)}");
        string text = string.Join(" and ", parts) + HowLong(viewer);
        if (SpeakerGives.Total > 0 || SpeakerGets.Total > 0)
        {
            string trade = (SpeakerGives.Total, SpeakerGets.Total) switch
            {
                ( > 0, > 0) => $"{Name(Speaker)} {Verb(Speaker, "trade", "trades")} {Cards(SpeakerGives)} for {Cards(SpeakerGets)}",
                ( > 0, _) => $"{Name(Speaker)} {Verb(Speaker, "give", "gives")} {Cards(SpeakerGives)} (for a card of {Whose(Target)} choice)",
                _ => $"{Name(Target)} {Verb(Target, "give", "gives")} {Cards(SpeakerGets)} (for a card of {Whose(Speaker)} choice)",
            };
            text += $", if {trade}";
        }
        return Capital(text);
    }

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// How long the promises last. nb / ns count the promiser's robber moves (the next one unless a number was said); a spot
    /// lasts the rest of the game unless a number of turns was said.
    /// </summary>
    private string HowLong(int viewer)
    {
        var all = SpeakerPromises.Concat(TargetPromises).ToList();
        if (all.All(t => t.Kind == PromiseKind.NoBuild))
            return Length is { } turns ? $" for {turns} turn{(turns == 1 ? "" : "s")}" : " for the rest of the game";
        string times = Length is { } n and > 1 ? $"next {n} times" : "next time";
        if (SpeakerPromises.Count > 0 && TargetPromises.Count > 0)
            return $" the {times} each of them moves the robber";
        int promiser = SpeakerPromises.Count > 0 ? Speaker : Target;
        return promiser == viewer ? $" the {times} you move the robber" : $" the {times} they move the robber";
    }

    private static string Terms(IReadOnlyList<PromiseTerm> terms, Board? board) =>
        string.Join(" or ", terms.Select(t => t.Kind switch
        {
            PromiseKind.NoBlock => "block",
            PromiseKind.NoSteal => "steal from",
            _ => $"take the {(board is null ? $"#{t.Vertex}" : Spots.Name(board, t.Vertex))} spot from",
        }).Distinct());

    private static string Cards(ResourceSet set)
    {
        var parts = new List<string>();
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (set[r] > 0)
                parts.Add(set[r] == 1 ? $"{(ResourceNames.Of(r)[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a")} {ResourceNames.Of(r)}" : $"{set[r]} {ResourceNames.Of(r)}");
        return string.Join(" and ", parts);
    }
}

/// <summary>
/// Reads chat lines into deals, with no language model: key words and the community shorthand. "nb" = non-block (no robber
/// on your tiles), "ns" = non-steal; "rob" means both; they cover the promiser's next robber move ("for 2 turns" = the next
/// two). A card named with a promise is its price: "wheat nb?" = "give me a wheat (for a card of mine) and I won't block
/// you". A spot is its numbers, "6 5 9" (a promise not to take it; it lasts the rest of the game). "X for Y" = the speaker
/// gives X and wants Y, where either side may be promises ("nb for nb"). "me" / "don't" turn a promise into a request
/// ("don't block me, I'll give you ore"). Colour names pick the target.
/// </summary>
public static class PhraseReader
{
    private static readonly HashSet<string> Yes = new() { "deal", "ok", "okay", "k", "yes", "yep", "yeah", "ya", "sure", "accept", "accepted", "done", "agreed", "fine", "bet", "y" };
    private static readonly HashSet<string> No = new() { "no", "nah", "nope", "pass", "decline", "n", "no thanks", "no deal", "nty", "no ty" };

    private static readonly Dictionary<string, int> ResourceWords = new()
    {
        ["brick"] = 0, ["bricks"] = 0, ["clay"] = 0,
        ["wood"] = 1, ["woods"] = 1, ["lumber"] = 1, ["log"] = 1, ["logs"] = 1,
        ["sheep"] = 2, ["wool"] = 2,
        ["wheat"] = 3, ["wheats"] = 3, ["grain"] = 3,
        ["ore"] = 4, ["ores"] = 4, ["rock"] = 4, ["rocks"] = 4, ["stone"] = 4,
    };

    private static readonly Dictionary<string, int> Numbers = new() { ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5 };

    /// <summary>
    /// Reads <paramref name="text"/> said by <paramref name="speaker"/>. <paramref name="names"/> are the seats' colour names
    /// (lower case, e.g. "orange"); <paramref name="defaultTarget"/> is who "you" means when no name is said (-1 = anyone);
    /// <paramref name="board"/> finds spots named by their numbers; when several corners share the numbers,
    /// <paramref name="isOpen"/> (still buildable) narrows them down.
    /// </summary>
    public static Reading Read(string text, int speaker, IReadOnlyList<string> names, int defaultTarget, Board? board = null,
        Func<int, bool>? isOpen = null)
    {
        string clean = Normalize(text);
        string bare = clean.Replace("?", "").Replace("!", "").Replace(".", "").Trim();
        var none = Array.Empty<PromiseTerm>();
        if (Yes.Contains(bare))
            return new Reading(TalkKind.Accept, speaker, defaultTarget, none, none, default, default, null);
        if (No.Contains(bare))
            return new Reading(TalkKind.Refuse, speaker, defaultTarget, none, none, default, default, null);

        // Length: "for 2 turns" / "for two knights" (taken out first: its "for" isn't the trade's).
        int? length = null;
        var said = Regex.Match(clean, @"\bfor (\d+|one|two|three|four|five) (turns?|rounds?|knights?|robbers?|times|robber moves?)\b");
        if (said.Success)
        {
            string n = said.Groups[1].Value;
            length = Math.Clamp(int.TryParse(n, out int parsed) ? parsed : Numbers[n], 1, 10);
            clean = clean.Remove(said.Index, said.Length);
        }

        var tokens = Regex.Split(clean, @"[^a-z0-9']+").Where(t => t.Length > 0).ToList();
        string? problem = MarkSpots(tokens, board, isOpen);

        int target = defaultTarget;
        for (int seat = 0; seat < names.Count; seat++)
            if (seat != speaker && tokens.Contains(names[seat]))
                target = seat;
        if (tokens.Any(t => t is "anyone" or "anybody" or "everyone" or "all"))
            target = -1;

        // Split into the speaker's side and the wanted side at the first "for" (after the length was removed).
        int forAt = tokens.IndexOf("for");
        var mineSide = forAt >= 0 ? tokens.Take(forAt).ToList() : tokens;
        var wantedSide = forAt >= 0 ? tokens.Skip(forAt + 1).ToList() : new List<string>();

        var speakerPromises = new List<PromiseTerm>();
        var targetPromises = new List<PromiseTerm>();
        var gives = new int[GameConstants.ResourceCount];
        var gets = new int[GameConstants.ResourceCount];

        if (forAt >= 0)
        {
            ReadPromises(mineSide, speakerPromises, targetPromises);
            // Everything on the wanted side is asked of the target ("nb for nb", "wheat for wood, nb me?").
            var wanted = new List<PromiseTerm>();
            ReadPromises(wantedSide, wanted, wanted);
            targetPromises.AddRange(wanted);
            ReadCards(mineSide, gives);
            ReadCards(wantedSide, gets);
            // "wheat for sheep nb": a promise tacked onto the cards the speaker wants is the speaker's sweetener.
            if (speakerPromises.Count == 0 && gets.Sum() > 0 && !wantedSide.Contains("me") && !wantedSide.Contains("my"))
                (speakerPromises, targetPromises) = (targetPromises, speakerPromises);
        }
        else
        {
            ReadPromises(tokens, speakerPromises, targetPromises);
            // Cards with direction words: "give me X" = the speaker gets X, "give you X" / "i'll give X" = the speaker gives X.
            var loose = new int[GameConstants.ResourceCount];
            ReadDirectedCards(tokens, gives, gets, loose);
            // A card with no direction pays for the promise: the receiver of the promise pays it.
            bool speakerPays = speakerPromises.Count == 0 && targetPromises.Count > 0;
            for (int r = 0; r < loose.Length; r++)
                (speakerPays ? gives : gets)[r] += loose[r];
        }

        if (problem is not null)
            return new Reading(TalkKind.Chatter, speaker, target, none, none, default, default, length, problem);
        if (speakerPromises.Count == 0 && targetPromises.Count == 0)
            return new Reading(TalkKind.Chatter, speaker, target, none, none, default, default, length);
        return new Reading(TalkKind.Proposal, speaker, target, Distinct(speakerPromises), Distinct(targetPromises),
            ResourceSet.From(gives), ResourceSet.From(gets), length);
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Trim().ToLowerInvariant());
        sb.Replace("’", "'").Replace("non-block", "nb").Replace("non block", "nb").Replace("no block", "nb")
          .Replace("non-steal", "ns").Replace("non steal", "ns").Replace("no steal", "ns")
          .Replace("do not", "dont").Replace("don't", "dont").Replace("won't", "wont").Replace("will not", "wont")
          .Replace("i'll", "ill").Replace("i will", "ill");
        return sb.ToString();
    }

    /// <summary>
    /// Replaces each spot named by its numbers ("6 5 9", "6/5/9": two or three numbers in a row, not a card count) with a
    /// "spot:V" token. Returns a problem when the numbers match no spot or several, or there's no board to look them up.
    /// </summary>
    private static string? MarkSpots(List<string> tokens, Board? board, Func<int, bool>? isOpen)
    {
        static bool IsNumber(string t) => int.TryParse(t, out int n) && n is >= 2 and <= 12 and not 7;
        string? problem = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            int end = i;
            while (end < tokens.Count && end - i < 3 && IsNumber(tokens[end]))
                end++;
            int count = end - i;
            bool isCardCount = end < tokens.Count && ResourceWords.ContainsKey(tokens[end]);
            if (count < 2 || isCardCount)
                continue;
            var numbers = tokens.GetRange(i, count).Select(int.Parse).ToList();
            string said = string.Join(" ", numbers);
            if (board is null)
                problem ??= $"Can't look up the {said} spot without the board";
            else
            {
                var found = Spots.Find(board, numbers);
                if (found.Count > 1 && isOpen is not null && found.Count(isOpen) >= 1)
                    found = found.Where(isOpen).ToList(); // only open spots are worth a promise
                if (found.Count == 1)
                {
                    tokens.RemoveRange(i, count);
                    tokens.Insert(i, $"spot:{found[0]}");
                    continue;
                }
                problem ??= found.Count == 0 ? $"There's no {said} spot on this board" : $"{said} matches {found.Count} open spots: add the third number, or click the spot";
            }
            i = end - 1;
        }
        return problem;
    }

    /// <summary>
    /// Finds promise words. A promise is the speaker's unless the clause is about "me"/"my" or asks ("dont …", "please"),
    /// in which case it's asked of the target. <paramref name="own"/> gets the speaker-side terms.
    /// </summary>
    private static void ReadPromises(List<string> tokens, List<PromiseTerm> own, List<PromiseTerm> asked)
    {
        // Clauses split at "and", "if", "then" so "i wont block you if you dont steal from me" reads both halves.
        var clause = new List<string>();
        foreach (string t in tokens.Append("and"))
        {
            if (t is "and" or "if" or "then" or "but")
            {
                ReadClause(clause, own, asked);
                clause.Clear();
            }
            else
                clause.Add(t);
        }
    }

    private static void ReadClause(List<string> words, List<PromiseTerm> own, List<PromiseTerm> asked)
    {
        if (words.Count == 0)
            return;
        var terms = new List<PromiseTerm>();
        for (int i = 0; i < words.Count; i++)
        {
            string w = words[i];
            if (w == "nb" || w == "block" || w == "blocking" || (w == "robber" && !words.Contains("rob")))
                terms.Add(new PromiseTerm(PromiseKind.NoBlock));
            else if (w == "ns" || w == "steal" || w == "stealing")
                terms.Add(new PromiseTerm(PromiseKind.NoSteal));
            else if (w is "rob" or "robbing" or "nbns")
            {
                terms.Add(new PromiseTerm(PromiseKind.NoBlock));
                terms.Add(new PromiseTerm(PromiseKind.NoSteal));
            }
            else if (w.StartsWith("spot:"))
                terms.Add(new PromiseTerm(PromiseKind.NoBuild, int.Parse(w[5..])));
        }
        if (terms.Count == 0)
            return;
        bool aboutMe = words.Contains("me") || words.Contains("my") || words.Contains("mine");
        bool asking = words[0] is "dont" or "please" or "pls" or "plz" or "can" or "could" or "will" or "would" or "leave" or "stay";
        bool mine = words.Contains("i") || words.Contains("ill") || words.Contains("im");
        (aboutMe || (asking && !mine) ? asked : own).AddRange(terms);
    }

    private static void ReadCards(List<string> tokens, int[] into)
    {
        for (int i = 0; i < tokens.Count; i++)
            if (ResourceWords.TryGetValue(tokens[i], out int r))
                into[r] += CountBefore(tokens, i);
    }

    private static void ReadDirectedCards(List<string> tokens, int[] gives, int[] gets, int[] loose)
    {
        // Direction lasts from "give me"/"send me"/"trade me" (speaker gets) or "give you"/"ill give"/"i give" (speaker gives)
        // to the end of the clause.
        int[]? current = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i];
            if (t is "and" or "if" or "then")
                current = null;
            else if (t is "give" or "send" or "trade" or "pay")
            {
                string next = i + 1 < tokens.Count ? tokens[i + 1] : "";
                string prev = i > 0 ? tokens[i - 1] : "";
                if (next == "me")
                    current = gets;
                else if (next == "you" || prev is "i" or "ill" or "id")
                    current = gives;
            }
            else if (ResourceWords.TryGetValue(t, out int r))
                (current ?? loose)[r] += CountBefore(tokens, i);
        }
    }

    private static int CountBefore(List<string> tokens, int i)
    {
        if (i == 0)
            return 1;
        string prev = tokens[i - 1];
        if (int.TryParse(prev, out int n) && n is > 0 and <= 7)
            return n;
        return Numbers.TryGetValue(prev, out int w) ? w : 1;
    }

    private static List<PromiseTerm> Distinct(List<PromiseTerm> terms) => terms.Distinct().ToList();
}
