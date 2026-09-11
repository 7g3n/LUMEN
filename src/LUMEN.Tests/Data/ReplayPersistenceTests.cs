using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Achievements;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Replays;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// Replays and achievements have to survive a restart (spec §41, §64) — the point of both
/// is that they are still there tomorrow. The database is closed and reopened rather than
/// simulated, so what is verified is what is actually on disk.
/// </summary>
public class ReplayPersistenceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _databaseFile;
    private readonly string _replaysDir;
    private Database _db = null!;
    private ReplayRepository _replays = null!;
    private AchievementRepository _achievements = null!;
    private readonly Guid _player;

    public ReplayPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-replay-" + Guid.NewGuid().ToString("N"));
        _databaseFile = Path.Combine(_dir, "database", GameIdentity.DatabaseFileName);
        _replaysDir = Path.Combine(_dir, "replays");
        Directory.CreateDirectory(_replaysDir);

        Open();
        _player = new ProfileRepository(_db).Create("7g3").PlayerId;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Open()
    {
        _db = new Database(_databaseFile);
        _db.Open();
        _replays = new ReplayRepository(_db, _replaysDir);
        _achievements = new AchievementRepository(_db);
    }

    /// <summary>Closes and reopens everything, as a restart would.</summary>
    private void Restart()
    {
        _db.Dispose();
        Open();
    }

    private static Chart Chart(string difficulty = "NORMAL") => new Chart
    {
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 120) },
        Notes = Enumerable.Range(0, 24).Select(i => Note.Tap(1000 + i * 250, i % 4)).ToArray(),
        Meta = new ChartMeta
        {
            Title = "First Light", Artist = "LUMEN", Creator = "7g3",
            DifficultyName = difficulty, DifficultyLevel = 8, AudioFile = "song.wav",
        },
    }.Normalized();

    private Replay Record(Chart chart, string playerName = "7g3")
    {
        var recorder = new ReplayRecorder();
        var session = new GameplaySession(chart);

        foreach (Note note in chart.Notes)
        {
            var events = new[] { LaneEvent.Down(note.Lane, note.TimeMs) };
            recorder.Record(events);
            session.Update(note.TimeMs, events);
        }

        session.Finish();
        return recorder.Build(_player, playerName, chart, session.Score);
    }

    // --- replays ---

    [Fact]
    public void A_saved_replay_can_be_loaded_back()
    {
        Replay replay = Record(Chart());
        _replays.Save(replay);

        Replay? loaded = _replays.Load(replay.ReplayId);

        loaded.Should().NotBeNull();
        loaded!.Events.Should().Equal(replay.Events);
        loaded.Result.Should().Be(replay.Result);
    }

    [Fact]
    public void Replays_survive_a_restart()
    {
        Replay replay = Record(Chart());
        _replays.Save(replay);

        Restart();

        _replays.ListForPlayer(_player).Should().ContainSingle()
            .Which.ReplayId.Should().Be(replay.ReplayId);
        _replays.Load(replay.ReplayId).Should().NotBeNull();
    }

    [Fact]
    public void The_list_shows_what_a_row_needs_without_loading_the_events()
    {
        Replay replay = Record(Chart("MASTER"));
        _replays.Save(replay);

        ReplaySummary summary = _replays.ListForPlayer(_player).Single();

        summary.Title.Should().Be("First Light");
        summary.DifficultyName.Should().Be("MASTER");
        summary.Score.Should().Be(replay.Result.Score);
        summary.Accuracy.Should().Be(replay.Result.Accuracy);
        summary.EventCount.Should().Be(replay.EventCount);
        summary.PlayerName.Should().Be("7g3");
    }

    [Fact]
    public void A_renamed_player_sees_their_new_name_on_old_replays()
    {
        _replays.Save(Record(Chart()));

        new ProfileRepository(_db).Rename(_player, "Nagisa");

        _replays.ListForPlayer(_player).Single().PlayerName.Should().Be("Nagisa");
    }

    [Fact]
    public void Newest_replays_come_first()
    {
        _replays.Save(Record(Chart("EASY")));
        System.Threading.Thread.Sleep(5);
        _replays.Save(Record(Chart("MASTER")));

        _replays.ListForPlayer(_player).First().DifficultyName.Should().Be("MASTER");
    }

    [Fact]
    public void Deleting_a_replay_removes_the_row_and_the_file()
    {
        Replay replay = Record(Chart());
        _replays.Save(replay);
        string path = _replays.PathFor(replay.ReplayId);
        File.Exists(path).Should().BeTrue();

        _replays.Delete(replay.ReplayId);

        _replays.ListForPlayer(_player).Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void A_replay_whose_file_has_gone_loads_as_null()
    {
        Replay replay = Record(Chart());
        _replays.Save(replay);
        File.Delete(_replays.PathFor(replay.ReplayId));

        _replays.Load(replay.ReplayId).Should().BeNull();
    }

    [Fact]
    public void Rows_with_no_file_are_pruned()
    {
        Replay kept = Record(Chart("EASY"));
        Replay orphan = Record(Chart("MASTER"));
        _replays.Save(kept);
        _replays.Save(orphan);
        File.Delete(_replays.PathFor(orphan.ReplayId));

        _replays.PruneMissing().Should().Be(1);

        _replays.ListForPlayer(_player).Should().ContainSingle()
            .Which.ReplayId.Should().Be(kept.ReplayId);
    }

    [Fact]
    public void Pruning_keeps_the_newest_replays()
    {
        for (int i = 0; i < 5; i++)
        {
            _replays.Save(Record(Chart()));
            System.Threading.Thread.Sleep(3);
        }

        _replays.Prune(_player, keep: 2).Should().Be(3);
        _replays.Count(_player).Should().Be(2);
    }

    [Fact]
    public void A_chart_board_lists_its_best_replays_first()
    {
        Chart chart = Chart();
        Replay weak = Record(chart) with { Result = Record(chart).Result with { Score = 100 } };
        Replay strong = Record(chart) with { Result = Record(chart).Result with { Score = 900_000 } };
        _replays.Save(weak);
        _replays.Save(strong);

        _replays.ListForChart(ChartKey.For(chart)).First().Score.Should().Be(900_000);
    }

    // --- achievements ---

    [Fact]
    public void An_unlocked_achievement_survives_a_restart()
    {
        _achievements.Evaluate(_player, AchievementStats.Empty with { PlayCount = 1 })
            .Should().Contain(a => a.Id == "first-play");

        Restart();

        _achievements.UnlockedIds(_player).Should().Contain("first-play");
        _achievements.UnlockedCount(_player).Should().Be(1);
    }

    [Fact]
    public void Evaluating_again_does_not_unlock_the_same_thing_twice()
    {
        var stats = AchievementStats.Empty with { PlayCount = 5 };

        _achievements.Evaluate(_player, stats).Should().NotBeEmpty();
        _achievements.Evaluate(_player, stats).Should().BeEmpty();
    }

    [Fact]
    public void An_achievement_keeps_the_date_it_was_first_earned()
    {
        _achievements.Evaluate(_player, AchievementStats.Empty with { PlayCount = 1 });
        DateTime first = _achievements
            .All(_player, AchievementStats.Empty with { PlayCount = 1 })
            .First(a => a.Id == "first-play").UnlockedUtc!.Value;

        System.Threading.Thread.Sleep(5);
        _achievements.Evaluate(_player, AchievementStats.Empty with { PlayCount = 900 });

        _achievements.All(_player, AchievementStats.Empty with { PlayCount = 900 })
            .First(a => a.Id == "first-play").UnlockedUtc.Should().Be(first);
    }

    [Fact]
    public void The_full_list_shows_locked_ones_with_their_progress()
    {
        var stats = AchievementStats.Empty with { PlayCount = 50 };
        _achievements.Evaluate(_player, stats);

        IReadOnlyList<AchievementState> all = _achievements.All(_player, stats);

        all.Should().HaveCount(AchievementEngine.All.Count);
        all.First(a => a.Id == "first-play").Unlocked.Should().BeTrue();

        AchievementState plays100 = all.First(a => a.Id == "plays-100");
        plays100.Unlocked.Should().BeFalse();
        plays100.Progress.Should().Be(50);
    }

    [Fact]
    public void Unlocked_achievements_are_listed_before_locked_ones()
    {
        var stats = AchievementStats.Empty with { PlayCount = 1 };
        _achievements.Evaluate(_player, stats);

        IReadOnlyList<AchievementState> all = _achievements.All(_player, stats);

        all[0].Unlocked.Should().BeTrue();
        all[^1].Unlocked.Should().BeFalse();
    }

    [Fact]
    public void Achievements_belong_to_one_profile()
    {
        Guid other = new ProfileRepository(_db).Create("Nagisa").PlayerId;
        _achievements.Evaluate(_player, AchievementStats.Empty with { PlayCount = 1 });

        _achievements.UnlockedCount(_player).Should().Be(1);
        _achievements.UnlockedCount(other).Should().Be(0);
    }

    [Fact]
    public void Deleting_a_profile_takes_its_replays_and_achievements_with_it()
    {
        _replays.Save(Record(Chart()));
        _achievements.Evaluate(_player, AchievementStats.Empty with { PlayCount = 1 });

        new ProfileRepository(_db).Delete(_player);

        _replays.Count(_player).Should().Be(0);
        _achievements.UnlockedCount(_player).Should().Be(0);
    }
}
