using System;
using FontStashSharp;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Data;
using Lumen.Game.Config;
using Lumen.Game.Engine;
using Lumen.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game;

/// <summary>
/// Phase 1 foundation: a titled window, our own high-resolution clock and frame
/// limiter, a perf overlay, and a fatal-error screen that catches exceptions thrown
/// inside the loop instead of letting the process die (spec §1, §68, §96). Screens,
/// audio and input abstraction arrive in Phase 2+.
/// </summary>
internal sealed class LumenGame : Microsoft.Xna.Framework.Game
{
    private static readonly Color Ground = new(0x09, 0x0B, 0x10);
    private static readonly Color Accent = new(0x6F, 0xE0, 0xFF);
    private static readonly Color TextDim = new(0x8B, 0x94, 0xA5);
    private static readonly Color ErrorGround = new(0x1A, 0x0C, 0x0E);
    private static readonly Color ErrorAccent = new(0xE5, 0x6A, 0x60);

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
        _limiter.TargetFps = _display.FollowRefreshRate
            ? Math.Max(refresh, 60)
            : _display.FpsCap;

        Log.Info($"display {_display.Width}x{_display.Height} " +
                 $"vsync={_display.Vsync} cap={(_limiter.TargetFps == 0 ? "uncapped" : _limiter.TargetFps)} " +
                 $"(monitor {refresh}Hz)");

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _fonts.Load(AppContext.BaseDirectory);
    }

    protected override void Update(GameTime gameTime)
    {
        _clock.Advance();

        try
        {
            KeyboardState keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.Escape))
            {
                Exit();
            }

            if (_fatal is null)
            {
                UpdateGame();
            }
        }
        catch (Exception ex)
        {
            EnterFatal(ex);
        }

        base.Update(gameTime);
    }

    private void UpdateGame()
    {
        double t = _clock.TotalSeconds;

        if (_options.CrashTest && t >= 1.5)
        {
            throw new InvalidOperationException("Deliberate crash test (--crashtest).");
        }

        if (_options.Smoke && t >= 2.0)
        {
            Log.Info($"smoke ok - ~{_clock.Fps:F0} fps");
            Console.WriteLine($"smoke ok - ~{_clock.Fps:F0} fps");
            Exit();
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        double frameMs = _clock.DeltaSeconds * 1000.0;
        _frameMsSmoothed += (frameMs - _frameMsSmoothed) * 0.1;

        try
        {
            if (_fatal is { } fatal)
            {
                DrawFatal(fatal.ex, fatal.report);
            }
            else
            {
                DrawGame();
            }
        }
        catch (Exception ex)
        {
            // A failure in the game draw path is still recoverable to the error screen.
            if (_fatal is null)
            {
                EnterFatal(ex);
            }
        }

        base.Draw(gameTime);
        _limiter.Tick();
    }

    private void DrawGame()
    {
        GraphicsDevice.Clear(Ground);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;

        float pulse = (float)((Math.Sin(_clock.TotalSeconds * 1.5) + 1) / 2);
        var bar = new Rectangle((int)(w * 0.15f), h / 2 - 2, (int)(w * 0.7f * pulse) + 1, 4);

        _spriteBatch.Begin();
        _spriteBatch.Draw(_pixel, bar, Accent);

        if (_fonts.Loaded)
        {
            SpriteFontBase title = _fonts.Display(48);
            _spriteBatch.DrawString(title, GameIdentity.Name, new Vector2(w * 0.15f, h * 0.32f), Accent);

            SpriteFontBase tag = _fonts.Mono(13);
            _spriteBatch.DrawString(tag, GameIdentity.Tagline, new Vector2(w * 0.15f + 2, h * 0.32f + 62), TextDim);

            if (_display.ShowPerfOverlay)
            {
                DrawOverlay();
            }
        }

        _spriteBatch.End();
    }

    private void DrawOverlay()
    {
        SpriteFontBase font = _fonts.Mono(13);
        string[] lines =
        {
            $"{_clock.Fps,6:F0} fps   {_frameMsSmoothed,5:F2} ms",
            $"{GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height}   " +
                $"cap {( _limiter.TargetFps == 0 ? "off" : _limiter.TargetFps.ToString())}",
            $"db schema v{_db.SchemaVersion}   frame {_clock.FrameCount}",
            _paths.Root,
        };

        var pos = new Vector2(16, 14);
        foreach (string line in lines)
        {
            _spriteBatch.DrawString(font, line, pos, TextDim);
            pos.Y += 17;
        }
    }

    private void DrawFatal(Exception ex, string reportPath)
    {
        GraphicsDevice.Clear(ErrorGround);
        int w = GraphicsDevice.Viewport.Width;

        _spriteBatch.Begin();
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, w, 3), ErrorAccent);

        if (_fonts.Loaded)
        {
            var x = 48f;
            _spriteBatch.DrawString(_fonts.Display(30), $"{GameIdentity.Name} had to stop", new Vector2(x, 60), ErrorAccent);
            _spriteBatch.DrawString(_fonts.Body(16), $"{ex.GetType().Name}: {ex.Message}", new Vector2(x, 116), Color.White);
            _spriteBatch.DrawString(_fonts.Mono(13), $"report: {reportPath}", new Vector2(x, 156), TextDim);
            _spriteBatch.DrawString(_fonts.Mono(13), "press Esc to close", new Vector2(x, 182), TextDim);
        }
        else
        {
            _spriteBatch.Draw(_pixel, new Rectangle(48, 60, w - 96, 8), ErrorAccent);
        }

        _spriteBatch.End();
    }

    private void EnterFatal(Exception ex)
    {
        string report = CrashGuard.Handle(ex, terminating: false, showDialog: false);
        _fatal = (ex, report);

        if (_options.Smoke || _options.CrashTest)
        {
            // In non-interactive verification runs, don't sit on the error screen.
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
