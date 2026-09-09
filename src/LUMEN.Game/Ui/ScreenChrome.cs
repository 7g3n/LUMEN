using Microsoft.Xna.Framework;

namespace Lumen.Game.Ui;

/// <summary>Shared page furniture: the content column, the section header, the footer hint.</summary>
public static class ScreenChrome
{
    public const int ContentMaxWidth = 720;

    /// <summary>A centered content column, inset from the top.</summary>
    public static Rectangle Column(UiRenderer ui, int top = 96, int bottom = 72)
    {
        int width = Math.Min(ContentMaxWidth, ui.Width - 96);
        int x = (ui.Width - width) / 2;
        return new Rectangle(x, top, width, ui.Height - top - bottom);
    }

    /// <summary>Draws an eyebrow label + optional big title. Returns the Y below it.</summary>
    public static int Header(UiRenderer ui, Rectangle column, string eyebrow, string? title = null)
    {
        int y = column.Y;
        ui.Text(ui.Mono(Theme.Label), eyebrow.ToUpperInvariant(),
            new Rectangle(column.X, y, column.Width, 20), Theme.Accent);
        y += 26;

        if (title is { Length: > 0 })
        {
            var font = ui.Display(Theme.DisplayL);
            ui.Text(font, title, new Vector2(column.X, y), Theme.Text);
            y += (int)ui.Measure(font, title).Y + 14;
        }

        ui.FillRect(new Rectangle(column.X, y, column.Width, 1), Theme.Border);
        return y + 24;
    }

    public static void FooterHint(UiRenderer ui, string text)
    {
        ui.Text(ui.Mono(Theme.Label), text,
            new Rectangle(0, ui.Height - 40, ui.Width, 20), Theme.TextFaint, TextAlign.Center);
    }

    /// <summary>The luminous LUMEN wordmark, letter-spaced, centered horizontally at <paramref name="y"/>.</summary>
    public static void Wordmark(UiRenderer ui, string text, int y, float size, Color color)
    {
        var font = ui.Display(size);
        const float tracking = 0.28f;
        float glyphSpace = size * tracking;

        float total = 0;
        foreach (char c in text)
        {
            total += ui.Measure(font, c.ToString()).X + glyphSpace;
        }

        total -= glyphSpace;
        float x = (ui.Width - total) / 2f;

        foreach (char c in text)
        {
            string s = c.ToString();
            ui.Text(font, s, new Vector2(MathF.Round(x), y), color);
            x += ui.Measure(font, s).X + glyphSpace;
        }
    }
}
