using Catan.Core;

namespace Catan.AI;

/// <summary>
/// Deals random practice positions for Position practice: it plays a bot game on a new board to a random point in the middle
/// of the game and stops right after some player's roll, when that player has several different things they could do
/// (build, buy a card, trade with the bank, play a card). That player's seat is yours: you play the whole turn and the bots
/// grade each of your plays. Every position is real (from an actual game), so it's legal and natural, and the same seed
/// always deals the same position.
/// </summary>
public static class PositionDealer
{
    /// <summary>Kinds of play that count as a real choice (ending the turn always is one, so it isn't counted).</summary>
    private static readonly ActionType[] Choices =
    {
        ActionType.BuildRoad, ActionType.BuildSettlement, ActionType.BuildCity, ActionType.BuyDevCard, ActionType.BankTrade,
        ActionType.PlayKnight, ActionType.PlayRoadBuilding, ActionType.PlayYearOfPlenty, ActionType.PlayMonopoly,
    };

    /// <summary>Fewest kinds of play (besides ending the turn) a dealt position offers.</summary>
    public const int MinKinds = 2;

    public static Position Deal(BotWeights weights, ulong seed)
    {
        var rng = new Rng(seed, stream: 9);
        for (int attempt = 0; ; attempt++)
        {
            ulong game = seed * 31 + (ulong)attempt;
            // Somewhere in the middle: past the opening, before the end (turns count every seat's turn).
            int fromTurn = 12 + rng.NextInt(40);
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(game))),
                Enumerable.Range(0, GameConstants.PlayerCount)
                    .Select(i => (IPlayerAgent)new SmartBot(weights, SmartBotSettings.Training, game * 11 + (ulong)i)).ToArray(),
                new RngChance(game));
            bool justRolled = false;
            runner.ActionApplied += (action, _) => justRolled = action.Type == ActionType.RollDice;
            var legal = new List<GameAction>();
            while (!runner.IsOver && runner.State.TurnNumber < fromTurn + 40)
            {
                var s = runner.State;
                if (justRolled && s.TurnNumber >= fromTurn && Fits(s, legal))
                    return Practice(s);
                runner.StepAsync().GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>The player to move just rolled (not a 7), is in their build phase, has several kinds of play, and can't win right away.</summary>
    public static bool Fits(GameState s, List<GameAction> legal)
    {
        if (s.Phase != Phase.Main || s.Winner >= 0 || s.LastRoll == 7 || Rules.ActingSeat(s) != s.CurrentPlayer)
            return false;
        int seat = s.CurrentPlayer;
        if (s.TotalVP(seat) >= s.Settings.VpToWin - 2)
            return false; // one build from winning is no puzzle
        Rules.GetLegalActions(s, seat, legal);
        return legal.Select(a => a.Type).Where(Choices.Contains).Distinct().Count() >= MinKinds;
    }

    private static Position Practice(GameState s)
    {
        var p = Position.From(s, s.CurrentPlayer);
        return new Position
        {
            Board = p.Board, Settings = p.Settings, State = p.State, Seat = s.CurrentPlayer, Kind = ScenarioKind.Decision,
            Title = "Position practice",
            Description = $"You rolled {s.LastRoll}. Play your whole turn as well as you can, then end it: the bots rank each of your plays.",
        };
    }
}
