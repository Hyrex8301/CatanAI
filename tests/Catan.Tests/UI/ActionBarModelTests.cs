using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class ActionBarModelTests
{
    private static readonly (BarItem Item, ActionType Type)[] Mapped =
    {
        (BarItem.BuyDev, ActionType.BuyDevCard), (BarItem.Road, ActionType.BuildRoad), (BarItem.Settlement, ActionType.BuildSettlement),
        (BarItem.City, ActionType.BuildCity), (BarItem.Roll, ActionType.RollDice), (BarItem.EndTurn, ActionType.EndTurn),
    };

    [Fact]
    public void ButtonsAreEnabledExactlyWhenTheEngineListsTheMove()
    {
        int checkedPositions = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 10 + (ulong)i)).ToArray(), new RngChance(seed));
            var legal = new List<GameAction>();
            for (int step = 0; step < 600 && !runner.IsOver; step++)
            {
                var s = runner.State;
                int seat = Rules.ActingSeat(s);
                Rules.GetLegalActions(s, seat, legal);
                var v = PlayerView.From(s, seat, runner.Log);
                foreach (var (item, type) in Mapped)
                {
                    var state = ActionBarModel.State(item, v, legal);
                    bool listed = legal.Any(a => a.Type == type) && (type is not (ActionType.BuildRoad or ActionType.BuildSettlement or ActionType.BuildCity) || s.Phase == Phase.Main);
                    Assert.Equal(listed, state.Enabled);
                    if (!state.Enabled && item != BarItem.Roll)
                        Assert.Contains('\n', state.Tooltip); // always says why
                }
                Assert.Equal(s.Phase == Phase.Main && s.CurrentPlayer == seat, ActionBarModel.State(BarItem.Trade, v, legal).Enabled);

                // Someone else's move: nothing works for the other seats.
                int other = (seat + 1) % 4;
                var otherView = PlayerView.From(s, other, runner.Log);
                foreach (BarItem item in Enum.GetValues<BarItem>())
                    Assert.False(ActionBarModel.State(item, otherView, null).Enabled);
                checkedPositions++;
                runner.StepAsync().GetAwaiter().GetResult();
            }
        }
        Assert.True(checkedPositions > 2000);
    }

    [Fact]
    public void BuildModesPickTheirOwnSpotsOnlyInMain()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 0, hasRolled: true)
            .Settlement(0, Topology.Vertex(0, 0, Corner.N)).Road(0, Topology.EdgeBetween(Topology.Vertex(0, 0, Corner.N), Topology.Vertex(0, 0, Corner.NE)))
            .Hand(0, brick: 3, lumber: 3, wool: 1, grain: 3, ore: 3).Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, 0, legal);
        var v = PlayerView.From(s, 0);

        Assert.Empty(ActionBarModel.BoardActions(BuildMode.None, v, legal));
        var roads = ActionBarModel.BoardActions(BuildMode.Road, v, legal).ToList();
        Assert.NotEmpty(roads);
        Assert.All(roads, a => Assert.Equal(ActionType.BuildRoad, a.Type));
        Assert.Equal(legal.Count(a => a.Type == ActionType.BuildRoad), roads.Count);
        Assert.All(ActionBarModel.BoardActions(BuildMode.City, v, legal), a => Assert.Equal(ActionType.BuildCity, a.Type));
        Assert.Single(ActionBarModel.BoardActions(BuildMode.City, v, legal));
    }

    [Fact]
    public void QuickBuildsAreEveryLegalBuildInMainAndNothingElse()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 0, hasRolled: true)
            .Settlement(0, Topology.Vertex(0, 0, Corner.N)).Road(0, Topology.EdgeBetween(Topology.Vertex(0, 0, Corner.N), Topology.Vertex(0, 0, Corner.NE)))
            .Hand(0, brick: 1, lumber: 1).Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, 0, legal);
        var quick = ActionBarModel.QuickBuilds(PlayerView.From(s, 0), legal).ToList();
        Assert.NotEmpty(quick);
        Assert.All(quick, a => Assert.Equal(ActionType.BuildRoad, a.Type)); // a wood and a brick: roads only
        Assert.Equal(legal.Count(a => a.Type == ActionType.BuildRoad), quick.Count);
        Assert.Equal(PieceType.Road, ActionBarModel.PieceOf(quick[0]));

        var setup = new GameState(TestBoards.Standard);
        Rules.GetLegalActions(setup, legal);
        Assert.Empty(ActionBarModel.QuickBuilds(PlayerView.From(setup, 0), legal)); // setup already highlights its spots
    }

    [Fact]
    public void SetupAndRobberHighlightEveryLegalSpotWithoutAMode()
    {
        var s = new GameState(TestBoards.Standard);
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, legal);
        Assert.Equal(legal.Count, ActionBarModel.BoardActions(BuildMode.None, PlayerView.From(s, 0), legal).Count());

        var robber = new StateBuilder(TestBoards.Standard).Phase(Phase.MoveRobber, current: 0, hasRolled: true).Build();
        Rules.GetLegalActions(robber, legal);
        Assert.Equal(legal.Count, ActionBarModel.BoardActions(BuildMode.Road, PlayerView.From(robber, 0), legal).Count());
    }

    [Fact]
    public void TooltipsExplainCostPiecesAndWhatIsMissing()
    {
        var s = new StateBuilder(TestBoards.Standard).Phase(Phase.Main, current: 0, hasRolled: true).Hand(0, grain: 1, ore: 1).Build();
        var legal = new List<GameAction>();
        Rules.GetLegalActions(s, 0, legal);
        var city = ActionBarModel.State(BarItem.City, PlayerView.From(s, 0), legal);
        Assert.False(city.Enabled);
        Assert.Equal(4, city.Left);
        Assert.Equal("City: 2 wheat, 3 ore (4 left)\nYou need 1 more wheat and 2 more ore", city.Tooltip);

        var preRoll = new StateBuilder(TestBoards.Standard).Phase(Phase.PreRoll, current: 0).Hand(0, brick: 1, lumber: 1).Build();
        Rules.GetLegalActions(preRoll, 0, legal);
        Assert.EndsWith("Roll the dice first", ActionBarModel.State(BarItem.Road, PlayerView.From(preRoll, 0), legal).Tooltip);
        Assert.True(ActionBarModel.State(BarItem.Roll, PlayerView.From(preRoll, 0), legal).Enabled);
    }

    [Fact]
    public void LastRollComesFromTheEventLog()
    {
        var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(4))),
            Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot((ulong)i)).ToArray(), new RngChance(4));
        Assert.Null(ActionBarModel.Roll(PlayerView.From(runner.State, 0, runner.Log)));
        while (!runner.Log.For(0).OfType<DiceRolled>().Any())
            runner.StepAsync().GetAwaiter().GetResult();
        var roll = ActionBarModel.Roll(PlayerView.From(runner.State, 0, runner.Log))!;
        var d = runner.Log.For(0).OfType<DiceRolled>().Last();
        Assert.Equal((d.Seat, d.D1, d.D2), (roll.Seat, roll.D1, roll.D2));
        Assert.Equal(d.Total, roll.Total);
    }
}
