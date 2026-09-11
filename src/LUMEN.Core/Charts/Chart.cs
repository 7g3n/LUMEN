namespace Lumen.Core.Charts;

/// <summary>A single tempo marker (spec §46). <see cref="AtMs"/> is time from audio start.</summary>
public sealed record BpmPoint(double AtMs, double Bpm);

/// <summary>Descriptive fields for a chart (spec §60).</summary>
public sealed record ChartMeta
{
    public string Title { get; init; } = "Untitled";
    public string Artist { get; init; } = "Unknown";
    public string Creator { get; init; } = "";
    public string DifficultyName { get; init; } = "NORMAL";
    public double DifficultyLevel { get; init; } = 1.0;
    public string AudioFile { get; init; } = "";
    public double PreviewMs { get; init; }

    /// <summary>Free text from the author, shown in the chart's details (§60).</summary>
    public string Description { get; init; } = "";

    /// <summary>Lower-case keywords, e.g. <c>stream</c>, <c>technical</c> (§61).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Cover art file name, resolved inside a package. Null when there is none.</summary>
    public string? CoverFile { get; init; }

    /// <summary>Audio length. Written by the editor; 0 when not yet known.</summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// Records compare their members with <c>==</c>, which for <see cref="Tags"/> would be
    /// a reference check - two metadata blocks read from the same file would never be
    /// equal. Comparing the tags by sequence is what a caller means by "the same details".
    /// </summary>
    public bool Equals(ChartMeta? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Title == other.Title
               && Artist == other.Artist
               && Creator == other.Creator
               && DifficultyName == other.DifficultyName
               && DifficultyLevel.Equals(other.DifficultyLevel)
               && AudioFile == other.AudioFile
               && PreviewMs.Equals(other.PreviewMs)
               && Description == other.Description
               && CoverFile == other.CoverFile
               && DurationMs.Equals(other.DurationMs)
               && Tags.SequenceEqual(other.Tags);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        hash.Add(Artist);
        hash.Add(Creator);
        hash.Add(DifficultyName);
        hash.Add(DifficultyLevel);
        hash.Add(AudioFile);
        hash.Add(PreviewMs);
        hash.Add(Description);
        hash.Add(CoverFile);
        hash.Add(DurationMs);
        foreach (string tag in Tags)
        {
            hash.Add(tag);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Tag vocabulary the editor suggests (§61). Authors may add anything else.</summary>
public static class ChartTags
{
    public static readonly IReadOnlyList<string> Suggested = new[]
    {
        "speed", "technical", "stream", "dense", "beginner", "experimental", "jack", "stamina",
    };

    /// <summary>Trims, lower-cases and de-duplicates, so tags compare and sort predictably.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// A playable chart: metadata, a tempo map, an offset, and an ordered note list.
/// Immutable once built; <see cref="Normalized"/> guarantees notes are time-sorted.
/// </summary>
public sealed record Chart
{
    public int FormatVersion { get; init; } = GameIdentity.ChartFormatVersion;

    /// <summary>
    /// Stable identity for this chart across edits (spec §58).
    ///
    /// <see cref="ChartKey"/> is derived from the content, so it changes the moment a note
    /// is added - which is what makes it useless for version history. This id is stamped
    /// into the file the first time the editor saves it and never changes afterwards.
    /// Null means "not stamped yet": a chart from an older build that nobody has edited
    /// since simply has no history, rather than acquiring a different identity each time
    /// it is read.
    /// </summary>
    public Guid? Id { get; init; }

    public ChartMeta Meta { get; init; } = new();

    /// <summary>Shifts the whole note grid relative to the audio (spec §47).</summary>
    public double ChartOffsetMs { get; init; }

    public IReadOnlyList<BpmPoint> BpmPoints { get; init; } = new[] { new BpmPoint(0, 120) };

    public IReadOnlyList<Note> Notes { get; init; } = Array.Empty<Note>();

    public int LaneCount { get; init; } = 4;

    public double FirstNoteMs => Notes.Count > 0 ? Notes[0].TimeMs : 0;

    public double LastNoteMs => Notes.Count > 0 ? Notes.Max(n => Math.Max(n.TimeMs, n.EndTimeMs)) : 0;

    public int TapCount => Notes.Count(n => n.Kind == NoteKind.Tap);

    public int HoldCount => Notes.Count(n => n.Kind == NoteKind.Hold);

    /// <summary>Returns a copy with notes sorted by time then lane, and offset folded in.</summary>
    public Chart Normalized()
    {
        var notes = Notes
            .Select(n => n with
            {
                TimeMs = n.TimeMs + ChartOffsetMs,
                EndTimeMs = n.IsHold ? n.EndTimeMs + ChartOffsetMs : n.EndTimeMs,
            })
            .OrderBy(n => n.TimeMs)
            .ThenBy(n => n.Lane)
            .ToArray();

        var bpm = BpmPoints.OrderBy(b => b.AtMs).ToArray();
        if (bpm.Length == 0)
        {
            bpm = new[] { new BpmPoint(0, 120) };
        }

        return this with { Notes = notes, BpmPoints = bpm, ChartOffsetMs = 0 };
    }
}
