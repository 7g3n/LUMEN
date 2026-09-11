using Microsoft.Xna.Framework;

namespace Lumen.Game.Editor;

/// <summary>
/// Maps between the editor's timeline pixels and song time.
///
/// Time runs upward with the playhead near the bottom, the same way the chart looks while
/// it is being played. An editor whose mental model differs from the game's makes the
/// author translate in their head every time they test, which is the friction the spec
/// asks to remove (§86).
///
/// Kept apart from the drawing so hit-testing and the pixels can never disagree.
/// </summary>
public readonly record struct EditorTimeline(
    Rectangle Area, int LaneCount, double MsPerPixel, double PlayheadMs)
{
    /// <summary>Fraction of the timeline height the playhead sits at.</summary>
    public const float PlayheadRatio = 0.78f;

    /// <summary>Width reserved on the left for bar numbers.</summary>
    public const int Gutter = 64;

    public int FieldLeft => Area.X + Gutter;

    public int FieldWidth => Math.Max(80, Area.Width - Gutter - 16);

    public float LaneWidth => FieldWidth / (float)Math.Max(1, LaneCount);

    public int PlayheadY => Area.Y + (int)(Area.Height * PlayheadRatio);

    public float YAt(double timeMs) => PlayheadY - (float)((timeMs - PlayheadMs) / MsPerPixel);

    public double TimeAt(float y) => PlayheadMs + (PlayheadY - y) * MsPerPixel;

    public float XAtLane(int lane) => FieldLeft + lane * LaneWidth;

    /// <summary>The lane under a pixel, or null when the pointer is outside the field.</summary>
    public int? LaneAt(float x)
    {
        if (x < FieldLeft || x > FieldLeft + FieldWidth)
        {
            return null;
        }

        int lane = (int)((x - FieldLeft) / LaneWidth);
        return Math.Clamp(lane, 0, LaneCount - 1);
    }

    /// <summary>Song time at the top and bottom edges, for culling and grid queries.</summary>
    public double TopMs => TimeAt(Area.Y);

    public double BottomMs => Math.Max(0, TimeAt(Area.Bottom));
}
