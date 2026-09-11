using System;
using System.Text.Json.Nodes;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Xunit;

namespace Lumen.Tests.Core.Charts;

/// <summary>
/// A chart written by any past build has to keep opening (spec §98). These drive the
/// migrator with documents in the old shape, exactly as they would arrive off disk.
/// </summary>
public class ChartMigratorTests
{
    /// <summary>A v1 document, as the Phase 3 build wrote them.</summary>
    private const string V1 =
        """
        {
          "formatVersion": 1,
          "laneCount": 4,
          "meta": {
            "title": "First Light",
            "artist": "LUMEN",
            "creator": "7g3",
            "difficultyName": "NORMAL",
            "difficultyLevel": 3.5,
            "audioFile": "lumen-practice.wav",
            "previewMs": 3750
          },
          "timing": { "chartOffsetMs": 0, "bpm": [ { "atMs": 0, "bpm": 128 } ] },
          "notes": [
            { "type": "tap",  "ms": 1875, "lane": 0 },
            { "type": "hold", "ms": 3750, "lane": 2, "endMs": 5250 }
          ]
        }
        """;

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void A_v1_document_is_stamped_with_the_current_version()
    {
        JsonObject migrated = ChartMigrator.Migrate(Parse(V1));

        migrated["formatVersion"]!.GetValue<int>().Should().Be(GameIdentity.ChartFormatVersion);
    }

    [Fact]
    public void A_v1_document_gains_the_fields_v2_added()
    {
        var meta = (JsonObject)ChartMigrator.Migrate(Parse(V1))["meta"]!;

        meta.Should().ContainKey("description");
        meta.Should().ContainKey("tags");
        meta.Should().ContainKey("coverFile");
        meta.Should().ContainKey("durationMs");
    }

    [Fact]
    public void The_audio_length_is_derived_from_the_last_note()
    {
        // v1 never recorded a duration, but it is implicit in the notes. Deriving it is
        // the difference between an imported v1 chart showing its length and showing 0:00.
        var meta = (JsonObject)ChartMigrator.Migrate(Parse(V1))["meta"]!;

        meta["durationMs"]!.GetValue<double>().Should().Be(5250);
    }

    [Fact]
    public void A_duration_that_is_already_present_is_left_alone()
    {
        JsonObject document = Parse(V1);
        ((JsonObject)document["meta"]!)["durationMs"] = 128_000d;

        var meta = (JsonObject)ChartMigrator.Migrate(document)["meta"]!;

        meta["durationMs"]!.GetValue<double>().Should().Be(128_000);
    }

    [Fact]
    public void A_document_with_no_version_stamp_is_treated_as_v1()
    {
        JsonObject document = Parse(V1);
        document.Remove("formatVersion");

        JsonObject migrated = ChartMigrator.Migrate(document);

        migrated["formatVersion"]!.GetValue<int>().Should().Be(GameIdentity.ChartFormatVersion);
        ((JsonObject)migrated["meta"]!).Should().ContainKey("tags");
    }

    [Fact]
    public void A_document_from_a_newer_build_is_refused_with_an_explanation()
    {
        JsonObject document = Parse(V1);
        document["formatVersion"] = GameIdentity.ChartFormatVersion + 5;

        Action migrate = () => ChartMigrator.Migrate(document);

        migrate.Should().Throw<FormatException>()
            .WithMessage("*newer version*");
    }

    [Fact]
    public void A_document_already_at_the_current_version_is_untouched()
    {
        JsonObject document = Parse(V1);
        document["formatVersion"] = GameIdentity.ChartFormatVersion;
        ((JsonObject)document["meta"]!)["durationMs"] = 42d;

        var meta = (JsonObject)ChartMigrator.Migrate(document)["meta"]!;

        meta["durationMs"]!.GetValue<double>().Should().Be(42);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(99, false)]
    public void The_readable_range_is_reported(int version, bool readable)
    {
        ChartMigrator.CanRead(version).Should().Be(readable);
    }

    // --- through the reader ---

    [Fact]
    public void A_v1_file_loads_through_ChartJson_without_the_caller_knowing()
    {
        Chart chart = ChartJson.Deserialize(V1);

        chart.FormatVersion.Should().Be(GameIdentity.ChartFormatVersion);
        chart.Meta.Title.Should().Be("First Light");
        chart.Meta.DurationMs.Should().Be(5250);
        chart.Meta.Tags.Should().BeEmpty();
        chart.Meta.CoverFile.Should().BeNull();
        chart.Notes.Should().HaveCount(2);
        chart.Notes[1].IsHold.Should().BeTrue();
    }

    [Fact]
    public void A_migrated_chart_saved_again_comes_back_identically()
    {
        Chart once = ChartJson.Deserialize(V1);
        Chart twice = ChartJson.Deserialize(ChartJson.Serialize(once));

        twice.Meta.Should().Be(once.Meta);
        twice.Notes.Should().Equal(once.Notes);
        twice.BpmPoints.Should().Equal(once.BpmPoints);
    }

    [Fact]
    public void A_chart_that_is_not_an_object_is_rejected_clearly()
    {
        Action read = () => ChartJson.Deserialize("[1, 2, 3]");

        read.Should().Throw<FormatException>().WithMessage("*JSON object*");
    }
}
