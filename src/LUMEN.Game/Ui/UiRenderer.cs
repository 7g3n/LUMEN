using FontStashSharp;
using Lumen.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Lumen.Game.Ui;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Thin drawing surface for the UI layer: rectangles, borders and text on top of a
/// single <see cref="SpriteBatch"/>. Screens never touch the graphics device directly.
/// </summary>
public sealed class UiRenderer
{
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;
    private readonly Fonts _fonts;

    public UiRenderer(GraphicsDevice device, SpriteBatch batch, Texture2D pixel, Fonts fonts)
    {
        Device = device;
        _batch = batch;
        _pixel = pixel;
        _fonts = fonts;
    }

    public GraphicsDevice Device { get; }

    /// <summary>
    /// The height every screen is laid out against, whatever the window actually is.
    ///
    /// Screens position things in pixels, and pixels stop meaning anything the moment the
    /// window is not the size they were written for: laid out for 720 and run at 1080, the
    /// playfield becomes a thin strip down the middle and the result screen huddles in the
    /// top half with a third of the display left blank. Rather than ask every screen to do
    /// its own arithmetic, the whole UI is drawn into a fixed 720-tall space and scaled up
    /// to fit. A screen written once then looks the same at 720p, 1080p and 4K.
    /// </summary>
    public const int DesignHeight = 720;

    /// <summary>How much bigger the window is than the space screens are drawn in.</summary>
    public float Scale => Device.Viewport.Height / (float)DesignHeight;

    /// <summary>
    /// Width in that same space. Height is always <see cref="DesignHeight"/>; width is
    /// whatever the window's aspect ratio makes it, so a wider monitor genuinely is wider
    /// to lay out on rather than being letterboxed.
    /// </summary>
    public int Width => (int)MathF.Round(Device.Viewport.Width / Scale);

    public int Height => DesignHeight;

    public Rectangle Bounds => new(0, 0, Width, Height);

    /// <summary>Turns a real window position — a mouse cursor — into layout space.</summary>
    public Point ToLayout(Point windowPosition)
    {
        float scale = Scale;
        return new Point(
            (int)MathF.Round(windowPosition.X / scale),
            (int)MathF.Round(windowPosition.Y / scale));
    }

    public bool FontsReady => _fonts.Loaded;

    public SpriteFontBase Display(float size) => _fonts.Display(size);

    public SpriteFontBase Body(float size) => _fonts.Body(size);

    public SpriteFontBase Mono(float size) => _fonts.Mono(size);

    public void Begin() =>
        _batch.Begin(
            samplerState: SamplerState.LinearClamp,
            blendState: BlendState.AlphaBlend,
            transformMatrix: Matrix.CreateScale(Scale, Scale, 1f));

    public void End() => _batch.End();

    public void Clear(Color color) => Device.Clear(color);

    public void FillRect(Rectangle rect, Color color) => _batch.Draw(_pixel, rect, color);

    public void FillRect(int x, int y, int w, int h, Color color) =>
        _batch.Draw(_pixel, new Rectangle(x, y, w, h), color);

    public void StrokeRect(Rectangle rect, Color color, int thickness = 1)
    {
        FillRect(new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        FillRect(new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        FillRect(new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        FillRect(new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    public Vector2 Measure(SpriteFontBase font, string text) => font.MeasureString(text);

    public void Text(SpriteFontBase font, string text, Vector2 position, Color color)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _batch.DrawString(font, text, position, color);
        }
    }

    /// <summary>Draws text horizontally aligned within <paramref name="area"/>, vertically centered.</summary>
    public void Text(SpriteFontBase font, string text, Rectangle area, Color color, TextAlign align = TextAlign.Left)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Vector2 size = font.MeasureString(text);
        float x = align switch
        {
            TextAlign.Center => area.X + (area.Width - size.X) / 2f,
            TextAlign.Right => area.Right - size.X,
            _ => area.X,
        };
        float y = area.Y + (area.Height - size.Y) / 2f;
        _batch.DrawString(font, text, new Vector2(MathF.Round(x), MathF.Round(y)), color);
    }
}
