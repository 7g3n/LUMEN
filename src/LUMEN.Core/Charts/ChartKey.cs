using System.Security.Cryptography;
using System.Text;

namespace Lumen.Core.Charts;

/// <summary>
/// A stable identifier for "this chart" derived from its identity fields, so scores can
/// be grouped per chart before the real chart-id tables exist (Phase 5/7 migrate scores
/// onto those). Two charts with the same title/artist/difficulty/creator and note count
/// collide on purpose — that is the same chart for scoring.
/// </summary>
public static class ChartKey
{
    private const char Sep = '\u001f'; // unit separator

    public static string For(Chart chart)
    {
        ChartMeta m = chart.Meta;
        string seed = string.Join(Sep,
            m.Title.Trim().ToLowerInvariant(),
            m.Artist.Trim().ToLowerInvariant(),
            m.Creator.Trim().ToLowerInvariant(),
            m.DifficultyName.Trim().ToLowerInvariant(),
            chart.Notes.Count.ToString(),
            chart.LaneCount.ToString());

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
