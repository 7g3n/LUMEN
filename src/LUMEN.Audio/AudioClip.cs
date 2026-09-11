namespace Lumen.Audio;

/// <summary>
/// Fully-decoded audio held in memory as interleaved stereo float samples at a fixed
/// sample rate. Songs are a few MB decoded, so keeping the whole clip resident buys
/// glitch-free seeking and a simple sample-accurate position.
/// </summary>
public sealed class AudioClip
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    public AudioClip(float[] interleavedStereo)
    {
        Samples = interleavedStereo;
        FrameCount = interleavedStereo.Length / Channels;
    }

    /// <summary>Interleaved L,R,L,R… at <see cref="SampleRate"/>.</summary>
    public float[] Samples { get; }

    /// <summary>Number of stereo frames.</summary>
    public int FrameCount { get; }

    public double DurationSeconds => (double)FrameCount / SampleRate;

    /// <summary>
    /// A copy that plays at <paramref name="speed"/> times the original rate, for the
    /// editor's slow preview (spec §44).
    ///
    /// Resampling changes the pitch as well as the tempo, which is the honest trade here:
    /// proper time-stretching needs a phase vocoder, and for placing notes against a beat
    /// a half-speed track that sounds an octave down is not just acceptable but easier to
    /// hear. Position in the returned clip maps back to song time by multiplying by
    /// <paramref name="speed"/>.
    /// </summary>
    public AudioClip AtSpeed(double speed)
    {
        if (speed <= 0 || Math.Abs(speed - 1.0) < 1e-9 || FrameCount == 0)
        {
            return this;
        }

        int outFrames = (int)Math.Max(1, Math.Round(FrameCount / speed));
        var output = new float[outFrames * Channels];

        for (int f = 0; f < outFrames; f++)
        {
            double source = f * speed;
            int i0 = (int)source;
            int i1 = Math.Min(FrameCount - 1, i0 + 1);
            float t = (float)(source - i0);

            for (int c = 0; c < Channels; c++)
            {
                float a = Samples[i0 * Channels + c];
                float b = Samples[i1 * Channels + c];
                output[f * Channels + c] = a + (b - a) * t;
            }
        }

        return new AudioClip(output);
    }
}
