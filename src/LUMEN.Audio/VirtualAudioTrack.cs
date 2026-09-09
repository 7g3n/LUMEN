using System.Diagnostics;

namespace Lumen.Audio;

/// <summary>
/// Silent fallback used when no audio device is available. Keeps a real-time clock so
/// gameplay stays fully playable without sound (spec §96 — degrade, don't crash).
/// </summary>
public sealed class VirtualAudioTrack : IAudioTrack
{
    private readonly Stopwatch _stopwatch = new();
    private double _baseSeconds;

    public VirtualAudioTrack(double durationSeconds) => DurationSeconds = durationSeconds;

    public double DurationSeconds { get; }

    public bool HasOutput => false;

    public bool IsPlaying => _stopwatch.IsRunning;

    public float Volume { get; set; } = 1f;

    public double PositionSeconds =>
        Math.Clamp(_baseSeconds + _stopwatch.Elapsed.TotalSeconds, 0, DurationSeconds);

    public void Play() => _stopwatch.Start();

    public void Pause()
    {
        _baseSeconds = PositionSeconds;
        _stopwatch.Reset();
    }

    public void Stop()
    {
        _baseSeconds = 0;
        _stopwatch.Reset();
    }

    public void Seek(double seconds)
    {
        _baseSeconds = Math.Clamp(seconds, 0, DurationSeconds);
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Restart();
        }
        else
        {
            _stopwatch.Reset();
        }
    }

    public void Dispose() { }
}
