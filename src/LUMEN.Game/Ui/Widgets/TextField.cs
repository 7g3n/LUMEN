using Lumen.Core.Profiles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Ui.Widgets;

/// <summary>
/// Single-line Unicode text entry. The caret sits at the end (enough for a name field);
/// backspace repeats when held. Grapheme-aware length limiting matches
/// <see cref="PlayerName"/>.
/// </summary>
public sealed class TextField
{
    private double _backspaceHeld = -1;
    private double _caretBlink;

    public string Value { get; set; } = "";

    public int MaxGraphemes { get; set; } = PlayerName.MaxLength;

    public bool Focused { get; set; } = true;

    public void Update(InputFrame input)
    {
        if (!Focused)
        {
            return;
        }

        _caretBlink += input.DeltaSeconds;

        if (input.TypedText.Length > 0)
        {
            AppendWithinLimit(input.TypedText);
            _caretBlink = 0;
        }

        HandleBackspace(input);
    }

    private void AppendWithinLimit(string text)
    {
        string candidate = Value + text;
        while (candidate.Length > 0 && PlayerName.CountGraphemes(candidate) > MaxGraphemes)
        {
            candidate = RemoveLastGrapheme(candidate);
            if (candidate.Length <= Value.Length)
            {
                return; // nothing fits
            }
        }

        Value = candidate;
    }

    private void HandleBackspace(InputFrame input)
    {
        bool down = input.Down(Keys.Back);
        if (!down)
        {
            _backspaceHeld = -1;
            return;
        }

        bool fire;
        if (_backspaceHeld < 0)
        {
            fire = true;               // initial press
            _backspaceHeld = 0;
        }
        else
        {
            _backspaceHeld += input.DeltaSeconds;
            fire = _backspaceHeld >= 0.35; // repeat after a hold
            if (fire)
            {
                _backspaceHeld -= 0.045;   // ~22/sec
            }
        }

        if (fire && Value.Length > 0)
        {
            Value = RemoveLastGrapheme(Value);
            _caretBlink = 0;
        }
    }

    private static string RemoveLastGrapheme(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(value);
        int lastStart = 0;
        while (enumerator.MoveNext())
        {
            lastStart = enumerator.ElementIndex;
        }

        return value[..lastStart];
    }

    public void Draw(UiRenderer ui, Rectangle box, string placeholder = "")
    {
        ui.FillRect(box, Theme.SurfaceRaised);
        ui.StrokeRect(box, Focused ? Theme.Accent : Theme.BorderStrong, Focused ? 2 : 1);

        var inner = new Rectangle(box.X + 16, box.Y, box.Width - 32, box.Height);
        var font = ui.Body(Theme.DisplayM);

        bool showPlaceholder = Value.Length == 0 && placeholder.Length > 0;
        string text = showPlaceholder ? placeholder : Value;
        ui.Text(font, text, inner, showPlaceholder ? Theme.TextFaint : Theme.Text);

        if (Focused && _caretBlink % 1.0 < 0.5)
        {
            float textWidth = Value.Length == 0 ? 0 : ui.Measure(font, Value).X;
            int caretX = (int)(inner.X + textWidth) + 2;
            int caretH = (int)(font.MeasureString("Ag").Y);
            ui.FillRect(caretX, box.Y + (box.Height - caretH) / 2, 2, caretH, Theme.Accent);
        }
    }
}
