using FontStashSharp;
using Lumen.Core.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Lumen.Game.Rendering;

/// <summary>
/// Runtime TTF rendering via FontStashSharp — no MonoGame content pipeline. Three
/// roles matching the design system: a display face, a UI/body face, and a monospace
/// face for numbers and data. Fonts are bundled OFL files under <c>assets/fonts/</c>.
/// </summary>
public sealed class Fonts : IDisposable
{
    private readonly FontSystem _display = NewSystem();
    private readonly FontSystem _body = NewSystem();
    private readonly FontSystem _mono = NewSystem();

    public bool Loaded { get; private set; }

    public SpriteFontBase Display(float size) => _display.GetFont(size);

    public SpriteFontBase Body(float size) => _body.GetFont(size);

    public SpriteFontBase Mono(float size) => _mono.GetFont(size);

    public void Load(string assetsRoot)
    {
        string fontsDir = Path.Combine(assetsRoot, "assets", "fonts");

        bool display = TryAdd(_display, fontsDir, "ChakraPetch-SemiBold.ttf");
        bool body = TryAdd(_body, fontsDir, "ChakraPetch-Regular.ttf");
        bool mono = TryAdd(_mono, fontsDir, "IBMPlexMono-Medium.ttf");

        Loaded = display && body && mono;
        if (!Loaded)
        {
            Log.Warn($"fonts: not all faces loaded from {fontsDir} (display={display} body={body} mono={mono})");
        }
        else
        {
            Log.Info("fonts loaded");
        }
    }

    private static FontSystem NewSystem() => new(new FontSystemSettings
    {
        FontResolutionFactor = 2f, // render glyphs at 2x for crisper downscaling
        KernelWidth = 2,
        KernelHeight = 2,
    });

    private static bool TryAdd(FontSystem system, string dir, string file)
    {
        string path = Path.Combine(dir, file);
        if (!File.Exists(path))
        {
            Log.Warn($"font missing: {path}");
            return false;
        }

        system.AddFont(File.ReadAllBytes(path));
        return true;
    }

    /// <summary>
    /// Rasterises the glyphs the game draws with, at the sizes it draws them, before the
    /// first frame that needs them (spec §68).
    ///
    /// FontStashSharp builds its atlas lazily, so the first text at a new size pays for
    /// rasterising every glyph in it and uploading the texture. Left alone that cost lands
    /// on the first frame of a song — measured here at around 45 ms, which is a visible
    /// hitch exactly where a rhythm game can least afford one. Paying it during loading
    /// costs the player nothing.
    ///
    /// Measuring alone is not enough: it rasterises the glyphs, but the atlas texture is
    /// only uploaded — and the sprite shader only linked — when something is actually
    /// drawn. So the warm-up draws too, far off-screen, into the back buffer that the
    /// first real frame is about to clear.
    ///
    /// A size missing from these lists is not a bug: it simply warms on first use, the
    /// way everything did before.
    /// </summary>
    public void Warm(SpriteBatch batch)
    {
        if (!Loaded)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        batch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        try
        {
            Warm(_display, DisplaySizes, batch);
            Warm(_body, BodySizes, batch);
            Warm(_mono, MonoSizes, batch);
        }
        finally
        {
            batch.End();
        }

        Log.Info($"fonts warmed in {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    /// <summary>The sizes each face is actually drawn at, across every screen.</summary>
    private static readonly float[] DisplaySizes = { 22f, 30f, 34f, 36f, 44f, 52f, 60f };

    private static readonly float[] BodySizes = { 13f, 16f, 22f };

    private static readonly float[] MonoSizes = { 13f, 16f, 18f, 19f };

    /// <summary>
    /// Printable ASCII plus the judgement shape cues, which are the only non-ASCII
    /// characters the playfield draws.
    /// </summary>
    private const string WarmGlyphs =
        " !\"#$%&'()*+,-./0123456789:;<=>?@" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
        "abcdefghijklmnopqrstuvwxyz{|}~" +
        "◆▲●■✕·—×";

    /// <summary>Well clear of any viewport, so the warm-up is never visible.</summary>
    private static readonly Vector2 OffScreen = new(-20_000f, -20_000f);

    private static void Warm(FontSystem system, float[] sizes, SpriteBatch batch)
    {
        foreach (float size in sizes)
        {
            SpriteFontBase font = system.GetFont(size);
            font.MeasureString(WarmGlyphs);
            batch.DrawString(font, WarmGlyphs, OffScreen, Color.Transparent);
        }
    }

    public void Dispose()
    {
        _display.Dispose();
        _body.Dispose();
        _mono.Dispose();
    }
}
