using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Replays;
using Xunit;

namespace Lumen.Tests.Core.Replays;

/// <summary>
/// The promise of a replay is that it *is* the play (spec §41). These record a session,
/// feed the recording back, and require the two to agree exactly.
/// </summary>
public class ReplayTests
{
    private static Chart Chart(int notes = 40)
    {
        var list = new List<Note>();
        for (int i = 0; i < notes; i++)
        {
            list.Add(i % 5 == 4
                ? Note.Hold(1000 + i * 250, i % 4, 1000 + i * 250 + 500)
                : Note.Tap(1000 + i * 250, i % 4));
        }

        return new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 120) },
            Notes = list.ToArray(),
            Meta = new ChartMeta
            {
                Title = "First Light", Artist = "LUMEN", Creator = "7g3",
                DifficultyName = "NORMAL", DifficultyLevel = 8, AudioFile = "song.wav",
            },
        }.Normalized();
    }

    /// <summary>
    /// A human-ish play: mostly accurate, occasionally late, and missing a couple of notes
    /// entirely. A perfect run would not exercise the interesting paths.
    /// </summary>
    private static IReadOnlyList<LaneEvent> ImperfectPlay(Chart chart)
    {
        var events = new List<LaneEvent>();
        int index = 0;

        foreach (Note note in chart.Notes)
        {
            index++;
            if (index % 11 == 0)
            {
                continue; // missed entirely
            }

            double error = index % 3 == 0 ? 38 : index % 7 == 0 ? -22 : 4;
            events.Add(LaneEvent.Down(note.Lane, note.TimeMs + error));
            events.Add(LaneEvent.Up(note.Lane, (note.IsHold ? note.EndTimeMs : note.TimeMs) + error + 20));
        }

        return events.OrderBy(e => e.TimeMs).ToArray();
    }

    /// <summary>Runs a session in fixed slices, as the game loop does.</summary>
    private static ScoreState Play(Chart chart, Func<double, IReadOnlyList<LaneEvent>> source,
                                   double stepMs = 16)
    {
        var session = new GameplaySession(chart, BalanceConfig.Default);
        double end = chart.LastNoteMs + 2000;

        for (double t = 0; t <= end; t += stepMs)
        {
            session.Update(t, source(t));
        }

        session.Finish();
        return session.Score;
    }

    private static Func<double, IReadOnlyList<LaneEvent>> Feed(IReadOnlyList<LaneEvent> events)
    {
        var player = new ReplayPlayer(events);
        return player.Drain;
    }

    // --- recording ---

    [Fact]
    public void A_recorder_starts_empty()
    {
        var recorder = new ReplayRecorder();
        recorder.Count.Should().Be(0);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public void Recording_keeps_every_event_in_order()
    {
        var recorder = new ReplayRecorder();
        recorder.Record(new[] { LaneEvent.Down(0, 100), LaneEvent.Up(0, 140) });
        recorder.Record(new[] { LaneEvent.Down(1, 300) });

        recorder.Count.Should().Be(3);
        recorder.Events.Select(e => e.TimeMs).Should().Equal(100, 140, 300);
    }

    [Fact]
    public void A_built_replay_carries_what_the_play_scored()
    {
        Chart chart = Chart();
        IReadOnlyList<LaneEvent> events = ImperfectPlay(chart);

        var session = new GameplaySession(chart, BalanceConfig.Default);
        var recorder = new ReplayRecorder();
        var player = new ReplayPlayer(events);

        for (double t = 0; t <= chart.LastNoteMs + 2000; t += 16)
        {
            IReadOnlyList<LaneEvent> slice = player.Drain(t);
            recorder.Record(slice);
            session.Update(t, slice);
        }

        session.Finish();

        Replay replay = recorder.Build(Guid.NewGuid(), "7g3", chart, session.Score);

        replay.Result.Score.Should().Be(session.Score.Score);
        replay.Result.Accuracy.Should().Be(session.Score.Accuracy);
        replay.Result.MaxCombo.Should().Be(session.Score.MaxCombo);
        replay.Result.Miss.Should().Be(session.Score.Miss);
        replay.EventCount.Should().Be(events.Count);
        replay.ChartKey.Should().Be(ChartKey.For(chart));
        replay.PlayerName.Should().Be("7g3");
    }

    // --- determinism, the point of the whole feature ---

    [Fact]
    public void Playing_a_replay_back_reproduces_the_score_exactly()
    {
        Chart chart = Chart();
        IReadOnlyList<LaneEvent> events = ImperfectPlay(chart);

        ScoreState original = Play(chart, Feed(events));
        ScoreState replayed = Play(chart, Feed(events));

        replayed.Score.Should().Be(original.Score);
        replayed.Accuracy.Should().Be(original.Accuracy);
        replayed.MaxCombo.Should().Be(original.MaxCombo);
        replayed.Perfect.Should().Be(original.Perfect);
        replayed.Great.Should().Be(original.Great);
        replayed.Good.Should().Be(original.Good);
        replayed.Bad.Should().Be(original.Bad);
        replayed.Miss.Should().Be(original.Miss);
    }

    [Fact]
    public void Every_judgement_comes_out_the_same_on_playback()
    {
        Chart chart = Chart();
        IReadOnlyList<LaneEvent> events = ImperfectPlay(chart);

        static List<string> Run(Chart chart, IReadOnlyList<LaneEvent> events, double step)
        {
            var judged = new List<string>();
            var session = new GameplaySession(chart, BalanceConfig.Default);
            session.Judged += e => judged.Add($"{e.Judgement}:{e.IsTail}:{e.Note.Lane}");

            var player = new ReplayPlayer(events);
            for (double t = 0; t <= chart.LastNoteMs + 2000; t += step)
            {
                session.Update(t, player.Drain(t));
            }

            session.Finish();
            return judged;
        }

        Run(chart, events, 16).Should().Equal(Run(chart, events, 16));
    }

    [Fact]
    public void Playback_is_independent_of_the_frame_rate_it_is_watched_at()
    {
        // A replay recorded at 240Hz and watched at 60 has to score the same, or the
        // "replay" is a different play that merely looks similar.
        Chart chart = Chart();
        IReadOnlyList<LaneEvent> events = ImperfectPlay(chart);

        ScoreState at240 = Play(chart, Feed(events), stepMs: 1000.0 / 240);
        ScoreState at60 = Play(chart, Feed(events), stepMs: 1000.0 / 60);
        ScoreState at30 = Play(chart, Feed(events), stepMs: 1000.0 / 30);

        at60.Score.Should().Be(at240.Score);
        at30.Score.Should().Be(at240.Score);
        at60.Accuracy.Should().Be(at240.Accuracy);
        at30.Miss.Should().Be(at240.Miss);
    }

    [Fact]
    public void A_replay_that_is_serialised_and_read_back_still_reproduces_the_play()
    {
        Chart chart = Chart();
        IReadOnlyList<LaneEvent> events = ImperfectPlay(chart);

        var recorder = new ReplayRecorder();
        recorder.Record(events);
        ScoreState original = Play(chart, Feed(events));
        Replay replay = recorder.Build(Guid.NewGuid(), "7g3", chart, original);

        Replay reloaded = ReplayJson.Deserialize(ReplayJson.Serialize(replay));
        ScoreState replayed = Play(chart, Feed(reloaded.Events));

        replayed.Score.Should().Be(original.Score);
        replayed.Accuracy.Should().Be(original.Accuracy);
        replayed.Miss.Should().Be(original.Miss);
    }

    // --- the player ---

    [Fact]
    public void The_player_hands_over_events_only_when_they_are_due()
    {
        var player = new ReplayPlayer(new[]
        {
            LaneEvent.Down(0, 100), LaneEvent.Up(0, 150), LaneEvent.Down(1, 900),
        });

        player.Drain(50).Should().BeEmpty();
        player.Drain(150).Select(e => e.TimeMs).Should().Equal(100, 150);
        player.Drain(150).Should().BeEmpty();
        player.Drain(1000).Select(e => e.TimeMs).Should().Equal(900);
        player.Finished.Should().BeTrue();
    }

    [Fact]
    public void A_player_can_be_rewound()
    {
        var player = new ReplayPlayer(new[] { LaneEvent.Down(0, 100) });

        player.Drain(200).Should().ContainSingle();
        player.Reset();
        player.Drain(200).Should().ContainSingle();
    }

    [Fact]
    public void An_empty_replay_plays_back_as_a_full_miss_rather_than_failing()
    {
        Chart chart = Chart(notes: 8);

        ScoreState score = Play(chart, Feed(Array.Empty<LaneEvent>()));

        score.Miss.Should().BeGreaterThan(0);
        score.Score.Should().Be(0);
    }

    // --- serialisation ---

    [Fact]
    public void Serialisation_round_trips_every_field()
    {
        Chart chart = Chart();
        var recorder = new ReplayRecorder();
        recorder.Record(ImperfectPlay(chart));
        Replay replay = recorder.Build(
            Guid.NewGuid(), "なぎさ", chart, Play(chart, Feed(ImperfectPlay(chart))),
            inputOffsetMs: 12, audioOffsetMs: -7);

        Replay back = ReplayJson.Deserialize(ReplayJson.Serialize(replay));

        back.ReplayId.Should().Be(replay.ReplayId);
        back.PlayerId.Should().Be(replay.PlayerId);
        back.PlayerName.Should().Be("なぎさ");
        back.ChartKey.Should().Be(replay.ChartKey);
        back.InputOffsetMs.Should().Be(12);
        back.AudioOffsetMs.Should().Be(-7);
        back.Chart.Title.Should().Be("First Light");
        back.Result.Should().Be(replay.Result);
        back.Events.Should().Equal(replay.Events);
        back.RecordedUtc.Should().BeCloseTo(replay.RecordedUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_replay_from_a_newer_build_is_refused_with_an_explanation()
    {
        Chart chart = Chart(notes: 4);
        var recorder = new ReplayRecorder();
        recorder.Record(new[] { LaneEvent.Down(0, 100) });
        string json = ReplayJson.Serialize(
            recorder.Build(Guid.NewGuid(), "7g3", chart, Play(chart, Feed(Array.Empty<LaneEvent>()))));

        Action read = () => ReplayJson.Deserialize(
            json.Replace("\"formatVersion\":1", "\"formatVersion\":99"));

        read.Should().Throw<FormatException>().WithMessage("*newer version*");
    }

    [Fact]
    public void A_replay_with_mismatched_event_columns_is_reported_as_damaged()
    {
        Chart chart = Chart(notes: 4);
        var recorder = new ReplayRecorder();
        recorder.Record(new[] { LaneEvent.Down(0, 100), LaneEvent.Up(0, 140) });
        string json = ReplayJson.Serialize(
            recorder.Build(Guid.NewGuid(), "7g3", chart, Play(chart, Feed(Array.Empty<LaneEvent>()))));

        Action read = () => ReplayJson.Deserialize(json.Replace("\"ms\":[100,140]", "\"ms\":[100]"));

        read.Should().Throw<FormatException>().WithMessage("*damaged*");
    }

    [Fact]
    public void The_columnar_form_is_smaller_than_an_array_of_objects_would_be()
    {
        // The reason the format is shaped this way; worth pinning so a future tidy-up
        // does not quietly undo it.
        Chart chart = Chart(notes: 200);
        var recorder = new ReplayRecorder();
        recorder.Record(ImperfectPlay(chart));
        Replay replay = recorder.Build(
            Guid.NewGuid(), "7g3", chart, Play(chart, Feed(ImperfectPlay(chart))));

        int columnar = ReplayJson.Serialize(replay).Length;
        int asObjects = replay.Events
            .Sum(e => $"{{\"lane\":{e.Lane},\"isDown\":{e.IsDown},\"timeMs\":{e.TimeMs}}},".Length);

        columnar.Should().BeLessThan(asObjects);
    }
}
