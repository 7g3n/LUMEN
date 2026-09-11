using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Core;
using Lumen.Core.Charts;

namespace Lumen.Data.Packages;

/// <summary>
/// `.lumen` — a song, its charts and its audio in one file (spec §55–57).
///
/// A ZIP with a manifest, because the point of the format is that one file can be handed
/// to someone else and just work: the container has to survive email, cloud storage and
/// a decade, and it has to be openable by a human with a zip tool when something goes
/// wrong. Every entry carries a SHA-256 in the manifest, so a truncated download is
/// reported as damage rather than imported as a broken chart.
/// </summary>
public static class LumenPackage
{
    public const int FormatVersion = 1;
    public const string ManifestEntry = "manifest.json";
    public const string ChartsFolder = "charts/";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // --- manifest ---

    public sealed class Manifest
    {
        public int FormatVersion { get; set; } = LumenPackage.FormatVersion;
        public PackageInfo Package { get; set; } = new();
        public FileEntry? Audio { get; set; }
        public FileEntry? Cover { get; set; }
        public List<ChartEntry> Charts { get; set; } = new();
    }

    public sealed class PackageInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Creator { get; set; } = "";
        public string CreatedUtc { get; set; } = "";
        public string LumenVersion { get; set; } = GameIdentity.Version;
    }

    public sealed class FileEntry
    {
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public double DurationMs { get; set; }
    }

    public sealed class ChartEntry
    {
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string DifficultyName { get; set; } = "";
        public double DifficultyLevel { get; set; }
        public int NoteCount { get; set; }
    }

    // --- writing ---

    public sealed record Contents(
        IReadOnlyList<Chart> Charts, string AudioPath, string? CoverPath);

    /// <summary>
    /// Writes a package to <paramref name="targetPath"/>.
    ///
    /// Built in memory and then written through <see cref="AtomicFile"/>: a package is
    /// the thing an author is about to send somebody, and half of one that happens to
    /// open is worse than none at all.
    /// </summary>
    public static void Write(string targetPath, Contents contents)
    {
        if (contents.Charts.Count == 0)
        {
            throw new InvalidOperationException("A package needs at least one chart.");
        }

        if (!File.Exists(contents.AudioPath))
        {
            throw new FileNotFoundException("The song's audio is missing.", contents.AudioPath);
        }

        Chart first = contents.Charts[0];
        string audioName = Path.GetFileName(contents.AudioPath);
        byte[] audio = File.ReadAllBytes(contents.AudioPath);

        var manifest = new Manifest
        {
            Package = new PackageInfo
            {
                Title = first.Meta.Title,
                Artist = first.Meta.Artist,
                Creator = first.Meta.Creator,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            },
            Audio = new FileEntry
            {
                File = audioName,
                Sha256 = Hash(audio),
                DurationMs = first.Meta.DurationMs,
            },
        };

        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteBytes(zip, audioName, audio);

            if (contents.CoverPath is { Length: > 0 } cover && File.Exists(cover))
            {
                byte[] bytes = File.ReadAllBytes(cover);
                string name = Path.GetFileName(cover);
                WriteBytes(zip, name, bytes);
                manifest.Cover = new FileEntry { File = name, Sha256 = Hash(bytes) };
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Chart chart in contents.Charts)
            {
                // Charts inside the package reference the audio by the name it has here,
                // whatever it was called on the author's disk.
                Chart packaged = chart with
                {
                    Meta = chart.Meta with
                    {
                        AudioFile = audioName,
                        CoverFile = manifest.Cover?.File,
                    },
                };

                string name = ChartsFolder + UniqueName(used, packaged.Meta.DifficultyName);
                byte[] bytes = Encoding.UTF8.GetBytes(ChartJson.Serialize(packaged));
                WriteBytes(zip, name, bytes);

                manifest.Charts.Add(new ChartEntry
                {
                    File = name,
                    Sha256 = Hash(bytes),
                    DifficultyName = packaged.Meta.DifficultyName,
                    DifficultyLevel = packaged.Meta.DifficultyLevel,
                    NoteCount = packaged.Notes.Count,
                });
            }

            WriteBytes(zip, ManifestEntry,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Json)));
        }

        AtomicFile.WriteAllBytes(targetPath, buffer.ToArray());
    }

    // --- reading ---

    public sealed record Opened(
        Manifest Manifest, IReadOnlyList<Chart> Charts, byte[] Audio, byte[]? Cover)
    {
        public string AudioFileName => Manifest.Audio?.File ?? "";

        public string? CoverFileName => Manifest.Cover?.File;
    }

    /// <summary>
    /// Reads and verifies a package. Throws <see cref="InvalidDataException"/> with a
    /// player-readable message when the file is not a package, is damaged, or was written
    /// by a newer build.
    /// </summary>
    public static Opened Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    public static Opened Read(Stream stream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException($"This is not a {GameIdentity.Name} package.");
        }

        using (zip)
        {
            Manifest manifest = ReadManifest(zip);

            if (manifest.FormatVersion > FormatVersion)
            {
                throw new InvalidDataException(
                    $"This package was made with a newer version of {GameIdentity.Name} " +
                    $"(package format v{manifest.FormatVersion}; this build reads up to " +
                    $"v{FormatVersion}).");
            }

            byte[] audio = ReadVerified(zip, manifest.Audio?.File, manifest.Audio?.Sha256,
                "the song's audio");

            byte[]? cover = manifest.Cover is null
                ? null
                : ReadVerified(zip, manifest.Cover.File, manifest.Cover.Sha256, "the cover art");

            var charts = new List<Chart>();
            foreach (ChartEntry entry in manifest.Charts)
            {
                byte[] bytes = ReadVerified(zip, entry.File, entry.Sha256,
                    $"the {entry.DifficultyName} chart");

                try
                {
                    charts.Add(ChartJson.Deserialize(Encoding.UTF8.GetString(bytes)));
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"The {entry.DifficultyName} chart in this package could not be read: " +
                        ex.Message);
                }
            }

            if (charts.Count == 0)
            {
                throw new InvalidDataException("This package contains no charts.");
            }

            return new Opened(manifest, charts, audio, cover);
        }
    }

    /// <summary>Manifest only, for showing what is inside before committing to an import.</summary>
    public static Manifest Inspect(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return ReadManifest(zip);
    }

    // --- helpers ---

    private static Manifest ReadManifest(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.GetEntry(ManifestEntry)
                                ?? throw new InvalidDataException(
                                    $"This is not a {GameIdentity.Name} package — it has no manifest.");

        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        try
        {
            return JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd(), Json)
                   ?? throw new InvalidDataException("This package's manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"This package's manifest is damaged: {ex.Message}");
        }
    }

    private static byte[] ReadVerified(ZipArchive zip, string? name, string? expectedHash, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException($"This package is missing {what}.");
        }

        ZipArchiveEntry entry = zip.GetEntry(name)
                                ?? throw new InvalidDataException(
                                    $"This package is missing {what} ({name}).");

        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();

        if (!string.IsNullOrEmpty(expectedHash) && !Hash(bytes).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"This package is damaged — {what} failed its checksum.");
        }

        return bytes;
    }

    private static void WriteBytes(ZipArchive zip, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string UniqueName(HashSet<string> used, string difficultyName)
    {
        string stem = Slug(difficultyName);
        string name = stem + "." + GameIdentity.ChartExtension;

        int suffix = 2;
        while (!used.Add(name))
        {
            name = $"{stem}-{suffix++}.{GameIdentity.ChartExtension}";
        }

        return name;
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        string slug = new string(chars).Trim('-');

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Length > 0 ? slug : "chart";
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
