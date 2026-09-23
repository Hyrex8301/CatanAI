using Catan.Core;

namespace Catan.AI;

/// <summary>
/// Builds a complete <see cref="GameState"/> a bot can search on from what it may see: its <see cref="PlayerView"/> plus
/// one world sampled from its <see cref="HandTracker"/>. Opponents' hidden dev cards are dealt at random from the cards no
/// one has seen (the 25-card set minus cards played and the bot's own), respecting each opponent's public dev card count.
/// </summary>
public static class Determinizer
{
    private const int Seats = GameConstants.PlayerCount;
    private const int R = GameConstants.ResourceCount;
    private const int D = GameConstants.DevCardTypeCount;
    private static readonly int[] FullDeck = { 14, 5, 2, 2, 2 };

    public static GameState Build(PlayerView v, HandTracker tracker, Rng rng) => Build(v, tracker.Sample(rng), rng);

    public static GameState Build(PlayerView v, Hands hands, Rng rng)
    {
        var s = new GameState(v.Board, v.Settings);
        Array.Copy(v.VertexOwner, s.VertexOwner, s.VertexOwner.Length);
        Array.Copy(v.VertexLevel, s.VertexLevel, s.VertexLevel.Length);
        Array.Copy(v.EdgeOwner, s.EdgeOwner, s.EdgeOwner.Length);
        s.RobberHex = v.RobberHex;
        Array.Copy(v.RoadsLeft, s.RoadsLeft, Seats);
        Array.Copy(v.SettlementsLeft, s.SettlementsLeft, Seats);
        Array.Copy(v.CitiesLeft, s.CitiesLeft, Seats);

        for (int seat = 0; seat < Seats; seat++)
        {
            var hand = seat == v.Seat ? v.HandSet : hands[seat];
            for (int r = 0; r < R; r++)
                s.Hand[seat * R + r] = hand[r];
        }
        Array.Copy(v.Bank, s.Bank, R);

        DealDevCards(v, s, rng);
        Array.Copy(v.DevPlayed, s.DevPlayed, D);
        Array.Copy(v.KnightsPlayed, s.KnightsPlayed, Seats);
        if (v.Seat == v.CurrentPlayer)
            Array.Copy(v.DevBoughtThisTurn, s.DevBoughtThisTurn, D);

        s.LongestRoadOwner = v.LongestRoadOwner;
        s.LargestArmyOwner = v.LargestArmyOwner;
        Array.Copy(v.RoadLength, s.RoadLength, Seats);
        Array.Copy(v.PublicVP, s.PublicVP, Seats);

        s.Phase = v.Phase;
        s.CurrentPlayer = v.CurrentPlayer;
        s.TurnNumber = v.TurnNumber;
        s.SetupStep = v.SetupStep;
        s.LastRoll = v.LastRoll;
        s.FreeRoads = v.FreeRoads;
        s.OffersThisTurn = v.OffersThisTurn;
        s.HasRolled = v.HasRolled;
        s.DevPlayedThisTurn = v.DevPlayedThisTurn;
        Array.Copy(v.DiscardOwed, s.DiscardOwed, Seats);
        Array.Copy(v.Offers, s.Offers, s.Offers.Length);
        s.Winner = v.Winner;
        return s;
    }

    /// <summary>The bot's own dev cards are known; everyone else's come from the unseen pool, the rest is the deck.</summary>
    private static void DealDevCards(PlayerView v, GameState s, Rng rng)
    {
        Span<int> unseen = stackalloc int[D];
        int pool = 0;
        for (int t = 0; t < D; t++)
        {
            unseen[t] = FullDeck[t] - v.DevPlayed[t] - v.DevHand[t];
            pool += unseen[t];
            s.DevHand[v.Seat * D + t] = v.DevHand[t];
        }
        for (int seat = 0; seat < Seats; seat++)
        {
            if (seat == v.Seat)
                continue;
            for (int k = 0; k < v.DevCardCounts[seat]; k++)
            {
                int pick = rng.NextInt(pool), t = 0;
                while (pick >= unseen[t])
                    pick -= unseen[t++];
                unseen[t]--;
                pool--;
                s.DevHand[seat * D + t]++;
            }
        }
        for (int t = 0; t < D; t++)
            s.DevDeck[t] = unseen[t];
    }
}
