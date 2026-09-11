namespace Lumen.Core.Charts;

public enum IssueSeverity
{
    Warning,
    Error,
}

/// <summary>The areas the editor shows a tick or a cross against (spec §52).</summary>
public enum ValidationCheck
{
    Audio,
    Metadata,
    Bpm,
    Offset,
    Notes,
    Timing,
    Difficulty,
}

public sealed record ValidationIssue(
    IssueSeverity Severity, ValidationCheck Check, string Code, string Message, double? AtMs = null);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Warnings never block: a chart may be unusual on purpose.</summary>
    public bool CanExport => Errors == 0;

    public int Errors => Issues.Count(i => i.Severity == IssueSeverity.Error);

    public int Warnings => Issues.Count(i => i.Severity == IssueSeverity.Warning);

    public bool Passed(ValidationCheck check) =>
        !Issues.Any(i => i.Check == check && i.Severity == IssueSeverity.Error);

    public IEnumerable<ValidationIssue> For(ValidationCheck check) =>
        Issues.Where(i => i.Check == check);
}

/// <summary>
/// Checks a chart before it is exported (spec §52, §92).
///
/// The split between error and warning is the whole design. An error is something that
/// stops the chart working for whoever receives it — no audio, no notes, a note outside
/// the lanes. A warning is something the author may well have meant: overlapping notes
/// are a legitimate pattern, an empty artist field is untidy rather than broken. Blocking
/// on the second kind would teach authors to ignore the validator, which is worse than
/// not having one.
/// </summary>
public static class ChartValidator
{
    /// <summary>Two notes in one lane closer than this are almost certainly a mistake.</summary>
    public const double MinimumSameLaneGapMs = 30;

    /// <summary>A hold shorter than this cannot be held.</summary>
    public const double MinimumHoldMs = 60;

    public const double MaximumBpm = 1000;

    public const double LargeOffsetMs = 10_000;

    public static ValidationReport Validate(Chart chart)
    {
        var issues = new List<ValidationIssue>();

        CheckAudio(chart, issues);
        CheckMetadata(chart, issues);
        CheckBpm(chart, issues);
        CheckOffset(chart, issues);
        CheckNotes(chart, issues);
        CheckTiming(chart, issues);
        CheckDifficulty(chart, issues);

        return new ValidationReport(issues);
    }

    private static void CheckAudio(Chart chart, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(chart.Meta.AudioFile))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Audio,
                "MISSING_AUDIO", "This chart has no audio file."));
        }
    }

    private static void CheckMetadata(Chart chart, List<ValidationIssue> issues)
    {
        ChartMeta m = chart.Meta;

        if (string.IsNullOrWhiteSpace(m.Title))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Metadata,
                "MISSING_TITLE", "The title is empty."));
        }

        if (string.IsNullOrWhiteSpace(m.DifficultyName))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Metadata,
                "MISSING_DIFFICULTY_NAME", "The difficulty has no name."));
        }

        if (string.IsNullOrWhiteSpace(m.Artist))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Metadata,
                "MISSING_ARTIST", "The artist is empty."));
        }

        if (string.IsNullOrWhiteSpace(m.Creator))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Metadata,
                "MISSING_CREATOR", "The charter is empty."));
        }
    }

    private static void CheckBpm(Chart chart, List<ValidationIssue> issues)
    {
        if (chart.BpmPoints.Count == 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Bpm,
                "MISSING_BPM", "This chart has no BPM set."));
            return;
        }

        var ordered = chart.BpmPoints.OrderBy(p => p.AtMs).ToArray();

        if (ordered[0].AtMs > 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Bpm,
                "NO_INITIAL_BPM", "The first BPM point is not at the start of the song.", 0));
        }

        foreach (BpmPoint point in ordered)
        {
            if (point.Bpm <= 0 || point.Bpm > MaximumBpm || double.IsNaN(point.Bpm))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Bpm,
                    "INVALID_BPM", $"A BPM of {point.Bpm:0.##} is out of range.", point.AtMs));
            }
        }

        for (int i = 1; i < ordered.Length; i++)
        {
            if (Math.Abs(ordered[i].AtMs - ordered[i - 1].AtMs) < 0.001)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Bpm,
                    "DUPLICATE_BPM_POINT", "Two BPM changes share the same time.", ordered[i].AtMs));
            }
        }
    }

    private static void CheckOffset(Chart chart, List<ValidationIssue> issues)
    {
        if (double.IsNaN(chart.ChartOffsetMs) || double.IsInfinity(chart.ChartOffsetMs))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Offset,
                "INVALID_OFFSET", "The chart offset is not a number."));
            return;
        }

        if (Math.Abs(chart.ChartOffsetMs) > LargeOffsetMs)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Offset,
                "LARGE_OFFSET",
                $"A chart offset of {chart.ChartOffsetMs:0}ms is unusually large."));
        }
    }

    private static void CheckNotes(Chart chart, List<ValidationIssue> issues)
    {
        if (chart.Notes.Count == 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Notes,
                "NO_NOTES", "This chart has no notes."));
            return;
        }

        foreach (Note note in chart.Notes)
        {
            if (note.Lane < 0 || note.Lane >= chart.LaneCount)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Notes,
                    "NOTE_OUT_OF_LANE",
                    $"A note sits in lane {note.Lane + 1}, outside this chart's {chart.LaneCount}.",
                    note.TimeMs));
            }

            if (note.TimeMs < 0)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Notes,
                    "NEGATIVE_NOTE_TIME", "A note is placed before the song starts.", note.TimeMs));
            }

            if (note.IsHold && note.DurationMs < MinimumHoldMs)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Notes,
                    "HOLD_TOO_SHORT",
                    $"A hold is only {note.DurationMs:0}ms long.", note.TimeMs));
            }

            if (note.Kind is not (NoteKind.Tap or NoteKind.Hold))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Notes,
                    "UNSUPPORTED_NOTE_TYPE",
                    $"Notes of type \"{note.Kind}\" cannot be played by this version yet.",
                    note.TimeMs));
            }

            if (chart.Meta.DurationMs > 0 && note.TimeMs > chart.Meta.DurationMs)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Notes,
                    "NOTE_AFTER_AUDIO", "A note is placed after the end of the audio.", note.TimeMs));
            }
        }

        CheckOverlaps(chart, issues);
    }

    private static void CheckOverlaps(Chart chart, List<ValidationIssue> issues)
    {
        foreach (IGrouping<int, Note> lane in chart.Notes.GroupBy(n => n.Lane))
        {
            Note[] ordered = lane.OrderBy(n => n.TimeMs).ToArray();

            for (int i = 1; i < ordered.Length; i++)
            {
                Note previous = ordered[i - 1];
                Note current = ordered[i];
                double previousEnd = previous.IsHold ? previous.EndTimeMs : previous.TimeMs;

                if (current.TimeMs < previousEnd)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Notes,
                        "OVERLAPPING_NOTES",
                        $"Two notes overlap in lane {current.Lane + 1}.", current.TimeMs));
                }
                else if (current.TimeMs - previousEnd < MinimumSameLaneGapMs)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Notes,
                        "NOTES_TOO_CLOSE",
                        $"Two notes in lane {current.Lane + 1} are less than " +
                        $"{MinimumSameLaneGapMs:0}ms apart.", current.TimeMs));
                }
            }
        }
    }

    private static void CheckTiming(Chart chart, List<ValidationIssue> issues)
    {
        foreach (Note note in chart.Notes)
        {
            if (double.IsNaN(note.TimeMs) || double.IsInfinity(note.TimeMs))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Timing,
                    "INVALID_TIMING", "A note has an invalid time."));
            }

            if (note.IsHold && (double.IsNaN(note.EndTimeMs) || double.IsInfinity(note.EndTimeMs)))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Timing,
                    "INVALID_TIMING", "A hold has an invalid end time.", note.TimeMs));
            }

            if (note.IsHold && note.EndTimeMs < note.TimeMs)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Timing,
                    "HOLD_ENDS_BEFORE_IT_STARTS", "A hold ends before it begins.", note.TimeMs));
            }
        }
    }

    private static void CheckDifficulty(Chart chart, List<ValidationIssue> issues)
    {
        double level = chart.Meta.DifficultyLevel;

        if (double.IsNaN(level) || level < 1 || level > 20)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, ValidationCheck.Difficulty,
                "INVALID_DIFFICULTY", "The difficulty level must be between 1 and 20."));
            return;
        }

        double estimated = DifficultyAnalyzer.Analyze(chart).EstimatedLevel;
        if (Math.Abs(estimated - level) >= 3)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCheck.Difficulty,
                "LEVEL_DISAGREES_WITH_ANALYSIS",
                $"The set level is {level:0.0} but this chart analyses as {estimated:0.0}."));
        }
    }
}
