namespace Lumen.Core.Accessibility;

/// <summary>
/// The accessibility switches (spec §67), and the one rule the rest of the game follows:
/// nothing may be conveyed by colour alone, and nothing essential may be conveyed by
/// motion alone.
///
/// Kept in Core as plain data so the parts that have to honour it — the renderer, the
/// screens, the judgement display — can consult it without reaching into the UI layer,
/// and so the rules can be tested without a graphics device.
/// </summary>
public sealed record AccessibilityOptions
{
    /// <summary>
    /// Suppresses decorative movement: screen fades, count-ups, hit flourishes. What the
    /// player needs to read still appears, it simply appears rather than arriving.
    /// </summary>
    public bool ReducedMotion { get; init; }

    /// <summary>Raises text and border contrast. Layout never changes with it.</summary>
    public bool HighContrast { get; init; }

    /// <summary>
    /// Adds a shape marker beside every judgement so the grade is readable without
    /// distinguishing the colours.
    /// </summary>
    public bool ShapeCues { get; init; }

    /// <summary>0..1. Scales hit effects and particles; 0 removes them entirely.</summary>
    public double EffectIntensity { get; init; } = 1.0;

    public bool ShowJudgement { get; init; } = true;

    public bool ShowCombo { get; init; } = true;

    public static readonly AccessibilityOptions Default = new();

    /// <summary>
    /// Multiplier applied to any animation's progress rate. Callers multiply their
    /// elapsed time by this, so reduced motion makes every animation finish instantly
    /// rather than requiring each one to special-case the flag.
    /// </summary>
    public double MotionScale => ReducedMotion ? 0 : 1;

    /// <summary>
    /// How far through an animation to be, given how long it has been running. With
    /// reduced motion this is 1 from the first frame: the end state, immediately.
    /// </summary>
    public double Progress(double elapsedSeconds, double durationSeconds)
    {
        if (ReducedMotion || durationSeconds <= 0)
        {
            return 1;
        }

        return Math.Clamp(elapsedSeconds / durationSeconds, 0, 1);
    }

    /// <summary>Effect strength after the intensity slider and reduced motion.</summary>
    public double EffectStrength => ReducedMotion ? 0 : Math.Clamp(EffectIntensity, 0, 1);
}

/// <summary>
/// The glyph shown beside a judgement when shape cues are on (spec §67).
///
/// Distinct silhouettes rather than a palette: they read at a glance, survive any colour
/// vision, and stay legible on a bright background.
/// </summary>
public static class JudgementShapes
{
    public static string For(Judgement judgement) => judgement switch
    {
        Judgement.Perfect => "◆",
        Judgement.Great => "▲",
        Judgement.Good => "●",
        Judgement.Bad => "■",
        Judgement.Miss => "✕",
        _ => "",
    };

    /// <summary>
    /// The judgement's label, with its shape when cues are on.
    ///
    /// Built once rather than formatted on demand: the playfield asks for these while
    /// drawing, and a string composed per judgement per frame is exactly the sort of
    /// quiet allocation that eventually buys a collection in the middle of a song.
    /// </summary>
    public static string Label(Judgement judgement, bool shapeCues)
    {
        int index = (int)judgement;
        return (uint)index < (uint)Plain.Length
            ? (shapeCues ? WithShape[index] : Plain[index])
            : "";
    }

    private static readonly string[] Plain = BuildLabels(withShape: false);

    private static readonly string[] WithShape = BuildLabels(withShape: true);

    private static string[] BuildLabels(bool withShape)
    {
        var values = (Judgement[])Enum.GetValues(typeof(Judgement));
        int highest = 0;
        foreach (Judgement value in values)
        {
            highest = Math.Max(highest, (int)value);
        }

        var labels = new string[highest + 1];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = "";
        }

        foreach (Judgement value in values)
        {
            string name = value.ToString().ToUpperInvariant();
            labels[(int)value] = withShape ? $"{For(value)} {name}" : name;
        }

        return labels;
    }
}
