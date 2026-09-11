using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Charts;
using Lumen.Core.Editing;
using Xunit;

namespace Lumen.Tests.Core.Editing;

public class BeatGridTests
{
    // 120 BPM: one beat is exactly 500ms, which keeps the expected values readable.
    private static TempoMap Steady(double bpm = 120) => new(new[] { new BpmPoint(0, bpm) });

    [Fact]
    public void The_offered_divisions_include_the_triplet_family()
    {
        BeatGrid.Divisions.Should().Contain(new[] { 1, 2, 4, 8, 16, 32 });
        BeatGrid.Divisions.Should().Contain(new[] { 3, 6, 12, 24 });
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(12, true)]
    [InlineData(4, false)]
    [InlineData(16, false)]
    public void Triplet_divisions_are_recognised(int division, bool triplet)
    {
        BeatGrid.IsTriplet(division).Should().Be(triplet);
    }

    [Fact]
    public void Divisions_are_labelled_the_way_the_spec_writes_them()
    {
        BeatGrid.Label(4).Should().Be("1/4");
        BeatGrid.Label(12).Should().Be("1/12");
    }

    // --- snapping ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 0)]
    [InlineData(260, 500)]
    [InlineData(499, 500)]
    [InlineData(760, 1000)]
    public void Quarter_notes_snap_to_the_nearest_beat(double input, double expected)
    {
        BeatGrid.Snap(input, Steady(), division: 1).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void Eighths_snap_to_half_beats()
    {
        BeatGrid.Snap(230, Steady(), division: 2).Should().BeApproximately(250, 1e-6);
        BeatGrid.Snap(260, Steady(), division: 2).Should().BeApproximately(250, 1e-6);
    }

    [Fact]
    public void Triplets_snap_to_thirds_of_a_beat()
    {
        // A beat is 500ms, so 1/3 lands on 166.67 and 333.33.
        BeatGrid.Snap(160, Steady(), division: 3).Should().BeApproximately(500.0 / 3, 1e-6);
        BeatGrid.Snap(340, Steady(), division: 3).Should().BeApproximately(1000.0 / 3, 1e-6);
    }

    [Fact]
    public void Snapping_never_produces_a_negative_time()
    {
        BeatGrid.Snap(-400, Steady(), division: 4).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Snapping_is_idempotent()
    {
        double once = BeatGrid.Snap(1234, Steady(), division: 4);
        BeatGrid.Snap(once, Steady(), division: 4).Should().BeApproximately(once, 1e-6);
    }

    [Fact]
    public void A_chart_offset_moves_the_whole_grid_with_it()
    {
        // With a 120ms offset the grid sits on 120, 620, 1120 …
        BeatGrid.Snap(600, Steady(), division: 1, offsetMs: 120)
            .Should().BeApproximately(620, 1e-6);
    }

    [Fact]
    public void Snapping_follows_a_tempo_change()
    {
        // 120 BPM for two beats (1000ms), then 240 BPM: beats become 250ms long.
        var tempo = new TempoMap(new[] { new BpmPoint(0, 120), new BpmPoint(1000, 240) });

        BeatGrid.Snap(1260, tempo, division: 1).Should().BeApproximately(1250, 1e-6);
        BeatGrid.Snap(1400, tempo, division: 1).Should().BeApproximately(1500, 1e-6);
    }

    // --- stepping ---

    [Fact]
    public void Stepping_moves_exactly_one_grid_line()
    {
        BeatGrid.Step(0, Steady(), division: 4, direction: 1).Should().BeApproximately(125, 1e-6);
        BeatGrid.Step(125, Steady(), division: 4, direction: 1).Should().BeApproximately(250, 1e-6);
        BeatGrid.Step(250, Steady(), division: 4, direction: -1).Should().BeApproximately(125, 1e-6);
    }

    [Fact]
    public void Stepping_back_from_zero_stays_at_zero()
    {
        BeatGrid.Step(0, Steady(), division: 4, direction: -1).Should().Be(0);
    }

    [Fact]
    public void Stepping_from_an_unsnapped_time_lands_on_the_grid()
    {
        BeatGrid.Step(130, Steady(), division: 4, direction: 1).Should().BeApproximately(250, 1e-6);
    }

    // --- lines ---

    [Fact]
    public void Lines_cover_the_requested_span_only()
    {
        IReadOnlyList<GridLine> lines = BeatGrid.Lines(0, 2000, Steady(), division: 1);

        lines.Should().NotBeEmpty();
        lines.Select(l => l.TimeMs).Should().OnlyContain(t => t >= 0 && t <= 2000);
        lines.Select(l => l.TimeMs).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Every_fourth_beat_is_a_bar_line_in_four_four()
    {
        IReadOnlyList<GridLine> lines = BeatGrid.Lines(0, 4000, Steady(), division: 1, beatsPerBar: 4);

        lines.Where(l => l.Kind == GridLineKind.Bar).Select(l => l.TimeMs)
            .Should().Equal(0, 2000, 4000);
    }

    [Fact]
    public void Subdivisions_are_marked_apart_from_beats()
    {
        IReadOnlyList<GridLine> lines = BeatGrid.Lines(0, 1000, Steady(), division: 4);

        lines.Count(l => l.Kind == GridLineKind.Subdivision).Should().BeGreaterThan(0);
        lines.Where(l => l.Kind != GridLineKind.Subdivision).Select(l => l.TimeMs)
            .Should().Contain(new double[] { 0, 500, 1000 });
    }

    [Fact]
    public void A_backwards_or_empty_span_produces_nothing()
    {
        BeatGrid.Lines(2000, 1000, Steady(), 4).Should().BeEmpty();
        BeatGrid.Lines(1000, 1000, Steady(), 4).Should().BeEmpty();
    }

    [Fact]
    public void A_huge_span_is_bounded_rather_than_hanging_the_editor()
    {
        IReadOnlyList<GridLine> lines =
            BeatGrid.Lines(0, 60 * 60 * 1000, Steady(), division: 32, maxLines: 500);

        lines.Count.Should().BeLessThanOrEqualTo(500);
    }

    // --- readout ---

    [Fact]
    public void Bar_and_beat_are_one_based()
    {
        BeatGrid.BarBeatAt(0, Steady()).Should().Be((1, 1));
        BeatGrid.BarBeatAt(500, Steady()).Should().Be((1, 2));
        BeatGrid.BarBeatAt(2000, Steady()).Should().Be((2, 1));
        BeatGrid.BarBeatAt(2500, Steady()).Should().Be((2, 2));
    }

    [Fact]
    public void A_time_before_zero_reads_as_the_first_beat()
    {
        BeatGrid.BarBeatAt(-500, Steady()).Should().Be((1, 1));
    }
}
