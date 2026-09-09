using System.Text.Json;
using Lumen.Core.Diagnostics;

namespace Lumen.Core.Balance;

/// <summary>
/// Loads <see cref="BalanceConfig"/> from <c>settings/balance.json</c> if present,
/// otherwise writes the defaults there so the numbers are discoverable and tunable
/// without a rebuild (spec §22–23, "config化").
/// </summary>
public static class BalanceConfigFile
{
    public const string FileName = "balance.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static BalanceConfig LoadOrCreate(string settingsDir)
    {
        string path = Path.Combine(settingsDir, FileName);
        try
        {
            if (File.Exists(path))
            {
                BalanceConfig? loaded = JsonSerializer.Deserialize<BalanceConfig>(File.ReadAllText(path), Options);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("balance.json unreadable, using defaults", ex);
            return BalanceConfig.Default;
        }

        try
        {
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(path, JsonSerializer.Serialize(BalanceConfig.Default, Options));
        }
        catch (Exception ex)
        {
            Log.Warn("could not write balance.json", ex);
        }

        return BalanceConfig.Default;
    }
}
