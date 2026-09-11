using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Core.Library;

namespace Lumen.Data.Library;

/// <summary>
/// Walks the chart folders and brings the library index in line with what is on disk
/// (spec §59, §93).
///
/// The files are the source of truth and the index is a cache, so the scan is a full
/// reconciliation rather than an incremental log: every chart found is upserted, and any
/// row whose file has disappeared is dropped. That makes a chart added by hand - or one
/// deleted behind the game's back - behave the way a player expects on the next launch,
/// and it means a corrupt index can always be repaired by rescanning.
///
/// One unreadable chart never fails the scan. A file someone hand-edited into invalid
/// JSON is reported and skipped; the rest of the library still loads.
/// </summary>
public sealed class LibraryScanner
{
    private readonly ILibraryRepository _library;
    private readonly LumenPaths _paths;

    public LibraryScanner(ILibraryRepository library, LumenPaths paths)
    {
        _library = library;
        _paths = paths;
    }

    public sealed record Result(int Added, int Updated, int Removed, int Failed)
    {
        public int Total => Added + Updated;

        public bool ChangedAnything => Added > 0 || Updated > 0 || Removed > 0;
    }

    public Result Scan()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Every file this pass actually read. A chart key folds in the note count, so
        // editing a chart gives its file a new key; without knowing which files were
        // visited, the row under the old key would survive - the file still exists, it
        // just no longer holds that chart - and Song Select would list a difficulty that
        // is not there any more.
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> existing = _library.AllPaths();

        int added = 0;
        int updated = 0;
        int failed = 0;

        foreach ((string directory, ChartSource source) in Sources())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(
                         directory, $"*.{GameIdentity.ChartExtension}", SearchOption.AllDirectories))
            {
                scannedPaths.Add(file);

                LibraryChart? entry = TryRead(file, source);
                if (entry is null)
                {
                    failed++;
                    continue;
                }

                // A chart key that is already indexed is the same chart; keep its
                // original "added" date so sorting by newest stays meaningful.
                bool isNew = !existing.ContainsKey(entry.ChartKey);
                if (!isNew && _library.Get(entry.ChartKey) is { } previous)
                {
                    entry = entry with { AddedUtc = previous.AddedUtc };
                }

                _library.Upsert(entry);
                seen.Add(entry.ChartKey);

                if (isNew)
                {
                    added++;
                }
                else
                {
                    updated++;
                }
            }
        }

        int removed = 0;
        foreach ((string key, string path) in existing)
        {
            if (seen.Contains(key))
            {
                continue;
            }

            // Drop the row when its file is gone, or when the file was read this pass and
            // turned out to be a different chart than the one this row describes.
            bool stale = !File.Exists(path) || scannedPaths.Contains(path);
            if (!stale)
            {
                continue;
            }

            _library.Remove(key);
            removed++;
        }

        var result = new Result(added, updated, removed, failed);
        if (result.ChangedAnything || failed > 0)
        {
            Log.Info($"library scan: +{added} ~{updated} -{removed} !{failed}");
        }

        return result;
    }

    private IEnumerable<(string Directory, ChartSource Source)> Sources()
    {
        yield return (_paths.ChartsLocal, ChartSource.Local);
        yield return (_paths.ChartsImported, ChartSource.Imported);
    }

    private LibraryChart? TryRead(string file, ChartSource source)
    {
        try
        {
            Chart chart = ChartJson.Deserialize(File.ReadAllText(file)).Normalized();
            if (chart.Notes.Count == 0)
            {
                Log.Warn($"library scan: skipping a chart with no notes ({Path.GetFileName(file)})");
                return null;
            }

            DifficultyAnalyzer.Result analysis = DifficultyAnalyzer.Analyze(chart);

            // The author's own level wins when they set one; the analyser fills the gap
            // when they did not, so an unlevelled chart still sorts and scores sensibly.
            double level = chart.Meta.DifficultyLevel > 1.0
                ? chart.Meta.DifficultyLevel
                : analysis.EstimatedLevel;

            string audioPath = ResolveAudio(file, chart.Meta.AudioFile);
            DateTime writtenUtc = File.GetLastWriteTimeUtc(file);

            return new LibraryChart
            {
                ChartKey = ChartKey.For(chart),
                Meta = chart.Meta with { DifficultyLevel = level },
                ChartPath = file,
                AudioPath = audioPath,
                NoteCount = chart.Notes.Count,
                HoldCount = chart.HoldCount,
                LaneCount = chart.LaneCount,
                DurationMs = chart.LastNoteMs,
                Level = level,
                Attributes = analysis.Attributes,
                Source = source,
                AddedUtc = writtenUtc,
                UpdatedUtc = writtenUtc,
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"library scan: could not read {Path.GetFileName(file)} — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Audio sits next to the chart when it travelled with it, and in the songs folder
    /// otherwise. Looking in both means an imported package and a locally authored chart
    /// resolve the same way without the chart file having to record an absolute path -
    /// which would break the moment the file was shared.
    /// </summary>
    private string ResolveAudio(string chartFile, string audioFileName)
    {
        if (string.IsNullOrWhiteSpace(audioFileName))
        {
            return "";
        }

        string beside = Path.Combine(Path.GetDirectoryName(chartFile) ?? _paths.Songs, audioFileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        return Path.Combine(_paths.Songs, audioFileName);
    }
}
