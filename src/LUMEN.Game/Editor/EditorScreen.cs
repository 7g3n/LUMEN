using Lumen.Audio;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Core.Editing;
using Lumen.Core.Settings;
using Lumen.Data;
using Lumen.Data.Packages;
using Lumen.Game.Screens;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Editor;

/// <summary>
/// The chart editor (spec §42–51, §86–91).
///
/// The workflow the spec describes — import audio, set the BPM, place notes, test play,
/// fix, save — is the order of the toolbar, and every step is reachable without leaving
/// the screen. Every mutation goes through a command so undo is total, and playback uses
/// the game's own audio engine and conductor so what the author hears here is exactly
/// what a player will hear.
///
/// Built on LUMEN's own UI toolkit rather than the ImGui shell the plan sketched: the
/// toolkit already exists, matches the rest of the game, and avoids a dependency whose
/// only job would be to look different from every other screen.
/// </summary>
public sealed class EditorScreen : Screen
{
    private enum Tool { Tap, Hold, Select }

    private const int ToolbarHeight = 44;
    private const int WaveformHeight = 64;

    private readonly string? _openPath;

    private Chart _chart = null!;
    private readonly CommandStack _stack = new();
    private readonly NoteClipboard _clipboard = new();
    private readonly HashSet<Note> _selection = new();

    private TempoMap _tempo = null!;
    private AudioEngine _audio = null!;
    private AudioClip? _clip;
    private IAudioTrack? _track;
    private WaveformPeaks _peaks = WaveformPeaks.Empty;

    private string _chartPath = "";
    private string _audioPath = "";

    private Tool _tool = Tool.Tap;
    private int _division = 4;
    // Roughly two bars of a mid-tempo song in view, which is the span an author actually
    // works in: enough to see the phrase they are building, close enough to place on a
    // 1/16 grid without zooming first.
    private double _msPerPixel = 6.0;
    private double _playheadMs;
    private double _speed = 1.0;
    private bool _playing;

    private string? _status;
    private string? _error;
    private ValidationReport? _report;

    // Drag state. Held notes and the marquee both start on a press in the field.
    private bool _dragging;
    private Tool _dragKind;
    private Vector2 _dragStart;
    private double _dragStartTime;
    private int _dragStartLane;
    private Note[] _dragOrigin = Array.Empty<Note>();
    private Point _mouse;

    private EditorTimeline _timeline;
    private Rectangle _waveArea;

    public EditorScreen(string? openChartPath = null) => _openPath = openChartPath;

    // --- lifecycle ---

    public override void OnEnter()
    {
        _audio = new AudioEngine();

        if (_openPath is { Length: > 0 })
        {
            Load(_openPath);
        }
        else
        {
            _chart = NewChart();
            _tempo = new TempoMap(_chart.BpmPoints);
        }
    }

    public override void OnReveal()
    {
        // Coming back from a test play: the chart is whatever it was, but the track was
        // handed over, so rebuild playback from the clip we still hold.
        _playing = false;
        RebuildTrack();
    }

    public override void OnExit()
    {
        _track?.Dispose();
        _track = null;
    }

    private Chart NewChart() => new()
    {
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 160) },
        Notes = Array.Empty<Note>(),
        Meta = new ChartMeta
        {
            Title = "Untitled",
            Artist = "",
            // The author is whoever is signed in; they can change it afterwards (§78).
            Creator = Context.Session.ActiveProfile?.DisplayName ?? "",
            DifficultyName = "MASTER",
            DifficultyLevel = 10,
        },
    };

    private void Load(string path)
    {
        try
        {
            _chart = ChartJson.Deserialize(File.ReadAllText(path));
            _chartPath = path;
            _tempo = new TempoMap(_chart.BpmPoints);
            _stack.Clear();
            _selection.Clear();

            if (_chart.Meta.AudioFile.Length > 0)
            {
                string beside = Path.Combine(Path.GetDirectoryName(path) ?? "", _chart.Meta.AudioFile);
                LoadAudio(File.Exists(beside)
                    ? beside
                    : Path.Combine(Context.Paths.Songs, _chart.Meta.AudioFile));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"editor could not open {Path.GetFileName(path)}", ex);
            _error = $"That chart could not be opened: {ex.Message}";
            _chart = NewChart();
            _tempo = new TempoMap(_chart.BpmPoints);
        }
    }

    private void LoadAudio(string path)
    {
        try
        {
            _clip = _audio.Decode(path);
            _peaks = WaveformPeaks.From(_clip);
            _audioPath = path;
            RebuildTrack();
            _error = null;
        }
        catch (Exception ex)
        {
            Log.Warn($"editor could not decode {Path.GetFileName(path)}", ex);
            _error = $"That audio could not be decoded: {ex.Message}";
            _clip = null;
            _peaks = WaveformPeaks.Empty;
        }
    }

    /// <summary>Rebuilds the playback track, e.g. after a speed change.</summary>
    private void RebuildTrack()
    {
        _track?.Dispose();
        _track = null;

        if (_clip is null)
        {
            return;
        }

        float volume = (float)(Context.Settings.GetDouble(
            Context.Session.ActivePlayerId, "audio.music", 100) / 100.0);
        _track = _audio.CreateTrack(_clip.AtSpeed(_speed), volume);
        _track.Seek(_playheadMs / 1000.0 / _speed);
    }

    // --- document ---

    private void Do(IEditCommand command)
    {
        _chart = _stack.Apply(_chart, command);
        _tempo = new TempoMap(_chart.BpmPoints);
        PruneSelection();
    }

    private void Undo()
    {
        _chart = _stack.Undo(_chart);
        _tempo = new TempoMap(_chart.BpmPoints);
        PruneSelection();
    }

    private void Redo()
    {
        _chart = _stack.Redo(_chart);
        _tempo = new TempoMap(_chart.BpmPoints);
        PruneSelection();
    }

    /// <summary>Drops selected notes that no longer exist, e.g. after an undo.</summary>
    private void PruneSelection()
    {
        var present = new HashSet<Note>(_chart.Notes);
        _selection.RemoveWhere(n => !present.Contains(n));
    }

    private double Snap(double timeMs) =>
        BeatGrid.Snap(timeMs, _tempo, _division, _chart.ChartOffsetMs);

    // --- input ---

    public override void Update(InputFrame input)
    {
        _mouse = input.MousePosition;
        TickPlayback();

        if (HandleShortcuts(input))
        {
            return;
        }

        HandleTransport(input);
        HandleNavigation(input);
        HandlePointer(input);
    }

    private void TickPlayback()
    {
        if (_track is null || !_playing)
        {
            return;
        }

        // The stretched track runs on its own timeline; multiplying by the speed brings
        // the position back to song time.
        _playheadMs = _track.PositionSeconds * 1000.0 * _speed;

        if (_track.DurationSeconds > 0 && _track.PositionSeconds >= _track.DurationSeconds - 0.01)
        {
            Stop();
        }
    }

    private bool HandleShortcuts(InputFrame input)
    {
        bool ctrl = input.Down(Keys.LeftControl) || input.Down(Keys.RightControl);

        if (ctrl && input.Pressed(Keys.Z))
        {
            if (input.Down(Keys.LeftShift) || input.Down(Keys.RightShift))
            {
                Redo();
            }
            else
            {
                Undo();
            }

            return true;
        }

        if (ctrl && input.Pressed(Keys.Y))
        {
            Redo();
            return true;
        }

        if (ctrl && input.Pressed(Keys.C))
        {
            CopySelection();
            return true;
        }

        if (ctrl && input.Pressed(Keys.V))
        {
            Paste();
            return true;
        }

        if (ctrl && input.Pressed(Keys.A))
        {
            _selection.Clear();
            foreach (Note note in _chart.Notes)
            {
                _selection.Add(note);
            }

            return true;
        }

        if (ctrl && input.Pressed(Keys.S))
        {
            Save();
            return true;
        }

        return false;
    }

    private void HandleTransport(InputFrame input)
    {
        if (input.Pressed(Keys.Space))
        {
            TogglePlay();
        }
        else if (input.Pressed(Keys.Escape))
        {
            Stop();
            Manager.Pop();
        }
        else if (input.Pressed(Keys.F5))
        {
            TestPlay();
        }
        else if (input.Pressed(Keys.Delete) || input.Pressed(Keys.Back))
        {
            DeleteSelection();
        }
        else if (input.Pressed(Keys.D1))
        {
            _tool = Tool.Tap;
        }
        else if (input.Pressed(Keys.D2))
        {
            _tool = Tool.Hold;
        }
        else if (input.Pressed(Keys.D3))
        {
            _tool = Tool.Select;
        }
        else if (input.Pressed(Keys.I))
        {
            ImportAudio();
        }
        else if (input.Pressed(Keys.B))
        {
            NudgeBpm(-1);
        }
        else if (input.Pressed(Keys.N))
        {
            NudgeBpm(+1);
        }
        else if (input.Pressed(Keys.O))
        {
            NudgeOffset(-5);
        }
        else if (input.Pressed(Keys.P))
        {
            NudgeOffset(+5);
        }
        else if (input.Pressed(Keys.K))
        {
            NudgeLevel(-0.1);
        }
        else if (input.Pressed(Keys.L))
        {
            NudgeLevel(+0.1);
        }
        else if (input.Pressed(Keys.OemMinus))
        {
            ChangeSpeed(-1);
        }
        else if (input.Pressed(Keys.OemPlus))
        {
            ChangeSpeed(+1);
        }
        else if (input.Pressed(Keys.V))
        {
            _report = ChartValidator.Validate(_chart);
            _status = _report.CanExport
                ? $"Validation passed ({_report.Warnings} warning(s))."
                : $"{_report.Errors} problem(s) to fix before export.";
        }
        else if (input.Pressed(Keys.X))
        {
            ExportPackage();
        }
        else if (input.Pressed(Keys.H))
        {
            _showHistory = !_showHistory;
        }
    }

    private bool _showHistory;

    /// <summary>Bundles this chart and its audio into a `.lumen` package (spec §57).</summary>
    private void ExportPackage()
    {
        string path = Save();
        if (path.Length == 0)
        {
            return;
        }

        try
        {
            Lumen.Core.Library.LibraryChart? entry = Context.Library.Charts.All()
                .FirstOrDefault(c => string.Equals(c.ChartPath, path, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                _error = "The library has not picked this chart up yet — save it again.";
                return;
            }

            PackageService.ExportResult result = Context.Packages.Export(new[] { entry });
            _status = $"Exported {Path.GetFileName(result.Path)} " +
                      $"({result.SizeBytes / 1024:N0} KB) to the exports folder.";
            _error = null;
        }
        catch (Exception ex)
        {
            Log.Warn("export failed", ex);
            _error = ex.Message;
        }
    }

    /// <summary>Playback speeds the editor offers, slowest first.</summary>
    private static readonly double[] Speeds = { 0.25, 0.5, 0.75, 1.0 };

    private void ChangeSpeed(int direction)
    {
        int index = Array.IndexOf(Speeds, _speed);
        if (index < 0)
        {
            index = Speeds.Length - 1;
        }

        double next = Speeds[Math.Clamp(index + direction, 0, Speeds.Length - 1)];
        if (Math.Abs(next - _speed) < 1e-9)
        {
            return;
        }

        bool wasPlaying = _playing;
        Stop();
        _speed = next;
        RebuildTrack();

        if (wasPlaying)
        {
            TogglePlay();
        }
    }

    private void HandleNavigation(InputFrame input)
    {
        if (input.Pressed(Keys.Up))
        {
            SeekTo(BeatGrid.Step(_playheadMs, _tempo, _division, 1, _chart.ChartOffsetMs));
        }
        else if (input.Pressed(Keys.Down))
        {
            SeekTo(BeatGrid.Step(_playheadMs, _tempo, _division, -1, _chart.ChartOffsetMs));
        }
        else if (input.Pressed(Keys.Home))
        {
            SeekTo(0);
        }

        if (input.ScrollDelta != 0)
        {
            bool ctrl = input.Down(Keys.LeftControl) || input.Down(Keys.RightControl);
            if (ctrl)
            {
                _msPerPixel = Math.Clamp(
                    _msPerPixel * (input.ScrollDelta > 0 ? 0.85 : 1.18), 0.35, 20.0);
            }
            else
            {
                SeekTo(_playheadMs + Math.Sign(input.ScrollDelta) * _msPerPixel * 60);
            }
        }

        if (input.Pressed(Keys.OemOpenBrackets))
        {
            ChangeDivision(-1);
        }
        else if (input.Pressed(Keys.OemCloseBrackets))
        {
            ChangeDivision(1);
        }
    }

    private void ChangeDivision(int direction)
    {
        int index = BeatGrid.Divisions.ToList().IndexOf(_division);
        index = Math.Clamp(index + direction, 0, BeatGrid.Divisions.Count - 1);
        _division = BeatGrid.Divisions[index];
    }

    private void SeekTo(double timeMs)
    {
        _playheadMs = Math.Max(0, timeMs);
        _track?.Seek(_playheadMs / 1000.0 / _speed);
    }

    private void TogglePlay()
    {
        if (_track is null)
        {
            _error = "Import audio before playing.";
            return;
        }

        if (_playing)
        {
            Stop();
        }
        else
        {
            _track.Seek(_playheadMs / 1000.0 / _speed);
            _track.Play();
            _playing = true;
        }
    }

    private void Stop()
    {
        _track?.Pause();
        _playing = false;
    }

    // --- pointer editing ---

    private void HandlePointer(InputFrame input)
    {
        bool inField = _timeline.Area.Contains(_mouse);

        if (_waveArea.Contains(_mouse) && input.MouseClicked && _peaks.DurationMs > 0)
        {
            float t = (_mouse.X - _waveArea.X) / (float)Math.Max(1, _waveArea.Width);
            SeekTo(t * _peaks.DurationMs);
            return;
        }

        if (input.MouseClicked && inField && !_dragging)
        {
            BeginDrag();
        }

        if (_dragging && input.Mouse.LeftButton == ButtonState.Released)
        {
            EndDrag();
        }
    }

    private void BeginDrag()
    {
        int? lane = _timeline.LaneAt(_mouse.X);
        if (lane is null)
        {
            return;
        }

        double rawTime = _timeline.TimeAt(_mouse.Y);
        double time = Snap(rawTime);

        Note? hit = NoteAt(lane.Value, rawTime);

        _dragStart = new Vector2(_mouse.X, _mouse.Y);
        _dragStartTime = time;
        _dragStartLane = lane.Value;

        if (hit is { } note)
        {
            if (!_selection.Contains(note))
            {
                _selection.Clear();
                _selection.Add(note);
            }

            _dragOrigin = _selection.ToArray();
            _dragKind = Tool.Select;
            _dragging = true;
            return;
        }

        switch (_tool)
        {
            case Tool.Tap:
                Do(new AddNotes(Note.Tap(time, lane.Value)));
                break;
            case Tool.Hold:
                _dragKind = Tool.Hold;
                _dragging = true;
                break;
            default:
                _selection.Clear();
                _dragKind = Tool.Select;
                _dragOrigin = Array.Empty<Note>();
                _dragging = true;
                break;
        }
    }

    private void EndDrag()
    {
        _dragging = false;

        int? lane = _timeline.LaneAt(_mouse.X);
        double time = Snap(_timeline.TimeAt(_mouse.Y));

        if (_dragKind == Tool.Hold)
        {
            double end = Math.Max(time, _dragStartTime);
            double start = Math.Min(time, _dragStartTime);
            if (end - start > 1)
            {
                Do(new AddNotes(Note.Hold(start, _dragStartLane, end)));
            }

            return;
        }

        if (_dragOrigin.Length > 0 && lane is { } target)
        {
            double deltaTime = time - _dragStartTime;
            int deltaLane = target - _dragStartLane;
            if (Math.Abs(deltaTime) > 0.5 || deltaLane != 0)
            {
                Note[] moved = _dragOrigin.Select(n => Moved(n, deltaTime, deltaLane)).ToArray();
                Do(new ReplaceNotes(_dragOrigin, moved));

                _selection.Clear();
                foreach (Note note in moved)
                {
                    _selection.Add(note);
                }
            }

            return;
        }

        // A marquee: everything inside the dragged rectangle becomes the selection.
        SelectWithin(_dragStart, new Vector2(_mouse.X, _mouse.Y));
    }

    private Note Moved(Note note, double deltaTime, int deltaLane)
    {
        double time = Math.Max(0, note.TimeMs + deltaTime);
        return note with
        {
            TimeMs = time,
            Lane = Math.Clamp(note.Lane + deltaLane, 0, _chart.LaneCount - 1),
            EndTimeMs = note.IsHold ? time + note.DurationMs : 0,
        };
    }

    private void SelectWithin(Vector2 a, Vector2 b)
    {
        float left = Math.Min(a.X, b.X);
        float right = Math.Max(a.X, b.X);
        double from = _timeline.TimeAt(Math.Max(a.Y, b.Y));
        double to = _timeline.TimeAt(Math.Min(a.Y, b.Y));

        _selection.Clear();
        foreach (Note note in _chart.Notes)
        {
            float x = _timeline.XAtLane(note.Lane) + _timeline.LaneWidth / 2;
            if (x >= left && x <= right && note.TimeMs >= from && note.TimeMs <= to)
            {
                _selection.Add(note);
            }
        }
    }

    private Note? NoteAt(int lane, double timeMs)
    {
        double tolerance = _msPerPixel * 9;
        foreach (Note note in _chart.Notes)
        {
            if (note.Lane != lane)
            {
                continue;
            }

            if (Math.Abs(note.TimeMs - timeMs) <= tolerance)
            {
                return note;
            }

            if (note.IsHold && timeMs >= note.TimeMs && timeMs <= note.EndTimeMs + tolerance)
            {
                return note;
            }
        }

        return null;
    }

    // --- actions ---

    private void CopySelection()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        _clipboard.Copy(_selection);
        _status = $"Copied {_selection.Count} note{(_selection.Count == 1 ? "" : "s")}.";
    }

    private void Paste()
    {
        if (!_clipboard.HasContent)
        {
            return;
        }

        IReadOnlyList<Note> pasted = _clipboard.Paste(Snap(_playheadMs), _chart.LaneCount);
        Do(new AddNotes(pasted));

        _selection.Clear();
        foreach (Note note in pasted)
        {
            _selection.Add(note);
        }
    }

    private void DeleteSelection()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        Do(new RemoveNotes(_selection.ToArray()));
        _selection.Clear();
    }

    private void ImportAudio()
    {
        // No native file dialog in the toolkit yet, so the editor adopts whatever audio
        // sits in the songs folder. Dropping a file in there and pressing I is the whole
        // import flow until Phase 7 brings packages and a picker with them.
        try
        {
            string[] candidates = Directory.Exists(Context.Paths.Songs)
                ? Directory.GetFiles(Context.Paths.Songs, "*.wav")
                    .Concat(Directory.GetFiles(Context.Paths.Songs, "*.ogg"))
                    .Concat(Directory.GetFiles(Context.Paths.Songs, "*.mp3"))
                    .OrderBy(f => f)
                    .ToArray()
                : Array.Empty<string>();

            if (candidates.Length == 0)
            {
                _error = "Put an audio file in the songs folder first.";
                return;
            }

            int index = Array.IndexOf(candidates, _audioPath);
            string next = candidates[(index + 1) % candidates.Length];

            LoadAudio(next);
            Do(new SetMeta(_chart.Meta with { AudioFile = Path.GetFileName(next) }));
            _status = $"Audio: {Path.GetFileName(next)}";
        }
        catch (Exception ex)
        {
            _error = $"Could not read the songs folder: {ex.Message}";
        }
    }

    private void NudgeBpm(double delta)
    {
        double bpm = Math.Clamp(_tempo.BpmAt(0) + delta, 20, 400);
        var points = _chart.BpmPoints.ToList();
        points[0] = points[0] with { Bpm = bpm, AtMs = 0 };
        Do(new SetBpmPoints(points));
    }

    private void NudgeOffset(double deltaMs) =>
        Do(new SetChartOffset(_chart.ChartOffsetMs + deltaMs));

    private void NudgeLevel(double delta) =>
        Do(new SetMeta(_chart.Meta with
        {
            DifficultyLevel = Math.Clamp(_chart.Meta.DifficultyLevel + delta, 1, 20),
        }));

    private string Save()
    {
        try
        {
            // The identity stamp is not an edit - it is how this chart will be recognised
            // across every future edit - so it is applied outside the undo stack. Undoing
            // your way back past it would only orphan the history.
            if (_chart.Id is null)
            {
                _chart = _chart with { Id = Guid.NewGuid() };
            }

            if (_chartPath.Length == 0)
            {
                Directory.CreateDirectory(Context.Paths.ChartsLocal);
                string stem = Slug(_chart.Meta.Title) + "-" + Slug(_chart.Meta.DifficultyName);
                _chartPath = Path.Combine(
                    Context.Paths.ChartsLocal, $"{stem}.{GameIdentity.ChartExtension}");
            }

            string document = ChartJson.Serialize(_chart);
            AtomicFile.WriteAllText(_chartPath, document);
            _stack.MarkClean();

            int revision = Context.ChartVersions.Record(_chart.Id.Value, _chart, document);

            // The library index is a cache of these files, so tell it immediately rather
            // than waiting for the next visit to Song Select.
            Context.Library.Scan();

            _report = ChartValidator.Validate(_chart);
            _status = _report.CanExport
                ? $"Saved to {Path.GetFileName(_chartPath)} (revision {revision})."
                : $"Saved, but {_report.Errors} problem(s) block export.";
            _error = null;
            return _chartPath;
        }
        catch (Exception ex)
        {
            Log.Error("editor could not save the chart", ex);
            _error = $"Could not save: {ex.Message}";
            return "";
        }
    }

    private void TestPlay()
    {
        if (_chart.Notes.Count == 0)
        {
            _error = "Place some notes before test playing.";
            return;
        }

        if (_audioPath.Length == 0 || !File.Exists(_audioPath))
        {
            _error = "Import audio before test playing.";
            return;
        }

        string path = Save();
        if (path.Length == 0)
        {
            return;
        }

        // Hand the audio device over for the duration of the play; OnReveal takes it back.
        Stop();
        _track?.Dispose();
        _track = null;

        Manager.Push(new GameplayScreen(path, _audioPath));
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        string slug = new string(chars).Trim('-');
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Length > 0 ? slug : "untitled";
    }

    // --- drawing ---

    public override void Draw(UiRenderer ui)
    {
        int header = 64;
        var body = new Rectangle(0, header, ui.Width, ui.Height - header - ToolbarHeight - WaveformHeight);
        int sidebar = Math.Min(300, ui.Width / 4);

        _timeline = new EditorTimeline(
            new Rectangle(0, body.Y, body.Width - sidebar, body.Height),
            _chart.LaneCount, _msPerPixel, _playheadMs);
        _waveArea = new Rectangle(0, body.Bottom, ui.Width, WaveformHeight);

        DrawHeader(ui, header);
        DrawTimeline(ui);
        DrawSidebar(ui, new Rectangle(body.Right - sidebar + 12, body.Y + 8, sidebar - 24, body.Height - 16));
        DrawWaveform(ui);
        DrawToolbar(ui, new Rectangle(0, ui.Height - ToolbarHeight, ui.Width, ToolbarHeight));
    }

    private void DrawHeader(UiRenderer ui, int height)
    {
        ui.FillRect(new Rectangle(0, 0, ui.Width, height), Theme.Surface);
        ui.FillRect(new Rectangle(0, height - 1, ui.Width, 1), Theme.Border);

        ui.Text(ui.Mono(Theme.Label), "CHART EDITOR",
            new Rectangle(24, 10, 300, 16), Theme.Accent);

        string title = _chart.Meta.Title.Length > 0 ? _chart.Meta.Title : "Untitled";
        ui.Text(ui.Display(Theme.DisplayM), title, new Rectangle(24, 26, 420, 26), Theme.Text);

        string detail =
            $"{(_chart.Meta.Artist.Length > 0 ? _chart.Meta.Artist : "no artist")}   ·   " +
            $"{_tempo.BpmAt(0):0.#} BPM   ·   offset {_chart.ChartOffsetMs:0} ms   ·   " +
            $"{_chart.Meta.DifficultyName} {_chart.Meta.DifficultyLevel:0.0}   ·   " +
            $"{_chart.Notes.Count} notes";
        ui.Text(ui.Mono(Theme.Label), detail,
            new Rectangle(460, 26, ui.Width - 640, 26), Theme.TextMuted);

        if (_stack.IsDirty)
        {
            ui.Text(ui.Mono(Theme.Label), "UNSAVED",
                new Rectangle(0, 26, ui.Width - 24, 26), Theme.Bad, TextAlign.Right);
        }
    }

    private void DrawTimeline(UiRenderer ui)
    {
        Rectangle area = _timeline.Area;
        ui.FillRect(area, Theme.Ground);

        // Lanes.
        ui.FillRect(new Rectangle(_timeline.FieldLeft, area.Y, _timeline.FieldWidth, area.Height),
            Theme.Surface.WithAlpha(0.35f));

        for (int lane = 1; lane < _chart.LaneCount; lane++)
        {
            ui.FillRect(new Rectangle((int)_timeline.XAtLane(lane), area.Y, 1, area.Height), Theme.Border);
        }

        DrawGrid(ui, area);
        DrawNotes(ui, area);
        DrawPendingHold(ui);
        DrawMarquee(ui);

        // Playhead.
        ui.FillRect(new Rectangle(area.X, _timeline.PlayheadY - 1, area.Width, 2),
            _playing ? Theme.Good : Theme.AccentBright);
    }

    private void DrawGrid(UiRenderer ui, Rectangle area)
    {
        IReadOnlyList<GridLine> lines = BeatGrid.Lines(
            Math.Max(0, _timeline.BottomMs), _timeline.TopMs, _tempo, _division,
            beatsPerBar: 4, offsetMs: _chart.ChartOffsetMs, maxLines: 600);

        foreach (GridLine line in lines)
        {
            int y = (int)_timeline.YAt(line.TimeMs);
            if (y < area.Y || y > area.Bottom)
            {
                continue;
            }

            Color color = line.Kind switch
            {
                GridLineKind.Bar => Theme.BorderStrong,
                GridLineKind.Beat => Theme.Border,
                _ => Theme.Border.WithAlpha(0.45f),
            };
            ui.FillRect(new Rectangle(_timeline.FieldLeft, y, _timeline.FieldWidth, 1), color);

            if (line.Kind == GridLineKind.Bar)
            {
                (int bar, _) = BeatGrid.BarBeatAt(line.TimeMs, _tempo, 4, _chart.ChartOffsetMs);
                ui.Text(ui.Mono(Theme.Label), bar.ToString(),
                    new Rectangle(area.X + 8, y - 8, EditorTimeline.Gutter - 20, 16),
                    Theme.TextFaint, TextAlign.Right);
            }
        }
    }

    private void DrawNotes(UiRenderer ui, Rectangle area)
    {
        double top = _timeline.TopMs;
        double bottom = _timeline.BottomMs - 200;

        foreach (Note note in _chart.Notes)
        {
            double end = note.IsHold ? note.EndTimeMs : note.TimeMs;
            if (end < bottom || note.TimeMs > top)
            {
                continue;
            }

            bool selected = _selection.Contains(note);
            int x = (int)_timeline.XAtLane(note.Lane);
            int w = (int)_timeline.LaneWidth;
            int y = (int)_timeline.YAt(note.TimeMs);

            if (note.IsHold)
            {
                int tailY = (int)_timeline.YAt(note.EndTimeMs);
                var body = new Rectangle(x + w / 3, tailY, Math.Max(4, w / 3), Math.Max(3, y - tailY));
                ui.FillRect(body, selected ? Theme.Perfect.WithAlpha(0.5f) : Theme.Accent.WithAlpha(0.35f));
                DrawNoteHead(ui, x, w, tailY, selected);
            }

            DrawNoteHead(ui, x, w, y, selected);
        }
    }

    private void DrawNoteHead(UiRenderer ui, int x, int w, int y, bool selected)
    {
        var head = new Rectangle(x + 6, y - 5, Math.Max(8, w - 12), 10);
        ui.FillRect(head, selected ? Theme.Perfect : Theme.AccentBright);

        if (selected)
        {
            ui.StrokeRect(new Rectangle(head.X - 3, head.Y - 3, head.Width + 6, head.Height + 6),
                Theme.Perfect);
        }
    }

    private void DrawPendingHold(UiRenderer ui)
    {
        if (!_dragging || _dragKind != Tool.Hold)
        {
            return;
        }

        double time = Snap(_timeline.TimeAt(_mouse.Y));
        int x = (int)_timeline.XAtLane(_dragStartLane);
        int w = (int)_timeline.LaneWidth;
        int y0 = (int)_timeline.YAt(Math.Min(time, _dragStartTime));
        int y1 = (int)_timeline.YAt(Math.Max(time, _dragStartTime));

        ui.FillRect(new Rectangle(x + w / 3, y1, Math.Max(4, w / 3), Math.Max(3, y0 - y1)),
            Theme.Perfect.WithAlpha(0.4f));
    }

    private void DrawMarquee(UiRenderer ui)
    {
        if (!_dragging || _dragKind != Tool.Select || _dragOrigin.Length > 0)
        {
            return;
        }

        int x = (int)Math.Min(_dragStart.X, _mouse.X);
        int y = (int)Math.Min(_dragStart.Y, _mouse.Y);
        int w = (int)Math.Abs(_mouse.X - _dragStart.X);
        int h = (int)Math.Abs(_mouse.Y - _dragStart.Y);

        ui.FillRect(new Rectangle(x, y, w, h), Theme.Accent.WithAlpha(0.12f));
        ui.StrokeRect(new Rectangle(x, y, w, h), Theme.Accent);
    }

    private void DrawWaveform(UiRenderer ui)
    {
        ui.FillRect(_waveArea, Theme.Surface);
        ui.FillRect(new Rectangle(_waveArea.X, _waveArea.Y, _waveArea.Width, 1), Theme.Border);

        if (_peaks.DurationMs <= 0)
        {
            ui.Text(ui.Mono(Theme.Label), "no audio — press I to pick one from the songs folder",
                _waveArea, Theme.TextFaint, TextAlign.Center);
            return;
        }

        int midY = _waveArea.Y + _waveArea.Height / 2;
        int half = _waveArea.Height / 2 - 6;

        for (int x = 0; x < _waveArea.Width; x++)
        {
            double from = x / (double)_waveArea.Width * _peaks.DurationMs;
            double to = (x + 1) / (double)_waveArea.Width * _peaks.DurationMs;
            (float min, float max) = _peaks.Range(from, to);

            int top = midY - (int)(max * half);
            int bottom = midY - (int)(min * half);
            ui.FillRect(new Rectangle(_waveArea.X + x, top, 1, Math.Max(1, bottom - top)),
                Theme.Accent.WithAlpha(0.55f));
        }

        int playX = _waveArea.X + (int)(_playheadMs / _peaks.DurationMs * _waveArea.Width);
        ui.FillRect(new Rectangle(playX - 1, _waveArea.Y, 2, _waveArea.Height), Theme.Perfect);
    }

    private void DrawSidebar(UiRenderer ui, Rectangle area)
    {
        int y = area.Y;

        y = Section(ui, area, y, "POSITION");
        (int bar, int beat) = BeatGrid.BarBeatAt(_playheadMs, _tempo, 4, _chart.ChartOffsetMs);
        y = Line(ui, area, y, $"{Time(_playheadMs)}   bar {bar}:{beat}");
        y += 10;

        y = Section(ui, area, y, "TOOL");
        y = Line(ui, area, y, _tool switch
        {
            Tool.Tap => "1  TAP        (selected)",
            Tool.Hold => "2  HOLD       (selected)",
            _ => "3  SELECT     (selected)",
        });
        y = Line(ui, area, y, $"grid {BeatGrid.Label(_division)}" +
                              (BeatGrid.IsTriplet(_division) ? "  triplet" : ""));
        y = Line(ui, area, y, $"zoom {1000 / _msPerPixel:0} px/s");
        y = Line(ui, area, y, $"speed {_speed:0.##}x");
        y += 10;

        y = Section(ui, area, y, "SELECTION");
        y = Line(ui, area, y, _selection.Count == 0 ? "nothing selected" : $"{_selection.Count} notes");
        y = Line(ui, area, y, _clipboard.HasContent ? $"clipboard {_clipboard.Count}" : "clipboard empty");
        y += 10;

        y = Section(ui, area, y, "HISTORY");
        y = Line(ui, area, y, _stack.CanUndo ? $"undo: {_stack.UndoLabel}" : "nothing to undo");
        y = Line(ui, area, y, _stack.CanRedo ? $"redo: {_stack.RedoLabel}" : "nothing to redo");
        y += 10;

        if (_report is { } report)
        {
            y = Section(ui, area, y, "VALIDATION");
            foreach (ValidationCheck check in Enum.GetValues<ValidationCheck>())
            {
                bool ok = report.Passed(check);
                ui.Text(ui.Mono(Theme.Label), (ok ? "OK  " : "X   ") + check.ToString().ToUpperInvariant(),
                    new Rectangle(area.X, y, area.Width, 16), ok ? Theme.Good : Theme.Danger);
                y += 16;
            }

            ValidationIssue? first = report.Issues.FirstOrDefault();
            if (first is not null)
            {
                y = Line(ui, area, y, Truncate(first.Message, 34));
            }

            y += 10;
        }

        if (_showHistory && _chart.Id is { } chartId)
        {
            y = Section(ui, area, y, "HISTORY (H)");
            foreach (Lumen.Data.Repositories.ChartVersion version in
                     Context.ChartVersions.List(chartId).Take(6))
            {
                y = Line(ui, area, y,
                    $"v{version.Version}  {version.NoteCount} notes  " +
                    $"{version.SavedUtc.ToLocalTime():HH:mm}");
            }

            y += 10;
        }

        y = Section(ui, area, y, "SHORTCUTS");
        foreach (string hint in new[]
                 {
                     "Space  play / pause",
                     "1 2 3  tap, hold, select",
                     "[ ]    grid division",
                     "Ctrl+Z / Ctrl+Y  undo, redo",
                     "Ctrl+C / Ctrl+V  copy, paste",
                     "Ctrl+A / Del     all, delete",
                     "Ctrl+S  save",
                     "F5     test play",
                     "I      audio    B / N  BPM",
                     "O / P  offset   K / L  level",
                     "- / =  playback speed",
                     "V validate · X export · H history",
                     "Esc    leave",
                 })
        {
            y = Line(ui, area, y, hint);
        }

        if (_error is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _error,
                new Rectangle(area.X, area.Bottom - 40, area.Width, 36), Theme.Danger);
        }
        else if (_status is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _status,
                new Rectangle(area.X, area.Bottom - 40, area.Width, 36), Theme.TextMuted);
        }
    }

    private static int Section(UiRenderer ui, Rectangle area, int y, string title)
    {
        ui.Text(ui.Mono(Theme.Label), title, new Rectangle(area.X, y, area.Width, 16), Theme.Accent);
        return y + 20;
    }

    private static int Line(UiRenderer ui, Rectangle area, int y, string text)
    {
        ui.Text(ui.Mono(Theme.Label), text, new Rectangle(area.X, y, area.Width, 16), Theme.TextMuted);
        return y + 18;
    }

    private void DrawToolbar(UiRenderer ui, Rectangle area)
    {
        ui.FillRect(area, Theme.Surface);
        ui.FillRect(new Rectangle(area.X, area.Y, area.Width, 1), Theme.Border);

        string left = _playing ? "■ STOP  (Space)" : "▶ PLAY  (Space)";
        ui.Text(ui.Mono(Theme.Mono), left,
            new Rectangle(area.X + 24, area.Y, 200, area.Height), Theme.Text);

        string middle =
            $"TOOL {_tool.ToString().ToUpperInvariant()}    GRID {BeatGrid.Label(_division)}    " +
            $"SPEED {_speed:0.##}x    {Time(_playheadMs)}";
        ui.Text(ui.Mono(Theme.Mono), middle, area, Theme.TextMuted, TextAlign.Center);

        ui.Text(ui.Mono(Theme.Mono), "F5 TEST PLAY   ·   Ctrl+S SAVE",
            new Rectangle(area.X, area.Y, area.Width - 24, area.Height), Theme.Accent, TextAlign.Right);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..Math.Max(1, max - 1)] + "…";

    private static string Time(double ms)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds:000}";
    }
}
