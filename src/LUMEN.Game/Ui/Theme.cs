using Microsoft.Xna.Framework;

namespace Lumen.Game.Ui;

/// <summary>
/// The LUMEN visual identity: minimal, dark, luminous (spec §84). Colours mirror the
/// published design plan. Not derived from any existing rhythm game.
/// </summary>
public static class Theme
{
    // Surfaces
    public static Color Ground { get; private set; } = Hex(0x090B10);
    public static Color Surface { get; private set; } = Hex(0x12151D);
    public static Color SurfaceRaised { get; private set; } = Hex(0x171B25);
    public static Color Border { get; private set; } = Hex(0x262C39);
    public static Color BorderStrong { get; private set; } = Hex(0x333B4A);

    // Text
    public static Color Text { get; private set; } = Hex(0xE8EBF1);
    public static Color TextMuted { get; private set; } = Hex(0x909AAB);
    public static Color TextFaint { get; private set; } = Hex(0x606A7B);

    // Accent (luminous cyan)
    public static Color Accent { get; private set; } = Hex(0x6FE0FF);
    public static Color AccentBright { get; private set; } = Hex(0x8CE9FF);
    public static Color AccentSoft { get; private set; } = Hex(0x0E2A34);

    // Judgement / semantic
    public static Color Perfect { get; private set; } = Hex(0xF3C459);
    public static Color Great { get; private set; } = Hex(0x5AD1E6);
    public static Color Good { get; private set; } = Hex(0x5FC98A);
    public static Color Bad { get; private set; } = Hex(0xE19553);
    public static Color Miss { get; private set; } = Hex(0xE56A60);
    public static Color Danger { get; private set; } = Hex(0xE56A60);

    public static bool IsHighContrast { get; private set; }

    /// <summary>
    /// Switches the palette (spec §67).
    ///
    /// High contrast darkens the ground, brightens the text and strengthens every border,
    /// and pushes the judgement colours apart — but it changes no measurement, so every
    /// screen lays out identically in both modes and nothing has to be re-tested for
    /// position. The palette is global because a theme is exactly the kind of thing that
    /// is; every screen reads these each frame, so a change applies immediately.
    /// </summary>
    public static void Apply(bool highContrast)
    {
        IsHighContrast = highContrast;

        if (!highContrast)
        {
            Ground = Hex(0x090B10);
            Surface = Hex(0x12151D);
            SurfaceRaised = Hex(0x171B25);
            Border = Hex(0x262C39);
            BorderStrong = Hex(0x333B4A);
            Text = Hex(0xE8EBF1);
            TextMuted = Hex(0x909AAB);
            TextFaint = Hex(0x606A7B);
            Accent = Hex(0x6FE0FF);
            AccentBright = Hex(0x8CE9FF);
            AccentSoft = Hex(0x0E2A34);
            Perfect = Hex(0xF3C459);
            Great = Hex(0x5AD1E6);
            Good = Hex(0x5FC98A);
            Bad = Hex(0xE19553);
            Miss = Hex(0xE56A60);
            Danger = Hex(0xE56A60);
            return;
        }

        Ground = Hex(0x000000);
        Surface = Hex(0x0B0E14);
        SurfaceRaised = Hex(0x16202E);
        Border = Hex(0x5A6577);
        BorderStrong = Hex(0x93A1B5);
        Text = Hex(0xFFFFFF);
        TextMuted = Hex(0xD4DBE6);
        TextFaint = Hex(0xA7B1C0);
        Accent = Hex(0x7FE9FF);
        AccentBright = Hex(0xBFF4FF);
        AccentSoft = Hex(0x10384A);
        Perfect = Hex(0xFFD766);
        Great = Hex(0x7FE3FF);
        Good = Hex(0x7BF0A8);
        Bad = Hex(0xFFAE68);
        Miss = Hex(0xFF8178);
        Danger = Hex(0xFF8178);
    }

    // Type scale (px)
    public const float DisplayXl = 52f;
    public const float DisplayL = 34f;
    public const float DisplayM = 22f;
    public const float Body = 16f;
    public const float Label = 13f;
    public const float Mono = 13f;

    public static Color Hex(int rgb) => new((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color WithAlpha(this Color color, float alpha) =>
        new(color.R, color.G, color.B, (byte)MathHelper.Clamp(alpha * 255f, 0, 255));
}
