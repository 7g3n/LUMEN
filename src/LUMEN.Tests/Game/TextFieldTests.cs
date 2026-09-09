using FluentAssertions;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Lumen.Tests.Game;

public class TextFieldTests
{
    private static InputFrame Type(string text, params Keys[] held)
    {
        var keys = new KeyboardState(held);
        return new InputFrame
        {
            Keyboard = keys,
            PreviousKeyboard = new KeyboardState(),
            Mouse = new MouseState(),
            PreviousMouse = new MouseState(),
            TypedText = text,
            DeltaSeconds = 1.0 / 60,
        };
    }

    [Fact]
    public void Appends_typed_text()
    {
        var field = new TextField();
        field.Update(Type("Nag"));
        field.Update(Type("isa"));

        field.Value.Should().Be("Nagisa");
    }

    [Fact]
    public void Stops_appending_at_the_grapheme_limit()
    {
        var field = new TextField { MaxGraphemes = 4 };
        field.Update(Type("abcdefgh"));

        field.Value.Should().Be("abcd");
    }

    [Fact]
    public void Backspace_removes_one_grapheme_cluster()
    {
        var field = new TextField { Value = "ab\U0001F469" }; // 'a','b', woman emoji (1 cluster)
        field.Update(Type("", Keys.Back));

        field.Value.Should().Be("ab");
    }

    [Fact]
    public void Not_focused_ignores_input()
    {
        var field = new TextField { Focused = false, Value = "keep" };
        field.Update(Type("x", Keys.Back));

        field.Value.Should().Be("keep");
    }
}
