namespace Lumen.Audio;

/// <summary>
/// A playable audio track that exposes a position for the conductor to lock onto.
/// The position must advance in real time while playing and stay put while paused.
/// </summary>
public interface IAudioTrack : IDisposable
{
    double PositionSeconds { get; }

    double DurationSeconds { get; }

    bool IsPlaying { get; }

    /// <summary>0..1 linear.</summary>
    float Volume { get; set; }

    /// <summary>True if audio is actually coming out; false for the silent fallback track.</summary>
    bool HasOutput { get; }

    void Play();

    void Pause();

    void Stop();

    void Seek(double seconds);
}
