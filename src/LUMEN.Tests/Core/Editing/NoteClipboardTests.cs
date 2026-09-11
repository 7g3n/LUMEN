using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Charts;
using Lumen.Core.Editing;
using Xunit;

namespace Lumen.Tests.Core.Editing;

public class NoteClipboardTests
{
    [Fact]
    public void A_fresh_clipboard_is_empty()
    {
        var clipboard = new NoteClipboard();
        clipboard.HasContent.Should().BeFalse();
        clipboard.Paste(1000, laneCount: 4).Should().BeEmpty();
    }

    [Fact]
    public void Copying_nothing_leaves_the_clipboard_alone()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new List<Note>());
        clipboard.HasContent.Should().BeFalse();
    }

    [Fact]
    public void Paste_places_the_phrase_at_the_playhead()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(1000, 0), Note.Tap(1500, 2) });

        IReadOnlyList<Note> pasted = clipboard.Paste(4000, laneCount: 4);

        pasted.Select(n => n.TimeMs).Should().Equal(4000, 4500);
        pasted.Select(n => n.Lane).Should().Equal(0, 2);
    }

    [Fact]
    public void Relative_timing_inside_the_phrase_is_preserved()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(2000, 0), Note.Tap(2125, 1), Note.Tap(2500, 3) });

        IReadOnlyList<Note> pasted = clipboard.Paste(0, laneCount: 4);

        pasted.Select(n => n.TimeMs).Should().Equal(0, 125, 500);
    }

    [Fact]
    public void A_copied_hold_keeps_its_length()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Hold(1000, 1, 2500) });

        Note pasted = clipboard.Paste(5000, laneCount: 4).Single();

        pasted.IsHold.Should().BeTrue();
        pasted.TimeMs.Should().Be(5000);
        pasted.DurationMs.Should().Be(1500);
    }

    [Fact]
    public void The_phrase_can_be_pasted_into_other_lanes()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(0, 0), Note.Tap(250, 1) });

        IReadOnlyList<Note> pasted = clipboard.Paste(1000, laneCount: 4, laneOffset: 2);

        pasted.Select(n => n.Lane).Should().Equal(2, 3);
    }

    [Fact]
    public void A_lane_offset_that_would_overflow_is_clamped_rather_than_dropping_notes()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(0, 0), Note.Tap(250, 3) });

        IReadOnlyList<Note> pasted = clipboard.Paste(0, laneCount: 4, laneOffset: 3);

        pasted.Should().HaveCount(2);
        pasted.Select(n => n.Lane).Should().OnlyContain(l => l >= 0 && l < 4);
    }

    [Fact]
    public void Pasting_before_zero_clamps_to_the_start_of_the_song()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(1000, 0), Note.Tap(1500, 1) });

        IReadOnlyList<Note> pasted = clipboard.Paste(-800, laneCount: 4);

        pasted.Select(n => n.TimeMs).Should().OnlyContain(t => t >= 0);
    }

    [Fact]
    public void Copying_out_of_order_still_anchors_on_the_earliest_note()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(2000, 3), Note.Tap(1000, 0) });

        clipboard.Paste(0, laneCount: 4).Select(n => n.TimeMs).Should().Equal(0, 1000);
    }

    [Fact]
    public void The_clipboard_can_be_pasted_repeatedly()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(0, 0) });

        clipboard.Paste(500, 4).Single().TimeMs.Should().Be(500);
        clipboard.Paste(900, 4).Single().TimeMs.Should().Be(900);
        clipboard.Count.Should().Be(1);
    }

    [Fact]
    public void Clearing_empties_it()
    {
        var clipboard = new NoteClipboard();
        clipboard.Copy(new[] { Note.Tap(0, 0) });
        clipboard.Clear();

        clipboard.HasContent.Should().BeFalse();
    }
}
