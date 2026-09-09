using System.Diagnostics;

namespace Lumen.Game.Engine;

/// <summary>
/// The engine's own wall clock, independent of MonoGame's <c>GameTime</c>. Provides a
/// monotonic seconds value and a lightly smoothed FPS estimate for the debug overlay.
/// In Phase 3 the audio conductor reconciles song time against this same clock.
/// </summary>
public sealed class GameClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastSeconds;
    private double _fpsSmoothed;

    /// <summary>Seconds since the clock started. Monotonic, high resolution.</summary>
    public double TotalSeconds { get; private set; }

    /// <summary>Real time elapsed since the previous <see cref="Advance"/>, in seconds.</summary>
    public double DeltaSeconds { get; private set; }

    /// <summary>Exponentially smoothed frames per second.</summary>
    public double Fps => _fpsSmoothed;

    public long FrameCount { get; private set; }

    public void Advance()
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        DeltaSeconds = now - _lastSeconds;
        _lastSeconds = now;
        TotalSeconds = now;
        FrameCount++;

        if (DeltaSeconds > 0)
        {
            double instantaneous = 1.0 / DeltaSeconds;
            _fpsSmoothed = _fpsSmoothed <= 0
                ? instantaneous
                : _fpsSmoothed + (instantaneous - _fpsSmoothed) * 0.1;
        }
    }
}
