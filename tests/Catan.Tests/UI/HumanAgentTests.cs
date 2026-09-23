using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class HumanAgentTests
{
    private static PlayerView View() => PlayerView.From(new GameState(TestBoards.Standard), 0);

    [Fact]
    public async Task DecisionCompletesWhenTheHumanSubmits()
    {
        var human = new HumanAgent("You", TimeSpan.FromSeconds(20));
        int changes = 0;
        human.PromptChanged += () => changes++;
        var legal = new[] { new GameAction(ActionType.RollDice, 0) };

        var task = human.DecideAsync(View(), legal, default);
        Assert.False(task.IsCompleted);
        Assert.NotNull(human.Prompt);
        Assert.False(human.Prompt!.IsOptional);
        Assert.Same(legal, human.Prompt.Legal);

        Assert.True(human.Submit(legal[0]));
        Assert.Equal(legal[0], await task);
        Assert.Null(human.Prompt);
        Assert.Equal(2, changes);
        Assert.False(human.Submit(legal[0])); // nothing open any more
    }

    [Fact]
    public async Task OptionalAnswerCanBeSubmittedOrSkipped()
    {
        var human = new HumanAgent("You", TimeSpan.FromSeconds(20));
        var accept = new GameAction(ActionType.AcceptOffer, 1, 0);

        var answered = human.RespondAsync(View(), new[] { accept }, default);
        Assert.True(human.Prompt!.IsOptional);
        Assert.NotNull(human.Prompt.Deadline);
        human.Submit(accept);
        Assert.Equal(accept, await answered);

        var skipped = human.RespondAsync(View(), new[] { accept }, default);
        Assert.True(human.Skip());
        Assert.Null(await skipped);
        Assert.Null(human.Prompt);
    }

    [Fact]
    public async Task OptionalAnswerTimesOutToNothing()
    {
        var human = new HumanAgent("You", TimeSpan.FromMilliseconds(50));
        var answer = await human.RespondAsync(View(), Array.Empty<GameAction>(), default);
        Assert.Null(answer);
        Assert.Null(human.Prompt);
    }

    [Fact]
    public async Task CancellingTheGameCancelsTheQuestion()
    {
        var human = new HumanAgent("You", TimeSpan.FromSeconds(20));
        using var cts = new CancellationTokenSource();
        var decision = human.DecideAsync(View(), Array.Empty<GameAction>(), cts.Token);
        var response = new HumanAgent("You", TimeSpan.FromSeconds(20)).RespondAsync(View(), Array.Empty<GameAction>(), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decision);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
    }

    [Fact]
    public async Task AHumanCanPlayThroughTheRunner()
    {
        // The "human" always picks the first legal action (or a random discard); bots play the other seats.
        var human = new HumanAgent("You", TimeSpan.FromMilliseconds(1));
        var rng = new Rng(3);
        human.PromptChanged += () =>
        {
            if (human.Prompt is not { IsOptional: false } prompt)
                return;
            var choice = prompt.View.Phase == Phase.Discard ? Rules.RandomDiscard(prompt.View, rng) : prompt.Legal[^1];
            human.Submit(choice);
        };
        var agents = new IPlayerAgent[] { new RandomBot(1), human, new RandomBot(2), new RandomBot(3) };
        var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(8)), new GameSettings { MaxTurns = 200 }), agents, new RngChance(9), validate: true);
        await runner.RunAsync();
        Assert.True(runner.IsOver);
        Assert.Contains(runner.Actions, a => a.Seat == 1);
    }
}

public class GameTextTests
{
    private static readonly GameText Text = new(new[] { SeatColor.Blue, SeatColor.Red, SeatColor.White, SeatColor.Orange }, viewer: 1);

    [Fact]
    public void ViewerIsYouAndOthersAreColors()
    {
        Assert.Equal("You", Text.Seat(1));
        Assert.Equal("Blue", Text.Seat(0));
        Assert.Equal("You rolled 7 (3 + 4)", Text.Describe(new DiceRolled(1, 3, 4)));
    }

    [Fact]
    public void HiddenDetailsStayHidden()
    {
        Assert.Equal("Blue stole a card from White", Text.Describe(new CardStolen(0, 2, -1)));
        Assert.Equal("You stole 1 ore from White", Text.Describe(new CardStolen(1, 2, 4)));
        Assert.Equal("Orange bought a development card", Text.Describe(new DevCardBought(3, null)));
        Assert.Equal("You bought a development card (Year of Plenty)", Text.Describe(new DevCardBought(1, DevCardType.YearOfPlenty)));
    }

    [Fact]
    public void CardsAndTradesReadNaturally()
    {
        Assert.Equal("2 wool, 1 ore", GameText.Cards(new ResourceSet(0, 0, 2, 0, 1)));
        Assert.Equal("Blue traded 1 ore to You for 2 wool",
            Text.Describe(new TradeDone(0, 1, ResourceSet.Of(Resource.Ore), ResourceSet.Of(Resource.Wool, 2))));
        Assert.Equal("Bank: 4 grain → 1 brick",
            Text.Describe(new GameAction(ActionType.BankTrade, 1, Give: ResourceSet.Of(Resource.Grain, 4), Get: ResourceSet.Of(Resource.Brick))));
        Assert.Equal("Longest Road: You now hold it", Text.Describe(new AwardChanged(Award.LongestRoad, -1, 1)));
        Assert.Equal("Largest Army: Blue now holds it", Text.Describe(new AwardChanged(Award.LargestArmy, 1, 0)));
    }

    [Fact]
    public void EveryEventAndActionHasText()
    {
        foreach (var type in Enum.GetValues<ActionType>())
            Assert.DoesNotContain("GameAction", Text.Describe(new GameAction(type, 1)));
    }
}
