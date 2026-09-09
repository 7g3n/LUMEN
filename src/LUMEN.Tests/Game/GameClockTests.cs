using System.Threading;
using FluentAssertions;
using Lumen.Game.Engine;
using Xunit;

namespace Lumen.Tests.Game;

public class GameClockTests
{
    [Fact]
    public void Advance_moves_time_forward_and_counts_frames()
    {
        var clock = new GameClock();

        clock.Advance();
        Thread.Sleep(10);
        clock.Advance();

        clock.FrameCount.Should().Be(2);
        clock.TotalSeconds.Should().BeGreaterThan(0);
        clock.DeltaSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Fps_estimate_is_populated_after_a_few_frames()
    {
        var clock = new GameClock();

        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(5);
            clock.Advance();
        }

        clock.Fps.Should().BeGreaterThan(0);
        clock.Fps.Should().BeLessThan(10_000);
    }
}
