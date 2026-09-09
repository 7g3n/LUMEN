using Lumen.Game.Ui;
using Microsoft.Xna.Framework;

namespace Lumen.Game.Screens;

/// <summary>Per-profile settings keys (stored via <c>ISettingsRepository</c>).</summary>
public static class SettingKeys
{
    public const string MasterVolume = "audio.master";
    public const string MusicVolume = "audio.music";
    public const string SeVolume = "audio.se";
    public const string NoteSpeed = "gameplay.noteSpeed";
    public const string BackgroundDim = "gameplay.bgDim";
    public const string EffectIntensity = "gameplay.effects";
    public const string InputOffsetMs = "timing.inputOffsetMs";
    public const string AudioOffsetMs = "timing.audioOffsetMs";
}

/// <summary>One line in the settings list. Section headers are non-interactive.</summary>
public abstract class SettingRow
{
    public required string Label { get; init; }

    public virtual bool Interactive => true;

    public abstract string ValueText { get; }

    /// <summary>Left/Right adjust. <paramref name="direction"/> is -1 or +1.</summary>
    public virtual void Adjust(int direction) { }

    /// <summary>Enter / click.</summary>
    public virtual void Activate() { }
}

public sealed class SectionRow : SettingRow
{
    public override bool Interactive => false;
    public override string ValueText => "";
}

public sealed class InfoRow : SettingRow
{
    public required string Value { get; init; }
    public override bool Interactive => false;
    public override string ValueText => Value;
}

public sealed class ActionRow : SettingRow
{
    public required Action OnActivate { get; init; }
    public string ActionText { get; init; } = "OPEN";
    public override string ValueText => ActionText;
    public override void Activate() => OnActivate();
}

public sealed class ToggleRow : SettingRow
{
    public required Func<bool> Get { get; init; }
    public required Action<bool> Set { get; init; }
    public string? Note { get; init; }

    public override string ValueText => Get() ? "ON" : "OFF";
    public override void Adjust(int direction) => Set(direction > 0);
    public override void Activate() => Set(!Get());
}

public sealed class SliderRow : SettingRow
{
    public required Func<double> Get { get; init; }
    public required Action<double> Set { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Step { get; init; } = 5;
    public Func<double, string> Format { get; init; } = v => v.ToString("0");

    public override string ValueText => Format(Get());

    public override void Adjust(int direction)
    {
        double next = Math.Clamp(Get() + direction * Step, Min, Max);
        Set(Math.Round(next / Step) * Step);
    }

    public float Fraction => (float)((Get() - Min) / (Max - Min));
}

public sealed class ChoiceRow : SettingRow
{
    public required string[] Options { get; init; }
    public required Func<int> GetIndex { get; init; }
    public required Action<int> SetIndex { get; init; }
    public string? Note { get; init; }

    public override string ValueText => Options[Math.Clamp(GetIndex(), 0, Options.Length - 1)];

    public override void Adjust(int direction)
    {
        int i = (GetIndex() + direction + Options.Length) % Options.Length;
        SetIndex(i);
    }

    public override void Activate() => Adjust(1);
}
