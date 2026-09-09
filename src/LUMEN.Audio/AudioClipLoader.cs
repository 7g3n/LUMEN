using Lumen.Core.Diagnostics;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lumen.Audio;

/// <summary>
/// Decodes an audio file to an in-memory <see cref="AudioClip"/> (interleaved stereo
/// float at 44.1 kHz). WAV / MP3 / AIFF / FLAC go through NAudio; OGG through NVorbis.
/// </summary>
public static class AudioClipLoader
{
    public static AudioClip Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Audio file not found.", path);
        }

        using WaveStream reader = OpenReader(path);
        ISampleProvider samples = reader.ToSampleProvider();

        // Force stereo, then resample to the clip rate.
        if (samples.WaveFormat.Channels == 1)
        {
            samples = new MonoToStereoSampleProvider(samples);
        }
        else if (samples.WaveFormat.Channels > 2)
        {
            // Take the first two channels.
            samples = new MultiplexingSampleProvider(new[] { samples }, 2);
        }

        if (samples.WaveFormat.SampleRate != AudioClip.SampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, AudioClip.SampleRate);
        }

        return new AudioClip(ReadAll(samples));
    }

    private static WaveStream OpenReader(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext == ".ogg"
                ? new VorbisWaveReader(path)
                : new AudioFileReader(path);
        }
        catch (Exception ex)
        {
            Log.Error($"failed to open audio: {path}", ex);
            throw;
        }
    }

    private static float[] ReadAll(ISampleProvider provider)
    {
        var buffer = new List<float>(AudioClip.SampleRate * AudioClip.Channels * 8);
        var chunk = new float[AudioClip.SampleRate * AudioClip.Channels]; // ~1s
        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.AddRange(read == chunk.Length ? chunk : chunk[..read]);
        }

        // Ensure an even length (complete stereo frames).
        if (buffer.Count % AudioClip.Channels != 0)
        {
            buffer.Add(0f);
        }

        return buffer.ToArray();
    }
}
