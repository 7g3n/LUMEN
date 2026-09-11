using System;
using System.IO;
using System.Linq;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Data;
using Lumen.Data.Achievements;
using Lumen.Data.Backup;
using Lumen.Data.Library;
using Lumen.Data.Packages;
using Lumen.Data.Repositories;
using Lumen.Game.Config;
using Lumen.Game.Engine;
using Lumen.Game.Rendering;
using Lumen.Game.Screens;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Lumen.Game;

/// <summary>
/// The application shell: window, clock, frame limiter, input routing, and the screen
/// stack. Game rules live in LUMEN.Core; this class only wires and draws.
/// </summary>
internal sealed class LumenGame : Microsoft.Xna.Framework.Game
{
    private static readonly Color ErrorGround = new(0x1A, 0x0C, 0x0E);
    private static readonly Color ErrorAccent = new(0xE5, 0x6A, 0x60);
    private static readonly Color OverlayText = new(0x8B, 0x94, 0xA5);

    private readonly GraphicsDeviceManager _graphics;
    private readonly LumenPaths _paths;
    private readonly LaunchOptions _options;
    private readonly DisplayConfig _display;
    private readonly Database _db;
    private readonly GameClock _clock = new();
    private readonly FrameLimiter _limiter = new(0);
    private readonly Fonts _fonts = new();

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private UiRenderer _ui = null!;
    private InputRouter _input = null!;
    private ScreenManager _screens = null!;
    private GameContext _context = null!;

    private double _frameMsSmoothed;
    private string _overlayLine = "";
    private double _overlayBuiltAt = double.NegativeInfinity;
    private readonly FrameProfiler _profiler = new();

    // One frame, in the parts that can be named: the whole span, the game's own work,
    // the driver's present, and the limiter's deliberate wait. Recorded together at the
    // end of the frame so the four numbers describe the same frame (§68).
    private readonly System.Diagnostics.Stopwatch _frameWatch = new();
    private double _workMs;
    private double _waitMs;
    private (Exception ex, string report)? _fatal;

    public LumenGame(LumenPaths paths, LaunchOptions options, DisplayConfig display, Database db)
    {
        _paths = paths;
        _options = options;
        _display = display;
        _db = db;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = display.Width,
            PreferredBackBufferHeight = display.Height,
            SynchronizeWithVerticalRetrace = display.Vsync,
            GraphicsProfile = GraphicsProfile.HiDef,
            IsFullScreen = display.Fullscreen,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.AllowUserResizing = true;
        Window.Title = GameIdentity.WindowTitle;
    }

    protected override void Initialize()
    {
        Window.Title = GameIdentity.WindowTitle;
        _paths.EnsureCreated();

        int refresh = DisplayInfo.GetPrimaryRefreshRate();
        _limiter.TargetFps = _display.FollowRefreshRate ? Math.Max(refresh, 60) : _display.FpsCap;
        _profiler.SetBudget(FrameProfiler.BudgetForFps(
            _limiter.TargetFps > 0 ? _limiter.TargetFps : Math.Max(refresh, 60)));
        Log.Info($"display {_display.Width}x{_display.Height} vsync={_display.Vsync} " +
                 $"cap={(_limiter.TargetFps == 0 ? "uncapped" : _limiter.TargetFps)} (monitor {refresh}Hz)");

        BuildContext();

        // A package dropped on the window is the import gesture (§56); the manager passes
        // it to whichever screen is willing to take it.
        Window.FileDrop += (_, e) => _screens.DeliverFileDrop(e.Files);

        // Daily, and whenever the game has been updated — the moment before a migration
        // touches the database is exactly when a copy of the old one is worth having.
        _context.Backups.AutoBackupIfDue();

        base.Initialize();
    }

    private void BuildContext()
    {
        var profiles = new ProfileRepository(_db);
        var library = new LibraryService(new LibraryRepository(_db), _paths);
        var settings = new SettingsRepository(_db);
        var appMeta = new AppMetaStore(_db);
        var session = new Session(_db, profiles, appMeta);
        session.Restore();

        Core.Balance.BalanceConfig balance = Core.Balance.BalanceConfigFile.LoadOrCreate(_paths.Settings);

        var scores = new ScoreRepository(_db, balance);
        var replays = new ReplayRepository(_db, _paths.Replays);
        var achievements = new AchievementRepository(_db);
        var backups = new BackupService(_db, _paths, appMeta, library);

        // Debris from a write that was interrupted by a kill or a power cut (§74).
        int swept = new[] { _paths.Songs, _paths.ChartsLocal, _paths.ChartsImported, _paths.Replays }
            .Sum(folder => AtomicFile.SweepStaleTemporaries(folder));
        if (swept > 0)
        {
            Log.Info($"swept {swept} leftover temporary file(s)");
        }

        // A replay whose file has gone is one the player can see and cannot watch.
        int orphaned = replays.PruneMissing();
        if (orphaned > 0)
        {
            Log.Info($"dropped {orphaned} replay row(s) with no file");
        }

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
            Replays = replays,
            Achievements = new AchievementService(
                scores, profiles, library.Charts, achievements),
            Backups = backups,
            Frames = _profiler,
            AppMeta = appMeta,
            Display = _display,
            Balance = balance,
            Session = session,
            RequestExit = Exit,
        };

        _context.RefreshAccessibility();
        _screens = new ScreenManager(_context);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _fonts.Load(AppContext.BaseDirectory);
        _fonts.Warm(_spriteBatch);

        _ui = new UiRenderer(GraphicsDevice, _spriteBatch, _pixel, _fonts);
        _input = new InputRouter(Window);

        if (_options.CaptureDir is { } dir)
        {
            RunCapture(dir);
            Exit();
            return;
        }

        if (_options.AutoPlay)
        {
            StartAutoPlay();
            return;
        }

        _screens.SetRoot(_context.Session.HasProfile
            ? new MainMenuScreen()
            : new SetupScreen());
    }

    /// <summary>
    /// A chart given to <c>--autoplay</c> may be any length, so the watchdog allows for a
    /// long one; it is there to stop a hung play from hanging a script, not to time it.
    /// </summary>
    private double AutoPlayTimeoutSeconds => _options.AutoPlayChart is null ? 120 : 900;

    /// <summary>Plays a chart with perfect input end to end, then exits (verification).</summary>
    private void StartAutoPlay()
    {
        if (!_context.Session.HasProfile)
        {
            _context.Session.SetActive(_context.Profiles.Create("Autoplay"));
        }

        // A chart given on the command line is played as-is, with its audio resolved the
        // normal way; otherwise the bundled practice track, which every install has.
        string chartPath;
        string? audioPath = null;

        if (_options.AutoPlayChart is { Length: > 0 } requested)
        {
            chartPath = Path.GetFullPath(requested);
            if (!File.Exists(chartPath))
            {
                Console.WriteLine($"autoplay: no chart at {chartPath}");
                Log.Error($"autoplay: no chart at {chartPath}");
                Environment.Exit(2);
            }
        }
        else
        {
            Lumen.Game.Content.TestContent.Installed test =
                Lumen.Game.Content.TestContent.EnsureInstalled(_paths);
            chartPath = test.ChartPath;
            audioPath = test.AudioPath;
        }

        _profiler.Reset();
        _screens.SetRoot(new Screens.GameplayScreen(
            chartPath, audioPath, autoPlay: true,
            onComplete: result =>
            {
                string line = $"autoplay done: {result.Accuracy:0.00}% {result.Score:N0} " +
                              $"x{result.MaxCombo} P{result.Perfect}/G{result.Great}/g{result.Good}/" +
                              $"B{result.Bad}/M{result.Miss} FC={result.FullCombo} AP={result.AllPerfect}";
                Log.Info(line);
                Console.WriteLine(line);

                string frames = "frames: " + _profiler.Summary();
                Log.Info(frames);
                Console.WriteLine(frames);

                string worst = "worst frames: " + _profiler.StutterSummary();
                Log.Info(worst);
                Console.WriteLine(worst);
                Exit();
            }));
    }

    /// <summary>Renders each key screen to a PNG for visual verification, then exits.</summary>
    /// <summary>
    /// Renders each screen to a PNG, then exits (spec §101 verification, and the source of
    /// every screenshot of this game).
    ///
    /// The screens that depend on time are given it rather than being caught on their
    /// first frame: gameplay is played, by the autoplay input source, so the shot shows a
    /// combo and live judgements instead of a chart falling past an absent player. The
    /// result screen is the real result of that play — its accuracy, its PP, its rating
    /// change — because a screenshot of invented numbers is a screenshot of nothing.
    /// </summary>
    private void RunCapture(string dir)
    {
        Directory.CreateDirectory(dir);

        if (!_context.Session.HasProfile)
        {
            var demo = _context.Profiles.Create("Nagisa");
            _context.Session.SetActive(demo);
        }

        Lumen.Game.Content.TestContent.Installed test = Lumen.Game.Content.TestContent.EnsureInstalled(_paths);

        (string name, Screen screen)[] shots =
        {
            ("1-setup", new SetupScreen()),
            ("2-menu", new MainMenuScreen()),
            ("3-profile", new ProfileScreen()),
            ("4-settings", new SettingsScreen()),
            ("5-songselect", new SongSelectScreen()),
            ("6-editor", new Editor.EditorScreen(test.ChartPath)),
            ("7-replays", new ReplaysScreen()),
            ("8-calibration", new CalibrationScreen()),
            ("9-tutorial", new TutorialScreen(onFinished: () => { })),
        };

        using var target = new RenderTarget2D(GraphicsDevice, _display.Width, _display.Height);

        foreach ((string name, Screen screen) in shots)
        {
            _screens.SetRoot(screen);
            Capture(target, dir, name);
            screen.OnExit();
        }

        CapturePlay(target, dir, test);
    }

    /// <summary>
    /// Plays the practice chart through to its result, stopping twice for a picture: once
    /// mid-song with a combo running, and once on the result the play actually earned.
    /// </summary>
    private void CapturePlay(RenderTarget2D target, string dir, Lumen.Game.Content.TestContent.Installed test)
    {
        // Far enough in for a combo worth showing, and early enough that the chart is
        // still visibly falling.
        const double ShotAtSeconds = 12.0;
        const double GiveUpAfterSeconds = 90.0;

        var play = new Screens.GameplayScreen(test.ChartPath, test.AudioPath, autoPlay: true);
        _screens.SetRoot(play);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        bool shotTaken = false;

        while (elapsed.Elapsed.TotalSeconds < GiveUpAfterSeconds)
        {
            _screens.Update(_input.BeginFrame(1 / 240.0, _ui.Scale));
            System.Threading.Thread.Sleep(4);

            if (!shotTaken && elapsed.Elapsed.TotalSeconds >= ShotAtSeconds)
            {
                Capture(target, dir, "10-gameplay");
                shotTaken = true;
            }

            // The play pushes the result screen itself when the song ends.
            if (_screens.Top is ResultScreen)
            {
                break;
            }
        }

        if (_screens.Top is not ResultScreen)
        {
            Log.Warn("capture: the play did not reach a result");
            return;
        }

        // Let the staged reveal finish, so the shot has the PP and the rating on it rather
        // than catching them half counted up.
        var settle = System.Diagnostics.Stopwatch.StartNew();
        while (settle.Elapsed.TotalSeconds < 3.5)
        {
            _screens.Update(_input.BeginFrame(1 / 240.0, _ui.Scale));
            System.Threading.Thread.Sleep(4);
        }

        Capture(target, dir, "11-result");
    }

    private void Capture(RenderTarget2D target, string dir, string name)
    {
        GraphicsDevice.SetRenderTarget(target);
        _screens.Draw(_ui);
        GraphicsDevice.SetRenderTarget(null);

        string path = Path.Combine(dir, $"lumen-{name}.png");
        using (var fs = File.Create(path))
        {
            target.SaveAsPng(fs, target.Width, target.Height);
        }

        Log.Info($"captured {path}");
        Console.WriteLine($"captured {path}");
    }

    protected override void Update(GameTime gameTime)
    {
        _clock.Advance();
        _frameWatch.Restart();
        InputFrame input = _input.BeginFrame(_clock.DeltaSeconds, _ui.Scale);

        try
        {
            if (_options.CrashTest && _clock.TotalSeconds >= 1.5)
            {
                throw new InvalidOperationException("Deliberate crash test (--crashtest).");
            }

            if (_options.AutoPlay && _clock.TotalSeconds >= AutoPlayTimeoutSeconds)
            {
                Console.WriteLine("autoplay timed out");
                Log.Error("autoplay timed out");
                Environment.Exit(2);
            }

            if (_fatal is null)
            {
                _screens.Update(input);
            }
            else if (input.Pressed(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                Exit();
            }

            if (_options.Smoke && _clock.TotalSeconds >= 2.0)
            {
                Log.Info($"smoke ok - ~{_clock.Fps:F0} fps");
                Console.WriteLine($"smoke ok - ~{_clock.Fps:F0} fps");
                Exit();
            }
        }
        catch (Exception ex)
        {
            EnterFatal(ex);
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _frameMsSmoothed += (_clock.DeltaSeconds * 1000.0 - _frameMsSmoothed) * 0.1;

        try
        {
            if (_fatal is { } fatal)
            {
                DrawFatal(fatal.ex, fatal.report);
            }
            else
            {
                _screens.Draw(_ui);
                if (_display.ShowPerfOverlay && _fonts.Loaded)
                {
                    DrawOverlay();
                }
            }
        }
        catch (Exception ex)
        {
            if (_fatal is null)
            {
                EnterFatal(ex);
            }
        }

        base.Draw(gameTime);

        _workMs = _frameWatch.Elapsed.TotalMilliseconds;
        double beforeWait = _workMs;
        _limiter.Tick();
        _waitMs = _frameWatch.Elapsed.TotalMilliseconds - beforeWait;
    }

    /// <summary>
    /// The frame ends here, once the driver has taken the image. Recording at this point
    /// — rather than at the top of the next frame — means the work, present and wait
    /// figures all belong to the frame they are reported against.
    /// </summary>
    protected override void EndDraw()
    {
        double beforePresent = _frameWatch.Elapsed.TotalMilliseconds;
        base.EndDraw();
        double elapsed = _frameWatch.Elapsed.TotalMilliseconds;

        _profiler.Record(elapsed, _workMs, elapsed - beforePresent, _waitMs);
    }

    private void DrawOverlay()
    {
        var font = _ui.Mono(Theme.Mono);

        // Rebuilt a few times a second rather than every frame. An overlay that allocates
        // a string per frame makes the very measurement it displays worse — and it is on
        // by default, so it would have been doing that during every play.
        if (_clock.TotalSeconds - _overlayBuiltAt >= 0.25)
        {
            _overlayBuiltAt = _clock.TotalSeconds;
            _overlayLine = $"{_clock.Fps,5:F0} fps  {_frameMsSmoothed,4:F1} ms  " +
                           $"{_ui.Width}x{_ui.Height}  db v{_db.SchemaVersion}";
        }

        string line = _overlayLine;

        _ui.Begin();
        var size = _ui.Measure(font, line);
        _ui.FillRect(new Rectangle(8, 8, (int)size.X + 16, (int)size.Y + 10), Theme.Ground.WithAlpha(0.6f));
        _ui.Text(font, line, new Microsoft.Xna.Framework.Vector2(16, 13), OverlayText);
        _ui.End();
    }

    private void DrawFatal(Exception ex, string reportPath)
    {
        GraphicsDevice.Clear(ErrorGround);
        int w = GraphicsDevice.Viewport.Width;

        _ui.Begin();
        _ui.FillRect(new Rectangle(0, 0, w, 3), ErrorAccent);

        if (_fonts.Loaded)
        {
            const float x = 48f;
            _ui.Text(_ui.Display(Theme.DisplayL), $"{GameIdentity.Name} had to stop",
                new Microsoft.Xna.Framework.Vector2(x, 64), ErrorAccent);
            _ui.Text(_ui.Body(Theme.Body), $"{ex.GetType().Name}: {ex.Message}",
                new Microsoft.Xna.Framework.Vector2(x, 120), Color.White);
            _ui.Text(_ui.Mono(Theme.Mono), $"report: {reportPath}",
                new Microsoft.Xna.Framework.Vector2(x, 158), OverlayText);
            _ui.Text(_ui.Mono(Theme.Mono), "press Esc to close",
                new Microsoft.Xna.Framework.Vector2(x, 182), OverlayText);
        }

        _ui.End();
    }

    private void EnterFatal(Exception ex)
    {
        string report = CrashGuard.Handle(ex, terminating: false, showDialog: false);
        _fatal = (ex, report);

        if (_options.Smoke || _options.CrashTest)
        {
            Console.WriteLine($"fatal handled: {ex.GetType().Name} -> {report}");
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fonts.Dispose();
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
            _graphics?.Dispose();
        }

        base.Dispose(disposing);
    }
}
