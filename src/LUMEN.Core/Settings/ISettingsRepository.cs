using System.Globalization;

namespace Lumen.Core.Settings;

/// <summary>
/// Per-profile key/value settings: key bindings, input/audio offsets, volumes, gameplay
/// preferences (spec §65). Values are strings; typed access is via the extension methods.
/// </summary>
public interface ISettingsRepository
{
    string? Get(Guid playerId, string key);

    void Set(Guid playerId, string key, string value);

    IReadOnlyDictionary<string, string> GetAll(Guid playerId);

    void Remove(Guid playerId, string key);
}

public static class SettingsRepositoryExtensions
{
    public static string GetString(this ISettingsRepository repo, Guid playerId, string key, string fallback)
        => repo.Get(playerId, key) ?? fallback;

    public static int GetInt(this ISettingsRepository repo, Guid playerId, string key, int fallback)
        => int.TryParse(repo.Get(playerId, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public static double GetDouble(this ISettingsRepository repo, Guid playerId, string key, double fallback)
        => double.TryParse(repo.Get(playerId, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    public static bool GetBool(this ISettingsRepository repo, Guid playerId, string key, bool fallback)
        => bool.TryParse(repo.Get(playerId, key), out bool v) ? v : fallback;

    public static void SetInt(this ISettingsRepository repo, Guid playerId, string key, int value)
        => repo.Set(playerId, key, value.ToString(CultureInfo.InvariantCulture));

    public static void SetDouble(this ISettingsRepository repo, Guid playerId, string key, double value)
        => repo.Set(playerId, key, value.ToString("R", CultureInfo.InvariantCulture));

    public static void SetBool(this ISettingsRepository repo, Guid playerId, string key, bool value)
        => repo.Set(playerId, key, value ? "true" : "false");
}
