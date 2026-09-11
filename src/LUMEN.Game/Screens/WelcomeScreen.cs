using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Confirmation shown right after a profile is created (spec §81).
///
/// It leads into the tutorial rather than into the menu: a new player's next action
/// should be learning to play, not choosing from a list of seven things they have no
/// basis for choosing between yet. The menu is one key away for anyone who disagrees.
/// </summary>
public sealed class WelcomeScreen : Screen
{
    private readonly string _name;
    private double _time;

    public WelcomeScreen(string name) => _name = name;

    public override void Update(InputFrame input)
    {
        _time += input.DeltaSeconds;

        if (input.Pressed(Keys.Escape))
        {
            Manager.ReplaceAll(new MainMenuScreen());
            return;
        }

        if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space)
            || (input.MouseClicked && _time > 0.3))
        {
            StartTutorial();
        }
    }

    /// <summary>
    /// Tutorial, then straight into the practice track — the shortest honest path from
    /// "I have just named myself" to "I have a score", which is the thing the first run
    /// has to deliver.
    /// </summary>
    private void StartTutorial() =>
        Manager.ReplaceAll(new TutorialScreen(onFinished: () =>
            FirstPlay.Start(Manager, Context)));

    public override void Draw(UiRenderer ui)
    {
        int cx = ui.Width / 2;
        int y = (int)(ui.Height * 0.3f);

        ui.Text(ui.Mono(Theme.Label), "PROFILE CREATED",
            new Rectangle(0, y, ui.Width, 20), Theme.Accent, TextAlign.Center);

        ui.Text(ui.Display(Theme.DisplayL), $"Welcome, {_name}.",
            new Rectangle(0, y + 40, ui.Width, 48), Theme.Text, TextAlign.Center);

        ui.Text(ui.Body(Theme.Body), "Let's learn to play. It takes about a minute.",
            new Rectangle(0, y + 96, ui.Width, 24), Theme.TextMuted, TextAlign.Center);

        var button = new Rectangle(cx - 120, y + 160, 240, 50);
        ui.FillRect(button, Theme.AccentSoft);
        ui.StrokeRect(button, Theme.Accent, 2);
        ui.Text(ui.Display(Theme.DisplayM), "HOW TO PLAY", button, Theme.AccentBright, TextAlign.Center);

        ScreenChrome.FooterHint(ui, "Enter to start  ·  Esc to go to the menu");
    }
}
