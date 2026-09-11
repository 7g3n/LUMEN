using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Ui;

/// <summary>
/// Collects the window's text-input events between frames and packages raw
/// keyboard/mouse into an <see cref="InputFrame"/>. The gameplay input path (Phase 3)
/// is separate and latency-critical; this one only serves menus and the editor.
/// </summary>
public sealed class InputRouter
{
    private readonly StringBuilder _typed = new();
    private KeyboardState _previousKeyboard = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    public InputRouter(GameWindow window)
    {
        window.TextInput += OnTextInput;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        char c = e.Character;

        // Skip control characters; backspace/enter/tab are handled via key state.
        if (c is '\b' or '\r' or '\n' or '\t' || char.IsControl(c))
        {
            return;
        }

        _typed.Append(c);
    }

    public InputFrame BeginFrame(double deltaSeconds, float uiScale = 1f)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();

        var frame = new InputFrame
        {
            Keyboard = keyboard,
            PreviousKeyboard = _previousKeyboard,
            Mouse = mouse,
            PreviousMouse = _previousMouse,
            TypedText = _typed.ToString(),
            DeltaSeconds = deltaSeconds,
            UiScale = uiScale,
        };

        _typed.Clear();
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
        return frame;
    }
}
