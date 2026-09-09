using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Core.Diagnostics;
using Lumen.Data;

namespace Lumen.Game.Config;

/// <summary>
/// Window / rendering preferences, persisted to <c>settings/display.json</c>. A fuller
/// settings system (audio, gameplay, controls) arrives in Phase 2; this is the subset
/// the Phase 1 foundation needs.
/// </summary>
public sealed class DisplayConfig
{
    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public bool Fullscreen { get; set; }

    public bool Vsync { get; set; }

    /// <summary>Frame cap. 0 = uncapped, -1 = follow the monitor's refresh rate.</summary>
    public int FpsCap { get; set; } = -1;

    /// <summary>Draw the FPS / frame-time overlay.</summary>
    public bool ShowPerfOverlay { get; set; } = true;

    [JsonIgnore]
    public bool FollowRefreshRate => FpsCap < 0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static DisplayConfig Load(string settingsDir)
    {
        string path = Path.Combine(settingsDir, "display.json");
        try
        {
            string? json = AtomicFile.ReadAllTextOrNull(path);
            if (json is not null)
            {
                DisplayConfig? loaded = JsonSerializer.Deserialize<DisplayConfig>(json, JsonOptions);
                if (loaded is not null)
                {
                    return loaded.Clamped();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"display.json unreadable, using defaults", ex);
        }

        var fresh = new DisplayConfig();
        fresh.Save(settingsDir);
        return fresh;
    }

    public void Save(string settingsDir)
    {
        try
        {
            AtomicFile.WriteAllText(
                Path.Combine(settingsDir, "display.json"),
                JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warn("could not write display.json", ex);
        }
    }

    private DisplayConfig Clamped()
    {
        Width = Math.Clamp(Width, 960, 7680);
        Height = Math.Clamp(Height, 540, 4320);
        if (FpsCap > 1000)
        {
            FpsCap = 1000;
        }

        return this;
    }
}
