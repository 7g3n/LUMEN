using Microsoft.Xna.Framework;

namespace Lumen.Game.Ui;

/// <summary>
/// The LUMEN visual identity: minimal, dark, luminous (spec §84). Colours mirror the
/// published design plan. Not derived from any existing rhythm game.
/// </summary>
public static class Theme
{
    // Surfaces
    public static readonly Color Ground = Hex(0x090B10);
    public static readonly Color Surface = Hex(0x12151D);
    public static readonly Color SurfaceRaised = Hex(0x171B25);
    public static readonly Color Border = Hex(0x262C39);
    public static readonly Color BorderStrong = Hex(0x333B4A);

    // Text
    public static readonly Color Text = Hex(0xE8EBF1);
    public static readonly Color TextMuted = Hex(0x909AAB);
    public static readonly Color TextFaint = Hex(0x606A7B);

    // Accent (luminous cyan)
    public static readonly Color Accent = Hex(0x6FE0FF);
    public static readonly Color AccentBright = Hex(0x8CE9FF);
    public static readonly Color AccentSoft = Hex(0x0E2A34);

    // Judgement / semantic
    public static readonly Color Perfect = Hex(0xF3C459);
    public static readonly Color Great = Hex(0x5AD1E6);
    public static readonly Color Good = Hex(0x5FC98A);
    public static readonly Color Bad = Hex(0xE19553);
    public static readonly Color Miss = Hex(0xE56A60);
    public static readonly Color Danger = Hex(0xE56A60);

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
