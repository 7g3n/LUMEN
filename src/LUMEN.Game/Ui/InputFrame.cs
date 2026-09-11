using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Ui;

/// <summary>
/// An immutable snapshot of keyboard and mouse for one UI frame, plus any text typed
/// since the previous frame (Unicode, IME-aware — sourced from the window's text-input
/// event, spec §5).
/// </summary>
public sealed class InputFrame
{
    public required KeyboardState Keyboard { get; init; }

    public required KeyboardState PreviousKeyboard { get; init; }

    public required MouseState Mouse { get; init; }

    public required MouseState PreviousMouse { get; init; }

    /// <summary>Printable characters typed this frame, in order.</summary>
    public required string TypedText { get; init; }

    public double DeltaSeconds { get; init; }

    /// <summary>
    /// How much bigger the window is than the space screens lay out in
    /// (<see cref="UiRenderer.DesignHeight"/>). Mouse positions are reported through it,
    /// so a screen hit-tests against the same rectangles it drew.
    /// </summary>
    public float UiScale { get; init; } = 1f;

    public bool Pressed(Keys key) => Keyboard.IsKeyDown(key) && PreviousKeyboard.IsKeyUp(key);

    public bool Released(Keys key) => Keyboard.IsKeyUp(key) && PreviousKeyboard.IsKeyDown(key);

    public bool Down(Keys key) => Keyboard.IsKeyDown(key);

    public Point MousePosition => UiScale == 1f
        ? new Point(Mouse.X, Mouse.Y)
        : new Point((int)MathF.Round(Mouse.X / UiScale), (int)MathF.Round(Mouse.Y / UiScale));

    public bool MouseMoved => Mouse.X != PreviousMouse.X || Mouse.Y != PreviousMouse.Y;

    public bool MouseClicked =>
        Mouse.LeftButton == ButtonState.Pressed && PreviousMouse.LeftButton == ButtonState.Released;

    public int ScrollDelta => Mouse.ScrollWheelValue - PreviousMouse.ScrollWheelValue;
}
