using System.Linq;
using FluentAssertions;
using Lumen.Game.Engine;
using Xunit;

namespace Lumen.Tests.Game;

public class FrameProfilerTests
{
    private static FrameProfiler Paced(double budgetMs = 5.63)
    {
        var profiler = new FrameProfiler(budgetMs);
        profiler.Reset();
        return profiler;
    }

    /// <summary>A frame that was cheap to produce and cheap to present.</summary>
    private static void Good(FrameProfiler profiler, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            profiler.Record(totalMs: 4.16, workMs: 0.1, presentMs: 0.2, waitMs: 3.8);
        }
    }

    [Fact]
    public void Budget_follows_the_refresh_rate_it_is_pacing_to()
    {
        FrameProfiler.BudgetForFps(240).Should().BeApproximately(5.63, 0.01);
        FrameProfiler.BudgetForFps(60).Should().BeApproximately(22.5, 0.01);

        // Uncapped has no budget to be over.
        FrameProfiler.BudgetForFps(0).Should().Be(0);
    }

    [Fact]
    public void A_frame_that_beats_the_budget_is_not_counted_against_it()
    {
        FrameProfiler profiler = Paced();

        Good(profiler, 100);

        profiler.SampleCount.Should().Be(100);
        profiler.OverBudget.Should().Be(0);
        profiler.WorkOverBudget.Should().Be(0);
        profiler.Stutters.Should().BeEmpty();
        profiler.StutterSummary().Should().Be("none");
    }

    /// <summary>
    /// The distinction the whole class exists for: a frame the player saw as a stutter, but
    /// which the game spent no time on, is not the game's to answer for.
    /// </summary>
    [Fact]
    public void A_stall_in_the_driver_counts_against_the_frame_but_not_against_the_game()
    {
        FrameProfiler profiler = Paced();

        Good(profiler, 10);
        profiler.Record(totalMs: 62.8, workMs: 0.1, presentMs: 58.8, waitMs: 3.9);
        Good(profiler, 10);

        profiler.OverBudget.Should().Be(1);
        profiler.WorkOverBudget.Should().Be(0);
        profiler.Stutters.Single().Cause.Should().Be("present");
    }

    [Fact]
    public void A_frame_the_game_spent_too_long_on_is_the_games_own()
    {
        FrameProfiler profiler = Paced();

        Good(profiler, 5);
        profiler.Record(totalMs: 8.0, workMs: 7.2, presentMs: 0.6, waitMs: 0.1);

        profiler.OverBudget.Should().Be(1);
        profiler.WorkOverBudget.Should().Be(1);
        profiler.Stutters.Single().Cause.Should().Be("game");
    }

    [Fact]
    public void A_frame_spent_waiting_is_blamed_on_the_wait()
    {
        FrameProfiler profiler = Paced();

        profiler.Record(totalMs: 8.3, workMs: 0.1, presentMs: 0.2, waitMs: 8.0);

        profiler.Stutters.Single().Cause.Should().Be("wait");
    }

    [Fact]
    public void The_worst_frame_is_reported_with_where_in_the_song_it_happened()
    {
        FrameProfiler profiler = Paced();

        Good(profiler, 500);
        profiler.Record(totalMs: 62.8, workMs: 0.1, presentMs: 58.8, waitMs: 3.9);
        Good(profiler, 500);

        profiler.WorstMs.Should().BeApproximately(62.8, 0.001);
        profiler.WorstAtSample.Should().Be(500);
    }

    [Fact]
    public void Worst_frames_are_kept_worst_first_and_bounded()
    {
        FrameProfiler profiler = Paced();

        // More stutters than the list can hold, arriving smallest first.
        for (int i = 1; i <= FrameProfiler.StutterCapacity + 6; i++)
        {
            profiler.Record(totalMs: 10 + i, workMs: 0.1, presentMs: 9 + i, waitMs: 0.1);
        }

        profiler.Stutters.Should().HaveCount(FrameProfiler.StutterCapacity);
        profiler.Stutters.Should().BeInDescendingOrder(s => s.TotalMs);

        // The very worst must survive however many followed it.
        profiler.Stutters[0].TotalMs.Should().BeApproximately(
            10 + FrameProfiler.StutterCapacity + 6, 0.001);
    }

    [Fact]
    public void Worst_frames_survive_arriving_largest_first()
    {
        FrameProfiler profiler = Paced();

        for (int i = FrameProfiler.StutterCapacity + 6; i >= 1; i--)
        {
            profiler.Record(totalMs: 10 + i, workMs: 0.1, presentMs: 9 + i, waitMs: 0.1);
        }

        profiler.Stutters.Should().HaveCount(FrameProfiler.StutterCapacity);
        profiler.Stutters.Should().BeInDescendingOrder(s => s.TotalMs);
        profiler.Stutters[0].TotalMs.Should().BeApproximately(
            10 + FrameProfiler.StutterCapacity + 6, 0.001);
    }

    /// <summary>
    /// The percentile is there to describe the tail, which an average cannot: a run where
    /// one frame in fifty is terrible still averages well, and the 99th percentile is what
    /// says so.
    /// </summary>
    [Fact]
    public void Percentile_describes_the_tail_rather_than_the_average()
    {
        FrameProfiler profiler = Paced();

        for (int i = 0; i < 100; i++)
        {
            // Two bad frames in every hundred, so the worst one per cent is genuinely bad.
            profiler.Record(i % 50 == 0 ? 60.0 : 4.0, 0.1, 0.2, 3.7);
        }

        profiler.AverageMs.Should().BeLessThan(7);
        profiler.PercentileMs(50).Should().BeApproximately(4.0, 0.001);
        profiler.PercentileMs(99).Should().BeApproximately(60.0, 0.001);
    }

    /// <summary>
    /// And the flip side, stated so it cannot be mistaken for a bug later: a single bad
    /// frame in a hundred is below the 99th percentile by definition. That is what
    /// <see cref="FrameProfiler.WorstMs"/> is for.
    /// </summary>
    [Fact]
    public void A_single_frame_in_a_hundred_shows_up_as_the_worst_not_as_the_percentile()
    {
        FrameProfiler profiler = Paced();

        for (int i = 0; i < 99; i++)
        {
            profiler.Record(4.0, 0.1, 0.2, 3.7);
        }

        profiler.Record(60.0, 0.1, 56.0, 3.9);

        profiler.PercentileMs(99).Should().BeApproximately(4.0, 0.001);
        profiler.WorstMs.Should().BeApproximately(60.0, 0.001);
        profiler.OverBudget.Should().Be(1);
    }

    [Fact]
    public void Reset_starts_a_new_span()
    {
        FrameProfiler profiler = Paced();

        profiler.Record(62.8, 0.1, 58.8, 3.9);
        profiler.Reset();
        Good(profiler, 10);

        profiler.SampleCount.Should().Be(10);
        profiler.OverBudget.Should().Be(0);
        profiler.WorstMs.Should().BeApproximately(4.16, 0.001);
        profiler.Stutters.Should().BeEmpty();
    }

    [Fact]
    public void A_nonsense_frame_time_is_ignored_rather_than_poisoning_the_numbers()
    {
        FrameProfiler profiler = Paced();

        Good(profiler, 5);
        profiler.Record(double.NaN, 0.1, 0.2, 3.8);
        profiler.Record(-1, 0.1, 0.2, 3.8);

        profiler.SampleCount.Should().Be(5);
        profiler.AverageMs.Should().BeApproximately(4.16, 0.001);
    }

    [Fact]
    public void With_no_budget_nothing_is_over_it()
    {
        var profiler = new FrameProfiler();
        profiler.Reset();

        profiler.Record(500, 400, 90, 10);

        profiler.OverBudget.Should().Be(0);
        profiler.WorkOverBudget.Should().Be(0);
        profiler.WorstMs.Should().Be(500);
    }

    [Fact]
    public void An_empty_span_reports_zeroes_rather_than_dividing_by_none()
    {
        FrameProfiler profiler = Paced();

        profiler.AverageMs.Should().Be(0);
        profiler.AverageWorkMs.Should().Be(0);
        profiler.OverBudgetPercent.Should().Be(0);
        profiler.AllocatedBytesPerFrame.Should().Be(0);
        profiler.PercentileMs(99).Should().Be(0);
    }

    /// <summary>
    /// Three minutes at 240fps is about 43,000 frames, and the ring has to cover a whole
    /// song for a percentile to mean what it says.
    /// </summary>
    [Fact]
    public void The_ring_covers_a_three_minute_song_at_240fps()
    {
        FrameProfiler.Capacity.Should().BeGreaterThan((int)(180 * 240));
    }

    [Fact]
    public void The_summary_names_the_budget_and_what_went_over_it()
    {
        FrameProfiler profiler = Paced();

        Good(profiler, 10);
        profiler.Record(62.8, 0.1, 58.8, 3.9);

        string summary = profiler.Summary();

        summary.Should().Contain("11 frames");
        summary.Should().Contain("over budget");
        summary.Should().Contain("the game's own 0");
        profiler.StutterSummary().Should().Contain("present");
    }
}
