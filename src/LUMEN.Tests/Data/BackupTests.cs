using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Achievements;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Library;
using Lumen.Core.Profiles;
using Lumen.Core.Replays;
using Lumen.Core.Scores;
using Lumen.Data;
using Lumen.Data.Backup;
using Lumen.Data.Library;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// The spec's migration promise (§12–13): export on one machine, import on a clean one,
/// and everything is there. These build two independent installations in temp folders and
/// carry a backup between them.
/// </summary>
public class BackupTests : IDisposable
{
    /// <summary>One complete installation: its own folder tree, database and repositories.</summary>
    private sealed class Installation : IDisposable
    {
        public Installation(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, LumenPaths.PortableSentinelFileName), "");

            Paths = LumenPaths.Resolve(root);
            Paths.EnsureCreated();

            Db = new Database(Paths.DatabaseFile);
            Db.Open();

            Profiles = new ProfileRepository(Db);
            Scores = new ScoreRepository(Db, BalanceConfig.Default);
            LibraryRepo = new LibraryRepository(Db);
            Library = new LibraryService(LibraryRepo, Paths);
            Replays = new ReplayRepository(Db, Paths.Replays);
            Achievements = new AchievementRepository(Db);
            AppMeta = new AppMetaStore(Db);
            Backups = new BackupService(Db, Paths, AppMeta, Library);
        }

        public string Root { get; }
        public LumenPaths Paths { get; }
        public Database Db { get; }
        public ProfileRepository Profiles { get; }
        public ScoreRepository Scores { get; }
        public LibraryRepository LibraryRepo { get; }
        public LibraryService Library { get; }
        public ReplayRepository Replays { get; }
        public AchievementRepository Achievements { get; }
        public AppMetaStore AppMeta { get; }
        public BackupService Backups { get; }

        public void Dispose()
        {
            Db.Dispose();
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private readonly string _base;
    private readonly Installation _a;

    public BackupTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "lumen-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
        _a = new Installation(Path.Combine(_base, "a"));
    }

    public void Dispose()
    {
        _a.Dispose();
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private Installation Fresh(string name) => new(Path.Combine(_base, name));

    // --- fixtures ---

    private static Chart Chart(string difficulty = "MASTER", double level = 14.7, int notes = 40) =>
        new Chart
        {
            Id = Guid.NewGuid(),
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 128) },
            Notes = Enumerable.Range(0, notes).Select(i => Note.Tap(500 + i * 250, i % 4)).ToArray(),
            Meta = new ChartMeta
            {
                Title = "First Light", Artist = "LUMEN", Creator = "7g3",
                DifficultyName = difficulty, DifficultyLevel = level,
                AudioFile = "song.wav", DurationMs = 30_000,
                Tags = new[] { "stream" },
            },
        }.Normalized();

    private static PlayResult Result(Chart chart, double accuracy, long score) => new()
    {
        Chart = chart.Meta,
        Score = score,
        Accuracy = accuracy,
        MaxCombo = chart.Notes.Count,
        Perfect = chart.Notes.Count,
        Great = 0, Good = 0, Bad = 0, Miss = 0,
        FullCombo = true,
        AllPerfect = true,
        PlayedUtc = DateTime.UtcNow,
    };

    /// <summary>Fills an installation with a profile, a chart, a score, a replay and an unlock.</summary>
    private (Guid Player, Chart Chart) Populate(Installation install, string playerName = "7g3")
    {
        Guid player = install.Profiles.Create(playerName).PlayerId;

        Chart chart = Chart();
        File.WriteAllBytes(Path.Combine(install.Paths.Songs, "song.wav"), new byte[512]);
        File.WriteAllText(
            Path.Combine(install.Paths.ChartsLocal, $"first-light.{GameIdentity.ChartExtension}"),
            ChartJson.Serialize(chart));
        install.Library.Scan();

        install.Scores.Save(player, Result(chart, 99.4, 987_654), chart);
        install.LibraryRepo.SetFavorite(player, ChartKey.For(chart), true);

        var recorder = new ReplayRecorder();
        recorder.Record(chart.Notes.Select(n => LaneEvent.Down(n.Lane, n.TimeMs)).ToArray());
        var session = new GameplaySession(chart);
        session.Finish();
        install.Replays.Save(recorder.Build(player, playerName, chart, session.Score));

        install.Achievements.Evaluate(player, AchievementStats.Empty with { PlayCount = 1 });

        File.WriteAllText(Path.Combine(install.Paths.Settings, "balance.json"), "{ \"test\": 1 }");

        return (player, chart);
    }

    // --- the headline journey ---

    [Fact]
    public void Export_on_one_machine_imports_onto_a_clean_one_with_everything_intact()
    {
        (Guid player, Chart chart) = Populate(_a);
        string backup = _a.Backups.Create().Path;

        using Installation b = Fresh("b");
        b.LibraryRepo.Count().Should().Be(0);

        BackupService.RestoreResult result = b.Backups.Restore(backup);

        // Profile, with its id intact so every score stays attached (§80).
        Profile? restored = b.Profiles.Get(player);
        restored.Should().NotBeNull();
        restored!.DisplayName.Should().Be("7g3");

        // Scores and the numbers derived from them.
        b.Scores.GetStatistics(player).PlayCount.Should().Be(1);
        b.Scores.GetLatestSnapshot(player).Rating
            .Should().BeApproximately(_a.Scores.GetLatestSnapshot(player).Rating, 1e-9);
        b.Scores.GetChartBest(player, ChartKey.For(chart))!.Score.Should().Be(987_654);

        // The library, rebuilt from the chart files that travelled with it.
        b.LibraryRepo.Count().Should().Be(1);
        b.LibraryRepo.All().Single().Meta.Title.Should().Be("First Light");
        b.LibraryRepo.IsFavorite(player, ChartKey.For(chart)).Should().BeTrue();

        // Replays, including the event stream.
        ReplaySummary summary = b.Replays.ListForPlayer(player).Should().ContainSingle().Subject;
        b.Replays.Load(summary.ReplayId).Should().NotBeNull();

        // Achievements.
        b.Achievements.UnlockedIds(player).Should().Contain("first-play");

        result.FilesRestored.Should().BeGreaterThan(0);
        result.Rows.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_audio_and_chart_files_travel_with_the_backup()
    {
        Populate(_a);
        string backup = _a.Backups.Create().Path;

        using Installation b = Fresh("b");
        b.Backups.Restore(backup);

        File.Exists(Path.Combine(b.Paths.Songs, "song.wav")).Should().BeTrue();
        Directory.GetFiles(b.Paths.ChartsLocal).Should().ContainSingle();
        Directory.GetFiles(b.Paths.Settings).Should().Contain(f => f.EndsWith("balance.json"));

        // And the restored chart is playable: the library row points at files that exist.
        LibraryChart entry = b.LibraryRepo.All().Single();
        File.Exists(entry.ChartPath).Should().BeTrue();
        File.Exists(entry.AudioPath).Should().BeTrue();
    }

    [Fact]
    public void Restoring_the_same_backup_twice_changes_nothing_the_second_time()
    {
        Guid player = Populate(_a).Player;
        string backup = _a.Backups.Create().Path;

        using Installation b = Fresh("b");
        b.Backups.Restore(backup);
        b.Backups.Restore(backup);

        b.Profiles.GetAll().Should().ContainSingle();
        b.Scores.GetStatistics(player).PlayCount.Should().Be(1);
        b.Replays.Count(player).Should().Be(1);
    }

    [Fact]
    public void Restoring_adds_to_an_installation_rather_than_wiping_it()
    {
        // Import is a merge: destroying whatever was already there is not a reading of
        // "import" anyone would want to discover afterwards.
        Populate(_a, "7g3");
        string backup = _a.Backups.Create().Path;

        using Installation b = Fresh("b");
        Guid local = b.Profiles.Create("Nagisa").PlayerId;

        b.Backups.Restore(backup);

        b.Profiles.GetAll().Select(p => p.DisplayName)
            .Should().BeEquivalentTo("Nagisa", "7g3");
        b.Profiles.Get(local).Should().NotBeNull();
    }

    // --- the archive itself ---

    [Fact]
    public void The_manifest_says_what_is_inside()
    {
        Populate(_a);
        string backup = _a.Backups.Create().Path;

        BackupService.Manifest manifest = _a.Backups.Inspect(backup);

        manifest.FormatVersion.Should().Be(BackupService.FormatVersion);
        manifest.LumenVersion.Should().Be(GameIdentity.Version);
        manifest.SchemaVersion.Should().Be(_a.Db.SchemaVersion);
        manifest.Counts.Profiles.Should().Be(1);
        manifest.Counts.Scores.Should().Be(1);
        manifest.Counts.Charts.Should().Be(1);
        manifest.Counts.Replays.Should().Be(1);
        manifest.Counts.Files.Should().BeGreaterThan(0);
        manifest.Profiles.Should().Equal("7g3");
    }

    [Fact]
    public void Backups_are_named_by_the_moment_they_were_taken()
    {
        string name = BackupService.FileNameFor(new DateTime(2026, 9, 9, 14, 30, 5));

        name.Should().Be($"{GameIdentity.Slug}-backup-2026-09-09-143005.{GameIdentity.BackupExtension}");
    }

    [Fact]
    public void A_file_that_is_not_a_backup_is_refused_with_an_explanation()
    {
        string path = Path.Combine(_base, $"nope.{GameIdentity.BackupExtension}");
        File.WriteAllText(path, "just some text");

        Action restore = () => _a.Backups.Restore(path);

        restore.Should().Throw<InvalidDataException>()
            .WithMessage($"*not a {GameIdentity.Name} backup*");
    }

    [Fact]
    public void A_zip_with_no_manifest_is_refused()
    {
        string path = Path.Combine(_base, $"empty.{GameIdentity.BackupExtension}");
        using (var zip = new System.IO.Compression.ZipArchive(
                   File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Action restore = () => _a.Backups.Restore(path);

        restore.Should().Throw<InvalidDataException>().WithMessage("*no manifest*");
    }

    [Fact]
    public void A_backup_from_a_newer_build_is_refused()
    {
        Populate(_a);
        string backup = _a.Backups.Create().Path;

        using (var zip = System.IO.Compression.ZipFile.Open(
                   backup, System.IO.Compression.ZipArchiveMode.Update))
        {
            System.IO.Compression.ZipArchiveEntry entry = zip.GetEntry(BackupService.ManifestEntry)!;
            string json;
            using (var reader = new StreamReader(entry.Open()))
            {
                json = reader.ReadToEnd();
            }

            entry.Delete();
            using var writer = new StreamWriter(
                zip.CreateEntry(BackupService.ManifestEntry).Open());
            writer.Write(json.Replace("\"formatVersion\": 1", "\"formatVersion\": 99"));
        }

        Action restore = () => _a.Backups.Restore(backup);

        restore.Should().Throw<InvalidDataException>().WithMessage("*newer version*");
    }

    [Fact]
    public void An_entry_that_tries_to_escape_the_data_folder_is_written_by_its_leaf_name()
    {
        // The archive comes from someone else; a crafted "../../" name must not be able to
        // write outside the data tree.
        string path = Path.Combine(_base, $"evil.{GameIdentity.BackupExtension}");
        using (var zip = new System.IO.Compression.ZipArchive(
                   File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry(BackupService.ManifestEntry).Open()))
            {
                w.Write("{ \"formatVersion\": 1, \"schemaVersion\": 1 }");
            }

            using (var w = new StreamWriter(zip.CreateEntry(BackupService.DataEntry).Open()))
            {
                w.Write("{ \"tables\": {} }");
            }

            using (var w = new StreamWriter(zip.CreateEntry("songs/../../../escaped.wav").Open()))
            {
                w.Write("nope");
            }
        }

        _a.Backups.Restore(path);

        File.Exists(Path.Combine(_base, "escaped.wav")).Should().BeFalse();
        File.Exists(Path.Combine(_a.Paths.Songs, "escaped.wav")).Should().BeTrue();
    }

    // --- automatic backups ---

    [Fact]
    public void The_first_launch_is_due_a_backup()
    {
        _a.Backups.IsAutoBackupDue().Should().BeTrue();
    }

    [Fact]
    public void Nothing_is_due_again_the_same_day()
    {
        Populate(_a);
        _a.Backups.AutoBackupIfDue().Should().NotBeNull();

        _a.Backups.IsAutoBackupDue().Should().BeFalse();
        _a.Backups.AutoBackupIfDue().Should().BeNull();
    }

    [Fact]
    public void A_backup_is_due_again_the_next_day()
    {
        Populate(_a);
        _a.Backups.AutoBackupIfDue();

        _a.Backups.IsAutoBackupDue(DateTime.UtcNow + TimeSpan.FromDays(1)).Should().BeTrue();
    }

    [Fact]
    public void A_version_change_makes_one_due_immediately()
    {
        Populate(_a);
        _a.Backups.AutoBackupIfDue();
        _a.Backups.IsAutoBackupDue().Should().BeFalse();

        // What the next release looks like from here: the moment before a migration runs.
        _a.AppMeta.Set("last_backup_version", "0.0.1-old");

        _a.Backups.IsAutoBackupDue().Should().BeTrue();
    }

    [Fact]
    public void Old_automatic_backups_are_pruned()
    {
        Populate(_a);

        for (int i = 0; i < 5; i++)
        {
            _a.Backups.Create(Path.Combine(_a.Paths.Backups,
                $"{GameIdentity.Slug}-backup-2026-09-0{i + 1}-120000.{GameIdentity.BackupExtension}"));
        }

        _a.Backups.List().Should().HaveCount(5);
        _a.Backups.Prune(keep: 2).Should().Be(3);
        _a.Backups.List().Should().HaveCount(2);
    }

    [Fact]
    public void The_newest_backup_is_listed_first()
    {
        Populate(_a);
        _a.Backups.Create(Path.Combine(_a.Paths.Backups, $"old.{GameIdentity.BackupExtension}"));
        System.Threading.Thread.Sleep(20);
        _a.Backups.Create(Path.Combine(_a.Paths.Backups, $"new.{GameIdentity.BackupExtension}"));

        _a.Backups.List().First().FileName.Should().Be($"new.{GameIdentity.BackupExtension}");
    }

    [Fact]
    public void Restoring_from_an_automatic_backup_brings_a_deleted_profile_back()
    {
        Guid player = Populate(_a).Player;
        string backup = _a.Backups.Create().Path;

        _a.Profiles.Delete(player);
        _a.Profiles.Get(player).Should().BeNull();

        _a.Backups.Restore(backup);

        _a.Profiles.Get(player).Should().NotBeNull();
        _a.Scores.GetStatistics(player).PlayCount.Should().Be(1);
    }

    // --- surviving a kill ---

    [Fact]
    public void A_database_left_with_an_unfinished_write_ahead_log_still_opens()
    {
        // What being killed mid-write looks like on disk: committed data sitting in the
        // WAL with no clean shutdown. Copying the files while the connection is open
        // reproduces it without actually killing the process.
        Guid player = Populate(_a).Player;

        string copyRoot = Path.Combine(_base, "killed");
        Directory.CreateDirectory(Path.Combine(copyRoot, "database"));

        foreach (string file in Directory.GetFiles(_a.Paths.Database))
        {
            File.Copy(file, Path.Combine(copyRoot, "database", Path.GetFileName(file)));
        }

        using var recovered = new Database(
            Path.Combine(copyRoot, "database", GameIdentity.DatabaseFileName));
        recovered.Open();

        new ProfileRepository(recovered).Get(player).Should().NotBeNull();
        new ScoreRepository(recovered, BalanceConfig.Default)
            .GetStatistics(player).PlayCount.Should().Be(1);
    }

    [Fact]
    public void A_backup_written_over_an_existing_one_never_leaves_a_half_file()
    {
        Populate(_a);
        string path = Path.Combine(_a.Paths.Backups, $"fixed.{GameIdentity.BackupExtension}");

        _a.Backups.Create(path);
        long first = new FileInfo(path).Length;

        _a.Backups.Create(path);

        // Written through AtomicFile, so the target is either the old backup or the new
        // one and never a partial mix of the two.
        new FileInfo(path).Length.Should().BeGreaterThan(0);
        _a.Backups.Inspect(path).Counts.Scores.Should().Be(1);
        first.Should().BeGreaterThan(0);
    }
}
