using Lumen.Core.Charts;

namespace Lumen.Core.Editing;

/// <summary>
/// Copy and paste for notes (spec §88).
///
/// The clipboard stores notes relative to the earliest one it holds, so pasting at the
/// playhead reproduces the shape of the phrase wherever it lands. Storing absolute times
/// would make paste mean "put it back where it came from", which is never what the
/// gesture is for.
/// </summary>
public sealed class NoteClipboard
{
    private Note[] _relative = Array.Empty<Note>();

    public bool HasContent => _relative.Length > 0;

    public int Count => _relative.Length;

    /// <summary>Lowest lane in the copied phrase, so paste can be offset by lane too.</summary>
    public int BaseLane { get; private set; }

    public void Copy(IEnumerable<Note> notes)
    {
        Note[] source = notes.OrderBy(n => n.TimeMs).ThenBy(n => n.Lane).ToArray();
        if (source.Length == 0)
        {
            return;
        }

        double origin = source[0].TimeMs;
        BaseLane = source.Min(n => n.Lane);

        _relative = source
            .Select(n => n with
            {
                TimeMs = n.TimeMs - origin,
                EndTimeMs = n.IsHold ? n.EndTimeMs - origin : 0,
            })
            .ToArray();
    }

    public void Clear()
    {
        _relative = Array.Empty<Note>();
        BaseLane = 0;
    }

    /// <summary>
    /// The copied phrase placed at <paramref name="atMs"/>, optionally shifted by lane.
    /// Notes that would fall outside the chart's lanes are clamped rather than dropped -
    /// losing part of a pasted phrase silently is worse than nudging it into range.
    /// </summary>
    public IReadOnlyList<Note> Paste(double atMs, int laneCount, int laneOffset = 0)
    {
        if (_relative.Length == 0)
        {
            return Array.Empty<Note>();
        }

        return _relative
            .Select(n =>
            {
                int lane = Math.Clamp(n.Lane + laneOffset, 0, Math.Max(0, laneCount - 1));
                double time = Math.Max(0, atMs + n.TimeMs);
                return n with
                {
                    Lane = lane,
                    TimeMs = time,
                    EndTimeMs = n.IsHold ? time + (n.EndTimeMs - n.TimeMs) : 0,
                };
            })
            .ToArray();
    }
}
