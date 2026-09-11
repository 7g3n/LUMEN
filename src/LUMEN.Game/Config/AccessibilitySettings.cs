using Lumen.Core.Accessibility;
using Lumen.Core.Settings;
using Lumen.Game.Screens;
using Lumen.Game.Ui;

namespace Lumen.Game.Config;

/// <summary>
/// Reads the accessibility switches out of the per-profile settings and applies the ones
/// that are global (spec §67).
///
/// Stored per profile rather than per machine: two people sharing a computer do not share
/// a need for reduced motion, and the setting follows the player to another machine with
/// their backup.
/// </summary>
public static class AccessibilitySettings
{
    public static AccessibilityOptions Load(ISettingsRepository settings, Guid playerId) => new()
    {
        ReducedMotion = settings.GetBool(playerId, SettingKeys.ReducedMotion, false),
        HighContrast = settings.GetBool(playerId, SettingKeys.HighContrast, false),
        ShapeCues = settings.GetBool(playerId, SettingKeys.ShapeCues, false),
        ShowJudgement = settings.GetBool(playerId, SettingKeys.ShowJudgement, true),
        ShowCombo = settings.GetBool(playerId, SettingKeys.ShowCombo, true),
        EffectIntensity = settings.GetDouble(playerId, SettingKeys.EffectIntensity, 100) / 100.0,
    };

    /// <summary>Applies the options that live outside any one screen.</summary>
    public static void Apply(AccessibilityOptions options) => Theme.Apply(options.HighContrast);
}
