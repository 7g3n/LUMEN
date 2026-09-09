using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumen.Game.Engine;

/// <summary>
/// Caps the frame rate to a target without handing control to MonoGame's fixed
/// timestep (which we keep off for input/audio latency, spec §68). Uses a coarse
/// sleep for the bulk of the wait and a short spin for the final approach so the
/// pacing stays tight on 120–240 Hz displays.
/// </summary>
public sealed class FrameLimiter
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _nextFrameSeconds;

    /// <summary>Target frames per second. 0 means uncapped.</summary>
    public int TargetFps { get; set; }

    /// <summary>How much of the remaining wait to spend spinning rather than sleeping.</summary>
    public double SpinWindowSeconds { get; set; } = 0.0016;

    public FrameLimiter(int targetFps) => TargetFps = targetFps;

    /// <summary>Call once per frame, after Draw/Present.</summary>
    public void Tick()
    {
        if (TargetFps <= 0)
        {
            return;
        }

        double frame = 1.0 / TargetFps;
        double now = _stopwatch.Elapsed.TotalSeconds;

        if (_nextFrameSeconds < now - frame)
        {
            // Fell far behind (breakpoint, stall); resync instead of racing to catch up.
            _nextFrameSeconds = now + frame;
            return;
        }

        _nextFrameSeconds += frame;

        while (true)
        {
            double remaining = _nextFrameSeconds - _stopwatch.Elapsed.TotalSeconds;
            if (remaining <= 0)
            {
                break;
            }

            if (remaining > SpinWindowSeconds)
            {
                Sleep1Ms();
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }

    /// <summary>Reset the schedule, e.g. after a long load.</summary>
    public void Resync() => _nextFrameSeconds = _stopwatch.Elapsed.TotalSeconds;

    private static void Sleep1Ms()
    {
        // Thread.Sleep(1) can overshoot to ~15 ms on Windows unless the timer
        // resolution is raised; TimeBeginPeriod(1) is set once at startup.
        Thread.Sleep(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint ms);

    private static bool _highResolutionTimerRequested;

    /// <summary>Raise the OS timer resolution to 1 ms for the process. Call once at startup.</summary>
    public static void RequestHighResolutionTimer()
    {
        if (!_highResolutionTimerRequested)
        {
            TimeBeginPeriod(1);
            _highResolutionTimerRequested = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => TimeEndPeriod(1);
        }
    }
}
