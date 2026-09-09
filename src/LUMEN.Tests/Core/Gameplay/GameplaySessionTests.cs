using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Xunit;

namespace Lumen.Tests.Core.Gameplay;

public class GameplaySessionTests
{
    private static Chart TapChart(params (double ms, int lane)[] notes) => new Chart
    {
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 120) },
        Notes = notes.Select(n => Note.Tap(n.ms, n.lane)).ToArray(),
    }.Normalized();

    /// <summary>Feeds events in one-frame slices at 60 fps up to <paramref name="endMs"/>.</summary>
    private static void RunFrames(GameplaySession s, IEnumerable<LaneEvent> events, double endMs, double frameMs = 1000.0 / 60)
    {
        var queue = new Queue<LaneEvent>(events.OrderBy(e => e.TimeMs));
        for (double t = 0; t <= endMs; t += frameMs)
        {
            var slice = new List<LaneEvent>();
            while (queue.Count > 0 && queue.Peek().TimeMs <= t)
            {
                slice.Add(queue.Dequeue());
            }

            s.Update(t, slice);
        }

        s.Finish();
    }

    [Fact]
    public void All_perfect_full_combo_scores_the_maximum()
    {
        Chart chart = TapChart((1000, 0), (1500, 1), (2000, 2), (2500, 3), (3000, 0));
        var s = new GameplaySession(chart);

        var events = chart.Notes.Select(n => LaneEvent.Down(n.Lane, n.TimeMs));
        RunFrames(s, events, 4000);

        s.Score.Perfect.Should().Be(5);
        s.Score.Miss.Should().Be(0);
        s.Score.Accuracy.Should().BeApproximately(100, 1e-6);
        s.Score.FullCombo.Should().BeTrue();
        s.Score.AllPerfect.Should().BeTrue();
        s.Score.MaxCombo.Should().Be(5);
        s.Score.Score.Should().Be(BalanceConfig.Default.Score.MaxScore);
    }

    [Fact]
    public void No_input_misses_everything()
    {
        Chart chart = TapChart((1000, 0), (1500, 1), (2000, 2));
        var s = new GameplaySession(chart);

        RunFrames(s, Array.Empty<LaneEvent>(), 3000);

        s.Score.Miss.Should().Be(3);
        s.Score.Accuracy.Should().Be(0);
        s.Score.MaxCombo.Should().Be(0);
        s.Score.Score.Should().Be(0);
        s.AllResolved.Should().BeTrue();
    }

    [Fact]
    public void Result_is_identical_regardless_of_frame_slicing()
    {
        Chart chart = TapChart((1000, 0), (1180, 1), (1220, 2), (2000, 3), (2600, 0));
        var events = new[]
        {
            LaneEvent.Down(0, 1004),   // perfect
            LaneEvent.Down(1, 1210),   // great (err +30)
            LaneEvent.Down(2, 1300),   // good  (err +80)
            LaneEvent.Down(3, 2150),   // bad   (err +150 -> miss actually)
            // note at 2600 left unhit -> miss
        };

        var a = new GameplaySession(chart);
        RunFrames(a, events, 3200, frameMs: 1000.0 / 60);

        var b = new GameplaySession(chart);
        RunFrames(b, events, 3200, frameMs: 1000.0 / 240);

        var c = new GameplaySession(chart);
        RunFrames(c, events, 3200, frameMs: 7.0);

        foreach (var other in new[] { b, c })
        {
            other.Score.Score.Should().Be(a.Score.Score);
            other.Score.Accuracy.Should().BeApproximately(a.Score.Accuracy, 1e-9);
            other.Score.MaxCombo.Should().Be(a.Score.MaxCombo);
            other.Score.Perfect.Should().Be(a.Score.Perfect);
            other.Score.Great.Should().Be(a.Score.Great);
            other.Score.Good.Should().Be(a.Score.Good);
            other.Score.Bad.Should().Be(a.Score.Bad);
            other.Score.Miss.Should().Be(a.Score.Miss);
        }
    }

    [Fact]
    public void Combo_breaks_on_a_miss_then_recovers()
    {
        Chart chart = TapChart((1000, 0), (1500, 1), (2000, 2), (2500, 3));
        var s = new GameplaySession(chart);

        // hit 1 and 2, miss 3, hit 4
        var events = new[]
        {
            LaneEvent.Down(0, 1000),
            LaneEvent.Down(1, 1500),
            LaneEvent.Down(3, 2500),
        };
        RunFrames(s, events, 3000);

        s.Score.Miss.Should().Be(1);
        s.Score.MaxCombo.Should().Be(2);
        s.Score.FullCombo.Should().BeFalse();
    }

    [Fact]
    public void Ghost_tap_far_from_any_note_is_ignored()
    {
        Chart chart = TapChart((2000, 0));
        var s = new GameplaySession(chart);

        RunFrames(s, new[] { LaneEvent.Down(0, 500), LaneEvent.Down(0, 2000) }, 2500);

        s.Score.Perfect.Should().Be(1);
        s.Score.Bad.Should().Be(0);
        s.Score.Miss.Should().Be(0);
    }

    [Fact]
    public void Hold_hit_and_released_on_time_judges_head_and_tail()
    {
        Chart chart = new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 120) },
            Notes = new[] { Note.Hold(1000, 0, 2000) },
        }.Normalized();
        var s = new GameplaySession(chart);

        RunFrames(s, new[] { LaneEvent.Down(0, 1000), LaneEvent.Up(0, 2000) }, 3000);

        s.Score.Perfect.Should().Be(2); // head + tail
        s.Score.FullCombo.Should().BeTrue();
        s.Notes[0].HeadJudgement.Should().Be(Judgement.Perfect);
        s.Notes[0].TailJudgement.Should().Be(Judgement.Perfect);
    }

    [Fact]
    public void Hold_released_far_too_early_loses_the_tail()
    {
        Chart chart = new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 120) },
            Notes = new[] { Note.Hold(1000, 0, 3000) },
        }.Normalized();
        var s = new GameplaySession(chart);

        RunFrames(s, new[] { LaneEvent.Down(0, 1000), LaneEvent.Up(0, 1500) }, 4000);

        s.Notes[0].HeadJudgement.Should().Be(Judgement.Perfect);
        s.Notes[0].TailJudgement.Should().Be(Judgement.Miss);
        s.Score.Miss.Should().Be(1);
    }

    [Fact]
    public void Hold_head_missed_takes_the_tail_with_it()
    {
        Chart chart = new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 120) },
            Notes = new[] { Note.Hold(1000, 0, 2000) },
        }.Normalized();
        var s = new GameplaySession(chart);

        RunFrames(s, Array.Empty<LaneEvent>(), 3000);

        s.Score.Miss.Should().Be(2);
        s.Notes[0].HeadJudgement.Should().Be(Judgement.Miss);
        s.Notes[0].TailJudgement.Should().Be(Judgement.Miss);
    }

    [Fact]
    public void Earliest_pending_note_on_a_lane_is_hit_first()
    {
        Chart chart = TapChart((1000, 0), (1060, 0)); // fast jack on one lane
        var s = new GameplaySession(chart);

        RunFrames(s, new[] { LaneEvent.Down(0, 1005), LaneEvent.Down(0, 1065) }, 1500);

        s.Notes[0].HeadJudgement.Should().Be(Judgement.Perfect); // err +5
        s.Notes[1].HeadJudgement.Should().Be(Judgement.Perfect); // err +5
        s.Score.Perfect.Should().Be(2);
    }
}
