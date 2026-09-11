using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

public class ChartVersionTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly ChartVersionRepository _versions;
    private readonly Guid _chartId = Guid.NewGuid();

    public ChartVersionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-versions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        _db.Open();
        _versions = new ChartVersionRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private Chart Chart(int notes = 8, double level = 12) => new()
    {
        Id = _chartId,
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 128) },
        Notes = Enumerable.Range(0, notes).Select(i => Note.Tap(i * 500, i % 4)).ToArray(),
        Meta = new ChartMeta
        {
            Title = "First Light",
            Artist = "LUMEN",
            Creator = "7g3",
            DifficultyName = "MASTER",
            DifficultyLevel = level,
            AudioFile = "song.wav",
        },
    };

    private int Record(Chart chart) =>
        _versions.Record(_chartId, chart, ChartJson.Serialize(chart));

    [Fact]
    public void A_chart_with_no_history_reports_nothing()
    {
        _versions.List(_chartId).Should().BeEmpty();
        _versions.LatestVersion(_chartId).Should().Be(0);
    }

    [Fact]
    public void The_first_save_is_revision_one()
    {
        Record(Chart()).Should().Be(1);
        _versions.Count(_chartId).Should().Be(1);
    }

    [Fact]
    public void Each_change_adds_a_revision()
    {
        Record(Chart(notes: 8));
        Record(Chart(notes: 12));
        Record(Chart(notes: 16));

        _versions.List(_chartId).Select(v => v.Version).Should().Equal(3, 2, 1);
    }

    [Fact]
    public void Saving_the_same_document_again_does_not_add_a_revision()
    {
        // Test Play saves before it hands over, and the author may press Ctrl+S straight
        // after. The history is meant to show changes, not keystrokes.
        Chart chart = Chart();
        Record(chart);
        Record(chart);
        Record(chart);

        _versions.Count(_chartId).Should().Be(1);
    }

    [Fact]
    public void A_revision_can_be_read_back_as_a_chart()
    {
        Record(Chart(notes: 8));
        Record(Chart(notes: 20));

        Chart? first = _versions.Get(_chartId, 1);

        first.Should().NotBeNull();
        first!.Notes.Should().HaveCount(8);
        first.Id.Should().Be(_chartId);
    }

    [Fact]
    public void A_revision_remembers_what_it_was_when_it_was_saved()
    {
        Record(Chart(notes: 8, level: 12));
        Record(Chart(notes: 40, level: 15.5));

        ChartVersion newest = _versions.List(_chartId).First();

        newest.NoteCount.Should().Be(40);
        newest.DifficultyLevel.Should().Be(15.5);
        newest.Title.Should().Be("First Light");
        newest.DifficultyName.Should().Be("MASTER");
        newest.SavedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Asking_for_a_revision_that_does_not_exist_returns_nothing()
    {
        Record(Chart());
        _versions.Get(_chartId, 99).Should().BeNull();
    }

    [Fact]
    public void History_is_bounded_and_keeps_the_newest_revisions()
    {
        for (int i = 1; i <= ChartVersionRepository.Retention + 10; i++)
        {
            Record(Chart(notes: i));
        }

        _versions.Count(_chartId).Should().Be(ChartVersionRepository.Retention);

        var kept = _versions.List(_chartId).Select(v => v.Version).ToArray();
        kept.Should().BeInDescendingOrder();
        kept.Max().Should().Be(ChartVersionRepository.Retention + 10);
    }

    [Fact]
    public void Histories_of_different_charts_do_not_mix()
    {
        var other = Guid.NewGuid();
        Record(Chart());
        _versions.Record(other, Chart(), ChartJson.Serialize(Chart(notes: 4)));

        _versions.Count(_chartId).Should().Be(1);
        _versions.Count(other).Should().Be(1);
        _versions.List(other).Single().Version.Should().Be(1);
    }

    [Fact]
    public void An_unparseable_revision_returns_null_rather_than_throwing()
    {
        _versions.Record(_chartId, Chart(), "{ not a chart");

        Action read = () => _versions.Get(_chartId, 1);

        read.Should().NotThrow();
        _versions.Get(_chartId, 1).Should().BeNull();
    }
}
