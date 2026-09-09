using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>Stand-in for features that arrive in a later phase. Removed as each ships.</summary>
public sealed class PlaceholderScreen : Screen
{
    private readonly string _title;
    private readonly string _note;

    public PlaceholderScreen(string title, string note)
    {
        _title = title;
        _note = note;
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape) || input.Pressed(Keys.Enter))
        {
            Manager.Pop();
        }
    }

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui);
        int y = ScreenChrome.Header(ui, column, "Not yet", _title);

        ui.Text(ui.Body(Theme.Body), _note,
            new Rectangle(column.X, y + 8, column.Width, 24), Theme.TextMuted);

        ScreenChrome.FooterHint(ui, "Esc to go back");
    }
}
