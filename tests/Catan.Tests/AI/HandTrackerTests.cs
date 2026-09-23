using Catan.AI;
using Catan.Core;

namespace Catan.Tests.AI;

public class HandTrackerTests
{
    private static Hands TrueHands(GameState s) =>
        new(ResourceSet.From(s.HandOf(0)), ResourceSet.From(s.HandOf(1)), ResourceSet.From(s.HandOf(2)), ResourceSet.From(s.HandOf(3)));

    [Fact]
    public void StartsWithEveryoneEmptyHanded()
    {
        var t = new HandTracker(0);
        Assert.Equal(1, t.WorldCount);
        Assert.True(t.IsPossible(default));
    }

    [Fact]
    public void PublicEventsAreTrackedExactly()
    {
        var t = new HandTracker(0);
        var log = new List<GameEvent>
        {
            new ResourcesProduced(1, new ResourceSet(2, 2, 1, 1, 0)),
            new ResourcesProduced(2, ResourceSet.Of(Resource.Wool)),
            new DiceRolled(1, 3, 3),
            new Built(1, PieceType.Settlement, 5),                      // pays 1 brick, 1 lumber, 1 wool, 1 grain
            new BankTraded(1, ResourceSet.Of(Resource.Brick), ResourceSet.Of(Resource.Ore)), // the tracker just follows the cards
            new TradeDone(1, 2, ResourceSet.Of(Resource.Lumber), ResourceSet.Of(Resource.Wool)),
        };
        t.Update(log);
        Assert.Equal(1, t.WorldCount);
        Assert.Equal(new ResourceSet(0, 0, 1, 0, 1), t.Min(1));
        Assert.Equal(new ResourceSet(0, 0, 1, 0, 1), t.Max(1));
        Assert.Equal(new ResourceSet(0, 1, 0, 0, 0), t.Min(2));
    }

    [Fact]
    public void AnUnknownStealSplitsWorldsByTheVictimsCounts()
    {
        var t = new HandTracker(0);
        t.Update(new List<GameEvent>
        {
            new ResourcesProduced(2, new ResourceSet(3, 0, 0, 0, 1)),
            new DiceRolled(1, 3, 4),
            new CardStolen(1, 2, -1), // seat 0 doesn't see what seat 1 took from seat 2
        });
        Assert.Equal(2, t.WorldCount);
        Assert.Equal(0.75, t.Expected(1, (int)Resource.Brick), 9);
        Assert.Equal(0.25, t.Expected(1, (int)Resource.Ore), 9);
        Assert.Equal(new ResourceSet(2, 0, 0, 0, 0), t.Min(2));
        Assert.Equal(new ResourceSet(3, 0, 0, 0, 1), t.Max(2));
    }

    [Fact]
    public void LaterEventsRuleWorldsOut()
    {
        var t = new HandTracker(0);
        t.Update(new List<GameEvent>
        {
            new ResourcesProduced(2, new ResourceSet(1, 0, 0, 0, 3)),
            new DiceRolled(1, 3, 4),
            new CardStolen(1, 2, -1),
            new ResourcesProduced(1, new ResourceSet(0, 1, 0, 0, 0)),
            new Built(1, PieceType.Road, 7), // seat 1 had no brick of its own: the stolen card was the brick
        });
        Assert.Equal(1, t.WorldCount);
        Assert.Equal(new ResourceSet(0, 0, 0, 0, 3), t.Min(2));
    }

    [Fact]
    public void MonopolyRevealsExactCounts()
    {
        var t = new HandTracker(0);
        t.Update(new List<GameEvent>
        {
            new ResourcesProduced(2, new ResourceSet(2, 0, 2, 0, 0)),
            new DiceRolled(1, 3, 4),
            new CardStolen(1, 2, -1),
            new MonopolyTaken(3, 2, (int)Resource.Wool, 2), // seat 2 still had both wool: the steal took a brick
            new MonopolyTaken(3, 1, (int)Resource.Wool, 0),
        });
        Assert.Equal(1, t.WorldCount);
        Assert.Equal(new ResourceSet(1, 0, 0, 0, 0), t.Min(1));
        Assert.Equal(new ResourceSet(0, 0, 2, 0, 0), t.Min(3)); // 2 from seat 2, none from seat 1
    }

    [Fact]
    public void RoadBuildingRoadsAreFree()
    {
        var t = new HandTracker(0);
        t.Update(new List<GameEvent>
        {
            new ResourcesProduced(1, new ResourceSet(1, 1, 0, 0, 0)),
            new DiceRolled(1, 3, 4),
            new DevCardPlayed(1, DevCardType.RoadBuilding),
            new Built(1, PieceType.Road, 1),
            new AwardChanged(Award.LongestRoad, -1, 1),
            new Built(1, PieceType.Road, 2),
            new Built(1, PieceType.Road, 3), // the third road is paid
        });
        Assert.Equal(default, t.Min(1));
        Assert.Equal(default, t.Max(1));
    }

    /// <summary>
    /// The key property, over whole random games: from every seat's point of view, the true hands are always one of the
    /// tracked worlds, the bounds contain them, and a determinized state built from a sampled world is a valid game state.
    /// </summary>
    [Fact]
    public async Task NeverContradictsTheTrueHandsInRandomGames()
    {
        int maxWorlds = 0, determinized = 0;
        var rng = new Rng(77);
        for (ulong seed = 0; seed < 60; seed++)
        {
            var runner = RandomTestAgent.Game(seed, settings: new GameSettings { MaxTurns = 300 });
            var trackers = Enumerable.Range(0, 4).Select(i => new HandTracker(i)).ToArray();
            runner.ActionApplied += (_, _) =>
            {
                var s = runner.State;
                var truth = TrueHands(s);
                for (int viewer = 0; viewer < 4; viewer++)
                {
                    trackers[viewer].Update(runner.Log.For(viewer));
                    Assert.True(trackers[viewer].IsPossible(truth), $"seed {seed}, action {runner.Actions.Count}: seat {viewer} lost the true hands");
                    maxWorlds = Math.Max(maxWorlds, trackers[viewer].WorldCount);
                    for (int seat = 0; seat < 4; seat++)
                        Assert.True(trackers[viewer].Min(seat).FitsIn(truth[seat]) && truth[seat].FitsIn(trackers[viewer].Max(seat)));
                }
                if (runner.Actions.Count % 50 == 0 && s.Phase != Phase.GameOver)
                {
                    int viewer = runner.Actions.Count / 50 % 4;
                    var view = PlayerView.From(s, viewer, runner.Log);
                    var built = Determinizer.Build(view, trackers[viewer], rng);
                    var errors = StateValidator.Check(built);
                    Assert.True(errors.Count == 0, $"seed {seed}: determinized state invalid: {string.Join(" | ", errors)}");
                    Assert.Equal(PlayerView.From(s, viewer).HandSizes, PlayerView.From(built, viewer).HandSizes);
                    determinized++;
                }
            };
            await runner.RunAsync();
        }
        Assert.True(determinized > 100);
        Assert.True(maxWorlds < HandTracker.MaxWorlds, $"world cap reached ({maxWorlds})");
    }
}
