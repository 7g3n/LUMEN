using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Library;
using Lumen.Data;
using Lumen.Data.Library;
using Lumen.Data.Packages;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// The spec's headline promise for the format (§55–57): hand one file to a friend, they
/// import it and play. Export → wipe → import has to come back with the same charts.
/// </summary>
public class LumenPackageTests : IDisposable
{
    private readonly string _dir;
    private readonly LumenPaths _paths;
    private readonly Database _db;
    private readonly LibraryRepository _library;
    private readonly LibraryService _service;
    private readonly PackageService _packages;

    public LumenPackageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, LumenPaths.PortableSentinelFileName), "");

        _paths = LumenPaths.Resolve(_dir);
        _paths.EnsureCreated();

        _db = new Database(_paths.DatabaseFile);
        _db.Open();
        _library = new LibraryRepository(_db);
        _service = new LibraryService(_library, _paths);
        _packages = new PackageService(_paths, _service);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Chart Chart(string difficulty = "MASTER", double level = 14.7, int notes = 64) =>
        new()
        {
            Id = Guid.NewGuid(),
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 128) },
            Notes = Enumerable.Range(0, notes)
                .Select(i => Note.Tap(500 + i * 250, i % 4)).ToArray(),
            Meta = new ChartMeta
            {
                Title = "First Light",
                Artist = "LUMEN",
                Creator = "7g3",
                DifficultyName = difficulty,
                DifficultyLevel = level,
                AudioFile = "song.wav",
                DurationMs = 60_000,
                Description = "A test chart.",
                Tags = new[] { "beginner", "stream" },
            },
        };

    private string WriteAudio(string name = "song.wav")
    {
        string path = Path.Combine(_paths.Songs, name);
        var bytes = new byte[2048];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Puts charts in the library the way the editor would, then returns them.</summary>
    private IReadOnlyList<LibraryChart> Publish(params Chart[] charts)
    {
        WriteAudio();
        foreach (Chart chart in charts)
        {
            string file = Path.Combine(
                _paths.ChartsLocal,
                $"first-light-{chart.Meta.DifficultyName.ToLowerInvariant()}.{GameIdentity.ChartExtension}");
            File.WriteAllText(file, ChartJson.Serialize(chart));
        }

        _service.Scan();
        return _library.All();
    }

    // --- export / import round trip ---

    [Fact]
    public void Export_then_import_brings_the_chart_back_unchanged()
    {
        Chart original = Chart();
        IReadOnlyList<LibraryChart> published = Publish(original);

        PackageService.ExportResult exported = _packages.Export(published);
        File.Exists(exported.Path).Should().BeTrue();

        // Wipe the library exactly as a different machine would start.
        foreach (string file in Directory.GetFiles(_paths.ChartsLocal))
        {
            File.Delete(file);
        }

        File.Delete(Path.Combine(_paths.Songs, "song.wav"));
        _service.Scan();
        _library.Count().Should().Be(0);

        PackageService.ImportResult result = _packages.Import(exported.Path);

        result.Title.Should().Be("First Light");
        result.Difficulties.Should().Equal("MASTER");

        LibraryChart back = _library.All().Should().ContainSingle().Subject;
        back.Meta.Title.Should().Be(original.Meta.Title);
        back.Meta.DifficultyName.Should().Be(original.Meta.DifficultyName);
        back.Level.Should().Be(original.Meta.DifficultyLevel);
        back.NoteCount.Should().Be(original.Notes.Count);
        back.Source.Should().Be(ChartSource.Imported);
        File.Exists(back.AudioPath).Should().BeTrue();
    }

    [Fact]
    public void Every_difficulty_travels_in_one_package()
    {
        IReadOnlyList<LibraryChart> published = Publish(
            Chart("EASY", 3, notes: 24), Chart("MASTER", 14.7, notes: 64));

        PackageService.ExportResult exported = _packages.Export(published);
        exported.ChartCount.Should().Be(2);

        LumenPackage.Opened opened = LumenPackage.Read(exported.Path);
        opened.Charts.Select(c => c.Meta.DifficultyName)
            .Should().BeEquivalentTo("EASY", "MASTER");
    }

    [Fact]
    public void Metadata_and_tags_survive_the_journey()
    {
        IReadOnlyList<LibraryChart> published = Publish(Chart());

        PackageService.ExportResult exported = _packages.Export(published);
        Chart back = LumenPackage.Read(exported.Path).Charts.Single();

        back.Meta.Description.Should().Be("A test chart.");
        back.Meta.Tags.Should().Equal("beginner", "stream");
        back.Meta.Creator.Should().Be("7g3");
    }

    [Fact]
    public void The_chart_id_survives_so_history_follows_the_chart()
    {
        Chart original = Chart();
        PackageService.ExportResult exported = _packages.Export(Publish(original));

        LumenPackage.Read(exported.Path).Charts.Single().Id.Should().Be(original.Id);
    }

    [Fact]
    public void Importing_the_same_package_twice_does_not_duplicate_the_audio()
    {
        PackageService.ExportResult exported = _packages.Export(Publish(Chart()));

        _packages.Import(exported.Path);
        string[] after = Directory.GetFiles(_paths.Songs, "*.wav");
        _packages.Import(exported.Path);

        // Counts the audio specifically: an interrupted write elsewhere can leave a
        // *.tmp in the folder, and that is not what this test is about.
        Directory.GetFiles(_paths.Songs, "*.wav").Should().BeEquivalentTo(after);
    }

    [Fact]
    public void The_manifest_says_what_is_inside_without_unpacking_it()
    {
        PackageService.ExportResult exported = _packages.Export(
            Publish(Chart("EASY", 3, notes: 24), Chart("MASTER", 14.7)));

        LumenPackage.Manifest manifest = _packages.Inspect(exported.Path);

        manifest.Package.Title.Should().Be("First Light");
        manifest.Package.Artist.Should().Be("LUMEN");
        manifest.Charts.Should().HaveCount(2);
        manifest.Charts.Select(c => c.DifficultyName).Should().BeEquivalentTo("EASY", "MASTER");
        manifest.Audio!.Sha256.Should().NotBeNullOrWhiteSpace();
    }

    // --- refusing damaged input ---

    [Fact]
    public void A_file_that_is_not_a_package_is_refused_with_an_explanation()
    {
        string path = Path.Combine(_dir, "not-a-package." + GameIdentity.PackageExtension);
        File.WriteAllText(path, "this is just some text");

        Action read = () => LumenPackage.Read(path);

        read.Should().Throw<InvalidDataException>().WithMessage($"*not a {GameIdentity.Name} package*");
    }

    [Fact]
    public void A_zip_with_no_manifest_is_refused()
    {
        string path = Path.Combine(_dir, "empty." + GameIdentity.PackageExtension);
        using (var zip = new System.IO.Compression.ZipArchive(
                   File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Action read = () => LumenPackage.Read(path);

        read.Should().Throw<InvalidDataException>().WithMessage("*no manifest*");
    }

    [Fact]
    public void A_tampered_entry_fails_its_checksum()
    {
        PackageService.ExportResult exported = _packages.Export(Publish(Chart()));

        // Rewrite one chart inside the package, leaving the manifest's hash behind.
        using (var zip = System.IO.Compression.ZipFile.Open(
                   exported.Path, System.IO.Compression.ZipArchiveMode.Update))
        {
            System.IO.Compression.ZipArchiveEntry entry =
                zip.Entries.First(e => e.FullName.StartsWith("charts/"));
            string name = entry.FullName;
            entry.Delete();

            System.IO.Compression.ZipArchiveEntry replacement = zip.CreateEntry(name);
            using var writer = new StreamWriter(replacement.Open());
            writer.Write(ChartJson.Serialize(Chart("MASTER", 14.7, notes: 8)));
        }

        Action read = () => LumenPackage.Read(exported.Path);

        read.Should().Throw<InvalidDataException>().WithMessage("*checksum*");
    }

    [Fact]
    public void A_package_from_a_newer_build_is_refused()
    {
        PackageService.ExportResult exported = _packages.Export(Publish(Chart()));

        using (var zip = System.IO.Compression.ZipFile.Open(
                   exported.Path, System.IO.Compression.ZipArchiveMode.Update))
        {
            System.IO.Compression.ZipArchiveEntry manifest =
                zip.GetEntry(LumenPackage.ManifestEntry)!;
            string json;
            using (var reader = new StreamReader(manifest.Open()))
            {
                json = reader.ReadToEnd();
            }

            manifest.Delete();
            using var writer = new StreamWriter(
                zip.CreateEntry(LumenPackage.ManifestEntry).Open());
            writer.Write(json.Replace("\"formatVersion\": 1", "\"formatVersion\": 99"));
        }

        Action read = () => LumenPackage.Read(exported.Path);

        read.Should().Throw<InvalidDataException>().WithMessage("*newer version*");
    }

    // --- export refuses to send something broken ---

    [Fact]
    public void A_chart_that_fails_validation_is_not_exported()
    {
        Chart broken = Chart() with { Notes = Array.Empty<Note>() };
        WriteAudio();
        File.WriteAllText(
            Path.Combine(_paths.ChartsLocal, "broken." + GameIdentity.ChartExtension),
            ChartJson.Serialize(broken));

        // The scanner skips note-less charts, so publish a good one and point the export
        // at a hand-made library row for the broken file.
        IReadOnlyList<LibraryChart> published = Publish(Chart());
        LibraryChart entry = published.Single() with
        {
            ChartPath = Path.Combine(_paths.ChartsLocal, "broken." + GameIdentity.ChartExtension),
        };

        Action export = () => _packages.Export(new[] { entry });

        export.Should().Throw<InvalidOperationException>().WithMessage("*no notes*");
    }

    [Fact]
    public void Exporting_nothing_is_refused()
    {
        Action export = () => _packages.Export(Array.Empty<LibraryChart>());

        export.Should().Throw<InvalidOperationException>().WithMessage("*at least one*");
    }
}
