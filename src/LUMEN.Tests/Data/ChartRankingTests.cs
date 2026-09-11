using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Library;
using Lumen.Core.Scores;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// The local leaderboard (spec §39) and the per-chart bests Song Select shows (§35, §79).
/// </summary>
public class ChartRankingTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly ScoreRepository _scores;
    private readonly ProfileRepository _profiles;
    private readonly Guid _me;
    private readonly Guid _rival;

    public ChartRankingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-rank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        _db.Open();
        _scores = new ScoreRepository(_db, BalanceConfig.Default);
        _profiles = new ProfileRepository(_db);
        _me = _profiles.Create("7g3").PlayerId;
        _rival = _profiles.Create("Nagisa").PlayerId;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Chart Chart(string title = "First Light", string diff = "MASTER", int notes = 200)
    {
        var list = new List<Note>();
        for (int i = 0; i < notes; i++)
        {
            list.Add(Note.Tap(i * 250, i % 4));
        }

        return new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 120) },
            Notes = list.ToArray(),
            Meta = new ChartMeta
            {
                Title = title, Artist = "LUMEN", Creator = "7g3",
                DifficultyName = diff, DifficultyLevel = 14.0,
            },
        }.Normalized();
    }

    private static PlayResult Result(Chart chart, double accuracy, long score, int maxCombo, int miss = 0)
    {
        int perfect = Math.Max(0, chart.Notes.Count - miss);
        return new PlayResult
        {
            Chart = chart.Meta,
            Score = score,
            Accuracy = accuracy,
            MaxCombo = maxCombo,
            Perfect = perfect,
            Great = 0,
            Good = 0,
            Bad = 0,
            Miss = miss,
            FullCombo = miss == 0,
            AllPerfect = miss == 0,
            PlayedUtc = DateTime.UtcNow,
        };
    }

    private void Save(Guid player, Chart chart, double accuracy, long score, int maxCombo, int miss = 0) =>
        _scores.Save(player, Result(chart, accuracy, score, maxCombo, miss), chart);

    // --- ranking ---

    [Fact]
    public void An_unplayed_chart_has_an_empty_board()
    {
        _scores.GetChartRanking(ChartKey.For(Chart()), 10, _me).Should().BeEmpty();
    }

    [Fact]
    public void The_board_orders_players_by_their_best_score()
    {
        Chart chart = Chart();
        Save(_me, chart, 97.0, 900_000, 180);
        Save(_rival, chart, 99.0, 980_000, 200);

        IReadOnlyList<ChartRankingEntry> board = _scores.GetChartRanking(ChartKey.For(chart), 10, _me);

        board.Select(e => e.PlayerName).Should().Equal("Nagisa", "7g3");
        board[0].Rank.Should().Be(1);
        board[1].Rank.Should().Be(2);
    }

    [Fact]
    public void A_player_occupies_one_place_no_matter_how_often_they_play()
    {
        Chart chart = Chart();
        Save(_me, chart, 90.0, 700_000, 100);
        Save(_me, chart, 97.0, 900_000, 180);
        Save(_me, chart, 95.0, 850_000, 150);

        IReadOnlyList<ChartRankingEntry> board = _scores.GetChartRanking(ChartKey.For(chart), 10, _me);

        board.Should().ContainSingle();
        board[0].Score.Should().Be(900_000);
    }

    [Fact]
    public void The_players_own_row_is_marked()
    {
        Chart chart = Chart();
        Save(_me, chart, 97.0, 900_000, 180);
        Save(_rival, chart, 99.0, 980_000, 200);

        IReadOnlyList<ChartRankingEntry> board = _scores.GetChartRanking(ChartKey.For(chart), 10, _me);

        board.Single(e => e.PlayerName == "7g3").IsSelf.Should().BeTrue();
        board.Single(e => e.PlayerName == "Nagisa").IsSelf.Should().BeFalse();
    }

    [Fact]
    public void The_board_respects_its_limit()
    {
        Chart chart = Chart();
        Save(_me, chart, 97.0, 900_000, 180);
        Save(_rival, chart, 99.0, 980_000, 200);

        _scores.GetChartRanking(ChartKey.For(chart), 1, _me).Should().ContainSingle();
    }

    [Fact]
    public void Boards_are_per_chart()
    {
        Chart master = Chart(diff: "MASTER");
        Chart easy = Chart(diff: "EASY", notes: 60);
        Save(_me, master, 97.0, 900_000, 180);

        _scores.GetChartRanking(ChartKey.For(master), 10, _me).Should().ContainSingle();
        _scores.GetChartRanking(ChartKey.For(easy), 10, _me).Should().BeEmpty();
    }

    [Fact]
    public void A_renamed_player_keeps_their_place_under_the_new_name()
    {
        Chart chart = Chart();
        Save(_me, chart, 97.0, 900_000, 180);

        _profiles.Rename(_me, "seven");

        _scores.GetChartRanking(ChartKey.For(chart), 10, _me)
            .Single().PlayerName.Should().Be("seven");
    }

    // --- rank lookup ---

    [Fact]
    public void A_chart_the_player_has_never_touched_has_no_rank()
    {
        Chart chart = Chart();
        Save(_rival, chart, 99.0, 980_000, 200);

        _scores.GetChartRank(ChartKey.For(chart), _me).Should().BeNull();
    }

    [Fact]
    public void Rank_counts_the_players_ahead()
    {
        Chart chart = Chart();
        Save(_me, chart, 97.0, 900_000, 180);
        Save(_rival, chart, 99.0, 980_000, 200);

        _scores.GetChartRank(ChartKey.For(chart), _me).Should().Be(2);
        _scores.GetChartRank(ChartKey.For(chart), _rival).Should().Be(1);
    }

    [Fact]
    public void Rank_improves_when_the_player_beats_the_leader()
    {
        Chart chart = Chart();
        Save(_rival, chart, 99.0, 980_000, 200);
        Save(_me, chart, 97.0, 900_000, 180);
        _scores.GetChartRank(ChartKey.For(chart), _me).Should().Be(2);

        Save(_me, chart, 99.5, 995_000, 200);

        _scores.GetChartRank(ChartKey.For(chart), _me).Should().Be(1);
    }

    // --- per-chart stats ---

    [Fact]
    public void Chart_stats_are_empty_before_the_first_play()
    {
        _scores.GetChartStats(_me).Should().BeEmpty();
    }

    [Fact]
    public void Chart_stats_report_the_best_of_each_number_independently()
    {
        Chart chart = Chart();
        // The highest-scoring run is not the most accurate one, on purpose.
        Save(_me, chart, 99.4, 900_000, 200);
        Save(_me, chart, 96.0, 950_000, 200);

        ChartStats stats = _scores.GetChartStats(_me)[ChartKey.For(chart)];

        stats.BestScore.Should().Be(950_000);
        stats.BestAccuracy.Should().BeApproximately(99.4, 1e-9);
        stats.PlayCount.Should().Be(2);
        stats.BestPp.Should().BeGreaterThan(0);
        stats.Played.Should().BeTrue();
    }

    [Fact]
    public void Chart_stats_cover_every_chart_the_player_has_played()
    {
        Chart master = Chart(diff: "MASTER");
        Chart easy = Chart(diff: "EASY", notes: 60);
        Save(_me, master, 97.0, 900_000, 180);
        Save(_me, easy, 99.0, 980_000, 60);

        IReadOnlyDictionary<string, ChartStats> stats = _scores.GetChartStats(_me);

        stats.Should().HaveCount(2);
        stats.Should().ContainKey(ChartKey.For(master));
        stats.Should().ContainKey(ChartKey.For(easy));
    }

    [Fact]
    public void Chart_stats_belong_to_one_profile()
    {
        Chart chart = Chart();
        Save(_rival, chart, 99.0, 980_000, 200);

        _scores.GetChartStats(_me).Should().BeEmpty();
        _scores.GetChartStats(_rival).Should().ContainSingle();
    }
}
