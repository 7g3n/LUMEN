using FontStashSharp;
using Lumen.Core.Diagnostics;

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

    public void Dispose()
    {
        _display.Dispose();
        _body.Dispose();
        _mono.Dispose();
    }
}
