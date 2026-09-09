using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// A recoverable, user-facing error (spec §96): "Unable to load chart / [BACK]". Unlike
/// <c>CrashGuard</c>'s fatal screen, this one is expected and just returns to the menu.
/// </summary>
public sealed class ErrorNoticeScreen : Screen
{
    private readonly string _title;
    private readonly string _reason;

    public ErrorNoticeScreen(string title, string reason)
    {
        _title = title;
        _reason = reason;
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape) || input.Pressed(Keys.Enter))
        {
            Manager.ReplaceAll(new MainMenuScreen());
        }
    }

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui);
        int y = ScreenChrome.Header(ui, column, "Something went wrong", _title);

        ui.Text(ui.Body(Theme.Body), _reason,
            new Rectangle(column.X, y + 8, column.Width, 48), Theme.TextMuted);

        ScreenChrome.FooterHint(ui, "Enter to go back");
    }
}
