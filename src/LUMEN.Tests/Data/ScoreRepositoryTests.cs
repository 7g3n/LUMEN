using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

public class ScoreRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly ScoreRepository _scores;
    private readonly Guid _player;

    public ScoreRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-score-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        _db.Open();
        _scores = new ScoreRepository(_db, BalanceConfig.Default);
        _player = new ProfileRepository(_db).Create("Tester").PlayerId;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Chart Chart(string title = "First Light", string diff = "NORMAL", double level = 12, int notes = 400)
    {
        var list = new System.Collections.Generic.List<Note>();
        for (int i = 0; i < notes; i++)
        {
            list.Add(Note.Tap(i * 250, i % 4));
        }

        return new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 120) },
            Notes = list.ToArray(),
            Meta = new ChartMeta { Title = title, Artist = "LUMEN", Creator = "x", DifficultyName = diff, DifficultyLevel = level },
        }.Normalized();
    }

    private static PlayResult Result(Chart chart, double accuracy, int maxCombo, int miss, long score = 900_000)
    {
        int total = chart.Notes.Count;
        return new PlayResult
        {
            Chart = chart.Meta,
            Score = score,
            Accuracy = accuracy,
            MaxCombo = maxCombo,
            Perfect = total - miss,
            Great = 0, Good = 0, Bad = 0, Miss = miss,
            FullCombo = miss == 0,
            AllPerfect = miss == 0 && Math.Abs(accuracy - 100) < 1e-9,
            PlayedUtc = DateTime.UtcNow,
        };
    }

    [Fact]
    public void First_save_records_score_performance_and_snapshot()
    {
        Chart c = Chart();
        var outcome = _scores.Save(_player, Result(c, 99.0, c.Notes.Count, 0, 950_000), c);

        outcome.Score.Pp.Should().BeGreaterThan(0);
        outcome.Score.PerformanceRating.Should().BeGreaterThan(0);
        outcome.After.Rating.Should().BeGreaterThan(0);
        outcome.After.BestPp.Should().BeApproximately(outcome.PpBreakdown.FinalPp, 1e-6);
        outcome.IsFirstPlayOnChart.Should().BeTrue();
        outcome.IsPersonalBest.Should().BeTrue();
        outcome.IsPpRecord.Should().BeTrue();

        _scores.CountScores(_player).Should().Be(1);
        _scores.GetLatestSnapshot(_player).Rating.Should().Be(outcome.After.Rating);
    }

    [Fact]
    public void Only_the_best_score_per_chart_counts_toward_the_pools()
    {
        Chart c = Chart(level: 12);
        var first = _scores.Save(_player, Result(c, 95.0, 300, 5, 800_000), c);
        var better = _scores.Save(_player, Result(c, 100.0, c.Notes.Count, 0, 1_000_000), c);

        better.After.Rating.Should().BeGreaterThan(first.After.Rating);
        // one chart -> best pp equals this chart's best performance
        better.After.BestPp.Should().BeApproximately(better.PpBreakdown.FinalPp, 1e-6);

        var worseAgain = _scores.Save(_player, Result(c, 80.0, 100, 40, 500_000), c);
        worseAgain.After.Rating.Should().BeApproximately(better.After.Rating, 1e-6); // unchanged
        worseAgain.IsPersonalBest.Should().BeFalse();
    }

    [Fact]
    public void Rating_climbs_as_more_distinct_charts_are_cleared_well()
    {
        double last = 0;
        for (int i = 0; i < 6; i++)
        {
            Chart c = Chart(title: $"Song {i}", level: 12);
            var o = _scores.Save(_player, Result(c, 99.0, c.Notes.Count, 0, 950_000), c);
            o.After.Rating.Should().BeGreaterThanOrEqualTo(last);
            last = o.After.Rating;
        }

        last.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PersonalBest_is_by_score_and_PpRecord_is_by_pp()
    {
        Chart easy = Chart(title: "Easy", level: 5);
        Chart hard = Chart(title: "Hard", level: 16);

        _scores.Save(_player, Result(easy, 100.0, easy.Notes.Count, 0, 1_000_000), easy);
        var onHard = _scores.Save(_player, Result(hard, 96.0, 380, 3, 940_000), hard);

        onHard.IsPersonalBest.Should().BeTrue();  // first play on "Hard"
        onHard.IsPpRecord.Should().BeTrue();      // harder chart -> more pp than the easy FC
    }

    [Fact]
    public void Statistics_aggregate_across_plays()
    {
        Chart a = Chart(title: "A");
        Chart b = Chart(title: "B");
        _scores.Save(_player, Result(a, 100.0, a.Notes.Count, 0, 1_000_000), a);
        _scores.Save(_player, Result(b, 92.0, 200, 10, 850_000), b);

        var stats = _scores.GetStatistics(_player);
        stats.PlayCount.Should().Be(2);
        stats.FullCombos.Should().Be(1);
        stats.DistinctCharts.Should().Be(2);
        stats.BestAccuracy.Should().BeApproximately(100.0, 1e-9);
        stats.AverageAccuracy.Should().BeApproximately(96.0, 1e-9);
    }

    [Fact]
    public void Best_performances_come_back_ordered_by_pp()
    {
        for (int i = 0; i < 4; i++)
        {
            Chart c = Chart(title: $"S{i}", level: 8 + i * 2);
            _scores.Save(_player, Result(c, 98.0, c.Notes.Count - 2, 2, 900_000), c);
        }

        var best = _scores.GetBestPerformances(_player, 10);
        best.Should().HaveCount(4);
        best.Select(x => x.Pp).Should().BeInDescendingOrder();
    }

    [Fact]
    public void A_failed_save_rolls_back_completely()
    {
        // Poison the transaction: a bogus playerId violates the FK on scores.
        Chart c = Chart();
        Action act = () => _scores.Save(Guid.NewGuid(), Result(c, 99.0, c.Notes.Count, 0), c);

        act.Should().Throw<Exception>();
        // nothing partially written for the real player, and no orphan snapshot
        _scores.CountScores(_player).Should().Be(0);
        _scores.GetLatestSnapshot(_player).Should().Be(Lumen.Core.Scores.RatingSnapshot.Empty);
    }

    [Fact]
    public void Deleting_a_profile_cascades_scores_performances_and_snapshots()
    {
        Chart c = Chart();
        _scores.Save(_player, Result(c, 99.0, c.Notes.Count, 0), c);

        new ProfileRepository(_db).Delete(_player);

        _scores.CountScores(_player).Should().Be(0);
        _scores.GetBestPerformances(_player).Should().BeEmpty();
        _scores.GetLatestSnapshot(_player).Should().Be(Lumen.Core.Scores.RatingSnapshot.Empty);
    }

    [Fact]
    public void Skill_profile_reflects_played_charts()
    {
        Chart c = Chart(title: "Speedy", level: 14, notes: 800); // dense -> high speed attr
        _scores.Save(_player, Result(c, 98.0, c.Notes.Count, 0, 960_000), c);

        var skill = _scores.GetSkillProfile(_player);
        (skill.Speed + skill.Technical + skill.Reading + skill.Stamina + skill.Accuracy)
            .Should().BeGreaterThan(0);
    }
}
