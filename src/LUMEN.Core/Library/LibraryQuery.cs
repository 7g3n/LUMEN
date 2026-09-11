namespace Lumen.Core.Library;

public enum SongSort
{
    Title,
    Artist,
    Level,
    Recent,
    BestPp,
    Accuracy,
}

/// <summary>What the player has narrowed the list down to (spec §35).</summary>
public sealed record SongFilter
{
    public static readonly SongFilter None = new();

    /// <summary>Matched against title, artist and charter. Case- and accent-insensitive.</summary>
    public string Search { get; init; } = "";

    public bool FavoritesOnly { get; init; }

    public double MinLevel { get; init; }

    public double MaxLevel { get; init; } = 99;

    public ChartSource? Source { get; init; }

    public bool UnplayedOnly { get; init; }
}

/// <summary>
/// Grouping, filtering and sorting for Song Select.
///
/// Deliberately pure and in Core: the same list has to be produced identically whether it
/// came from SQLite, from a test fixture, or one day from an online library, and pushing
/// the ordering into SQL would make it untestable without a database and impossible to
/// reuse. The database's job is to hand over rows; deciding what the player sees is game
/// logic.
/// </summary>
public static class LibraryQuery
{
    /// <summary>Groups difficulties into songs, each song's charts ordered easiest first.</summary>
    public static IReadOnlyList<SongGroup> Group(IEnumerable<LibraryChart> charts)
    {
        return charts
            .GroupBy(c => c.SongKey)
            .Select(g =>
            {
                LibraryChart first = g.OrderBy(c => c.Level).First();
                return new SongGroup
                {
                    SongKey = g.Key,
                    Title = first.Meta.Title,
                    Artist = first.Meta.Artist,
                    Charts = g
                        .OrderBy(c => c.Level)
                        .ThenBy(c => c.Meta.DifficultyName, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                };
            })
            .ToArray();
    }

    /// <summary>
    /// The list as Song Select shows it. A song survives the filter when at least one of
    /// its difficulties does, and only the surviving difficulties are listed under it -
    /// filtering by level should not offer a song and then hide every chart on it.
    /// </summary>
    /// <param name="reversed">
    /// Flips whatever the chosen key's natural order is. Each key has the order a player
    /// wants by default - titles read A→Z, but "Recent" and "Best PP" read best-first -
    /// so the flag reverses that rather than meaning "descending", which would put the
    /// least interesting rows on top for half the keys.
    /// </param>
    public static IReadOnlyList<SongGroup> Apply(
        IEnumerable<LibraryChart> charts,
        SongFilter filter,
        SongSort sort,
        bool reversed,
        IReadOnlyCollection<string> favorites,
        IReadOnlyDictionary<string, ChartStats> stats)
    {
        var favoriteSet = favorites as ISet<string> ?? new HashSet<string>(favorites, StringComparer.Ordinal);
        string needle = Normalize(filter.Search);

        IEnumerable<LibraryChart> kept = charts.Where(c => Matches(c, filter, needle, favoriteSet, stats));
        IReadOnlyList<SongGroup> groups = Group(kept);

        return Sort(groups, sort, reversed, stats);
    }

    private static bool Matches(
        LibraryChart chart,
        SongFilter filter,
        string needle,
        ISet<string> favorites,
        IReadOnlyDictionary<string, ChartStats> stats)
    {
        if (chart.Level < filter.MinLevel || chart.Level > filter.MaxLevel)
        {
            return false;
        }

        if (filter.Source is { } source && chart.Source != source)
        {
            return false;
        }

        if (filter.FavoritesOnly && !favorites.Contains(chart.ChartKey))
        {
            return false;
        }

        if (filter.UnplayedOnly && Stats(stats, chart.ChartKey).Played)
        {
            return false;
        }

        if (needle.Length == 0)
        {
            return true;
        }

        return Normalize(chart.Meta.Title).Contains(needle, StringComparison.Ordinal)
               || Normalize(chart.Meta.Artist).Contains(needle, StringComparison.Ordinal)
               || Normalize(chart.Meta.Creator).Contains(needle, StringComparison.Ordinal)
               || Normalize(chart.Meta.DifficultyName).Contains(needle, StringComparison.Ordinal);
    }

    private static IReadOnlyList<SongGroup> Sort(
        IReadOnlyList<SongGroup> groups,
        SongSort sort,
        bool reversed,
        IReadOnlyDictionary<string, ChartStats> stats)
    {
        // Title is the tiebreaker everywhere, so the order is total and a redraw never
        // shuffles two songs that compare equal on the chosen key.
        IOrderedEnumerable<SongGroup> ordered = sort switch
        {
            SongSort.Artist => groups
                .OrderBy(g => g.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            SongSort.Level => groups
                .OrderBy(g => g.MaxLevel)
                .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            SongSort.Recent => groups
                .OrderByDescending(g => g.AddedUtc)
                .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            SongSort.BestPp => groups
                .OrderByDescending(g => Best(g, stats, s => s.BestPp))
                .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            SongSort.Accuracy => groups
                .OrderByDescending(g => Best(g, stats, s => s.BestAccuracy))
                .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => groups
                .OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Artist, StringComparer.CurrentCultureIgnoreCase),
        };

        List<SongGroup> result = ordered.ToList();
        if (reversed)
        {
            result.Reverse();
        }

        return result;
    }

    private static double Best(
        SongGroup group,
        IReadOnlyDictionary<string, ChartStats> stats,
        Func<ChartStats, double> select)
    {
        double best = 0;
        foreach (LibraryChart chart in group.Charts)
        {
            best = Math.Max(best, select(Stats(stats, chart.ChartKey)));
        }

        return best;
    }

    private static ChartStats Stats(IReadOnlyDictionary<string, ChartStats> stats, string chartKey) =>
        stats.TryGetValue(chartKey, out ChartStats? s) ? s : ChartStats.None;

    /// <summary>
    /// Lower-cased and width-folded, so a search for "lumen" finds "LUMEN" and a search
    /// typed on a Japanese keyboard in full-width characters finds the half-width title.
    /// </summary>
    private static string Normalize(string value) =>
        value.Trim().Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
}
