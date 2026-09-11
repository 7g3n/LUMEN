namespace Lumen.Core.Library;

/// <summary>
/// Difficulty bands (spec §26–27). Bands exist only to colour and group the list; every
/// calculation in the game uses the float level, never the band.
/// </summary>
public enum DifficultyBand
{
    Introductory,
    Basic,
    Advanced,
    Expert,
    Master,
    Apex,
}

public static class DifficultyBands
{
    /// <summary>
    /// Lower bound of each band, ascending. Chosen so the bands describe how a chart
    /// feels rather than splitting the range evenly: most charts a player meets early sit
    /// under 10, and the top two bands deliberately cover a narrow range because that is
    /// where small level differences matter most.
    /// </summary>
    private static readonly (double Min, DifficultyBand Band)[] Ladder =
    {
        (15.0, DifficultyBand.Apex),
        (13.0, DifficultyBand.Master),
        (10.0, DifficultyBand.Expert),
        (7.0, DifficultyBand.Advanced),
        (4.0, DifficultyBand.Basic),
        (0.0, DifficultyBand.Introductory),
    };

    public static DifficultyBand For(double level)
    {
        foreach ((double min, DifficultyBand band) in Ladder)
        {
            if (level >= min)
            {
                return band;
            }
        }

        return DifficultyBand.Introductory;
    }

    /// <summary>Exact level, as shown next to the difficulty name: <c>14.7</c>.</summary>
    public static string Precise(double level) => level.ToString("0.0");

    /// <summary>
    /// The form players say out loud: <c>14</c> or <c>14+</c>, where the plus means the
    /// upper half of the level. Used where space is tight, always alongside or in place
    /// of - never instead of - the precise value on the detail panel.
    /// </summary>
    public static string Short(double level)
    {
        int whole = (int)Math.Floor(level);
        bool upperHalf = level - whole >= 0.5;
        return upperHalf ? $"{whole}+" : whole.ToString();
    }

    public static string Name(DifficultyBand band) => band switch
    {
        DifficultyBand.Introductory => "INTRODUCTORY",
        DifficultyBand.Basic => "BASIC",
        DifficultyBand.Advanced => "ADVANCED",
        DifficultyBand.Expert => "EXPERT",
        DifficultyBand.Master => "MASTER",
        DifficultyBand.Apex => "APEX",
        _ => "UNKNOWN",
    };
}
