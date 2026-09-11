using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Data;
using Lumen.Data.Achievements;
using Lumen.Data.Backup;
using Lumen.Data.Library;
using Lumen.Data.Packages;
using Lumen.Data.Repositories;
using Lumen.Game;
using Lumen.Game.Config;
using Lumen.Game.Editor;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Lumen.Tests.Game;

/// <summary>
/// The whole import path, driven through the real editor: a file dropped from outside is
/// taken in, decoded, recorded on the chart, saved, and still attached when the chart is
/// opened again (spec §49).
///
/// Only <c>Update</c> and the drop handler run — drawing needs a graphics device — which is
/// enough, because every decision in that path is made outside <c>Draw</c>.
/// </summary>
public class EditorAudioDropTests : IDisposable
{
    private readonly string _exeDir;
    private readonly LumenPaths _paths;
    private readonly Database _db;
    private readonly GameContext _context;
    private readonly ScreenManager _screens;
    private readonly string _elsewhere;

    private KeyboardState _previousKeyboard = new();

    public EditorAudioDropTests()
    {
        _exeDir = Path.Combine(Path.GetTempPath(), "lumen-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_exeDir);
        File.WriteAllText(Path.Combine(_exeDir, LumenPaths.PortableSentinelFileName), "");

        _elsewhere = Path.Combine(_exeDir, "desktop");
        Directory.CreateDirectory(_elsewhere);

        _paths = LumenPaths.Resolve(_exeDir);
        _paths.EnsureCreated();

        _db = new Database(_paths.DatabaseFile);
        _db.Open();

        var profiles = new ProfileRepository(_db);
        var settings = new SettingsRepository(_db);
        var appMeta = new AppMetaStore(_db);
        var session = new Session(_db, profiles, appMeta);
        var library = new LibraryService(new LibraryRepository(_db), _paths);
        var scores = new ScoreRepository(_db, BalanceConfig.Default);

        _context = new GameContext
        {
            Paths = _paths,
            Database = _db,
            Profiles = profiles,
            Settings = settings,
            Scores = scores,
            Library = library,
            Packages = new PackageService(_paths, library),
            ChartVersions = new ChartVersionRepository(_db),
            Replays = new ReplayRepository(_db, _paths.Replays),
            Achievements = new AchievementService(
                scores, profiles, library.Charts, new AchievementRepository(_db)),
            Backups = new BackupService(_db, _paths, appMeta, library),
            Frames = new Lumen.Game.Engine.FrameProfiler(),
            AppMeta = appMeta,
            Display = new DisplayConfig(),
            Balance = BalanceConfig.Default,
            Session = session,
            RequestExit = () => { },
        };

        session.SetActive(profiles.Create("Nagisa"));
        _screens = new ScreenManager(_context);
    }

    public void Dispose()
    {
        _screens.Top?.OnExit();
        _db.Dispose();
        try { Directory.Delete(_exeDir, recursive: true); } catch { /* best effort */ }
    }

    // --- a real, decodable WAV ---

    /// <summary>
    /// An actual 16-bit PCM WAV, because the point of these tests is that the decoder runs.
    /// A file with the right extension and rubbish inside would prove nothing.
    /// </summary>
    private string WriteWav(string directory, string name, double seconds = 1.0)
    {
        const int Rate = 44100;
        const int Channels = 2;
        int frames = (int)(Rate * seconds);
        int dataBytes = frames * Channels * 2;

        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF".ToCharArray());
            w.Write(36 + dataBytes);
            w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray());
            w.Write(16);
            w.Write((short)1);                       // PCM
            w.Write((short)Channels);
            w.Write(Rate);
            w.Write(Rate * Channels * 2);            // byte rate
            w.Write((short)(Channels * 2));          // block align
            w.Write((short)16);                      // bits per sample
            w.Write("data".ToCharArray());
            w.Write(dataBytes);

            for (int i = 0; i < frames; i++)
            {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * 12000);
                w.Write(sample);
                w.Write(sample);
            }
        }

        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    // --- driving the screen ---

    private void Frame(params Keys[] held)
    {
        var keyboard = new KeyboardState(held);
        _screens.Update(new InputFrame
        {
            Keyboard = keyboard,
            PreviousKeyboard = _previousKeyboard,
            Mouse = new MouseState(),
            PreviousMouse = new MouseState(),
            TypedText = "",
            DeltaSeconds = 1 / 60.0,
        });
        _previousKeyboard = keyboard;
    }

    private void Press(Keys key, params Keys[] alsoHeld)
    {
        Frame(alsoHeld.Append(key).ToArray());
        Frame();
    }

    private EditorScreen OpenEditor(string? chartPath = null)
    {
        var editor = new EditorScreen(chartPath);
        _screens.SetRoot(editor);
        return editor;
    }

    private static void Drop(EditorScreen editor, params string[] paths) =>
        ((IFileDropTarget)editor).OnFilesDropped(paths);

    // --- the tests ---

    /// <summary>
    /// The bug this fixes. The window raises the drop and the manager offers it to the top
    /// screen, but the editor was not a drop target, so every file dropped on it fell on
    /// the floor.
    /// </summary>
    [Fact]
    public void The_editor_accepts_dropped_files_at_all()
    {
        OpenEditor().Should().BeAssignableTo<IFileDropTarget>();
    }

    [Fact]
    public void A_wav_dropped_from_outside_is_decoded_and_taken_into_the_songs_folder()
    {
        string source = WriteWav(_elsewhere, "track.wav");
        EditorScreen editor = OpenEditor();

        Drop(editor, source);

        editor.LoadedAudioFileName.Should().Be("track.wav");
        editor.LoadedAudioDurationMs.Should().BeApproximately(1000, 50);
        editor.ChartAudioFileName.Should().Be("track.wav");

        File.Exists(Path.Combine(_paths.Songs, "track.wav")).Should().BeTrue(
            "a chart records a name resolved against the songs folder, so the file has to be there");
    }

    /// <summary>
    /// The round trip the whole thing exists for: drop, save, close, open again, and the
    /// audio is still attached.
    /// </summary>
    [Fact]
    public void The_audio_is_still_attached_after_saving_and_reopening()
    {
        string source = WriteWav(_elsewhere, "track.wav");
        EditorScreen editor = OpenEditor();

        Drop(editor, source);

        // A note, so there is a chart worth saving, then Ctrl+S.
        Press(Keys.D1);
        Press(Keys.S, Keys.LeftControl);

        string[] saved = Directory.GetFiles(_paths.ChartsLocal, "*.lumenchart");
        saved.Should().ContainSingle("Ctrl+S must have written the chart");

        Chart onDisk = ChartJson.Deserialize(File.ReadAllText(saved[0]));
        onDisk.Meta.AudioFile.Should().Be("track.wav");
        onDisk.Meta.AudioFile.Should().NotContain(":",
            "an absolute path would not survive the trip to another machine");

        editor.OnExit();
        EditorScreen reopened = OpenEditor(saved[0]);

        reopened.LoadedAudioFileName.Should().Be("track.wav");
        reopened.LoadedAudioDurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_file_already_in_the_songs_folder_is_loaded_where_it_lies()
    {
        string source = WriteWav(_paths.Songs, "resident.wav");
        EditorScreen editor = OpenEditor();

        Drop(editor, source);

        editor.LoadedAudioFileName.Should().Be("resident.wav");
        Directory.GetFiles(_paths.Songs, "*.wav").Should().HaveCount(1, "nothing was duplicated");
    }

    [Fact]
    public void Something_that_is_not_audio_leaves_the_editor_as_it_was()
    {
        string junk = Path.Combine(_elsewhere, "cover.png");
        File.WriteAllBytes(junk, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        EditorScreen editor = OpenEditor();
        Drop(editor, junk);

        editor.LoadedAudioFileName.Should().BeEmpty();
        editor.ChartAudioFileName.Should().BeEmpty();
        Directory.GetFiles(_paths.Songs).Should().BeEmpty("nothing unusable should be taken in");
    }

    [Fact]
    public void A_wav_that_is_not_really_a_wav_is_reported_rather_than_crashing_the_editor()
    {
        string liar = Path.Combine(_elsewhere, "lying.wav");
        File.WriteAllText(liar, "this is not audio at all");

        EditorScreen editor = OpenEditor();
        Action drop = () => Drop(editor, liar);

        drop.Should().NotThrow("a bad file is something for the author to see, not a crash");
        editor.LoadedAudioFileName.Should().BeEmpty();
    }

    [Fact]
    public void A_mixed_drop_picks_the_audio_out_of_it()
    {
        string junk = Path.Combine(_elsewhere, "notes.txt");
        File.WriteAllText(junk, "lyrics");
        string source = WriteWav(_elsewhere, "song.wav");

        EditorScreen editor = OpenEditor();
        Drop(editor, junk, source);

        editor.LoadedAudioFileName.Should().Be("song.wav");
    }

    [Fact]
    public void Dropping_a_second_song_replaces_the_first()
    {
        EditorScreen editor = OpenEditor();

        Drop(editor, WriteWav(_elsewhere, "first.wav"));
        editor.LoadedAudioFileName.Should().Be("first.wav");

        Drop(editor, WriteWav(_elsewhere, "second.wav", 2.0));

        editor.LoadedAudioFileName.Should().Be("second.wav");
        editor.ChartAudioFileName.Should().Be("second.wav");
        editor.LoadedAudioDurationMs.Should().BeApproximately(2000, 100);
    }

    [Fact]
    public void Dropping_nothing_changes_nothing()
    {
        EditorScreen editor = OpenEditor();
        Drop(editor);

        editor.LoadedAudioFileName.Should().BeEmpty();
    }

    /// <summary>
    /// The drop has to reach the editor the way the window delivers it, not only when a
    /// test calls the handler directly.
    /// </summary>
    [Fact]
    public void A_drop_delivered_the_way_the_window_delivers_it_reaches_the_editor()
    {
        string source = WriteWav(_elsewhere, "track.wav");
        EditorScreen editor = OpenEditor();

        _screens.DeliverFileDrop(new[] { source });

        editor.LoadedAudioFileName.Should().Be("track.wav");
    }

    /// <summary>
    /// Undo has to reach the audio change too, or the editor's history quietly lies about
    /// what it can take back.
    /// </summary>
    [Fact]
    public void Attaching_audio_is_undoable_like_any_other_edit()
    {
        EditorScreen editor = OpenEditor();
        Drop(editor, WriteWav(_elsewhere, "track.wav"));

        editor.ChartAudioFileName.Should().Be("track.wav");

        Press(Keys.Z, Keys.LeftControl);

        editor.ChartAudioFileName.Should().BeEmpty();
    }
}
