using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Charts;
using Xunit;

namespace Lumen.Tests.Core.Charts;

public class ChartValidatorTests
{
    private static Chart Good(params Note[] notes) => new()
    {
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 128) },
        Notes = notes.Length > 0
            ? notes
            : new[] { Note.Tap(1000, 0), Note.Tap(1500, 1), Note.Hold(2000, 2, 2750) },
        Meta = new ChartMeta
        {
            Title = "First Light",
            Artist = "LUMEN",
            Creator = "7g3",
            DifficultyName = "NORMAL",
            DifficultyLevel = 4,
            AudioFile = "song.wav",
            DurationMs = 60_000,
        },
    };

    private static string[] Codes(Chart chart) =>
        ChartValidator.Validate(chart).Issues.Select(i => i.Code).ToArray();

    [Fact]
    public void A_well_formed_chart_passes_every_check()
    {
        ValidationReport report = ChartValidator.Validate(Good());

        report.CanExport.Should().BeTrue();
        report.Errors.Should().Be(0);
        foreach (ValidationCheck check in Enum.GetValues<ValidationCheck>())
        {
            report.Passed(check).Should().BeTrue(check.ToString());
        }
    }

    // --- errors block export ---

    [Fact]
    public void Missing_audio_is_an_error()
    {
        Chart chart = Good() with { Meta = Good().Meta with { AudioFile = "" } };

        Codes(chart).Should().Contain("MISSING_AUDIO");
        ChartValidator.Validate(chart).CanExport.Should().BeFalse();
    }

    [Fact]
    public void An_empty_title_is_an_error()
    {
        Chart chart = Good() with { Meta = Good().Meta with { Title = "   " } };
        Codes(chart).Should().Contain("MISSING_TITLE");
    }

    [Fact]
    public void No_bpm_is_an_error()
    {
        Chart chart = Good() with { BpmPoints = Array.Empty<BpmPoint>() };

        Codes(chart).Should().Contain("MISSING_BPM");
        ChartValidator.Validate(chart).Passed(ValidationCheck.Bpm).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-120)]
    [InlineData(5000)]
    public void An_impossible_bpm_is_an_error(double bpm)
    {
        Chart chart = Good() with { BpmPoints = new[] { new BpmPoint(0, bpm) } };
        Codes(chart).Should().Contain("INVALID_BPM");
    }

    [Fact]
    public void No_notes_is_an_error()
    {
        Chart chart = Good() with { Notes = Array.Empty<Note>() };
        Codes(chart).Should().Contain("NO_NOTES");
    }

    [Fact]
    public void A_note_outside_the_lanes_is_an_error()
    {
        Codes(Good(Note.Tap(1000, 9))).Should().Contain("NOTE_OUT_OF_LANE");
    }

    [Fact]
    public void A_note_before_the_song_starts_is_an_error()
    {
        Codes(Good(Note.Tap(-500, 0))).Should().Contain("NEGATIVE_NOTE_TIME");
    }

    [Fact]
    public void An_unplayably_short_hold_is_an_error()
    {
        Codes(Good(Note.Hold(1000, 0, 1010))).Should().Contain("HOLD_TOO_SHORT");
    }

    [Fact]
    public void A_hold_that_ends_before_it_starts_is_an_error()
    {
        Codes(Good(Note.Hold(2000, 0, 1000))).Should().Contain("HOLD_ENDS_BEFORE_IT_STARTS");
    }

    [Fact]
    public void A_level_outside_the_scale_is_an_error()
    {
        Chart chart = Good() with { Meta = Good().Meta with { DifficultyLevel = 99 } };
        Codes(chart).Should().Contain("INVALID_DIFFICULTY");
    }

    [Fact]
    public void An_invalid_offset_is_an_error()
    {
        Chart chart = Good() with { ChartOffsetMs = double.NaN };
        Codes(chart).Should().Contain("INVALID_OFFSET");
    }

    // --- warnings never block ---

    [Fact]
    public void Overlapping_notes_are_a_warning_because_they_may_be_deliberate()
    {
        Chart chart = Good(Note.Hold(1000, 0, 3000), Note.Tap(2000, 0));

        Codes(chart).Should().Contain("OVERLAPPING_NOTES");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    [Fact]
    public void Notes_crammed_together_in_one_lane_are_a_warning()
    {
        Chart chart = Good(Note.Tap(1000, 0), Note.Tap(1010, 0));

        Codes(chart).Should().Contain("NOTES_TOO_CLOSE");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    [Fact]
    public void An_empty_artist_or_charter_is_a_warning()
    {
        Chart chart = Good() with { Meta = Good().Meta with { Artist = "", Creator = "" } };

        Codes(chart).Should().Contain("MISSING_ARTIST").And.Contain("MISSING_CREATOR");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    [Fact]
    public void A_note_past_the_end_of_the_audio_is_a_warning()
    {
        Chart chart = Good(Note.Tap(90_000, 0));
        Codes(chart).Should().Contain("NOTE_AFTER_AUDIO");
    }

    [Fact]
    public void A_note_kind_this_version_cannot_play_is_a_warning()
    {
        Chart chart = Good(new Note { Kind = NoteKind.Flick, TimeMs = 1000, Lane = 0 });

        Codes(chart).Should().Contain("UNSUPPORTED_NOTE_TYPE");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    [Fact]
    public void A_first_bpm_point_away_from_zero_is_a_warning()
    {
        Chart chart = Good() with { BpmPoints = new[] { new BpmPoint(500, 128) } };

        Codes(chart).Should().Contain("NO_INITIAL_BPM");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    [Fact]
    public void A_level_far_from_the_analysis_is_a_warning_not_a_veto()
    {
        // The analyser is advice; the author's number is the one that counts (§26).
        Chart chart = Good() with { Meta = Good().Meta with { DifficultyLevel = 19 } };

        Codes(chart).Should().Contain("LEVEL_DISAGREES_WITH_ANALYSIS");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    [Fact]
    public void An_unusually_large_offset_is_a_warning()
    {
        Chart chart = Good() with { ChartOffsetMs = 30_000 };

        Codes(chart).Should().Contain("LARGE_OFFSET");
        ChartValidator.Validate(chart).CanExport.Should().BeTrue();
    }

    // --- report shape ---

    [Fact]
    public void Errors_and_warnings_are_counted_separately()
    {
        Chart chart = Good(Note.Tap(1000, 9), Note.Tap(1005, 1), Note.Tap(1010, 1)) with
        {
            Meta = Good().Meta with { Artist = "" },
        };

        ValidationReport report = ChartValidator.Validate(chart);

        report.Errors.Should().BeGreaterThan(0);
        report.Warnings.Should().BeGreaterThan(0);
        (report.Errors + report.Warnings).Should().Be(report.Issues.Count);
    }

    [Fact]
    public void Issues_can_be_read_per_check()
    {
        Chart chart = Good() with { Meta = Good().Meta with { AudioFile = "" } };

        ChartValidator.Validate(chart).For(ValidationCheck.Audio).Should().ContainSingle();
    }

    [Fact]
    public void An_issue_carries_the_time_to_jump_to_where_there_is_one()
    {
        ValidationReport report = ChartValidator.Validate(Good(Note.Hold(1234, 0, 1240)));

        report.Issues.Should().Contain(i => i.Code == "HOLD_TOO_SHORT" && i.AtMs == 1234);
    }
}
