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
}
