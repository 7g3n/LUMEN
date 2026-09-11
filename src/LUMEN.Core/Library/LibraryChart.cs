using Lumen.Core.Charts;

namespace Lumen.Core.Library;

/// <summary>Where a chart came from. Authored and received content stay distinguishable (§93).</summary>
public enum ChartSource
{
    Local,
    Imported,
    Builtin,
}

/// <summary>
/// One difficulty as the library knows it: everything Song Select needs to list, sort and
/// launch a chart, without reading the chart file. The notes stay on disk — the row only
/// carries the counts and the analysed level, so listing a thousand charts is one query
/// rather than a thousand file parses.
/// </summary>
public sealed record LibraryChart
{
    public required string ChartKey { get; init; }

    public required ChartMeta Meta { get; init; }

    /// <summary>Absolute path of the <c>.lumenchart</c> file backing this row.</summary>
    public required string ChartPath { get; init; }

    /// <summary>Absolute path of the audio the chart references.</summary>
    public required string AudioPath { get; init; }

    public required int NoteCount { get; init; }

    public required int HoldCount { get; init; }

    public int LaneCount { get; init; } = 4;

    /// <summary>Last note time; used for the listed length, not the audio file's length.</summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// The level every other system consumes. The analyser's estimate is stored here so
    /// that a chart whose author never set a level still sorts and scores sensibly.
    /// </summary>
    public double Level { get; init; }

    public ChartAttributes Attributes { get; init; } = ChartAttributes.Zero;

    public ChartSource Source { get; init; } = ChartSource.Local;

    public DateTime AddedUtc { get; init; }

    public DateTime UpdatedUtc { get; init; }

    /// <summary>
    /// Charts that belong to the same song. Grouping is derived from title + artist
    /// rather than stored: until Phase 7 introduces packages there is no song record to
    /// key on, and two files that agree on both fields are the same song by any
    /// reasonable reading.
    /// </summary>
    public string SongKey => SongKeyFor(Meta.Title, Meta.Artist);

    public static string SongKeyFor(string title, string artist) =>
        $"{title.Trim().ToLowerInvariant()}{artist.Trim().ToLowerInvariant()}";
}

/// <summary>A song and every difficulty charted for it, ordered easiest first.</summary>
public sealed record SongGroup
{
    public required string SongKey { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required IReadOnlyList<LibraryChart> Charts { get; init; }

    public double MinLevel => Charts.Count == 0 ? 0 : Charts.Min(c => c.Level);

    public double MaxLevel => Charts.Count == 0 ? 0 : Charts.Max(c => c.Level);

    public double DurationMs => Charts.Count == 0 ? 0 : Charts.Max(c => c.DurationMs);

    public DateTime AddedUtc => Charts.Count == 0 ? DateTime.MinValue : Charts.Max(c => c.AddedUtc);
}

/// <summary>
/// A player's results on one chart, as Song Select shows them (§35, §79). Score, accuracy
/// and PP are each the best seen on that chart — the highest-scoring run is not
/// necessarily the most accurate one, and the screen reports all three.
/// </summary>
public sealed record ChartStats(long BestScore, double BestAccuracy, double BestPp, int PlayCount)
{
    public static readonly ChartStats None = new(0, 0, 0, 0);

    public bool Played => PlayCount > 0;
}
