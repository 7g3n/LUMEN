using System.Diagnostics;
using Lumen.Audio;
using Lumen.Core.Charts;

namespace Lumen.Game.Engine;

/// <summary>
/// The authoritative song clock for gameplay (spec §68). Takes the audio track's
/// position as ground truth and extrapolates between its (chunky) updates with a
/// stopwatch so the value is smooth and monotonic. The audio offset is folded in here;
/// the input offset is applied to input events by the caller.
/// </summary>
public sealed class Conductor
{
    private readonly IAudioTrack _track;
    private readonly Stopwatch _wall = Stopwatch.StartNew();

    private double _anchorTrackSeconds;
    private double _anchorWallSeconds;
    private double _lastRawSeconds = -1;
    private double _lastReturnedMs;

    public Conductor(IAudioTrack track, TempoMap tempo, double audioOffsetMs)
    {
        _track = track;
        Tempo = tempo;
        AudioOffsetMs = audioOffsetMs;
    }

    public TempoMap Tempo { get; }

    public double AudioOffsetMs { get; set; }

    public double DurationMs => _track.DurationSeconds * 1000.0;

    public bool IsPlaying => _track.IsPlaying;

    /// <summary>Current song time in milliseconds, including the audio offset.</summary>
    public double SongTimeMs { get; private set; }

    public double BeatAtNow => Tempo.BeatAt(SongTimeMs);

    public void Play() => _track.Play();

    public void Pause() => _track.Pause();

    public void Seek(double songTimeMs)
    {
        _track.Seek(Math.Max(0, (songTimeMs - AudioOffsetMs) / 1000.0));
        _lastRawSeconds = -1;
        _lastReturnedMs = songTimeMs;
        SongTimeMs = songTimeMs;
    }

    /// <summary>Call once per frame before feeding the gameplay session.</summary>
    public void Tick()
    {
        double raw = _track.PositionSeconds;
        double wall = _wall.Elapsed.TotalSeconds;

        if (!_track.IsPlaying)
        {
            SongTimeMs = raw * 1000.0 + AudioOffsetMs;
            _lastReturnedMs = SongTimeMs;
            _lastRawSeconds = raw;
            return;
        }

        // Re-anchor whenever the track reports a fresh position.
        if (raw != _lastRawSeconds)
        {
            _anchorTrackSeconds = raw;
            _anchorWallSeconds = wall;
            _lastRawSeconds = raw;
        }

        double extrapolated = _anchorTrackSeconds + (wall - _anchorWallSeconds);

        // Bound runaway if the audio thread stalls.
        extrapolated = Math.Min(extrapolated, _anchorTrackSeconds + 0.10);

        double ms = extrapolated * 1000.0 + AudioOffsetMs;
        if (ms < _lastReturnedMs)
        {
            ms = _lastReturnedMs; // keep monotonic
        }

        _lastReturnedMs = ms;
        SongTimeMs = ms;
    }
}
