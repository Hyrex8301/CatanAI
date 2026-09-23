using Catan.Core;

namespace Catan.Tests;

public class RngTests
{
    [Fact]
    public void MatchesPcg32ReferenceOutput()
    {
        // pcg32-demo from pcg-c-basic: pcg32_srandom_r(&rng, 42u, 54u), then pcg32_random_r.
        var rng = new Rng(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (uint want in expected)
            Assert.Equal(want, rng.NextUInt());
    }

    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new Rng(12345);
        var b = new Rng(12345);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextInt(1000), b.NextInt(1000));
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        var a = new Rng(1);
        var b = new Rng(2);
        int same = 0;
        for (int i = 0; i < 100; i++)
            if (a.NextUInt() == b.NextUInt())
                same++;
        Assert.True(same < 3);
    }

    [Fact]
    public void NextIntStaysInBounds()
    {
        var rng = new Rng(7);
        foreach (int bound in new[] { 1, 2, 3, 6, 7, 19, 25, 1000, int.MaxValue })
            for (int i = 0; i < 2000; i++)
            {
                int x = rng.NextInt(bound);
                Assert.InRange(x, 0, bound - 1);
            }
    }

    [Fact]
    public void NextIntRejectsNonPositiveBound()
    {
        var rng = new Rng(7);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-5));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(19)]
    public void NextIntIsRoughlyUniform(int bound)
    {
        const int perBucket = 10_000;
        var rng = new Rng(99);
        var counts = new int[bound];
        for (int i = 0; i < bound * perBucket; i++)
            counts[rng.NextInt(bound)]++;

        // Chi-square with bound-1 degrees of freedom; 50 is far beyond the 99.99th percentile for df <= 18.
        double chi2 = counts.Sum(c => (c - perBucket) * (double)(c - perBucket) / perBucket);
        Assert.True(chi2 < 50, $"chi-square {chi2:F1} for bound {bound}");
    }
}

public class RngChanceTests
{
    [Fact]
    public void DiceSumsFollowPips()
    {
        const int rolls = 360_000;
        var chance = new RngChance(3);
        var sums = new int[13];
        for (int i = 0; i < rolls; i++)
        {
            var (d1, d2) = chance.RollDice();
            Assert.InRange(d1, 1, 6);
            Assert.InRange(d2, 1, 6);
            sums[d1 + d2]++;
        }
        for (int n = 2; n <= 12; n++)
        {
            double expected = rolls * (6 - Math.Abs(7 - n)) / 36.0;
            Assert.InRange(sums[n], expected * 0.95, expected * 1.05);
        }
    }

    [Fact]
    public void StealIsWeightedByHandAndNeverPicksAnEmptySlot()
    {
        var chance = new RngChance(5);
        int[] hand = { 0, 3, 0, 1, 0 };
        var counts = new int[5];
        for (int i = 0; i < 40_000; i++)
            counts[chance.PickStolenCard(hand)]++;

        Assert.Equal(0, counts[0] + counts[2] + counts[4]);
        Assert.InRange(counts[1], 29_000, 31_000); // 3/4 of draws
        Assert.InRange(counts[3], 9_000, 11_000);  // 1/4 of draws
    }

    [Fact]
    public void DevDrawIsWeightedByDeck()
    {
        var chance = new RngChance(8);
        int[] deck = { 14, 5, 2, 2, 2 };
        var counts = new int[5];
        for (int i = 0; i < 250_000; i++)
            counts[chance.DrawDevCard(deck)]++;
        for (int t = 0; t < 5; t++)
        {
            double expected = 250_000 * deck[t] / 25.0;
            Assert.InRange(counts[t], expected * 0.95, expected * 1.05);
        }
    }

    [Fact]
    public void PickingFromEmptyThrows()
    {
        var chance = new RngChance(1);
        Assert.Throws<InvalidOperationException>(() => chance.PickStolenCard(new int[5]));
        Assert.Throws<InvalidOperationException>(() => chance.DrawDevCard(new int[5]));
    }

    [Fact]
    public void SameSeedGivesSameOutcomes()
    {
        var a = new RngChance(77);
        var b = new RngChance(77);
        int[] hand = { 2, 2, 2, 2, 2 };
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.RollDice(), b.RollDice());
            Assert.Equal(a.PickStolenCard(hand), b.PickStolenCard(hand));
        }
    }
}

public class ScriptedChanceTests
{
    [Fact]
    public void ReturnsScriptedOutcomesInOrder()
    {
        var chance = new ScriptedChance()
            .Roll(7).Dice(2, 3)
            .Steal(Resource.Ore)
            .Draw(DevCardType.VictoryPoint).Draw(DevCardType.Knight);

        var (a1, a2) = chance.RollDice();
        Assert.Equal(7, a1 + a2);
        Assert.Equal((2, 3), chance.RollDice());
        Assert.Equal((int)Resource.Ore, chance.PickStolenCard(new[] { 1, 1, 1, 1, 1 }));
        int[] deck = { 14, 5, 2, 2, 2 };
        Assert.Equal((int)DevCardType.VictoryPoint, chance.DrawDevCard(deck));
        Assert.Equal((int)DevCardType.Knight, chance.DrawDevCard(deck));
        Assert.True(chance.IsExhausted);
    }

    [Fact]
    public void ThrowsWhenNothingScriptedAndNoFallback()
    {
        var chance = new ScriptedChance();
        Assert.Throws<InvalidOperationException>(() => chance.RollDice());
        Assert.Throws<InvalidOperationException>(() => chance.PickStolenCard(new[] { 1, 0, 0, 0, 0 }));
        Assert.Throws<InvalidOperationException>(() => chance.DrawDevCard(new[] { 1, 0, 0, 0, 0 }));
    }

    [Fact]
    public void UsesFallbackAfterScriptRunsOut()
    {
        var chance = new ScriptedChance(new RngChance(1)).Roll(12);
        Assert.Equal((6, 6), chance.RollDice());
        var (d1, d2) = chance.RollDice();
        Assert.InRange(d1, 1, 6);
        Assert.InRange(d2, 1, 6);
    }

    [Fact]
    public void RejectsImpossibleScripts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedChance().Roll(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedChance().Roll(13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedChance().Dice(0, 6));

        var steal = new ScriptedChance().Steal(Resource.Brick);
        Assert.Throws<InvalidOperationException>(() => steal.PickStolenCard(new[] { 0, 5, 0, 0, 0 }));

        var draw = new ScriptedChance().Draw(DevCardType.Monopoly);
        Assert.Throws<InvalidOperationException>(() => draw.DrawDevCard(new[] { 14, 5, 2, 2, 0 }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(12)]
    public void RollProducesTheRequestedTotal(int total)
    {
        var (d1, d2) = new ScriptedChance().Roll(total).RollDice();
        Assert.Equal(total, d1 + d2);
    }
}
