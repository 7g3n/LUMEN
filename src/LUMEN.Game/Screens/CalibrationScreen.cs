using Lumen.Audio;
using Lumen.Core.Diagnostics;
using Lumen.Core.Settings;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Timing calibration (spec §66).
///
/// A metronome plays, the player taps along, and the median of their errors becomes their
/// input offset. The median rather than the mean, because the one tap somebody fumbles
/// should not drag the answer — and because a player who is nervous about the first few
/// beats still gets a usable number.
///
/// The click track is a generated clip played through the game's own audio engine, so the
/// reference the player is tapping against is the same clock gameplay uses. Comparing
/// taps to a stopwatch instead would measure the stopwatch, not the sound.
/// </summary>
public sealed class CalibrationScreen : Screen
{
    private const double Bpm = 100;
    private const double BeatMs = 60_000.0 / Bpm;
    private const int Beats = 24;

    /// <summary>Taps further than this from a beat are a mistake, not an offset.</summary>
    private const double OutlierMs = 250;

    private const int MinimumTaps = 6;

    private AudioEngine _audio = null!;
    private IAudioTrack? _track;
    private readonly List<double> _errors = new();

    private bool _running;
    private double _lastBeatFlashMs = double.NegativeInfinity;
    private int _lastBeatIndex = -1;
    private string? _message;
    private double? _detectedMs;

    public override void OnEnter()
    {
        _audio = new AudioEngine();
        _message = "Press Space to start, then tap along with the beat.";
    }

    public override void OnExit()
    {
        _track?.Dispose();
        _track = null;
    }

    private double PositionMs => (_track?.PositionSeconds ?? 0) * 1000.0;

    private double CurrentOffset =>
        Context.Settings.GetDouble(Context.Session.ActivePlayerId, SettingKeys.InputOffsetMs, 0);

    // --- running ---

    private void Start()
    {
        _errors.Clear();
        _detectedMs = null;
        _lastBeatIndex = -1;

        try
        {
            _track?.Dispose();
            _track = _audio.CreateTrack(Metronome.Build(Bpm, Beats), volume: 0.8f);
            _track.Play();
            _running = true;
            _message = "Tap on every click.";
        }
        catch (Exception ex)
        {
            Log.Warn("calibration could not start the metronome", ex);
            _message = "The metronome could not be started.";
            _running = false;
        }
    }

    private void Stop()
    {
        _track?.Pause();
        _running = false;
        Finish();
    }

    private void Finish()
    {
        if (_errors.Count < MinimumTaps)
        {
            _message = $"Not enough taps yet — {MinimumTaps} are needed for a reliable number.";
            _detectedMs = null;
            return;
        }

        _detectedMs = Median(_errors);
        _message = "Press Enter to apply, or Space to measure again.";
    }

    /// <summary>Middle value, which is what makes one fumbled tap harmless.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>
    /// Signed distance from a tap to the beat it belongs to. Positive means the player
    /// tapped late, which is the sign the input offset is written with: a late player
    /// needs their inputs pulled back towards the note.
    /// </summary>
    public static double ErrorForTap(double tapMs, double beatMs)
    {
        double beats = Math.Round(tapMs / beatMs);
        return tapMs - beats * beatMs;
    }

    private void Tap()
    {
        if (!_running || _track is null)
        {
            return;
        }

        double error = ErrorForTap(PositionMs, BeatMs);
        if (Math.Abs(error) > OutlierMs)
        {
            return;
        }

        _errors.Add(error);
    }

    private void Apply()
    {
        if (_detectedMs is not { } detected)
        {
            return;
        }

        double offset = Math.Round(CurrentOffset + detected);
        Context.Settings.SetDouble(
            Context.Session.ActivePlayerId, SettingKeys.InputOffsetMs, offset);

        Log.Info($"calibration applied: {detected:+0.0;-0.0} ms -> input offset {offset:0} ms");
        _message = $"Input offset is now {offset:0} ms.";
        _detectedMs = null;
    }

    // --- input ---

    public override void Update(InputFrame input)
    {
        if (_running && _track is not null)
        {
            double position = PositionMs;

            int beat = (int)(position / BeatMs);
            if (beat != _lastBeatIndex)
            {
                _lastBeatIndex = beat;
                _lastBeatFlashMs = position;
            }

            if (position >= Beats * BeatMs || !_track.IsPlaying)
            {
                Stop();
            }
        }

        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        if (input.Pressed(Keys.Enter) && _detectedMs is not null)
        {
            Apply();
            return;
        }

        if (input.Pressed(Keys.Space))
        {
            if (_running)
            {
                Stop();
            }
            else
            {
                Start();
            }

            return;
        }

        // Any lane key taps, so the player calibrates with the keys they actually play on.
        foreach (Keys key in Input.KeyBindings
                     .Load(Context.Settings, Context.Session.ActivePlayerId).Lanes)
        {
            if (input.Pressed(key))
            {
                Tap();
                return;
            }
        }
    }

    // --- drawing ---

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 96);
        int y = ScreenChrome.Header(ui, column, "Timing Calibration");

        ui.Text(ui.Body(Theme.Body), "Listen to the beat. Tap when you hear it.",
            new Rectangle(column.X, y, column.Width, 24), Theme.TextMuted);
        y += 44;

        DrawBeatLights(ui, new Rectangle(column.X, y, column.Width, 40));
        y += 64;

        DrawReadout(ui, new Rectangle(column.X, y, column.Width, 120));
        y += 140;

        if (_message is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _message,
                new Rectangle(column.X, y, column.Width, 20), Theme.TextMuted);
        }

        ScreenChrome.FooterHint(ui, _detectedMs is null
            ? "Space to start / stop  ·  tap with your lane keys  ·  Esc to go back"
            : "Enter to apply  ·  Space to measure again  ·  Esc to go back");
    }

    private void DrawBeatLights(UiRenderer ui, Rectangle area)
    {
        const int Lights = 8;
        int size = 14;
        int gap = 18;
        int totalWidth = Lights * size + (Lights - 1) * gap;
        int x = area.X + (area.Width - totalWidth) / 2;

        // The flash is decoration; with reduced motion every light stays lit so the beat
        // is still countable without anything moving (§67).
        bool reduced = Context.Accessibility.ReducedMotion;
        double sinceBeat = PositionMs - _lastBeatFlashMs;
        int active = _lastBeatIndex % Lights;

        for (int i = 0; i < Lights; i++)
        {
            bool lit = reduced
                ? _running
                : _running && i == active && sinceBeat < BeatMs * 0.45;

            var rect = new Rectangle(x + i * (size + gap), area.Y + 12, size, size);
            ui.FillRect(rect, lit ? Theme.Accent : Theme.Surface);
            ui.StrokeRect(rect, lit ? Theme.AccentBright : Theme.Border);
        }
    }

    private void DrawReadout(UiRenderer ui, Rectangle area)
    {
        (string Label, string Value)[] cells =
        {
            ("TAPS", $"{_errors.Count} / {Beats}"),
            ("DETECTED", _detectedMs is { } d ? $"{d:+0;-0;0} ms" : "—"),
            ("INPUT OFFSET", $"{CurrentOffset:0} ms"),
        };

        int gap = 12;
        int cellWidth = (area.Width - gap * (cells.Length - 1)) / cells.Length;

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new Rectangle(area.X + i * (cellWidth + gap), area.Y, cellWidth, 76);
            ui.FillRect(cell, Theme.Surface);
            ui.StrokeRect(cell, Theme.Border);

            ui.Text(ui.Mono(Theme.Label), cells[i].Label,
                new Rectangle(cell.X + 14, cell.Y + 12, cell.Width - 28, 16), Theme.TextFaint);
            ui.Text(ui.Display(Theme.DisplayM), cells[i].Value,
                new Rectangle(cell.X + 14, cell.Y + 34, cell.Width - 28, 30), Theme.Text);
        }

        if (_detectedMs is { } detected)
        {
            string explanation = detected > 0
                ? "You tap after the beat, so your inputs will be pulled earlier."
                : detected < 0
                    ? "You tap before the beat, so your inputs will be pushed later."
                    : "You are right on the beat.";

            ui.Text(ui.Body(Theme.Label), explanation,
                new Rectangle(area.X, area.Y + 88, area.Width, 20), Theme.TextMuted);
        }
    }
}
