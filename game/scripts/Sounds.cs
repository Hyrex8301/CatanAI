using System;
using System.Collections.Generic;
using Catan.UI;
using Godot;

/// <summary>
/// Plays the game's sounds, made in code (<see cref="SoundSynth"/>): only key moments, soft, at the volume in Settings.
/// The same sound asked for again within a moment plays once. Works from any screen: players attach to the scene tree's root and free themselves when done.
/// </summary>
public static class Sounds
{
    /// <summary>Everything plays at this share of the volume setting: soft.</summary>
    private const double Softness = 0.6;

    private const double RepeatGapSeconds = 0.07;

    private static readonly Dictionary<GameSound, AudioStreamWav> Made = new();
    private static readonly Dictionary<GameSound, double> LastPlayed = new();

    public static void Play(GameSound sound)
    {
        double volume = GameSession.Options.Volume * Softness;
        if (volume <= 0.001 || Engine.GetMainLoop() is not SceneTree tree)
            return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (LastPlayed.TryGetValue(sound, out double last) && now - last < RepeatGapSeconds)
            return;
        LastPlayed[sound] = now;

        var player = new AudioStreamPlayer
        {
            Stream = Stream(sound),
            VolumeDb = Mathf.LinearToDb((float)volume),
        };
        tree.Root.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    /// <summary>The sound as 16-bit mono PCM, made once and kept.</summary>
    private static AudioStreamWav Stream(GameSound sound)
    {
        if (Made.TryGetValue(sound, out var made))
            return made;
        var samples = SoundSynth.Render(sound);
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Clamp((int)(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)v;
            bytes[2 * i + 1] = (byte)(v >> 8);
        }
        made = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = SoundSynth.SampleRate, Stereo = false, Data = bytes };
        Made[sound] = made;
        return made;
    }
}

/// <summary>Which sound a game event makes, heard from <c>viewer</c>'s seat (null: most events are silent).</summary>
public static class GameSounds
{
    public static GameSound? For(Catan.Core.GameEvent e, int viewer, int currentPlayer) => e switch
    {
        Catan.Core.DiceRolled => GameSound.Dice,
        Catan.Core.Built { Piece: Catan.Core.PieceType.Road } => GameSound.Road,
        Catan.Core.Built { Piece: Catan.Core.PieceType.Settlement } => GameSound.Settlement,
        Catan.Core.Built => GameSound.City,
        Catan.Core.TradeDone => GameSound.Trade,
        Catan.Core.TurnEnded when currentPlayer == viewer => GameSound.YourTurn,
        Catan.Core.GameEnded g => g.Winner == viewer ? GameSound.Win : GameSound.Lose,
        _ => null,
    };
}
