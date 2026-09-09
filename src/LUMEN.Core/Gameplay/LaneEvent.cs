namespace Lumen.Core.Gameplay;

/// <summary>
/// A single lane press or release, timestamped against the same song clock the
/// conductor uses. Produced by the input layer (any provider), consumed by
/// <see cref="GameplaySession"/>. Ordering by <see cref="TimeMs"/> is the caller's job.
/// </summary>
public readonly record struct LaneEvent(int Lane, bool IsDown, double TimeMs)
{
    public static LaneEvent Down(int lane, double timeMs) => new(lane, true, timeMs);

    public static LaneEvent Up(int lane, double timeMs) => new(lane, false, timeMs);
}
