using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Core.Library;
using Lumen.Data.Library;

namespace Lumen.Data.Packages;

/// <summary>
/// Export and import of `.lumen` packages against this installation (spec §55–57).
///
/// Import is deliberately additive and idempotent-ish: audio and charts are copied in
/// under names derived from the package, an existing file of the same name is reused
/// rather than duplicated, and the library is rescanned afterwards so the new charts
/// appear exactly as if the author had made them here.
/// </summary>
public sealed class PackageService
{
    private readonly LumenPaths _paths;
    private readonly LibraryService _library;

    public PackageService(LumenPaths paths, LibraryService library)
    {
        _paths = paths;
        _library = library;
    }

    // --- export ---

    public sealed record ExportResult(string Path, int ChartCount, long SizeBytes);

    /// <summary>
    /// Bundles the given charts - which must share a song - with their audio.
    /// Validation runs first: a package that cannot be played by whoever receives it is
    /// not worth sending (§92).
    /// </summary>
    public ExportResult Export(IReadOnlyList<LibraryChart> charts, string? targetPath = null)
    {
        if (charts.Count == 0)
        {
            throw new InvalidOperationException("Select at least one chart to export.");
        }

        var loaded = new List<Chart>();
        foreach (LibraryChart entry in charts)
        {
            Chart chart = ChartJson.Deserialize(File.ReadAllText(entry.ChartPath));
            ValidationReport report = ChartValidator.Validate(chart);

            if (!report.CanExport)
            {
                ValidationIssue first = report.Issues.First(i => i.Severity == IssueSeverity.Error);
                throw new InvalidOperationException(
                    $"{entry.Meta.DifficultyName} cannot be exported yet: {first.Message}");
            }

            loaded.Add(chart);
        }

        LibraryChart source = charts[0];
        if (source.AudioPath.Length == 0 || !File.Exists(source.AudioPath))
        {
            throw new FileNotFoundException(
                "The audio for this song could not be found.", source.AudioPath);
        }

        Directory.CreateDirectory(_paths.Exports);
        string path = targetPath ?? Path.Combine(
            _paths.Exports, $"{Slug(source.Meta.Title)}.{GameIdentity.PackageExtension}");

        LumenPackage.Write(path, new LumenPackage.Contents(loaded, source.AudioPath, CoverFor(source)));

        Log.Info($"exported {loaded.Count} chart(s) to {Path.GetFileName(path)}");
        return new ExportResult(path, loaded.Count, new FileInfo(path).Length);
    }

    private string? CoverFor(LibraryChart chart)
    {
        if (chart.Meta.CoverFile is not { Length: > 0 } cover)
        {
            return null;
        }

        string beside = Path.Combine(Path.GetDirectoryName(chart.ChartPath) ?? "", cover);
        if (File.Exists(beside))
        {
            return beside;
        }

        string inSongs = Path.Combine(_paths.Songs, cover);
        return File.Exists(inSongs) ? inSongs : null;
    }

    // --- import ---

    public sealed record ImportResult(
        string Title, string Artist, IReadOnlyList<string> Difficulties, int Added);

    /// <summary>
    /// Unpacks a package into this installation and rescans the library.
    /// Throws <see cref="InvalidDataException"/> with a readable message when the file is
    /// not a package or is damaged.
    /// </summary>
    public ImportResult Import(string packagePath)
    {
        LumenPackage.Opened package = LumenPackage.Read(packagePath);

        Directory.CreateDirectory(_paths.Songs);
        Directory.CreateDirectory(_paths.ChartsImported);

        string audioName = UniqueFileName(_paths.Songs, package.AudioFileName, package.Audio);
        string audioPath = Path.Combine(_paths.Songs, audioName);
        if (!File.Exists(audioPath))
        {
            AtomicFile.WriteAllBytes(audioPath, package.Audio);
        }

        string? coverName = null;
        if (package.Cover is { } cover && package.CoverFileName is { Length: > 0 } name)
        {
            coverName = UniqueFileName(_paths.Songs, name, cover);
            string coverPath = Path.Combine(_paths.Songs, coverName);
            if (!File.Exists(coverPath))
            {
                AtomicFile.WriteAllBytes(coverPath, cover);
            }
        }

        var difficulties = new List<string>();
        int added = 0;

        foreach (Chart chart in package.Charts)
        {
            Chart landed = chart with
            {
                Meta = chart.Meta with { AudioFile = audioName, CoverFile = coverName },
            };

            string stem = $"{Slug(landed.Meta.Title)}-{Slug(landed.Meta.DifficultyName)}";
            string file = Path.Combine(
                _paths.ChartsImported, $"{stem}.{GameIdentity.ChartExtension}");

            // Two packages can legitimately contain the same difficulty name for the same
            // title; the second one gets its own file rather than overwriting the first.
            int suffix = 2;
            while (File.Exists(file) && !SameChart(file, landed))
            {
                file = Path.Combine(
                    _paths.ChartsImported, $"{stem}-{suffix++}.{GameIdentity.ChartExtension}");
            }

            AtomicFile.WriteAllText(file, ChartJson.Serialize(landed));
            difficulties.Add(landed.Meta.DifficultyName);
            added++;
        }

        _library.Scan();

        Log.Info($"imported {added} chart(s) from {Path.GetFileName(packagePath)}");
        return new ImportResult(
            package.Manifest.Package.Title, package.Manifest.Package.Artist, difficulties, added);
    }

    /// <summary>Manifest only, so the player can be told what they are about to import.</summary>
    public LumenPackage.Manifest Inspect(string packagePath) => LumenPackage.Inspect(packagePath);

    // --- helpers ---

    /// <summary>
    /// Reuses an existing file when it is byte-identical, and picks a fresh name when it
    /// is not. Importing the same package twice therefore costs no extra disk, while two
    /// different songs that happen to be called <c>song.wav</c> stay separate.
    /// </summary>
    private static string UniqueFileName(string directory, string preferred, byte[] contents)
    {
        if (string.IsNullOrWhiteSpace(preferred))
        {
            preferred = "audio.wav";
        }

        string stem = Path.GetFileNameWithoutExtension(preferred);
        string extension = Path.GetExtension(preferred);
        string name = preferred;
        int suffix = 2;

        while (File.Exists(Path.Combine(directory, name)))
        {
            if (LumenPackage.Hash(File.ReadAllBytes(Path.Combine(directory, name)))
                == LumenPackage.Hash(contents))
            {
                return name;
            }

            name = $"{stem}-{suffix++}{extension}";
        }

        return name;
    }

    private static bool SameChart(string path, Chart chart)
    {
        try
        {
            Chart existing = ChartJson.Deserialize(File.ReadAllText(path));
            return existing.Id is not null && existing.Id == chart.Id;
        }
        catch
        {
            return false;
        }
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

        return slug.Length > 0 ? slug : "song";
    }
}
