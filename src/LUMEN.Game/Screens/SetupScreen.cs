using Lumen.Core.Diagnostics;
using Lumen.Core.Profiles;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// First-run setup (spec §4, §81). Player name is required; validation follows §5.
/// On success a profile is created with a fresh UUID and the game moves on.
/// </summary>
public sealed class SetupScreen : Screen
{
    private readonly TextField _field = new() { MaxGraphemes = PlayerName.MaxLength };
    private PlayerNameError _shownError = PlayerNameError.None;
    private bool _attempted;
    private Rectangle _continueButton;

    public override void OnEnter() => _field.Focused = true;

    public override void Update(InputFrame input)
    {
        _field.Update(input);

        PlayerNameResult live = PlayerName.Validate(_field.Value);
        if (_attempted)
        {
            _shownError = live.Error;
        }

        bool submit = input.Pressed(Keys.Enter)
                      || (input.MouseClicked && _continueButton.Contains(input.MousePosition));

        if (submit)
        {
            _attempted = true;
            if (live.IsValid)
            {
                CreateProfile(live.Normalized);
            }
            else
            {
                _shownError = live.Error;
            }
        }
    }

    private void CreateProfile(string name)
    {
        Profile profile = Context.Profiles.Create(name);
        Context.Session.SetActive(profile);
        Log.Info($"profile created: {profile.DisplayName}");
        Manager.Replace(new WelcomeScreen(profile.DisplayName));
    }

    public override void Draw(UiRenderer ui)
    {
        int cx = ui.Width / 2;
        int top = (int)(ui.Height * 0.24f);

        ScreenChrome.Wordmark(ui, Core.GameIdentity.Name, top, Theme.DisplayXl, Theme.Accent);

        ui.Text(ui.Mono(Theme.Label), Core.GameIdentity.Tagline,
            new Rectangle(0, top + 74, ui.Width, 20), Theme.TextMuted, TextAlign.Center);

        int fieldWidth = Math.Min(420, ui.Width - 120);
        var fieldBox = new Rectangle(cx - fieldWidth / 2, top + 140, fieldWidth, 56);

        ui.Text(ui.Mono(Theme.Label), "PLAYER NAME",
            new Rectangle(fieldBox.X, fieldBox.Y - 26, fieldBox.Width, 18), Theme.TextMuted);

        _field.Draw(ui, fieldBox, placeholder: "7g3");

        int count = PlayerName.CountGraphemes(_field.Value);
        ui.Text(ui.Mono(Theme.Label), $"{count}/{PlayerName.MaxLength}",
            new Rectangle(fieldBox.X, fieldBox.Bottom + 8, fieldBox.Width, 16), Theme.TextFaint, TextAlign.Right);

        if (_shownError != PlayerNameError.None)
        {
            string msg = new PlayerNameResult(false, _field.Value, _shownError).Message;
            ui.Text(ui.Body(Theme.Label), msg,
                new Rectangle(fieldBox.X, fieldBox.Bottom + 8, fieldBox.Width, 16), Theme.Danger);
        }

        bool valid = PlayerName.IsValid(_field.Value);
        _continueButton = new Rectangle(cx - fieldWidth / 2, fieldBox.Bottom + 44, fieldWidth, 50);
        ui.FillRect(_continueButton, valid ? Theme.AccentSoft : Theme.Surface);
        ui.StrokeRect(_continueButton, valid ? Theme.Accent : Theme.Border, valid ? 2 : 1);
        ui.Text(ui.Display(Theme.DisplayM), "CONTINUE", _continueButton,
            valid ? Theme.AccentBright : Theme.TextFaint, TextAlign.Center);

        ScreenChrome.FooterHint(ui, "type your name  ·  Enter to continue");
    }
}
