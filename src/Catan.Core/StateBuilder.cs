namespace Catan.Core;

/// <summary>
/// Fluent setup of any position for tests and practice. Place pieces and cards; Build() derives everything cached
/// (pieces left, bank, deck, road lengths, awards, VP) and throws if the result fails <see cref="StateValidator"/>.
/// </summary>
public sealed class StateBuilder
{
    private const int R = GameConstants.ResourceCount;
    private const int D = GameConstants.DevCardTypeCount;

    private readonly GameState _s;
    private int? _longestRoadOwner, _largestArmyOwner;
    private bool? _hasRolled;

    public StateBuilder(Board board, GameSettings? settings = null) => _s = new GameState(board, settings);

    public StateBuilder Settlement(int seat, int vertex) => Building(seat, vertex, 1);

    public StateBuilder City(int seat, int vertex) => Building(seat, vertex, 2);

    public StateBuilder Road(int seat, int edge)
    {
        _s.EdgeOwner[edge] = (sbyte)seat;
        return this;
    }

    public StateBuilder Roads(int seat, params int[] edges)
    {
        foreach (int e in edges)
            Road(seat, e);
        return this;
    }

    /// <summary>Sets the seat's hand exactly; the cards come out of the bank.</summary>
    public StateBuilder Hand(int seat, int brick = 0, int lumber = 0, int wool = 0, int grain = 0, int ore = 0) =>
        Hand(seat, new ResourceSet(brick, lumber, wool, grain, ore));

    public StateBuilder Hand(int seat, ResourceSet hand)
    {
        for (int r = 0; r < R; r++)
            _s.Hand[seat * R + r] = hand[r];
        return this;
    }

    /// <summary>Sets the seat's dev cards exactly; they come out of the deck.</summary>
    public StateBuilder DevCards(int seat, int knight = 0, int victoryPoint = 0, int roadBuilding = 0, int yearOfPlenty = 0, int monopoly = 0)
    {
        int[] counts = { knight, victoryPoint, roadBuilding, yearOfPlenty, monopoly };
        for (int t = 0; t < D; t++)
            _s.DevHand[seat * D + t] = counts[t];
        return this;
    }

    /// <summary>Marks some of the current player's dev cards as bought this turn (they must also be in its hand).</summary>
    public StateBuilder BoughtThisTurn(DevCardType type, int count = 1)
    {
        _s.DevBoughtThisTurn[(int)type] = count;
        return this;
    }

    public StateBuilder KnightsPlayed(int seat, int count)
    {
        _s.KnightsPlayed[seat] = count;
        return this;
    }

    public StateBuilder Robber(int hex)
    {
        _s.RobberHex = hex;
        return this;
    }

    /// <param name="hasRolled">Defaults to false in setup and PreRoll, true elsewhere.</param>
    public StateBuilder Phase(Phase phase, int current = 0, bool? hasRolled = null)
    {
        _s.Phase = phase;
        _s.CurrentPlayer = current;
        _hasRolled = hasRolled;
        return this;
    }

    /// <summary>Overrides the Longest Road holder (-1 for none). By default the seat longest alone with 5+ holds it.</summary>
    public StateBuilder LongestRoadOwner(int seat)
    {
        _longestRoadOwner = seat;
        return this;
    }

    /// <summary>Overrides the Largest Army holder (-1 for none). By default the seat with the most Knights (3+, alone) holds it.</summary>
    public StateBuilder LargestArmyOwner(int seat)
    {
        _largestArmyOwner = seat;
        return this;
    }

    public GameState Build()
    {
        var s = _s.Clone();
        const int seats = GameConstants.PlayerCount;

        for (int seat = 0; seat < seats; seat++)
        {
            int settlements = 0, cities = 0, roads = 0;
            for (int v = 0; v < Topology.VertexCount; v++)
                if (s.VertexOwner[v] == seat)
                {
                    if (s.VertexLevel[v] == 1) settlements++;
                    else cities++;
                }
            for (int e = 0; e < Topology.EdgeCount; e++)
                if (s.EdgeOwner[e] == seat)
                    roads++;
            s.SettlementsLeft[seat] = Costs.SettlementsPerPlayer - settlements;
            s.CitiesLeft[seat] = Costs.CitiesPerPlayer - cities;
            s.RoadsLeft[seat] = Costs.RoadsPerPlayer - roads;
        }

        for (int r = 0; r < R; r++)
        {
            s.Bank[r] = Costs.BankPerResource;
            for (int seat = 0; seat < seats; seat++)
                s.Bank[r] -= s.Hand[seat * R + r];
        }

        s.DevPlayed[(int)DevCardType.Knight] = s.KnightsPlayed.Sum();
        for (int t = 0; t < D; t++)
        {
            s.DevDeck[t] = StandardPieces.DevDeck[t] - s.DevPlayed[t];
            for (int seat = 0; seat < seats; seat++)
                s.DevDeck[t] -= s.DevHand[seat * D + t];
        }

        for (int seat = 0; seat < seats; seat++)
            s.RoadLength[seat] = Core.LongestRoad.Compute(s, seat);
        s.LongestRoadOwner = _longestRoadOwner ?? StateValidator.UniqueMax(s.RoadLength, 5);
        s.LargestArmyOwner = _largestArmyOwner ?? StateValidator.UniqueMax(s.KnightsPlayed, 3);

        for (int seat = 0; seat < seats; seat++)
        {
            int vp = (s.LongestRoadOwner == seat ? 2 : 0) + (s.LargestArmyOwner == seat ? 2 : 0);
            for (int v = 0; v < Topology.VertexCount; v++)
                if (s.VertexOwner[v] == seat)
                    vp += s.VertexLevel[v];
            s.PublicVP[seat] = vp;
        }

        s.HasRolled = _hasRolled ?? s.Phase is not (Core.Phase.SetupSettlement or Core.Phase.SetupRoad or Core.Phase.PreRoll);

        var errors = StateValidator.Check(s);
        if (errors.Count > 0)
            throw new InvalidOperationException("StateBuilder produced an invalid state:\n" + string.Join("\n", errors));
        return s;
    }

    private StateBuilder Building(int seat, int vertex, byte level)
    {
        _s.VertexOwner[vertex] = (sbyte)seat;
        _s.VertexLevel[vertex] = level;
        return this;
    }
}
