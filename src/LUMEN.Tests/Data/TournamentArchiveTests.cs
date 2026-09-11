using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Tournaments;
using Lumen.Data;
using Lumen.Data.Repositories;
using Lumen.Data.Tournaments;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// A tournament taken out of one installation and put into another (spec: Tournament
/// Export / Import).
/// </summary>
public class TournamentArchiveTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _here;
    private readonly Database _elsewhere;
    private readonly TournamentRepository _hereRepo;
    private readonly TournamentRepository _elsewhereRepo;
    private readonly TournamentService _service;

    private const string Chart = "chart-key-first-light";

    public TournamentArchiveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-arc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _here = Open("a");
        _elsewhere = Open("b");

        _hereRepo = new TournamentRepository(_here);
        _elsewhereRepo = new TournamentRepository(_elsewhere);
        _service = new TournamentService(_hereRepo);
    }

    private Database Open(string name)
    {
        var db = new Database(Path.Combine(_dir, name, GameIdentity.DatabaseFileName));
        db.Open();
        return db;
    }

    public void Dispose()
    {
        _here.Dispose();
        _elsewhere.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Path_(string name) => System.IO.Path.Combine(_dir, name);

    /// <summary>A tournament that has actually been played, so there is something to carry.</summary>
    private (Tournament Tournament, List<Guid> Players) Played()
    {
        Tournament tournament = _service.Create(
            "Winter Open", TournamentFormat.SingleElimination, TournamentRules.Official, "LUMEN");

        var players = new List<Guid>();
        for (int i = 1; i <= 2; i++)
        {
            var id = Guid.NewGuid();
            _service.AddParticipant(tournament.Id, id, $"Player {i}", i);
            players.Add(id);
        }

        _service.AddSong(tournament.Id, new TournamentSong
        {
            TournamentId = tournament.Id,
            ChartKey = Chart,
            Title = "First Light",
            DifficultyName = "NORMAL",
            Level = 3.5,
            ChartHash = Chart,
        });

        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        _service.SelectSongs(match.Id, new[] { Chart });
        _service.MarkReady(match.Id);
        _service.StartMatch(match.Id);

        TournamentMatchResult win = _service.SubmitResult(Result(match.Id, players[0], 990_000));
        TournamentMatchResult loss = _service.SubmitResult(Result(match.Id, players[1], 900_000));

        _service.ConfirmResult(win.Id, match.Id);
        _service.ConfirmResult(loss.Id, match.Id);

        return (_service.Get(tournament.Id)!, players);
    }

    private static TournamentMatchResult Result(Guid matchId, Guid player, long score) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        PlayerId = player,
        ChartKey = Chart,
        Score = score,
        Accuracy = score / 10_000.0,
        MaxCombo = 96,
        Perfect = 96,
        ChartHash = Chart,
    };

    // --- export ---

    [Fact]
    public void Exporting_writes_a_file_that_describes_itself()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("winter.lumentourney");

        TournamentArchive.Manifest manifest = TournamentArchive.Export(_hereRepo, tournament.Id, file);

        File.Exists(file).Should().BeTrue();
        manifest.TournamentId.Should().Be(tournament.Id);
        manifest.Name.Should().Be("Winter Open");
        manifest.Participants.Should().Be(2);
        manifest.Results.Should().Be(2);
        manifest.Events.Should().BeGreaterThan(5);
        manifest.LumenVersion.Should().Be(GameIdentity.FullVersion);
    }

    [Fact]
    public void The_archive_carries_the_log_as_well_as_the_results()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("winter.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        using ZipArchive zip = ZipFile.OpenRead(file);

        zip.Entries.Select(e => e.FullName).Should().Contain(
            "manifest.json", "tournament.json", "bracket.json", "results.json", "events.json");
    }

    [Fact]
    public void A_finished_export_leaves_no_temporary_behind()
    {
        (Tournament tournament, _) = Played();
        TournamentArchive.Export(_hereRepo, tournament.Id, Path_("winter.lumentourney"));

        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void A_tournament_that_is_not_there_cannot_be_exported()
    {
        Action export = () => TournamentArchive.Export(_hereRepo, Guid.NewGuid(), Path_("x.lumentourney"));

        export.Should().Throw<InvalidOperationException>();
    }

    // --- import ---

    /// <summary>
    /// The whole point: an event run on one machine, opened on another, with the bracket,
    /// the results and the log of how it got there.
    /// </summary>
    [Fact]
    public void A_tournament_exported_here_opens_there()
    {
        (Tournament tournament, List<Guid> players) = Played();
        string file = Path_("winter.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Tournament imported = TournamentArchive.Import(_elsewhereRepo, file);

        imported.Id.Should().Be(tournament.Id);
        imported.Name.Should().Be("Winter Open");
        imported.Status.Should().Be(TournamentStatus.Complete);
        imported.WinnerPlayerId.Should().Be(players[0]);
        imported.Rules.MaxAttempts.Should().Be(1, "the rules travelled with it");
        imported.RuleHash.Should().Be(tournament.RuleHash);

        _elsewhereRepo.Participants(imported.Id).Should().HaveCount(2);
        _elsewhereRepo.Songs(imported.Id).Should().ContainSingle();
        _elsewhereRepo.Matches(imported.Id).Should().ContainSingle();
        _elsewhereRepo.Results(imported.Id).Should().HaveCount(2);
        _elsewhereRepo.Events(imported.Id).Should().NotBeEmpty();
    }

    [Fact]
    public void Confirmed_results_are_still_confirmed_on_the_other_machine()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("winter.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Tournament imported = TournamentArchive.Import(_elsewhereRepo, file);

        _elsewhereRepo.Results(imported.Id).Should().OnlyContain(r => r.IsConfirmed);
    }

    [Fact]
    public void The_winner_of_the_imported_match_is_the_one_who_won_it()
    {
        (Tournament tournament, List<Guid> players) = Played();
        string file = Path_("winter.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Tournament imported = TournamentArchive.Import(_elsewhereRepo, file);

        _elsewhereRepo.Matches(imported.Id).Single()
            .WinnerPlayerId.Should().Be(players[0]);
    }

    /// <summary>
    /// Merging two versions of a competition is not something software can do correctly:
    /// if the local copy and the file disagree about a match, only a person knows which is
    /// right. So it refuses rather than guessing.
    /// </summary>
    [Fact]
    public void Importing_one_that_is_already_here_is_refused_rather_than_merged()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("winter.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Action again = () => TournamentArchive.Import(_hereRepo, file);

        again.Should().Throw<InvalidOperationException>().WithMessage("*already on this machine*");
    }

    // --- refusing bad files ---

    [Fact]
    public void A_file_that_is_not_a_tournament_is_refused_readably()
    {
        string file = Path_("notes.lumentourney");
        File.WriteAllText(file, "this is not a zip at all");

        Action read = () => TournamentArchive.ReadManifest(file);

        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_zip_with_no_manifest_is_refused()
    {
        string file = Path_("empty.lumentourney");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Action read = () => TournamentArchive.ReadManifest(file);

        read.Should().Throw<InvalidDataException>().WithMessage("*not a LUMEN tournament export*");
    }

    [Fact]
    public void A_file_that_is_not_there_says_so()
    {
        Action read = () => TournamentArchive.ReadManifest(Path_("nothing.lumentourney"));

        read.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// An archive from a future build is refused by name rather than half-read. Guessing
    /// at a format you do not know is how you import a tournament that is quietly wrong.
    /// </summary>
    [Fact]
    public void An_archive_from_a_newer_build_is_refused()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("future.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Rewrite(file, "manifest.json", json =>
            json.Replace($"\"formatVersion\": {TournamentArchive.FormatVersion}",
                         "\"formatVersion\": 99"));

        Action read = () => TournamentArchive.ReadManifest(file);

        read.Should().Throw<InvalidDataException>().WithMessage("*newer version*");
    }

    [Fact]
    public void An_archive_with_results_for_a_match_that_is_not_in_it_is_refused()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("tampered.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Rewrite(file, "bracket.json", _ => "{\"rounds\":[],\"matches\":[]}");

        Action import = () => TournamentArchive.Import(_elsewhereRepo, file);

        import.Should().Throw<InvalidDataException>().WithMessage("*not in the file*");
    }

    [Fact]
    public void An_archive_with_no_tournament_in_it_is_refused()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("hollow.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Rewrite(file, "tournament.json", _ => "{\"participants\":[],\"songs\":[]}");

        Action import = () => TournamentArchive.Import(_elsewhereRepo, file);

        import.Should().Throw<InvalidDataException>().WithMessage("*no tournament*");
    }

    /// <summary>A refused import must leave nothing behind to clean up.</summary>
    [Fact]
    public void A_refused_import_writes_nothing()
    {
        (Tournament tournament, _) = Played();
        string file = Path_("tampered.lumentourney");
        TournamentArchive.Export(_hereRepo, tournament.Id, file);

        Rewrite(file, "bracket.json", _ => "{\"rounds\":[],\"matches\":[]}");

        try
        {
            TournamentArchive.Import(_elsewhereRepo, file);
        }
        catch (InvalidDataException)
        {
            // expected
        }

        _elsewhereRepo.All().Should().BeEmpty();
    }

    /// <summary>Replaces one entry of a zip, for the tampering cases above.</summary>
    private static void Rewrite(string path, string entryName, Func<string, string> edit)
    {
        var entries = new Dictionary<string, string>();

        using (ZipArchive read = ZipFile.OpenRead(path))
        {
            foreach (ZipArchiveEntry entry in read.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                entries[entry.FullName] = reader.ReadToEnd();
            }
        }

        entries[entryName] = edit(entries[entryName]);

        File.Delete(path);

        using ZipArchive write = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string body) in entries)
        {
            using var writer = new StreamWriter(write.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(body);
        }
    }
}
