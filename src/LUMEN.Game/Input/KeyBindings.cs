using Lumen.Core.Settings;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Input;

/// <summary>
/// Physical key per lane (spec §21). Defaults to A S D F; fully rebindable and stored
/// per profile as a comma-separated list in settings under <c>controls.lanes</c>.
/// </summary>
public sealed class KeyBindings
{
    public const string SettingKey = "controls.lanes";

    private static readonly Keys[] DefaultLanes = { Keys.A, Keys.S, Keys.D, Keys.F };

    public KeyBindings(IReadOnlyList<Keys> lanes) => Lanes = lanes.ToArray();

    public IReadOnlyList<Keys> Lanes { get; private set; }

    public int LaneCount => Lanes.Count;

    public static KeyBindings Default => new(DefaultLanes);

    public Keys this[int lane] => Lanes[lane];

    public void SetLane(int lane, Keys key)
    {
        var copy = Lanes.ToArray();
        copy[lane] = key;
        Lanes = copy;
    }

    public static KeyBindings Load(ISettingsRepository settings, Guid playerId, int laneCount = 4)
    {
        string? raw = settings.Get(playerId, SettingKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return FromDefaults(laneCount);
        }

        var parsed = new List<Keys>();
        foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            parsed.Add(Enum.TryParse(token, ignoreCase: true, out Keys k) ? k : Keys.None);
        }

        while (parsed.Count < laneCount)
        {
            parsed.Add(DefaultLanes[parsed.Count % DefaultLanes.Length]);
        }

        return new KeyBindings(parsed.Take(laneCount).ToList());
    }

    public void Save(ISettingsRepository settings, Guid playerId) =>
        settings.Set(playerId, SettingKey, string.Join(",", Lanes));

    private static KeyBindings FromDefaults(int laneCount)
    {
        var lanes = new List<Keys>();
        for (int i = 0; i < laneCount; i++)
        {
            lanes.Add(DefaultLanes[i % DefaultLanes.Length]);
        }

        return new KeyBindings(lanes);
    }
}
