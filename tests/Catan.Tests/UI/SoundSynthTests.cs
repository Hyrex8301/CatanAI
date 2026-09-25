using Catan.UI;

namespace Catan.Tests.UI;

public class SoundSynthTests
{
    public static IEnumerable<object[]> Every() => Enum.GetValues<GameSound>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Every))]
    public void EverySoundIsShortCleanAndAudible(GameSound sound)
    {
        var s = SoundSynth.Render(sound);
        Assert.InRange(s.Length, SoundSynth.SampleRate / 20, SoundSynth.SampleRate * 3); // 50 ms to 3 s
        Assert.All(s, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
        Assert.True(s.Max(Math.Abs) > 0.3f, "too quiet");
        Assert.True(Math.Abs(s[^1]) < 0.01f, "ends in a click");
        Assert.Equal(s, SoundSynth.Render(sound)); // the same every time
    }
}
