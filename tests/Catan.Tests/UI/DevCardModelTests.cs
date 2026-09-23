using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class DevCardModelTests
{
    private static readonly DevCardType[] Types = Enum.GetValues<DevCardType>();

    [Fact]
    public void PlayableExactlyWhenTheEngineListsAPlayAndPicksBuildListedActions()
    {
        int plays = 0;
        var legal = new List<GameAction>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 5 + (ulong)i)).ToArray(), new RngChance(seed));
            for (int step = 0; step < 3000 && !runner.IsOver; step++)
            {
                var s = runner.State;
                int seat = Rules.ActingSeat(s);
                Rules.GetLegalActions(s, seat, legal);
                var v = PlayerView.From(s, seat, runner.Log);
                foreach (var type in Types)
                {
                    var (playable, why) = DevCardModel.State(v, legal, type);
                    var listed = legal.Where(a => a.Type is ActionType.PlayKnight or ActionType.PlayRoadBuilding or ActionType.PlayYearOfPlenty or ActionType.PlayMonopoly
                                                  && DevCardModel.Action(type, seat, a.Type == ActionType.PlayMonopoly ? ResourceSet.Of((Resource)a.Target) : a.Get) == a).ToList();
                    Assert.Equal(listed.Count > 0, playable);
                    if (!playable)
                        Assert.False(string.IsNullOrEmpty(why));
                    plays += listed.Count;
                }
                runner.StepAsync().GetAwaiter().GetResult();
            }
        }
        Assert.True(plays > 50, $"only {plays} dev card plays seen");
    }

    [Fact]
    public void PicksTurnIntoTheRightActions()
    {
        var two = ResourceSet.Of(Resource.Wool) + ResourceSet.Of(Resource.Ore);
        Assert.Equal(new GameAction(ActionType.PlayYearOfPlenty, 2, Get: two), DevCardModel.Action(DevCardType.YearOfPlenty, 2, two));
        Assert.Null(DevCardModel.Action(DevCardType.YearOfPlenty, 2, ResourceSet.Of(Resource.Wool)));
        Assert.Equal(new GameAction(ActionType.PlayMonopoly, 1, (int)Resource.Grain), DevCardModel.Action(DevCardType.Monopoly, 1, ResourceSet.Of(Resource.Grain)));
        Assert.Null(DevCardModel.Action(DevCardType.VictoryPoint, 0, default));
        Assert.Equal(2, DevCardModel.Picks(DevCardType.YearOfPlenty));
        Assert.Equal(1, DevCardModel.Picks(DevCardType.Monopoly));
        Assert.Equal(0, DevCardModel.Picks(DevCardType.Knight));
    }

    [Fact]
    public void ReasonsExplainWhyNot()
    {
        var bought = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 0, hasRolled: true)
            .DevCards(0, knight: 1, victoryPoint: 1).BoughtThisTurn(DevCardType.Knight).Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(bought, 0, legal);
        var v = PlayerView.From(bought, 0);
        Assert.Equal((false, "You can't play a card on the turn you bought it."), DevCardModel.State(v, legal, DevCardType.Knight));
        Assert.StartsWith("Victory Point cards aren't played", DevCardModel.State(v, legal, DevCardType.VictoryPoint).Why);

        var played = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 0, hasRolled: true).DevCards(0, monopoly: 1).DevPlayedThisTurn().Build();
        Rules.GetLegalActions(played, 0, legal);
        Assert.Equal((false, "You've already played a development card this turn."), DevCardModel.State(PlayerView.From(played, 0), legal, DevCardType.Monopoly));

        var theirTurn = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 1, hasRolled: true).DevCards(0, knight: 1).Build();
        Assert.Equal((false, "You can only play development cards on your own turn."), DevCardModel.State(PlayerView.From(theirTurn, 0), null, DevCardType.Knight));
    }
}
