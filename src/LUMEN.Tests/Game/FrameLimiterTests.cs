using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using Lumen.Game.Engine;
using Xunit;

namespace Lumen.Tests.Game;

public class FrameLimiterTests
{
    [Fact]
    public void Uncapped_never_waits()
    {
        var limiter = new FrameLimiter(0);
        var watch = Stopwatch.StartNew();

        for (int i = 0; i < 50; i++)
        {
            limiter.Tick();
        }

        watch.Elapsed.TotalMilliseconds.Should().BeLessThan(20);
    }

    /// <summary>
    /// The pacing itself, measured rather than asserted about: a capped loop that does no
    /// work should still take about as long as the cap says it will. The tolerance is wide
    /// because this runs on a shared build machine, not a real-time system.
    /// </summary>
    [Fact]
    public void A_capped_loop_paces_to_roughly_the_target()
    {
        FrameLimiter.RequestHighResolutionTimer();

        var limiter = new FrameLimiter(120);
        limiter.Resync();

        // One warm-up frame: the very first Tick establishes the schedule.
        limiter.Tick();

        var watch = Stopwatch.StartNew();
        const int Frames = 60;
        for (int i = 0; i < Frames; i++)
        {
            limiter.Tick();
        }

        double perFrameMs = watch.Elapsed.TotalMilliseconds / Frames;
        perFrameMs.Should().BeInRange(6.0, 12.0); // 120fps is 8.33ms
    }

    /// <summary>
    /// After a stall the limiter resyncs rather than racing to catch up — and resyncs to
    /// now, not to a frame from now. Getting that wrong made the frame two after every
    /// hitch wait out a stall the game had already recovered from, which showed up in the
    /// profiler as a second late frame trailing every real one.
    /// </summary>
    [Fact]
    public void A_stall_costs_one_frame_of_catching_up_and_not_two()
    {
        FrameLimiter.RequestHighResolutionTimer();

        var limiter = new FrameLimiter(120);
        limiter.Resync();
        limiter.Tick();

        // A stall far longer than a frame.
        Thread.Sleep(60);

        // The frame that notices the stall must not wait at all.
        var watch = Stopwatch.StartNew();
        limiter.Tick();
        double resyncMs = watch.Elapsed.TotalMilliseconds;

        // The one after it waits a single frame, not two.
        watch.Restart();
        limiter.Tick();
        double nextMs = watch.Elapsed.TotalMilliseconds;

        resyncMs.Should().BeLessThan(2);
        nextMs.Should().BeInRange(4.0, 13.0); // one frame at 120fps, generously bounded
    }

    [Fact]
    public void Changing_the_target_changes_the_pace()
    {
        FrameLimiter.RequestHighResolutionTimer();

        var limiter = new FrameLimiter(240) { TargetFps = 60 };
        limiter.Resync();
        limiter.Tick();

        var watch = Stopwatch.StartNew();
        limiter.Tick();

        watch.Elapsed.TotalMilliseconds.Should().BeInRange(10.0, 25.0); // 60fps is 16.7ms
    }
}
