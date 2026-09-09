using System;
using System.Diagnostics;
using Lumen.Core;
using Lumen.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Lumen.Game;

/// <summary>
/// Phase 0 shell: opens a titled window, clears to the LUMEN ground colour, and runs
/// an uncapped loop with a high-resolution clock. Everything else — screens, audio,
/// input abstraction, database bootstrap — arrives in Phase 1+.
/// </summary>
internal sealed class LumenGame : Microsoft.Xna.Framework.Game
{
    // LUMEN dark ground (#090B10) and luminous accent (#6FE0FF).
    private static readonly Color Ground = new(0x09, 0x0B, 0x10);
    private static readonly Color Accent = new(0x6F, 0xE0, 0xFF);

    private readonly GraphicsDeviceManager _graphics;
    private readonly LumenPaths _paths;
    private readonly LaunchOptions _options;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;

    private double _fpsAccum;
    private int _fpsFrames;
    private double _fps;

    public LumenGame(LumenPaths paths, LaunchOptions options)
    {
        _paths = paths;
        _options = options;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false, // uncapped; a real frame limiter lands in Phase 1
            GraphicsProfile = GraphicsProfile.HiDef,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false; // decouple update rate from a fixed timestep (spec §68)
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        Window.Title = GameIdentity.WindowTitle;
        _paths.EnsureCreated();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    protected override void Update(GameTime gameTime)
    {
        double dt = gameTime.ElapsedGameTime.TotalSeconds;
        _fpsAccum += dt;
        _fpsFrames++;
        if (_fpsAccum >= 0.5)
        {
            _fps = _fpsFrames / _fpsAccum;
            _fpsAccum = 0;
            _fpsFrames = 0;
        }

        // Smoke mode: prove the window comes up, then exit cleanly for CI / verification.
        if (_options.Smoke && _clock.Elapsed.TotalSeconds >= 2.0)
        {
            Console.WriteLine($"smoke ok — ~{_fps:F0} fps");
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Ground);

        // A single luminous bar that breathes — enough to confirm we are actually
        // rendering each frame without needing a font yet.
        float t = (float)((Math.Sin(_clock.Elapsed.TotalSeconds * 1.5) + 1) / 2);
        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        var bar = new Rectangle((int)(w * 0.15f), h / 2 - 2, (int)(w * 0.7f * t) + 1, 4);

        _spriteBatch.Begin();
        _spriteBatch.Draw(_pixel, bar, Accent);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
            _graphics?.Dispose();
        }

        base.Dispose(disposing);
    }
}
