using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Charts;
using Lumen.Core.Library;
using Xunit;

namespace Lumen.Tests.Core.Library;

public class LibraryQueryTests
{
    private static LibraryChart Chart(
        string title, string difficulty = "NORMAL", double level = 10,
        string artist = "LUMEN", string creator = "7g3",
        ChartSource source = ChartSource.Local, int daysOld = 0)
    {
        return new LibraryChart
        {
            ChartKey = $"{title}:{difficulty}".ToLowerInvariant(),
            Meta = new ChartMeta
            {
                Title = title,
                Artist = artist,
                Creator = creator,
                DifficultyName = difficulty,
                DifficultyLevel = level,
            },
            ChartPath = $@"C:\charts\{title}-{difficulty}.lumenchart",
            AudioPath = $@"C:\songs\{title}.wav",
            NoteCount = 500,
            HoldCount = 20,
            Level = level,
            Source = source,
            AddedUtc = DateTime.UtcNow.AddDays(-daysOld),
            UpdatedUtc = DateTime.UtcNow.AddDays(-daysOld),
        };
    }

    private static readonly string[] NoFavorites = Array.Empty<string>();
    private static readonly Dictionary<string, ChartStats> NoStats = new();

    private static IReadOnlyList<SongGroup> Apply(
        IEnumerable<LibraryChart> charts,
        SongFilter? filter = null,
        SongSort sort = SongSort.Title,
        IReadOnlyCollection<string>? favorites = null,
        IReadOnlyDictionary<string, ChartStats>? stats = null)
    {
        return LibraryQuery.Apply(charts, filter ?? SongFilter.None, sort, reversed: false,
            favorites ?? NoFavorites, stats ?? NoStats);
    }

    // --- grouping ---

    [Fact]
    public void Difficulties_of_one_song_group_together()
    {
        IReadOnlyList<SongGroup> groups = LibraryQuery.Group(new[]
        {
            Chart("First Light", "EASY", 3),
            Chart("First Light", "HARD", 8),
            Chart("Second Light", "NORMAL", 5),
        });

        groups.Should().HaveCount(2);
        groups.Single(g => g.Title == "First Light").Charts.Should().HaveCount(2);
    }

    [Fact]
    public void Difficulties_are_ordered_easiest_first()
    {
        IReadOnlyList<SongGroup> groups = LibraryQuery.Group(new[]
        {
            Chart("First Light", "MASTER", 14.7),
            Chart("First Light", "EASY", 3),
            Chart("First Light", "HARD", 9),
        });

        groups.Single().Charts.Select(c => c.Level).Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_same_title_by_a_different_artist_is_a_different_song()
    {
        IReadOnlyList<SongGroup> groups = LibraryQuery.Group(new[]
        {
            Chart("First Light", artist: "LUMEN"),
            Chart("First Light", artist: "Someone Else"),
        });

        groups.Should().HaveCount(2);
    }

    [Fact]
    public void Grouping_ignores_case_and_surrounding_space_in_the_song_identity()
    {
        IReadOnlyList<SongGroup> groups = LibraryQuery.Group(new[]
        {
            Chart("First Light", "EASY", 3),
            Chart(" first light ", "HARD", 8),
        });

        groups.Should().HaveCount(1);
    }

    [Fact]
    public void A_song_group_reports_its_level_range()
    {
        SongGroup group = LibraryQuery.Group(new[]
        {
            Chart("First Light", "EASY", 3.2),
            Chart("First Light", "MASTER", 14.7),
        }).Single();

        group.MinLevel.Should().Be(3.2);
        group.MaxLevel.Should().Be(14.7);
    }

    // --- search ---

    [Fact]
    public void Search_matches_the_title()
    {
        IReadOnlyList<SongGroup> result = Apply(
            new[] { Chart("First Light"), Chart("Nightfall") },
            new SongFilter { Search = "night" });

        result.Should().ContainSingle().Which.Title.Should().Be("Nightfall");
    }

    [Fact]
    public void Search_matches_the_artist_and_the_charter()
    {
        var charts = new[] { Chart("A", artist: "Aurora"), Chart("B", creator: "nagisa") };

        Apply(charts, new SongFilter { Search = "aurora" }).Should().ContainSingle();
        Apply(charts, new SongFilter { Search = "nagisa" }).Should().ContainSingle();
    }

    [Fact]
    public void Search_ignores_case_and_surrounding_space()
    {
        Apply(new[] { Chart("First Light") }, new SongFilter { Search = "  FIRST  " })
            .Should().ContainSingle();
    }

    [Fact]
    public void Search_folds_full_width_characters_onto_half_width_ones()
    {
        // Typed on a Japanese keyboard without switching modes.
        Apply(new[] { Chart("LUMEN") }, new SongFilter { Search = "ＬＵＭＥＮ" })
            .Should().ContainSingle();
    }

    [Fact]
    public void An_empty_search_keeps_everything()
    {
        Apply(new[] { Chart("A"), Chart("B") }, new SongFilter { Search = "   " })
            .Should().HaveCount(2);
    }

    [Fact]
    public void A_search_that_matches_nothing_returns_nothing()
    {
        Apply(new[] { Chart("A") }, new SongFilter { Search = "zzz" }).Should().BeEmpty();
    }

    // --- filtering ---

    [Fact]
    public void A_level_filter_hides_the_difficulties_outside_it_not_just_the_song()
    {
        IReadOnlyList<SongGroup> result = Apply(
            new[] { Chart("First Light", "EASY", 3), Chart("First Light", "MASTER", 14.7) },
            new SongFilter { MinLevel = 10 });

        result.Should().ContainSingle();
        result[0].Charts.Should().ContainSingle().Which.Meta.DifficultyName.Should().Be("MASTER");
    }

    [Fact]
    public void A_song_disappears_when_none_of_its_difficulties_survive_the_filter()
    {
        Apply(new[] { Chart("First Light", "EASY", 3) }, new SongFilter { MinLevel = 10 })
            .Should().BeEmpty();
    }

    [Fact]
    public void Favourites_only_keeps_favourited_charts()
    {
        LibraryChart favorite = Chart("First Light", "MASTER", 14.7);
        LibraryChart other = Chart("Nightfall");

        IReadOnlyList<SongGroup> result = Apply(
            new[] { favorite, other },
            new SongFilter { FavoritesOnly = true },
            favorites: new[] { favorite.ChartKey });

        result.Should().ContainSingle().Which.Title.Should().Be("First Light");
    }

    [Fact]
    public void Source_filter_separates_authored_from_imported_content()
    {
        var charts = new[]
        {
            Chart("Mine", source: ChartSource.Local),
            Chart("Theirs", source: ChartSource.Imported),
        };

        Apply(charts, new SongFilter { Source = ChartSource.Imported })
            .Should().ContainSingle().Which.Title.Should().Be("Theirs");
    }

    [Fact]
    public void Unplayed_only_hides_charts_that_already_have_a_score()
    {
        LibraryChart played = Chart("Played");
        LibraryChart fresh = Chart("Fresh");
        var stats = new Dictionary<string, ChartStats>
        {
            [played.ChartKey] = new(900_000, 98.5, 320, 3),
        };

        Apply(new[] { played, fresh }, new SongFilter { UnplayedOnly = true }, stats: stats)
            .Should().ContainSingle().Which.Title.Should().Be("Fresh");
    }

    // --- sorting ---

    [Fact]
    public void Title_sort_is_alphabetical_and_case_insensitive()
    {
        Apply(new[] { Chart("zephyr"), Chart("Aurora"), Chart("machine") }, sort: SongSort.Title)
            .Select(g => g.Title).Should().Equal("Aurora", "machine", "zephyr");
    }

    [Fact]
    public void Level_sort_orders_by_the_hardest_difficulty_of_each_song()
    {
        var charts = new[]
        {
            Chart("Hard song", "MASTER", 15),
            Chart("Easy song", "EASY", 2),
            Chart("Easy song", "NORMAL", 6),
        };

        Apply(charts, sort: SongSort.Level).Select(g => g.Title)
            .Should().Equal("Easy song", "Hard song");
    }

    [Fact]
    public void Recent_sort_puts_the_newest_song_first()
    {
        var charts = new[]
        {
            Chart("Old", daysOld: 30),
            Chart("New", daysOld: 0),
            Chart("Middle", daysOld: 10),
        };

        Apply(charts, sort: SongSort.Recent).Select(g => g.Title)
            .Should().Equal("New", "Middle", "Old");
    }

    [Fact]
    public void Best_pp_sort_puts_the_biggest_result_first()
    {
        LibraryChart a = Chart("A");
        LibraryChart b = Chart("B");
        LibraryChart c = Chart("C");
        var stats = new Dictionary<string, ChartStats>
        {
            [a.ChartKey] = new(0, 0, 120, 1),
            [b.ChartKey] = new(0, 0, 480, 1),
        };

        Apply(new[] { a, b, c }, sort: SongSort.BestPp, stats: stats)
            .Select(g => g.Title).Should().Equal("B", "A", "C");
    }

    [Fact]
    public void Accuracy_sort_uses_the_best_accuracy_on_any_difficulty_of_the_song()
    {
        LibraryChart easy = Chart("Song", "EASY", 3);
        LibraryChart hard = Chart("Song", "HARD", 12);
        LibraryChart other = Chart("Other");
        var stats = new Dictionary<string, ChartStats>
        {
            [easy.ChartKey] = new(0, 91.0, 0, 1),
            [hard.ChartKey] = new(0, 99.2, 0, 1),
            [other.ChartKey] = new(0, 95.0, 0, 1),
        };

        Apply(new[] { easy, hard, other }, sort: SongSort.Accuracy, stats: stats)
            .Select(g => g.Title).Should().Equal("Song", "Other");
    }

    [Fact]
    public void Sorting_is_total_so_songs_that_tie_keep_a_stable_order()
    {
        var charts = new[] { Chart("B", level: 10), Chart("A", level: 10), Chart("C", level: 10) };

        Apply(charts, sort: SongSort.Level).Select(g => g.Title).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Reversing_flips_an_ascending_key()
    {
        IReadOnlyList<SongGroup> result = LibraryQuery.Apply(
            new[] { Chart("A"), Chart("B") }, SongFilter.None, SongSort.Title,
            reversed: true, NoFavorites, NoStats);

        result.Select(g => g.Title).Should().Equal("B", "A");
    }

    [Fact]
    public void Reversing_a_best_first_key_puts_the_weakest_result_on_top()
    {
        var charts = new[] { Chart("Old", daysOld: 30), Chart("New", daysOld: 0) };

        IReadOnlyList<SongGroup> result = LibraryQuery.Apply(
            charts, SongFilter.None, SongSort.Recent, reversed: true, NoFavorites, NoStats);

        result.Select(g => g.Title).Should().Equal("Old", "New");
    }

    [Fact]
    public void An_empty_library_produces_an_empty_list_rather_than_throwing()
    {
        Apply(Array.Empty<LibraryChart>()).Should().BeEmpty();
    }
}
