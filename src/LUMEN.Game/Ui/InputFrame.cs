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

    public bool Pressed(Keys key) => Keyboard.IsKeyDown(key) && PreviousKeyboard.IsKeyUp(key);

    public bool Released(Keys key) => Keyboard.IsKeyUp(key) && PreviousKeyboard.IsKeyDown(key);

    public bool Down(Keys key) => Keyboard.IsKeyDown(key);

    public Point MousePosition => new(Mouse.X, Mouse.Y);

    public bool MouseMoved => Mouse.X != PreviousMouse.X || Mouse.Y != PreviousMouse.Y;

    public bool MouseClicked =>
        Mouse.LeftButton == ButtonState.Pressed && PreviousMouse.LeftButton == ButtonState.Released;

    public int ScrollDelta => Mouse.ScrollWheelValue - PreviousMouse.ScrollWheelValue;
}
