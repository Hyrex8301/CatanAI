using Catan.Core;

namespace Catan.UI;

/// <summary>One player's final score, broken down the way the game-over screen shows it.</summary>
public sealed record FinalScore(int Seat, int Total, int Settlements, int Cities, int VictoryCards, bool LongestRoad, bool LargestArmy,
    int Knights, int RoadLength, int CardsFromRolls)
{
    /// <summary>Points from each source: settlements 1 each, cities 2 each, VP cards 1 each, each award 2.</summary>
    public int Points => Settlements + 2 * Cities + VictoryCards + (LongestRoad ? 2 : 0) + (LargestArmy ? 2 : 0);
}

/// <summary>The game-over screen: standings with each player's points by source, and how often each number was rolled.</summary>
/// <param name="Standings">Highest score first (the winner leads on a tie).</param>
/// <param name="Rolls">Index 2-12: how many times that total was rolled.</param>
public sealed record GameOverSummary(int Winner, IReadOnlyList<FinalScore> Standings, int[] Rolls, int Turns)
{
    /// <summary>
    /// Built from the finished game. Hidden Victory Point cards are revealed at the end, as in the real game, so this reads
    /// the game state; it refuses a game that isn't over. <paramref name="events"/> is any seat's log (rolls and production
    /// are public).
    /// </summary>
    public static GameOverSummary From(GameState s, IReadOnlyList<GameEvent> events)
    {
        if (s.Phase != Phase.GameOver)
            throw new InvalidOperationException("The game isn't over: hidden cards stay hidden until then.");
        var rolls = new int[13];
        var fromRolls = new int[GameConstants.PlayerCount];
        bool afterRoll = false;
        foreach (var e in events)
            switch (e)
            {
                case DiceRolled d:
                    rolls[d.Total]++;
                    afterRoll = true;
                    break;
                case ResourcesProduced p when afterRoll:
                    fromRolls[p.Seat] += p.Gained.Total;
                    break;
                case TurnEnded or DevCardPlayed:
                    afterRoll = false; // production after a roll only (not setup, not Year of Plenty)
                    break;
            }

        var scores = new List<FinalScore>();
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
        {
            int settlements = 0, cities = 0;
            for (int v = 0; v < Topology.VertexCount; v++)
                if (s.VertexOwner[v] == seat)
                {
                    if (s.VertexLevel[v] == 2)
                        cities++;
                    else
                        settlements++;
                }
            scores.Add(new FinalScore(seat, s.TotalVP(seat), settlements, cities,
                s.DevHand[seat * GameConstants.DevCardTypeCount + (int)DevCardType.VictoryPoint],
                s.LongestRoadOwner == seat, s.LargestArmyOwner == seat, s.KnightsPlayed[seat], LongestRoad.Compute(s, seat), fromRolls[seat]));
        }
        var standings = scores.OrderByDescending(f => f.Total).ThenByDescending(f => f.Seat == s.Winner).ThenBy(f => f.Seat).ToList();
        return new GameOverSummary(s.Winner, standings, rolls, s.TurnNumber);
    }
}
