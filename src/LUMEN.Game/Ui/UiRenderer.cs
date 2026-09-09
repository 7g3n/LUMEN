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

    public int Width => Device.Viewport.Width;

    public int Height => Device.Viewport.Height;

    public Rectangle Bounds => new(0, 0, Width, Height);

    public bool FontsReady => _fonts.Loaded;

    public SpriteFontBase Display(float size) => _fonts.Display(size);

    public SpriteFontBase Body(float size) => _fonts.Body(size);

    public SpriteFontBase Mono(float size) => _fonts.Mono(size);

    public void Begin() =>
        _batch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);

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
