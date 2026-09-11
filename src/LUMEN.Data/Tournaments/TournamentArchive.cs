using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Core.Tournaments;

namespace Lumen.Data.Tournaments;

/// <summary>
/// Taking a tournament out of the game and putting one back (spec: Tournament Export /
/// Import).
///
/// A ZIP holding a logical dump — the tournament, its entrants, its pool, its bracket, its
/// results and its whole event log — rather than a copy of database rows. Logical because
/// the archive outlives the schema: an event exported today should still open on a build
/// whose tables have moved on, and a row dump would not.
///
/// The log travels with it deliberately. A results file on its own is a claim; a results
/// file with the sequence of decisions that produced it, the build it ran on and the rules
/// that were in force is something somebody else can check.
/// </summary>
public static class TournamentArchive
{
    public const int FormatVersion = 1;

    public const string Extension = "lumentourney";

    private const string ManifestEntry = "manifest.json";
    private const string TournamentEntry = "tournament.json";
    private const string ResultsEntry = "results.json";
    private const string BracketEntry = "bracket.json";
    private const string EventsEntry = "events.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>What the archive says about itself, before anything is trusted.</summary>
    public sealed class Manifest
    {
        public int FormatVersion { get; set; } = TournamentArchive.FormatVersion;

        public string LumenVersion { get; set; } = GameIdentity.FullVersion;

        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

        public Guid TournamentId { get; set; }

        public string Name { get; set; } = "";

        public int Participants { get; set; }

        public int Matches { get; set; }

        public int Results { get; set; }

        public int Events { get; set; }
    }

    private sealed class Bundle
    {
        public Tournament? Tournament { get; set; }

        public List<TournamentParticipant> Participants { get; set; } = new();

        public List<TournamentSong> Songs { get; set; } = new();
    }

    private sealed class BracketBundle
    {
        public List<TournamentRound> Rounds { get; set; } = new();

        public List<TournamentMatch> Matches { get; set; } = new();
    }

    // --- export ---

    /// <summary>Writes a tournament to <paramref name="path"/>, atomically.</summary>
    public static Manifest Export(ITournamentRepository repo, Guid tournamentId, string path)
    {
        Tournament tournament = repo.Get(tournamentId)
            ?? throw new InvalidOperationException("That tournament does not exist.");

        IReadOnlyList<TournamentParticipant> participants = repo.Participants(tournamentId);
        IReadOnlyList<TournamentRound> rounds = repo.Rounds(tournamentId);
        IReadOnlyList<TournamentMatch> matches = repo.Matches(tournamentId);
        IReadOnlyList<TournamentMatchResult> results = repo.Results(tournamentId);
        IReadOnlyList<TournamentEvent> events = repo.Events(tournamentId);

        var manifest = new Manifest
        {
            TournamentId = tournamentId,
            Name = tournament.Name,
            Participants = participants.Count,
            Matches = matches.Count,
            Results = results.Count,
            Events = events.Count,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Written beside the destination and renamed into place, like every other file the
        // game writes: an export interrupted halfway leaves debris, never a half-archive
        // that looks importable.
        string temp = path + ".tmp";

        try
        {
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(zip, ManifestEntry, manifest);
                Write(zip, TournamentEntry, new Bundle
                {
                    Tournament = tournament,
                    Participants = participants.ToList(),
                    Songs = repo.Songs(tournamentId).ToList(),
                });
                Write(zip, BracketEntry, new BracketBundle
                {
                    Rounds = rounds.ToList(),
                    Matches = matches.ToList(),
                });
                Write(zip, ResultsEntry, results.ToList());
                Write(zip, EventsEntry, events.ToList());
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }

        Log.Info($"tournament exported: {tournament.Name} -> {path}");
        return manifest;
    }

    private static void Write<T>(ZipArchive zip, string name, T value)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(value, Json));
    }

    // --- inspection ---

    /// <summary>
    /// Reads the manifest without importing anything, so a file can be described — and
    /// refused — before it is allowed to touch the database.
    /// </summary>
    public static Manifest ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("That tournament file is not there.", path);
        }

        using var zip = ZipFile.OpenRead(path);

        ZipArchiveEntry entry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException(
                "That file has no tournament manifest in it, so it is not a LUMEN tournament export.");

        Manifest manifest = Read<Manifest>(entry)
            ?? throw new InvalidDataException("The tournament manifest could not be read.");

        if (manifest.FormatVersion > FormatVersion)
        {
            throw new InvalidDataException(
                $"That tournament was exported by a newer version of {GameIdentity.Name} " +
                $"(format {manifest.FormatVersion}; this build reads {FormatVersion}).");
        }

        return manifest;
    }

    private static T? Read<T>(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Json);
    }

    // --- import ---

    /// <summary>
    /// Brings an exported tournament in.
    ///
    /// A tournament that is already here is left alone rather than merged over. Merging two
    /// versions of a competition is not something software can do correctly: if the local
    /// copy and the file disagree about a match, only a person knows which is right.
    /// </summary>
    public static Tournament Import(ITournamentRepository repo, string path)
    {
        Manifest manifest = ReadManifest(path);

        using var zip = ZipFile.OpenRead(path);

        Bundle bundle = Read<Bundle>(Require(zip, TournamentEntry))
            ?? throw new InvalidDataException("The tournament could not be read from that file.");

        Tournament tournament = bundle.Tournament
            ?? throw new InvalidDataException("That file has no tournament in it.");

        if (repo.Get(tournament.Id) is not null)
        {
            throw new InvalidOperationException(
                $"\"{tournament.Name}\" is already on this machine. " +
                "Delete it first if you mean to replace it.");
        }

        BracketBundle bracket = Read<BracketBundle>(Require(zip, BracketEntry)) ?? new BracketBundle();
        var results = Read<List<TournamentMatchResult>>(Require(zip, ResultsEntry)) ?? new();
        var events = Read<List<TournamentEvent>>(Require(zip, EventsEntry)) ?? new();

        Validate(tournament, bundle, bracket, results);

        repo.Create(tournament);

        foreach (TournamentParticipant participant in bundle.Participants)
        {
            repo.AddParticipant(participant with { TournamentId = tournament.Id });
        }

        foreach (TournamentSong song in bundle.Songs)
        {
            repo.AddSong(song with { TournamentId = tournament.Id });
        }

        repo.SaveBracket(tournament.Id, new Bracket(bracket.Rounds, bracket.Matches));

        foreach (TournamentMatchResult result in results)
        {
            repo.SubmitResult(result);

            if (result.ConfirmedUtc is { } confirmed)
            {
                repo.ConfirmResult(result.Id, confirmed);
            }
        }

        foreach (TournamentEvent recorded in events)
        {
            repo.Record(recorded with { TournamentId = tournament.Id });
        }

        // The tournament as stored may differ from the manifest's own count if the file was
        // edited; re-reading it is the honest thing to report.
        Log.Info($"tournament imported: {tournament.Name} " +
                 $"({bundle.Participants.Count} players, {results.Count} results)");

        return repo.Get(tournament.Id) ?? tournament;
    }

    private static ZipArchiveEntry Require(ZipArchive zip, string name) =>
        zip.GetEntry(name)
        ?? throw new InvalidDataException($"That tournament file is missing {name} and cannot be read.");

    /// <summary>
    /// Refuses an archive that does not hang together.
    ///
    /// Not security — a file somebody hands you can say anything — but the difference
    /// between failing at the door with a sentence and failing halfway through an import
    /// with a foreign key error and a half-written tournament.
    /// </summary>
    private static void Validate(
        Tournament tournament,
        Bundle bundle,
        BracketBundle bracket,
        List<TournamentMatchResult> results)
    {
        if (tournament.Id == Guid.Empty)
        {
            throw new InvalidDataException("That tournament has no identity.");
        }

        if (string.IsNullOrWhiteSpace(tournament.Name))
        {
            throw new InvalidDataException("That tournament has no name.");
        }

        var roundIds = bracket.Rounds.Select(r => r.Id).ToHashSet();
        if (bracket.Matches.Any(m => !roundIds.Contains(m.RoundId)))
        {
            throw new InvalidDataException(
                "That tournament has matches belonging to rounds that are not in the file.");
        }

        var matchIds = bracket.Matches.Select(m => m.Id).ToHashSet();
        if (results.Any(r => !matchIds.Contains(r.MatchId)))
        {
            throw new InvalidDataException(
                "That tournament has results for matches that are not in the file.");
        }

        var players = bundle.Participants.Select(p => p.PlayerId).ToHashSet();
        if (results.Any(r => !players.Contains(r.PlayerId)))
        {
            throw new InvalidDataException(
                "That tournament has results from players who were never entered in it.");
        }

        if (bundle.Participants.Select(p => p.PlayerId).Distinct().Count() != bundle.Participants.Count)
        {
            throw new InvalidDataException("That tournament has the same player entered twice.");
        }
    }
}
