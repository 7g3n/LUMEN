using Lumen.Audio;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Core.Accessibility;
using Lumen.Core.Achievements;
using Lumen.Core.Gameplay;
using Lumen.Core.Replays;
using Lumen.Core.Settings;
using Lumen.Core.Tournaments;
using Lumen.Game.Content;
using Lumen.Game.Engine;
using Lumen.Game.Input;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// A play: audio + conductor + the deterministic <see cref="GameplaySession"/> + the
/// playfield (spec §15–24, §19). Tap and Hold only in Phase 3.
/// </summary>
public sealed class GameplayScreen : Screen
{
    private enum Phase { Loading, Playing, Paused, Finished }

    private readonly string _chartPath;
    private readonly string? _audioPathOverride;
    private readonly bool _autoPlay;
    private readonly Action<PlayResult>? _onComplete;

    private Queue<LaneEvent>? _autoEvents;
    private readonly ReplayRecorder _recorder = new();
    private readonly Replay? _watching;
    private ReplayPlayer? _playback;
    private IReadOnlyList<AchievementDefinition> _unlocked = Array.Empty<AchievementDefinition>();
    private Phase _phase = Phase.Loading;
    private AudioEngine _audio = null!;
    private IAudioTrack _track = null!;
    private Conductor _conductor = null!;
    private GameplaySession _session = null!;
    private LaneInputSource _input = null!;
    private Chart _chart = null!;

    private double _inputOffsetMs;
    private double _lastJudgementTime = -10;
    private Judgement _lastJudgement;
    private double _lastErrorMs;
    private readonly List<Popup> _popups = new();
    private MenuList _pauseMenu = new("RESUME", "RESTART", "QUIT");

    /// <summary>
    /// Settings the playfield draws with, read once when the play starts.
    ///
    /// They used to be read inside <c>Draw</c>, which put a database query in the frame
    /// loop: harmless-looking, but it allocated on every frame and that allocation is what
    /// eventually forces a collection in the middle of a song. Nothing here can change
    /// while a play is running, so reading it once is also the honest thing to do.
    /// </summary>
    private double _pxPerMs;

    /// <summary>Reused across frames so draining the autoplay queue allocates nothing.</summary>
    private readonly List<LaneEvent> _autoSlice = new(8);

    // The HUD's numbers change a few times a second but are drawn a few hundred times a
    // second. Formatting them only when they actually change keeps the frame loop free of
    // the string garbage that would otherwise dominate it.
    private long _hudScore = -1;
    private string _hudScoreText = "0";
    private double _hudAccuracy = double.NaN;
    private string _hudAccuracyText = "100.00%";
    private int _hudCombo = -1;
    private string _hudComboText = "0";

    /// <param name="tournamentRules">
    /// Set when this play is a tournament match. Two things follow from it, and both
    /// matter: the rules are enforced rather than offered — a tournament that forbids
    /// pausing has to actually refuse one — and the result does not touch the player's
    /// normal scores, PP or Rating. A tournament is a separate competition, and entering
    /// one should not move somebody's everyday rating because an organiser picked a hard
    /// chart under rules they did not choose.
    /// </param>
    public GameplayScreen(string chartPath, string? audioPath = null,
                          bool autoPlay = false, Action<PlayResult>? onComplete = null,
                          Replay? watch = null,
                          TournamentRules? tournamentRules = null)
    {
        _chartPath = chartPath;
        _audioPathOverride = audioPath;
        _autoPlay = autoPlay;
        _onComplete = onComplete;
        _watching = watch;
        _tournamentRules = tournamentRules;
    }

    private readonly TournamentRules? _tournamentRules;

    /// <summary>A line shown briefly over the playfield — a refused pause, say.</summary>
    private string? _status;
    private double _statusUntilMs;

    /// <summary>True while a recorded play is being watched rather than played (§41).</summary>
    private bool IsWatching => _watching is not null;

    /// <summary>True while this play is a tournament match.</summary>
    private bool IsTournamentPlay => _tournamentRules is not null;

    public override Color BackgroundColor => Theme.Ground;

    public override void OnEnter()
    {
        try
        {
            _chart = ChartJson.Deserialize(File.ReadAllText(_chartPath)).Normalized();
            string audioPath = _audioPathOverride
                               ?? Path.Combine(Context.Paths.Songs, _chart.Meta.AudioFile);

            _audio = new AudioEngine();
            float musicVol = (float)(Context.Settings.GetDouble(Context.Session.ActivePlayerId, "audio.music", 100) / 100.0)
                             * (float)(Context.Settings.GetDouble(Context.Session.ActivePlayerId, "audio.master", 80) / 100.0);
            _track = _audio.LoadTrack(audioPath, musicVol);

            _inputOffsetMs = Context.Settings.GetDouble(Context.Session.ActivePlayerId, "timing.inputOffsetMs", 0);

            // Watching replays the recorded stream through the same path a live play
            // takes, so the session cannot tell the difference.
            if (_watching is not null)
            {
                _playback = new ReplayPlayer(_watching);
            }
            double audioOffsetMs = Context.Settings.GetDouble(Context.Session.ActivePlayerId, "timing.audioOffsetMs", 0);

            _conductor = new Conductor(_track, new TempoMap(_chart.BpmPoints), audioOffsetMs);
            _session = new GameplaySession(_chart, Context.Balance);
            _session.Judged += OnJudged;

            var bindings = KeyBindings.Load(Context.Settings, Context.Session.ActivePlayerId, _chart.LaneCount);
            _input = new LaneInputSource(bindings);

            if (_autoPlay)
            {
                _autoEvents = new Queue<LaneEvent>(BuildAutoEvents(_chart));
            }

            double noteSpeed = Context.Settings.GetDouble(
                Context.Session.ActivePlayerId, "gameplay.noteSpeed", 6.0);
            _pxPerMs = noteSpeed * 0.13;

            _conductor.Seek(0);
            _conductor.Play();
            _phase = Phase.Playing;

            // Measure the song, not the decode and device setup that just happened: a
            // stutter while loading is not the stutter this is looking for (§68).
            Context.Frames.Reset();

            Log.Info($"play: {_chart.Meta.Title} [{_chart.Meta.DifficultyName}] audio={( _track.HasOutput ? "on" : "silent")}");
        }
        catch (Exception ex)
        {
            Log.Error("failed to start play", ex);
            Manager.Replace(new ErrorNoticeScreen("Couldn't start this chart", ex.Message));
        }
    }

    public override void OnExit()
    {
        _track?.Dispose();
    }

    private void OnJudged(JudgementEvent e)
    {
        _lastJudgement = e.Judgement;
        _lastJudgementTime = _conductor.SongTimeMs;
        _lastErrorMs = e.ErrorMs;
        _popups.Add(new Popup(e.Judgement, e.Note.Lane, _conductor.SongTimeMs));
        if (_popups.Count > 24)
        {
            _popups.RemoveRange(0, _popups.Count - 24);
        }
    }

    public override void Update(InputFrame input)
    {
        switch (_phase)
        {
            case Phase.Playing:
                UpdatePlaying(input);
                break;
            case Phase.Paused:
                UpdatePaused(input);
                break;
            case Phase.Finished:
                break;
        }
    }

    private void UpdatePlaying(InputFrame input)
    {
        _conductor.Tick();
        double now = _conductor.SongTimeMs;

        IReadOnlyList<LaneEvent> events =
            _playback is not null ? _playback.Drain(now)
            : _autoEvents is not null ? DrainAuto(now)
            : _input.Poll(input.Keyboard, now - _inputOffsetMs);

        // Recorded before judging, so the stream is exactly what the session was given.
        if (!IsWatching && events.Count > 0)
        {
            _recorder.Record(events);
        }

        _session.Update(now, events);

        if (input.Pressed(Keys.Escape))
        {
            // A tournament that forbids pausing refuses it here rather than trusting the
            // player not to press the key. The pause menu is also where Restart lives, so
            // the two rules are enforced by the same door.
            if (_tournamentRules is { PauseAllowed: false })
            {
                _status = "Pausing is not allowed in this tournament.";
                _statusUntilMs = _conductor.SongTimeMs + 2000;
                return;
            }

            _conductor.Pause();
            _pauseMenu = _tournamentRules is { RetryAllowed: false }
                ? new MenuList("RESUME", "FORFEIT")
                : new MenuList("RESUME", "RESTART", "QUIT");
            _phase = Phase.Paused;
            return;
        }

        bool pastEnd = now >= _chart.LastNoteMs + 1500 || now >= _conductor.DurationMs;
        if (pastEnd || _session.AllResolved && now >= _chart.LastNoteMs + 400)
        {
            _session.Finish();
            _track.Pause();
            _phase = Phase.Finished;
            PlayResult result = PlayResult.From(_session);
            Log.Info($"result: {result.Accuracy:0.00}% {result.Score:N0} x{result.MaxCombo} " +
                     $"P{result.Perfect}/G{result.Great}/g{result.Good}/B{result.Bad}/M{result.Miss}");
            Log.Info("frames: " + Context.Frames.Summary());
            Log.Info("worst frames: " + Context.Frames.StutterSummary());

            Core.Scores.ScoreSaveOutcome? outcome = SaveResult(result);

            if (_onComplete is not null)
            {
                _onComplete(result);
                return;
            }

            Manager.Push(new ResultScreen(result, outcome,
                onDismiss: () => Manager.ReplaceAll(new MainMenuScreen())));
        }
    }

    private void UpdatePaused(InputFrame input)
    {
        int choice = _pauseMenu.Update(input, PauseMenuArea());
        if (input.Pressed(Keys.Escape))
        {
            choice = 0;
        }

        if (choice == 0)
        {
            _conductor.Play();
            _phase = Phase.Playing;
            return;
        }

        // A tournament match has a shorter menu, because restarting is not on offer.
        // Leaving one is forfeiting it, and it hands the caller the play as it stood.
        if (IsTournamentPlay)
        {
            if (choice == 1)
            {
                Log.Info("tournament match forfeited");
                _session.Finish();
                _track.Pause();
                _phase = Phase.Finished;
                _onComplete?.Invoke(PlayResult.From(_session));
            }

            return;
        }

        switch (choice)
        {
            case 1:
                Manager.Replace(new GameplayScreen(_chartPath, _audioPathOverride));
                break;
            case 2:
                Manager.ReplaceAll(new MainMenuScreen());
                break;
        }
    }

    private Core.Scores.ScoreSaveOutcome? SaveResult(PlayResult result)
    {
        // Watching a replay is not playing it: nothing is scored, recorded or unlocked.
        if (IsWatching)
        {
            return null;
        }

        // A tournament match is scored by the tournament, in its own tables. Saving it here
        // as well would feed an organiser's chart choice into the player's Rating, which is
        // the one thing the two systems are kept apart to prevent.
        if (IsTournamentPlay)
        {
            Log.Info("tournament play: not recorded against the player's normal scores");
            return null;
        }

        try
        {
            Guid playerId = Context.Session.ActivePlayerId;
            Core.Scores.ScoreSaveOutcome outcome = Context.Scores.Save(playerId, result, _chart);
            Context.Profiles.AddPlayTime(playerId, (long)Math.Min(_conductor.DurationMs, _conductor.SongTimeMs));
            Context.Session.Refresh();
            Log.Info($"saved: +{outcome.PpBreakdown.FinalPp:0.0}pp  rating {outcome.Before.Rating:0.00}->{outcome.After.Rating:0.00}" +
                     $"{(outcome.IsPersonalBest ? " PB" : "")}{(outcome.IsPpRecord ? " PPREC" : "")}");

            SaveReplay(playerId, outcome.Score.ScoreId);

            // Evaluated after the score is in, so the statistics it reads include this play.
            _unlocked = Context.Achievements.Evaluate(playerId);
            foreach (AchievementDefinition unlocked in _unlocked)
            {
                Log.Info($"achievement unlocked: {unlocked.Name}");
            }

            return outcome;
        }
        catch (Exception ex)
        {
            Log.Error("failed to save score", ex);
            return null;
        }
    }

    /// <summary>
    /// Stores the recording. A failure here is logged and swallowed: the score is already
    /// safe, and losing a replay is not worth turning a finished play into an error.
    /// </summary>
    private void SaveReplay(Guid playerId, Guid scoreId)
    {
        if (_recorder.Count == 0)
        {
            return;
        }

        try
        {
            Replay replay = _recorder.Build(
                playerId,
                Context.Session.ActiveProfile?.DisplayName ?? "",
                _chart, _session.Score,
                _inputOffsetMs,
                Context.Settings.GetDouble(playerId, "timing.audioOffsetMs", 0));

            Context.Replays.Save(replay, scoreId);
            Log.Info($"replay saved: {replay.EventCount} events");
        }
        catch (Exception ex)
        {
            Log.Warn("replay could not be saved", ex);
        }
    }

    private static IEnumerable<LaneEvent> BuildAutoEvents(Chart chart)
    {
        var list = new List<LaneEvent>();
        foreach (Note n in chart.Notes)
        {
            list.Add(LaneEvent.Down(n.Lane, n.TimeMs));
            if (n.IsHold)
            {
                list.Add(LaneEvent.Up(n.Lane, n.EndTimeMs));
            }
        }

        return list.OrderBy(e => e.TimeMs).ThenBy(e => e.IsDown ? 0 : 1);
    }

    private IReadOnlyList<LaneEvent> DrainAuto(double now)
    {
        _autoSlice.Clear();
        while (_autoEvents!.Count > 0 && _autoEvents.Peek().TimeMs <= now)
        {
            _autoSlice.Add(_autoEvents.Dequeue());
        }

        return _autoSlice;
    }

    private Rectangle PauseMenuArea() => new(Manager.Context.Display.Width / 2 - 120,
        (int)(GameplayLayout.Height * 0.5f), 240, 3 * 44);

    // --- rendering ---

    public override void Draw(UiRenderer ui)
    {
        var layout = new GameplayLayout(ui, _chart.LaneCount);
        double now = _phase == Phase.Loading ? 0 : _conductor.SongTimeMs;

        DrawField(ui, layout);
        DrawNotes(ui, layout, now, _pxPerMs);
        DrawPopups(ui, layout, now);
        DrawHud(ui, layout, now);

        if (_phase == Phase.Playing && now < _chart.FirstNoteMs - 400)
        {
            DrawCountdown(ui, layout, now);
        }

        if (_phase == Phase.Paused)
        {
            DrawPauseOverlay(ui);
        }
    }

    private void DrawField(UiRenderer ui, GameplayLayout l)
    {
        ui.FillRect(new Rectangle(l.FieldX, 0, l.FieldWidth, ui.Height), Theme.Surface.WithAlpha(0.5f));

        for (int lane = 0; lane <= _chart.LaneCount; lane++)
        {
            int x = l.LaneX(lane);
            ui.FillRect(new Rectangle(x, 0, 1, ui.Height), Theme.Border);
        }

        // Hit line
        ui.FillRect(new Rectangle(l.FieldX, l.HitLineY, l.FieldWidth, 2), Theme.Accent.WithAlpha(0.8f));

        // Lane held glow + key hints
        for (int lane = 0; lane < _chart.LaneCount; lane++)
        {
            if (_phase == Phase.Playing && lane < _input.LaneHeld.Count && _input.LaneHeld[lane])
            {
                ui.FillRect(new Rectangle(l.LaneX(lane) + 1, 0, l.LaneWidth - 1, ui.Height),
                    Theme.Accent.WithAlpha(0.06f));
                ui.FillRect(new Rectangle(l.LaneX(lane) + 1, l.HitLineY - 3, l.LaneWidth - 1, 6),
                    Theme.Accent.WithAlpha(0.5f));
            }
        }
    }

    private void DrawNotes(UiRenderer ui, GameplayLayout l, double now, double pxPerMs)
    {
        // Indexed rather than foreach: the list is reached through an interface, and
        // enumerating one of those allocates an enumerator on every frame.
        IReadOnlyList<NoteObject> notes = _session.Notes;
        for (int i = 0; i < notes.Count; i++)
        {
            NoteObject note = notes[i];

            if (note.Status == NoteStatus.Done && note.Note.TimeMs < now - 200)
            {
                continue;
            }

            int laneX = l.LaneX(note.Lane) + 4;
            int w = l.LaneWidth - 8;
            int headY = (int)(l.HitLineY - (note.Note.TimeMs - now) * pxPerMs);

            if (note.IsHold)
            {
                int tailY = (int)(l.HitLineY - (note.Note.EndTimeMs - now) * pxPerMs);
                var body = new Rectangle(laneX + w / 4, Math.Min(headY, tailY), w / 2, Math.Abs(headY - tailY));
                if (body.Bottom > -20 && body.Y < ui.Height + 20)
                {
                    Color c = note.Status == NoteStatus.Holding ? Theme.Accent : Theme.Great;
                    ui.FillRect(body, c.WithAlpha(0.35f));
                }
            }

            if (headY > -20 && headY < ui.Height + 20 && note.HeadJudgement is null)
            {
                ui.FillRect(new Rectangle(laneX, headY - 6, w, 12), Theme.Text);
                ui.FillRect(new Rectangle(laneX, headY - 6, w, 2), Theme.Accent);
            }
        }
    }

    private void DrawPopups(UiRenderer ui, GameplayLayout l, double now)
    {
        // A predicate here would capture `now` into a fresh closure every frame; the
        // popups are already in spawn order, so dropping the expired prefix is both
        // cheaper and simpler.
        int expired = 0;
        while (expired < _popups.Count && now - _popups[expired].SpawnMs > 450)
        {
            expired++;
        }

        if (expired > 0)
        {
            _popups.RemoveRange(0, expired);
        }

        AccessibilityOptions a11y = Context.Accessibility;
        if (!a11y.ShowJudgement)
        {
            return;
        }

        foreach (Popup p in _popups)
        {
            double age = (now - p.SpawnMs) / 450.0;
            if (age < 0) continue;

            // Reduced motion keeps the word still and lets it fade; the information is in
            // the word, and the drift is decoration (§67).
            float alpha = (float)(1 - age);
            int y = a11y.ReducedMotion
                ? l.HitLineY - 60
                : (int)(l.HitLineY - 60 - age * 24);

            // The shape makes the grade readable without telling the colours apart.
            string label = JudgementShapes.Label(p.Judgement, a11y.ShapeCues);

            ui.Text(ui.Display(Theme.DisplayM), label,
                new Rectangle(l.LaneX(p.Lane), y, l.LaneWidth, 24),
                JudgementColor(p.Judgement).WithAlpha(alpha), TextAlign.Center);
        }
    }

    private void DrawHud(UiRenderer ui, GameplayLayout l, double now)
    {
        ScoreState s = _session.Score;

        if (s.Score != _hudScore)
        {
            _hudScore = s.Score;
            _hudScoreText = s.Score.ToString("N0");
        }

        if (Math.Abs(s.Accuracy - _hudAccuracy) > 0.0001)
        {
            _hudAccuracy = s.Accuracy;
            _hudAccuracyText = s.Accuracy.ToString("0.00") + "%";
        }

        ui.Text(ui.Mono(18), _hudScoreText,
            new Rectangle(0, 24, ui.Width - 40, 24), Theme.Text, TextAlign.Right);
        ui.Text(ui.Mono(Theme.Label), _hudAccuracyText,
            new Rectangle(0, 50, ui.Width - 40, 18), Theme.TextMuted, TextAlign.Right);

        if (s.Combo > 1 && Context.Accessibility.ShowCombo)
        {
            if (s.Combo != _hudCombo)
            {
                _hudCombo = s.Combo;
                _hudComboText = s.Combo.ToString();
            }

            ui.Text(ui.Display(44), _hudComboText,
                new Rectangle(l.FieldX, l.HitLineY / 2, l.FieldWidth, 48), Theme.Text, TextAlign.Center);
            ui.Text(ui.Mono(Theme.Label), "COMBO",
                new Rectangle(l.FieldX, l.HitLineY / 2 + 46, l.FieldWidth, 16), Theme.TextFaint, TextAlign.Center);
        }

        // last timing error meter
        if (_lastJudgementTime >= 0 && now - _lastJudgementTime < 800 && !double.IsNaN(_lastErrorMs))
        {
            int meterW = l.FieldWidth;
            int mx = l.FieldX;
            int my = l.HitLineY + 40;
            ui.FillRect(new Rectangle(mx, my, meterW, 1), Theme.Border);
            int px = mx + meterW / 2 + (int)Math.Clamp(_lastErrorMs * 1.4, -meterW / 2, meterW / 2);
            ui.FillRect(new Rectangle(px - 1, my - 5, 2, 10), JudgementColor(_lastJudgement));
        }

        // progress
        double frac = _conductor.DurationMs > 0 ? Math.Clamp(now / _conductor.DurationMs, 0, 1) : 0;
        ui.FillRect(new Rectangle(0, 0, (int)(ui.Width * frac), 3), Theme.Accent.WithAlpha(0.6f));

        if (!_track.HasOutput)
        {
            ui.Text(ui.Mono(Theme.Label), "SILENT (no audio device)",
                new Rectangle(16, ui.Height - 30, 300, 16), Theme.Bad);
        }

        if (IsTournamentPlay)
        {
            ui.Text(ui.Mono(Theme.Label), "TOURNAMENT",
                new Rectangle(16, 24, 200, 16), Theme.AccentBright);
        }

        if (_status is { Length: > 0 } && now < _statusUntilMs)
        {
            ui.Text(ui.Body(Theme.Body), _status,
                new Rectangle(l.FieldX, l.HitLineY + 70, l.FieldWidth, 20),
                Theme.Bad, TextAlign.Center);
        }
    }

    private void DrawCountdown(UiRenderer ui, GameplayLayout l, double now)
    {
        double beatsToFirst = _session.Tempo.BeatAt(_chart.FirstNoteMs) - _session.Tempo.BeatAt(Math.Max(0, now));
        int count = (int)Math.Ceiling(beatsToFirst);
        if (count is > 0 and <= 8)
        {
            ui.Text(ui.Display(Theme.DisplayL), count.ToString(),
                new Rectangle(l.FieldX, l.HitLineY / 2 - 60, l.FieldWidth, 40), Theme.TextMuted, TextAlign.Center);
        }

        ui.Text(ui.Mono(Theme.Label), "GET READY",
            new Rectangle(l.FieldX, l.HitLineY / 2 - 16, l.FieldWidth, 18), Theme.TextFaint, TextAlign.Center);
    }

    private void DrawPauseOverlay(UiRenderer ui)
    {
        ui.FillRect(ui.Bounds, Theme.Ground.WithAlpha(0.7f));
        ui.Text(ui.Display(Theme.DisplayL), "PAUSED",
            new Rectangle(0, (int)(ui.Height * 0.34f), ui.Width, 40), Theme.Text, TextAlign.Center);
        _pauseMenu.Draw(ui, PauseMenuArea(), TextAlign.Center);
    }

    private static Color JudgementColor(Judgement j) => j switch
    {
        Judgement.Perfect => Theme.Perfect,
        Judgement.Great => Theme.Great,
        Judgement.Good => Theme.Good,
        Judgement.Bad => Theme.Bad,
        _ => Theme.Miss,
    };

    private readonly record struct Popup(Judgement Judgement, int Lane, double SpawnMs);
}

/// <summary>Playfield geometry for the current viewport.</summary>
public readonly struct GameplayLayout
{
    public static int Height { get; private set; }

    public GameplayLayout(UiRenderer ui, int laneCount)
    {
        Height = ui.Height;
        LaneWidth = 96;
        FieldWidth = LaneWidth * laneCount;
        FieldX = (ui.Width - FieldWidth) / 2;
        HitLineY = ui.Height - 130;
    }

    public int LaneWidth { get; }
    public int FieldWidth { get; }
    public int FieldX { get; }
    public int HitLineY { get; }

    public int LaneX(int lane) => FieldX + lane * LaneWidth;
}
