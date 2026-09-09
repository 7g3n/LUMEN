using Lumen.Core.Diagnostics;

namespace Lumen.Audio;

/// <summary>
/// Entry point for audio. Decodes a file and hands back a track; if the device can't be
/// opened, returns a silent <see cref="VirtualAudioTrack"/> so the game keeps working.
/// </summary>
public sealed class AudioEngine
{
    /// <summary>Decodes <paramref name="path"/> and returns a ready-to-play track.</summary>
    public IAudioTrack LoadTrack(string path, float volume = 1f)
    {
        AudioClip clip;
        try
        {
            clip = AudioClipLoader.Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"could not decode audio '{path}' — running silent", ex);
            return new VirtualAudioTrack(0) { Volume = volume };
        }

        try
        {
            return new WasapiAudioTrack(clip) { Volume = volume };
        }
        catch (Exception ex)
        {
            Log.Warn("no audio output device — running silent", ex);
            return new VirtualAudioTrack(clip.DurationSeconds) { Volume = volume };
        }
    }

    /// <summary>Decode-only, for waveform analysis / the editor (Phase 6).</summary>
    public AudioClip Decode(string path) => AudioClipLoader.Load(path);
}
