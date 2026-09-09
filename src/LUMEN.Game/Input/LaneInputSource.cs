using Lumen.Core.Gameplay;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Input;

/// <summary>
/// Turns keyboard state changes into <see cref="LaneEvent"/>s stamped with the current
/// song time. Frame-polled for Phase 3; a lower-latency raw-input path is a Phase 10
/// optimisation. The abstraction (this class implements <see cref="IGameplayInput"/>)
/// lets gamepad / touch providers slot in later (spec §20).
/// </summary>
public sealed class LaneInputSource : IGameplayInput
{
    private readonly KeyBindings _bindings;
    private readonly bool[] _down;
    private readonly List<LaneEvent> _events = new(8);

    public LaneInputSource(KeyBindings bindings)
    {
        _bindings = bindings;
        _down = new bool[bindings.LaneCount];
    }

    public int LaneCount => _bindings.LaneCount;

    public IReadOnlyList<bool> LaneHeld => _down;

    public IReadOnlyList<LaneEvent> Poll(KeyboardState keyboard, double songTimeMs)
    {
        _events.Clear();

        for (int lane = 0; lane < _bindings.LaneCount; lane++)
        {
            bool nowDown = keyboard.IsKeyDown(_bindings[lane]);
            if (nowDown == _down[lane])
            {
                continue;
            }

            _down[lane] = nowDown;
            _events.Add(new LaneEvent(lane, nowDown, songTimeMs));
        }

        return _events;
    }
}

/// <summary>Common surface for lane-event sources (keyboard now; gamepad/touch later).</summary>
public interface IGameplayInput
{
    int LaneCount { get; }

    IReadOnlyList<bool> LaneHeld { get; }

    IReadOnlyList<LaneEvent> Poll(KeyboardState keyboard, double songTimeMs);
}
