using System;
using System.IO;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Data;
using Lumen.Data.Library;
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
        Log.Info($"display {_display.Width}x{_display.Height} vsync={_display.Vsync} " +
                 $"cap={(_limiter.TargetFps == 0 ? "uncapped" : _limiter.TargetFps)} (monitor {refresh}Hz)");

        BuildContext();

        base.Initialize();
    }

    private void BuildContext()
    {
        var profiles = new ProfileRepository(_db);
        var settings = new SettingsRepository(_db);
        var appMeta = new AppMetaStore(_db);
        var session = new Session(_db, profiles, appMeta);
        session.Restore();

        Core.Balance.BalanceConfig balance = Core.Balance.BalanceConfigFile.LoadOrCreate(_paths.Settings);

        _context = new GameContext
        {
            Paths = _paths,
            Database = _db,
            Profiles = profiles,
            Settings = settings,
            Scores = new ScoreRepository(_db, balance),
            Library = new LibraryService(new LibraryRepository(_db), _paths),
            AppMeta = appMeta,
            Display = _display,
            Balance = balance,
            Session = session,
            RequestExit = Exit,
        };

        _screens = new ScreenManager(_context);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _fonts.Load(AppContext.BaseDirectory);

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

    /// <summary>Plays the practice chart with perfect input end to end, then exits (verification).</summary>
    private void StartAutoPlay()
    {
        if (!_context.Session.HasProfile)
        {
            _context.Session.SetActive(_context.Profiles.Create("Autoplay"));
        }

        Lumen.Game.Content.TestContent.Installed test = Lumen.Game.Content.TestContent.EnsureInstalled(_paths);
        _screens.SetRoot(new Screens.GameplayScreen(
            test.ChartPath, test.AudioPath, autoPlay: true,
            onComplete: result =>
            {
                string line = $"autoplay done: {result.Accuracy:0.00}% {result.Score:N0} " +
                              $"x{result.MaxCombo} P{result.Perfect}/G{result.Great}/g{result.Good}/" +
                              $"B{result.Bad}/M{result.Miss} FC={result.FullCombo} AP={result.AllPerfect}";
                Log.Info(line);
                Console.WriteLine(line);
                Exit();
            }));
    }

    /// <summary>Renders each key screen to a PNG for visual verification, then exits.</summary>
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
            ("7-gameplay", new Screens.GameplayScreen(test.ChartPath, test.AudioPath)),
        };

        using var target = new RenderTarget2D(GraphicsDevice, _display.Width, _display.Height);

        foreach ((string name, Screen screen) in shots)
        {
            _screens.SetRoot(screen);

            // Let time-based screens settle so the shot shows a real working state.
            if (screen is Screens.GameplayScreen)
            {
                var warmup = System.Diagnostics.Stopwatch.StartNew();
                var fake = _input.BeginFrame(0);
                while (warmup.Elapsed.TotalSeconds < 3.2)
                {
                    _screens.Update(fake);
                    System.Threading.Thread.Sleep(4);
                }
            }

            GraphicsDevice.SetRenderTarget(target);
            _screens.Draw(_ui);
            GraphicsDevice.SetRenderTarget(null);

            string path = Path.Combine(dir, $"lumen-{name}.png");
            using (var fs = File.Create(path))
            {
                target.SaveAsPng(fs, target.Width, target.Height);
            }

            screen.OnExit();
            Log.Info($"captured {path}");
            Console.WriteLine($"captured {path}");
        }
    }

    protected override void Update(GameTime gameTime)
    {
        _clock.Advance();
        InputFrame input = _input.BeginFrame(_clock.DeltaSeconds);

        try
        {
            if (_options.CrashTest && _clock.TotalSeconds >= 1.5)
            {
                throw new InvalidOperationException("Deliberate crash test (--crashtest).");
            }

            if (_options.AutoPlay && _clock.TotalSeconds >= 120)
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
        _limiter.Tick();
    }

    private void DrawOverlay()
    {
        var font = _ui.Mono(Theme.Mono);
        string line = $"{_clock.Fps,5:F0} fps  {_frameMsSmoothed,4:F1} ms  " +
                      $"{_ui.Width}x{_ui.Height}  db v{_db.SchemaVersion}";

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
