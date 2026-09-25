namespace Catan.UI;

/// <summary>The moments that make a sound (everything else is silent: the user's choice, 2026-09-25).</summary>
public enum GameSound : byte { YourTurn, Dice, Road, Settlement, City, Trade, Win, Lose }

/// <summary>
/// Every sound the game makes, built in code (no recordings, nothing to credit), soft and direct: gentle attacks, few
/// overtones, a low-pass filter, one clear hit and a short tail. The user picked them on 2026-09-25 from a few versions
/// each: a soft marimba for your turn, trades, winning and losing; felt dice; a soft thud for placing pieces (lighter for a
/// road, a double thud for a city). Mono samples in -1..1 at <see cref="SampleRate"/>, deterministic.
/// </summary>
public static class SoundSynth
{
    public const int SampleRate = 44100;

    public static float[] Render(GameSound sound) => sound switch
    {
        GameSound.YourTurn => Notes((0, 67), (0.1, 72)),                                   // G up to C: "your go"
        GameSound.Trade => Notes((0, 72), (0.07, 76)),                                     // C E, quick
        GameSound.Win => Notes((0, 60), (0.12, 64), (0.24, 67), (0.36, 72), (0.36, 64)),   // a rising run to a chord
        GameSound.Lose => Notes((0, 67), (0.18, 63), (0.36, 60)),                          // G E♭ C, falling
        GameSound.Dice => Dice(),
        GameSound.Road => Build(1.25, taps: 1),                                            // lighter
        GameSound.Settlement => Build(1.0, taps: 1),
        GameSound.City => Build(0.8, taps: 2),                                             // lower, twice
        _ => Array.Empty<float>(),
    };

    private static double Frequency(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>Soft marimba notes (start in seconds, MIDI pitch), mixed, filtered and normalized.</summary>
    private static float[] Notes(params (double At, int Midi)[] notes)
    {
        double length = notes.Max(n => n.At) + 0.5;
        var mix = new float[(int)(length * SampleRate)];
        foreach (var (at, midi) in notes)
            Add(mix, Marimba(Frequency(midi), 0.45), (int)(at * SampleRate), 1);
        return Normalize(Trim(LowPass(mix, 2600)), 0.75f);
    }

    /// <summary>A soft marimba bar: the fundamental with a gentle attack and even decay, and a faint bar overtone.</summary>
    private static float[] Marimba(double f, double seconds)
    {
        var s = new float[(int)(seconds * SampleRate)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / SampleRate;
            double v = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 11) + 0.12 * Math.Sin(2 * Math.PI * f * 3.93 * t) * Math.Exp(-t * 40);
            s[i] = (float)(v * Math.Min(1, t / 0.004));
        }
        return s;
    }

    /// <summary>Placing a piece: a soft thud (two for a city), pitched by <paramref name="scale"/>.</summary>
    private static float[] Build(double scale, int taps)
    {
        var mix = new float[(int)(0.5 * SampleRate)];
        for (int tap = 0; tap < taps; tap++)
            Add(mix, Thud(95 * scale, 41 + tap), (int)(tap * 0.11 * SampleRate), tap == 0 ? 1 : 0.85f);
        return Normalize(Trim(LowPass(mix, 1600)), 0.8f);
    }

    /// <summary>A soft thud: a low sine that drops in pitch, with a little dull noise.</summary>
    private static float[] Thud(double f, int seed)
    {
        var rng = new Random(seed);
        var s = new float[(int)(0.2 * SampleRate)];
        double phase = 0, filtered = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / SampleRate;
            phase += 2 * Math.PI * f * (1 + 0.8 * Math.Exp(-t * 40)) / SampleRate;
            filtered += 0.08 * ((rng.NextDouble() * 2 - 1) - filtered);
            s[i] = (float)((Math.Sin(phase) * Math.Exp(-t * 22) + filtered * Math.Exp(-t * 90) * 3) * Math.Min(1, t / 0.003));
        }
        return s;
    }

    /// <summary>Felt dice: a few muffled taps that settle, like dice coming to rest on a felt board.</summary>
    private static float[] Dice()
    {
        var rng = new Random(70);
        var mix = new float[(int)(0.5 * SampleRate)];
        double at = 0;
        for (int hit = 0; hit < 5; hit++)
        {
            Add(mix, Tap(420 + rng.NextDouble() * 120, 50 + hit), (int)(at * SampleRate), (float)Math.Pow(0.72, hit));
            at += 0.045 + hit * 0.02 + rng.NextDouble() * 0.015;
        }
        return Normalize(Trim(LowPass(mix, 1800)), 0.7f);
    }

    /// <summary>One muffled tap: a quickly damped wooden body and a dull contact noise.</summary>
    private static float[] Tap(double f, int seed)
    {
        var rng = new Random(seed);
        var s = new float[(int)(0.06 * SampleRate)];
        double filtered = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / SampleRate;
            double body = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 90) + 0.5 * Math.Sin(2 * Math.PI * f * 2.43 * t) * Math.Exp(-t * 135)
                          + 0.18 * Math.Sin(2 * Math.PI * f * 4.1 * t) * Math.Exp(-t * 216);
            filtered += 0.15 * ((rng.NextDouble() * 2 - 1) - filtered);
            s[i] = (float)((body + filtered * Math.Exp(-t * 180) * 4.8) * Math.Min(1, t / 0.0015));
        }
        return s;
    }

    /// <summary>A gentle one-pole low-pass: takes the edge off, keeps the body.</summary>
    private static float[] LowPass(float[] s, double cutoff)
    {
        double a = 1 - Math.Exp(-2 * Math.PI * cutoff / SampleRate), y = 0;
        for (int i = 0; i < s.Length; i++)
            s[i] = (float)(y += a * (s[i] - y));
        return s;
    }

    private static void Add(float[] into, float[] sound, int offset, float level)
    {
        for (int i = 0; i < sound.Length && offset + i < into.Length; i++)
            into[offset + i] += sound[i] * level;
    }

    /// <summary>Scales to the given peak, with a short fade-out so nothing ends in a click.</summary>
    private static float[] Normalize(float[] s, float peak)
    {
        float max = 0;
        foreach (float v in s)
            max = Math.Max(max, Math.Abs(v));
        float gain = max > 0 ? peak / max : 0;
        int fade = Math.Min(s.Length, SampleRate / 50);
        for (int i = 0; i < s.Length; i++)
        {
            float edge = i >= s.Length - fade ? (float)(s.Length - i) / fade : 1;
            s[i] *= gain * edge;
        }
        return s;
    }

    /// <summary>Cuts trailing near-silence.</summary>
    private static float[] Trim(float[] s)
    {
        int end = s.Length;
        while (end > 1 && Math.Abs(s[end - 1]) < 0.0005f)
            end--;
        return s[..end];
    }
}
