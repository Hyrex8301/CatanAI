using Catan.Core;

namespace Catan.UI;

/// <summary>
/// The turn timer, counting down: <see cref="RollSeconds"/> to roll, then <see cref="TurnSeconds"/> for the rest of the
/// turn, plus <see cref="ActionBonusSeconds"/> for every move the current player makes (placing, building, robbing,
/// trading, playing a card), or <see cref="KnightBonusSeconds"/> for a knight. A setup turn gets the turn time straight
/// away. Only the clock: the game screen decides what happens when it runs out (for you, the game rolls or ends your
/// turn; bots never take that long).
/// </summary>
public sealed class TurnClock
{
    public const double RollSeconds = 5, TurnSeconds = 60, ActionBonusSeconds = 10;

    /// <summary>Playing a knight adds more, so there's time to talk about where the robber goes.</summary>
    public const double KnightBonusSeconds = 30;

    private (int Player, int Turn) _turn = (-1, -1);

    /// <summary>When the current player's time runs out (UTC).</summary>
    public DateTime Deadline { get; private set; }

    /// <summary>Starts a new countdown when the turn changes. Call with every fresh view.</summary>
    public void Update(PlayerView view, DateTime now)
    {
        if ((view.CurrentPlayer, view.TurnNumber) == _turn)
            return;
        _turn = (view.CurrentPlayer, view.TurnNumber);
        Deadline = now.AddSeconds(view.Phase == Phase.PreRoll ? RollSeconds : TurnSeconds);
    }

    /// <summary>After a move is applied: rolling starts the turn time, other moves by the current player add the bonus.</summary>
    public void OnAction(GameAction action, int currentPlayer, DateTime now)
    {
        if (action.Seat != currentPlayer)
            return; // answers to trades don't touch the current player's clock
        if (action.Type == ActionType.RollDice)
            Deadline = now.AddSeconds(TurnSeconds);
        else if (action.Type == ActionType.PlayKnight)
            Deadline = Deadline.AddSeconds(KnightBonusSeconds);
        else if (action.Type != ActionType.EndTurn)
            Deadline = Deadline.AddSeconds(ActionBonusSeconds);
    }

    public double Remaining(DateTime now) => Math.Max(0, (Deadline - now).TotalSeconds);

    public bool Expired(DateTime now) => now >= Deadline;
}
