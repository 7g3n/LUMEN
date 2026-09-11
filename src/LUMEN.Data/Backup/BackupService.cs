using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Data.Library;

namespace Lumen.Data.Backup;

/// <summary>
/// `.lumenbackup` — everything a player would lose by changing computer (spec §12–13).
///
/// A ZIP holding a logical dump of the database plus the files it points at: charts,
/// songs, replays and the settings snapshots. Logical rather than a copy of the database
/// file, so a backup taken today still restores on a build whose schema has moved on.
///
/// Restore merges rather than replaces. On a clean machine that produces an identical
/// installation, which is the migration case; on a machine that already has data it adds
/// to it, which is the only safe reading of "import" when the alternative is silently
/// destroying whatever was there.
/// </summary>
public sealed class BackupService
{
    public const int FormatVersion = 1;
    public const string ManifestEntry = "manifest.json";
    public const string DataEntry = "database.json";

    /// <summary>Automatic backups older than this many are removed.</summary>
    public const int AutoBackupsKept = 10;

    public static readonly TimeSpan AutoBackupInterval = TimeSpan.FromDays(1);

    private const string LastBackupKey = "last_backup_utc";
    private const string LastBackupVersionKey = "last_backup_version";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Database _db;
    private readonly LumenPaths _paths;
    private readonly AppMetaStore _appMeta;
    private readonly LibraryService? _library;

    public BackupService(Database db, LumenPaths paths, AppMetaStore appMeta,
                         LibraryService? library = null)
    {
        _db = db;
        _paths = paths;
        _appMeta = appMeta;
        _library = library;
    }

    // --- manifest ---

    public sealed class Manifest
    {
        public int FormatVersion { get; set; } = BackupService.FormatVersion;
        public string LumenVersion { get; set; } = GameIdentity.Version;
        public int SchemaVersion { get; set; }
        public string CreatedUtc { get; set; } = "";
        public string InstallId { get; set; } = "";
        public Counts Counts { get; set; } = new();
        public List<string> Profiles { get; set; } = new();
    }

    public sealed class Counts
    {
        public int Profiles { get; set; }
        public int Scores { get; set; }
        public int Charts { get; set; }
        public int Replays { get; set; }
        public int Files { get; set; }
    }

    // --- creating ---

    public sealed record BackupInfo(string Path, string FileName, DateTime CreatedUtc, long SizeBytes);

    /// <summary>`lumen-backup-2026-09-09-143005.lumenbackup`</summary>
    public static string FileNameFor(DateTime whenLocal) =>
        $"{GameIdentity.Slug}-backup-{whenLocal:yyyy-MM-dd-HHmmss}.{GameIdentity.BackupExtension}";

    /// <summary>
    /// Writes a backup. <paramref name="targetPath"/> defaults to a timestamped file in
    /// the backups folder; export chooses a name in the exports folder instead.
    /// </summary>
    public BackupInfo Create(string? targetPath = null)
    {
        string path = targetPath ?? Path.Combine(_paths.Backups, FileNameFor(DateTime.Now));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject dump = DatabaseDump.Export(_db);
        var manifest = new Manifest
        {
            SchemaVersion = _db.SchemaVersion,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            InstallId = _appMeta.InstallId,
        };

        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, DataEntry, Encoding.UTF8.GetBytes(DatabaseDump.ToJson(dump)));

            int files = 0;
            files += AddFolder(zip, _paths.ChartsLocal, "charts/local/");
            files += AddFolder(zip, _paths.ChartsImported, "charts/imported/");
            files += AddFolder(zip, _paths.Songs, "songs/");
            files += AddFolder(zip, _paths.Replays, "replays/");
            files += AddFolder(zip, _paths.Settings, "settings/");

            manifest.Counts = new Counts
            {
                Profiles = RowCount(dump, "profiles"),
                Scores = RowCount(dump, "scores"),
                Charts = RowCount(dump, "charts"),
                Replays = RowCount(dump, "replays"),
                Files = files,
            };
            manifest.Profiles = ProfileNames(dump);

            Write(zip, ManifestEntry,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Json)));
        }

        // Built in memory and placed atomically: a half-written backup that happens to
        // open is worse than no backup, because it is the one a player reaches for.
        AtomicFile.WriteAllBytes(path, buffer.ToArray());

        _appMeta.Set(LastBackupKey, manifest.CreatedUtc);
        _appMeta.Set(LastBackupVersionKey, GameIdentity.Version);

        var info = new BackupInfo(path, Path.GetFileName(path),
            DateTime.Parse(manifest.CreatedUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
            new FileInfo(path).Length);

        Log.Info($"backup written: {info.FileName} ({info.SizeBytes / 1024:N0} KB, " +
                 $"{manifest.Counts.Scores} scores, {manifest.Counts.Files} files)");
        return info;
    }

    // --- automatic ---

    /// <summary>
    /// Takes a backup when one is due: nothing yet today, or the game has been updated
    /// since the last one. A version change matters because that is when a migration is
    /// about to touch the database, and the moment before is exactly when a player wants
    /// a copy of what they had.
    /// </summary>
    public BackupInfo? AutoBackupIfDue(DateTime? nowUtc = null)
    {
        DateTime now = nowUtc ?? DateTime.UtcNow;

        if (!IsAutoBackupDue(now))
        {
            return null;
        }

        try
        {
            BackupInfo info = Create();
            Prune(AutoBackupsKept);
            return info;
        }
        catch (Exception ex)
        {
            // A backup that cannot be written is worth a line in the log, not a refusal
            // to start the game.
            Log.Warn("automatic backup failed", ex);
            return null;
        }
    }

    public bool IsAutoBackupDue(DateTime? nowUtc = null)
    {
        DateTime now = nowUtc ?? DateTime.UtcNow;

        if (_appMeta.Get(LastBackupVersionKey) is { } version && version != GameIdentity.Version)
        {
            return true;
        }

        if (_appMeta.Get(LastBackupKey) is not { } last
            || !DateTime.TryParse(last, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime when))
        {
            return true;
        }

        return now - when.ToUniversalTime() >= AutoBackupInterval;
    }

    // --- listing ---

    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(_paths.Backups))
        {
            return Array.Empty<BackupInfo>();
        }

        return Directory
            .EnumerateFiles(_paths.Backups, $"*.{GameIdentity.BackupExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupInfo(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
            .ToArray();
    }

    /// <summary>Keeps the newest <paramref name="keep"/> backups in the backups folder.</summary>
    public int Prune(int keep)
    {
        int removed = 0;

        foreach (BackupInfo backup in List().Skip(Math.Max(0, keep)))
        {
            try
            {
                File.Delete(backup.Path);
                removed++;
            }
            catch (Exception ex)
            {
                Log.Warn($"could not remove old backup {backup.FileName}", ex);
            }
        }

        return removed;
    }

    // --- reading and restoring ---

    /// <summary>Manifest only, so a player can be told what they are about to restore.</summary>
    public Manifest Inspect(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var zip = OpenArchive(stream);
        return ReadManifest(zip);
    }

    public sealed record RestoreResult(Manifest Manifest, int Tables, int Rows, int FilesRestored);

    /// <summary>
    /// Merges a backup into this installation: files first, then the rows that point at
    /// them, all inside one transaction. A failure part-way leaves the database exactly as
    /// it was — a restored file with no row is invisible, which is the harmless direction.
    /// </summary>
    public RestoreResult Restore(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using ZipArchive zip = OpenArchive(stream);

        Manifest manifest = ReadManifest(zip);

        if (manifest.FormatVersion > FormatVersion)
        {
            throw new InvalidDataException(
                $"This backup was made with a newer version of {GameIdentity.Name} " +
                $"(backup format v{manifest.FormatVersion}; this build reads up to " +
                $"v{FormatVersion}).");
        }

        ZipArchiveEntry data = zip.GetEntry(DataEntry)
                               ?? throw new InvalidDataException(
                                   "This backup contains no database.");

        int files = RestoreFiles(zip);

        JsonObject dump;
        using (var reader = new StreamReader(data.Open(), Encoding.UTF8))
        {
            dump = DatabaseDump.FromJson(reader.ReadToEnd());
        }

        DatabaseDump.Result result = DatabaseDump.Import(_db, dump);

        // The library index is a cache of the chart files that just landed.
        _library?.Scan();

        Log.Info($"backup restored: {result.Rows} rows across {result.Tables} tables, {files} files");
        return new RestoreResult(manifest, result.Tables, result.Rows, files);
    }

    // --- internals ---

    private static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException($"This is not a {GameIdentity.Name} backup.");
        }
    }

    private static Manifest ReadManifest(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.GetEntry(ManifestEntry)
                                ?? throw new InvalidDataException(
                                    $"This is not a {GameIdentity.Name} backup — it has no manifest.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);

        try
        {
            return JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd(), Json)
                   ?? throw new InvalidDataException("This backup's manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"This backup's manifest is damaged: {ex.Message}");
        }
    }

    private int AddFolder(ZipArchive zip, string directory, string prefix)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            Write(zip, prefix + Path.GetFileName(file), File.ReadAllBytes(file));
            count++;
        }

        return count;
    }

    private int RestoreFiles(ZipArchive zip)
    {
        int restored = 0;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string? directory = TargetFolder(entry.FullName);
            if (directory is null)
            {
                continue;
            }

            // Entry names come from a file someone else made, so only the leaf is used —
            // a crafted "../../" name must not be able to write outside the data folder.
            string name = Path.GetFileName(entry.FullName);
            if (name.Length == 0)
            {
                continue;
            }

            Directory.CreateDirectory(directory);

            using Stream source = entry.Open();
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);

            AtomicFile.WriteAllBytes(Path.Combine(directory, name), buffer.ToArray());
            restored++;
        }

        return restored;
    }

    private string? TargetFolder(string entryName) => entryName switch
    {
        _ when entryName.StartsWith("charts/local/", StringComparison.Ordinal) => _paths.ChartsLocal,
        _ when entryName.StartsWith("charts/imported/", StringComparison.Ordinal) => _paths.ChartsImported,
        _ when entryName.StartsWith("songs/", StringComparison.Ordinal) => _paths.Songs,
        _ when entryName.StartsWith("replays/", StringComparison.Ordinal) => _paths.Replays,
        _ when entryName.StartsWith("settings/", StringComparison.Ordinal) => _paths.Settings,
        _ => null,
    };

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static int RowCount(JsonObject dump, string table) =>
        dump["tables"]?[table] is JsonArray rows ? rows.Count : 0;

    private static List<string> ProfileNames(JsonObject dump)
    {
        var names = new List<string>();

        if (dump["tables"]?["profiles"] is not JsonArray rows)
        {
            return names;
        }

        foreach (JsonNode? row in rows)
        {
            if (row?["display_name"] is { } name)
            {
                names.Add(name.ToString());
            }
        }

        return names;
    }
}
