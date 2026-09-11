namespace Lumen.Core.Editing;

/// <summary>
/// What the editor should do about a set of files dropped onto it.
///
/// The decision is kept here, away from the screen, for two reasons: it is the part with
/// rules worth testing — which extensions count, what happens when somebody drops four
/// files or a screenshot — and a graphics device has nothing to do with any of them.
/// </summary>
public enum AudioDropOutcome
{
    /// <summary>One audio file was identified and should be loaded.</summary>
    Accepted,

    /// <summary>Nothing was dropped, which is not an error worth shouting about.</summary>
    Empty,

    /// <summary>Files were dropped, but none of them were audio this game can read.</summary>
    UnsupportedFormat,

    /// <summary>The file is audio, but too large to be a song.</summary>
    TooLarge,

    /// <summary>The path does not point at a readable file.</summary>
    Unreadable,
}

/// <summary>The verdict on a drop, and the message the editor should show for it.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Path">The file to load, when one was accepted.</param>
/// <param name="Message">What to tell the author. Empty when there is nothing to say.</param>
/// <param name="IgnoredCount">How many other files were passed over.</param>
public readonly record struct AudioDropResult(
    AudioDropOutcome Outcome, string Path, string Message, int IgnoredCount)
{
    public bool IsAccepted => Outcome == AudioDropOutcome.Accepted;
}

/// <summary>
/// Decides what a drop onto the chart editor means (spec §49).
/// </summary>
public static class AudioDrop
{
    /// <summary>
    /// The formats the editor accepts, lower-case and with the dot.
    ///
    /// One list, in one place, so adding a format is adding a line here and teaching the
    /// decoder about it — not hunting through a screen for a hard-coded ".wav". The set is
    /// deliberately what the bundled decoders genuinely handle rather than everything they
    /// might: claiming a format and then failing to decode it is worse than declining it.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
        new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };

    /// <summary>
    /// The largest file treated as a song — about nineteen minutes of CD-quality WAV.
    ///
    /// A cap exists because decoding happens into memory as 32-bit floats, so a file four
    /// times this size becomes gigabytes of RAM and a frozen editor. Refusing it with a
    /// sentence is kinder than appearing to hang.
    /// </summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    /// <summary>Human-readable list of what is accepted, for error messages.</summary>
    public static string SupportedList =>
        string.Join(" / ", SupportedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Picks the audio file out of a drop.
    ///
    /// A drop of several files takes the first one that is audio rather than refusing the
    /// lot: dragging a folder's worth of files onto the editor is a reasonable thing to do,
    /// and the author's intent — "use this song" — is not ambiguous just because a cover
    /// image came along with it. What was passed over is reported so nothing happens
    /// silently.
    /// </summary>
    public static AudioDropResult Classify(IReadOnlyList<string> paths, Func<string, long>? sizeOf = null)
    {
        if (paths is null || paths.Count == 0)
        {
            return new AudioDropResult(AudioDropOutcome.Empty, "", "", 0);
        }

        sizeOf ??= DefaultSizeOf;

        var supported = paths.Where(IsSupported).ToList();
        if (supported.Count == 0)
        {
            string dropped = string.Join(", ", paths
                .Select(p => Path.GetExtension(p).ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Distinct()
                .Take(3));

            string what = dropped.Length > 0 ? $" ({dropped})" : "";
            return new AudioDropResult(
                AudioDropOutcome.UnsupportedFormat, "",
                $"That is not an audio format LUMEN can read{what}. Supported: {SupportedList}.",
                paths.Count);
        }

        string chosen = supported[0];
        int ignored = paths.Count - 1;

        long size = sizeOf(chosen);
        if (size < 0)
        {
            return new AudioDropResult(
                AudioDropOutcome.Unreadable, chosen,
                $"{Path.GetFileName(chosen)} could not be read.", ignored);
        }

        if (size == 0)
        {
            return new AudioDropResult(
                AudioDropOutcome.Unreadable, chosen,
                $"{Path.GetFileName(chosen)} is empty.", ignored);
        }

        if (size > MaxBytes)
        {
            return new AudioDropResult(
                AudioDropOutcome.TooLarge, chosen,
                $"{Path.GetFileName(chosen)} is {size / 1024 / 1024} MB. " +
                $"The limit is {MaxBytes / 1024 / 1024} MB.",
                ignored);
        }

        string note = ignored > 0
            ? $"Loaded {Path.GetFileName(chosen)} — ignored {ignored} other file{(ignored == 1 ? "" : "s")}."
            : $"Loaded {Path.GetFileName(chosen)}.";

        return new AudioDropResult(AudioDropOutcome.Accepted, chosen, note, ignored);
    }

    /// <summary>-1 for anything that cannot be measured, which the caller reads as unreadable.</summary>
    private static long DefaultSizeOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1;
        }
        catch
        {
            return -1;
        }
    }
}
