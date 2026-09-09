using System.Threading;
using Lumen.Core.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Lumen.Audio;

/// <summary>
/// Plays an <see cref="AudioClip"/> through WASAPI (shared, event-driven). Position is
/// derived from the number of frames handed to the device minus the output latency, so
/// it tracks what the player actually hears closely enough for the conductor to lock
/// onto (fine tuning is the audio-offset calibration, spec §66).
/// </summary>
public sealed class WasapiAudioTrack : IAudioTrack, ISampleProvider
{
    private readonly AudioClip _clip;
    private readonly WasapiOut _output;
    private readonly double _outputLatencySeconds;

    private long _framePosition; // stereo frames delivered; read/written on two threads
    private float _volume = 1f;
    private bool _playing;
    private bool _disposed;

    public WasapiAudioTrack(AudioClip clip, int requestedLatencyMs = 60)
    {
        _clip = clip;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioClip.SampleRate, AudioClip.Channels);

        _output = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, requestedLatencyMs);
        _output.Init(this);
        _outputLatencySeconds = requestedLatencyMs / 1000.0;
        _output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                Log.Error("audio playback stopped with error", e.Exception);
            }
        };
    }

    public WaveFormat WaveFormat { get; }

    public bool HasOutput => true;

    public double DurationSeconds => _clip.DurationSeconds;

    public bool IsPlaying => _playing;

    public double PositionSeconds
    {
        get
        {
            double delivered = (double)Interlocked.Read(ref _framePosition) / AudioClip.SampleRate;
            double heard = _playing ? delivered - _outputLatencySeconds : delivered;
            return Math.Clamp(heard, 0, DurationSeconds);
        }
    }

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public void Play()
    {
        if (_disposed) return;
        _playing = true;
        _output.Play();
    }

    public void Pause()
    {
        _playing = false;
        _output.Pause();
    }

    public void Stop()
    {
        _playing = false;
        _output.Pause();
        Interlocked.Exchange(ref _framePosition, 0);
    }

    public void Seek(double seconds)
    {
        long frame = (long)Math.Clamp(seconds * AudioClip.SampleRate, 0, _clip.FrameCount);
        Interlocked.Exchange(ref _framePosition, frame);
    }

    // --- ISampleProvider: pulled on the WASAPI render thread ---
    public int Read(float[] buffer, int offset, int count)
    {
        long frame = Interlocked.Read(ref _framePosition);
        int startSample = (int)(frame * AudioClip.Channels);
        int available = _clip.Samples.Length - startSample;
        if (available <= 0)
        {
            _playing = false;
            return 0;
        }

        int n = Math.Min(count, available);
        float v = _volume;
        for (int i = 0; i < n; i++)
        {
            buffer[offset + i] = _clip.Samples[startSample + i] * v;
        }

        Interlocked.Add(ref _framePosition, n / AudioClip.Channels);
        return n;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _output.Dispose(); } catch { /* ignore */ }
    }
}
