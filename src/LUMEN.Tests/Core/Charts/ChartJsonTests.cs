using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Xunit;

namespace Lumen.Tests.Core.Charts;

public class ChartJsonTests
{
    private static Chart Sample() => new()
    {
        LaneCount = 4,
        ChartOffsetMs = -12,
        BpmPoints = new[] { new BpmPoint(0, 128), new BpmPoint(32000, 140) },
        Notes = new[]
        {
            Note.Tap(1000, 0),
            Note.Hold(2000, 2, 2750),
            Note.Tap(1500, 3),
        },
        Meta = new ChartMeta
        {
            Title = "First Light",
            Artist = "LUMEN",
            Creator = "7g3",
            DifficultyName = "MASTER",
            DifficultyLevel = 14.7,
            AudioFile = "song.ogg",
        },
    };

    [Fact]
    public void Round_trips_through_json()
    {
        Chart original = Sample();
        string json = ChartJson.Serialize(original);

        Chart back = ChartJson.Deserialize(json);

        back.Meta.Should().BeEquivalentTo(original.Meta);
        back.LaneCount.Should().Be(4);
        back.ChartOffsetMs.Should().Be(-12);
        back.BpmPoints.Should().BeEquivalentTo(original.BpmPoints);
        back.Notes.Should().BeEquivalentTo(original.Notes);
        back.FormatVersion.Should().Be(GameIdentity.ChartFormatVersion);
    }

    [Fact]
    public void Hold_notes_keep_their_end_time()
    {
        Chart back = ChartJson.Deserialize(ChartJson.Serialize(Sample()));
        Note hold = back.Notes.Single(n => n.IsHold);
        hold.EndTimeMs.Should().Be(2750);
        hold.DurationMs.Should().Be(750);
    }

    [Fact]
    public void Unknown_note_type_falls_back_to_tap()
    {
        const string json = """
        { "formatVersion": 1, "laneCount": 4,
          "timing": { "bpm": [ { "atMs": 0, "bpm": 120 } ] },
          "notes": [ { "type": "sparkle", "ms": 500, "lane": 1 } ] }
        """;

        ChartJson.Deserialize(json).Notes.Single().Kind.Should().Be(NoteKind.Tap);
    }
}
