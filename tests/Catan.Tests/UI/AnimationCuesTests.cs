using Catan.AI;
using Catan.Core;
using Catan.UI;

namespace Catan.Tests.UI;

public class AnimationCuesTests
{
    private static readonly SeatColor[] Colors = { SeatColor.Blue, SeatColor.Red, SeatColor.White, SeatColor.Orange };

    [Fact]
    public void ProducedCardsFlyFromMatchingHexesAndEveryCardIsAnimated()
    {
        int flights = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var runner = new GameRunner(new GameState(BoardGenerator.Balanced(new Rng(seed))),
                Enumerable.Range(0, 4).Select(i => (IPlayerAgent)new RandomBot(seed * 7 + (ulong)i)).ToArray(), new RngChance(seed));
            int viewer = (int)(seed % 4);
            var cues = new AnimationCues(new GameText(Colors, viewer), viewer);
            int seen = 0, lastRoll = 0;
            for (int step = 0; step < 1500 && !runner.IsOver; step++)
            {
                runner.StepAsync().GetAwaiter().GetResult();
                var events = runner.Log.For(viewer);
                var v = PlayerView.From(runner.State, viewer, runner.Log);
                for (; seen < events.Count; seen++)
                {
                    var e = events[seen];
                    var list = cues.For(e, v).ToList();
                    if (e is DiceRolled d)
                        lastRoll = d.Total;
                    if (e is ResourcesProduced p)
                    {
                        var flies = list.OfType<FlyCard>().ToList();
                        Assert.Equal(p.Gained.Total, flies.Count);
                        Assert.All(flies, f => Assert.Equal(Spot.Seat(p.Seat), f.To));
                        foreach (var f in flies.Where(f => f.From.Kind == SpotKind.Hex))
                        {
                            Assert.Equal(f.Resource, v.Board.ResourceAt(f.From.Id));
                            if (v.Phase is not (Phase.SetupSettlement or Phase.SetupRoad) && lastRoll != 0)
                                Assert.Equal(lastRoll, v.Board.NumberAt(f.From.Id));
                        }
                        flights += flies.Count(f => f.From.Kind == SpotKind.Hex);
                    }
                    if (e is CardStolen { Resource: < 0 })
                        Assert.Contains(list, c => c is FlyCard { Resource: -1 });
                    // Toasts about steals only for the players involved.
                    if (e is CardStolen s && s.Thief != viewer && s.Victim != viewer)
                        Assert.DoesNotContain(list, c => c is Toast);
                }
            }
        }
        Assert.True(flights > 200, $"only {flights} cards flew from hexes");
    }

    [Fact]
    public void BuildsPopAndTheRobberSlides()
    {
        var cues = new AnimationCues(new GameText(Colors, 0), 0);
        var v = PlayerView.From(new GameState(TestBoards.Standard), 0);
        Assert.Equal(new Cue[] { new PopPiece(PieceType.City, 12) }, cues.For(new Built(1, PieceType.City, 12), v).ToArray());
        Assert.Equal(new Cue[] { new SlideRobber(4) }, cues.For(new RobberMoved(2, 4), v).ToArray());
        Assert.Equal(new Cue[] { new FlashNumber(8) }, cues.For(new DiceRolled(2, 3, 5), v).ToArray());
    }

    [Fact]
    public void ToastsSpeakToYou()
    {
        var cues = new AnimationCues(new GameText(Colors, 0), 0);
        var v = PlayerView.From(new GameState(TestBoards.Standard), 0);
        Assert.Contains(new Toast("Red stole a sheep from you"), cues.For(new CardStolen(1, 0, (int)Resource.Wool), v));
        Assert.Contains(new Toast("You stole a card from White"), cues.For(new CardStolen(0, 2, -1), v));
        Assert.Contains(new Toast("You got Longest Road!"), cues.For(new AwardChanged(Award.LongestRoad, 1, 0), v));
        Assert.Contains(new Toast("Red took your 3 wheat (Monopoly)"), cues.For(new MonopolyTaken(1, 0, (int)Resource.Grain, 3), v));
        Assert.Contains(new Toast("Traded with White: you gave 1 ore, got 1 brick"),
            cues.For(new TradeDone(2, 0, ResourceSet.Of(Resource.Brick), ResourceSet.Of(Resource.Ore)), v));
    }

    [Fact]
    public void YearOfPlentyCardsComeFromTheBank()
    {
        var cues = new AnimationCues(new GameText(Colors, 0), 0);
        var v = PlayerView.From(new GameState(TestBoards.Standard), 0);
        cues.For(new DevCardPlayed(0, DevCardType.YearOfPlenty), v).ToList();
        var flies = cues.For(new ResourcesProduced(0, new ResourceSet(0, 0, 0, 2, 0)), v).OfType<FlyCard>().ToList();
        Assert.Equal(2, flies.Count);
        Assert.All(flies, f => Assert.Equal(Spot.Bank, f.From));
    }
}
