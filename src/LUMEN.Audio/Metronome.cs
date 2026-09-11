namespace Lumen.Audio;

/// <summary>
/// A generated click track for calibration (spec §66).
///
/// Synthesised rather than shipped as a file, for the same reason the practice song is:
/// nothing copyrighted travels with the game, and the click is defined by the code that
/// the calibration screen measures against — the first sample of each click is exactly
/// the beat, with no encoder delay or file-format padding in between.
/// </summary>
public static class Metronome
{
    private const int Rate = AudioClip.SampleRate;

    /// <summary>A short, bright click: easy to place in time and not unpleasant repeated.</summary>
    private const double ClickSeconds = 0.035;
    private const double ClickHz = 1800;
    private const double AccentHz = 2400;

    /// <summary>
    /// <paramref name="beats"/> clicks at <paramref name="bpm"/>, with every fourth one
    /// accented so the player can hear where the bar starts.
    /// </summary>
    public static AudioClip Build(double bpm, int beats, int beatsPerBar = 4)
    {
        if (bpm <= 0)
        {
            bpm = 100;
        }

        beats = Math.Max(1, beats);

        double beatSeconds = 60.0 / bpm;
        int totalFrames = (int)((beats * beatSeconds + 0.5) * Rate);
        var mono = new float[totalFrames];

        for (int beat = 0; beat < beats; beat++)
        {
            bool accent = beatsPerBar > 0 && beat % beatsPerBar == 0;
            AddClick(mono, beat * beatSeconds, accent ? AccentHz : ClickHz, accent ? 0.85 : 0.6);
        }

        return new AudioClip(ToStereo(mono));
    }

    private static void AddClick(float[] buffer, double atSeconds, double frequency, double amplitude)
    {
        int start = (int)(atSeconds * Rate);
        int length = (int)(ClickSeconds * Rate);

        for (int i = 0; i < length; i++)
        {
            int index = start + i;
            if (index < 0 || index >= buffer.Length)
            {
                break;
            }

            double t = (double)i / Rate;

            // A fast exponential decay: the click's attack is the part being timed, so it
            // has to be the loudest sample and everything after it has to get out of the way.
            double envelope = Math.Exp(-38 * t);
            buffer[index] += (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * amplitude);
        }
    }

    private static float[] ToStereo(float[] mono)
    {
        var stereo = new float[mono.Length * AudioClip.Channels];

        for (int i = 0; i < mono.Length; i++)
        {
            float sample = Math.Clamp(mono[i], -1f, 1f);
            stereo[i * 2] = sample;
            stereo[i * 2 + 1] = sample;
        }

        return stereo;
    }
}
