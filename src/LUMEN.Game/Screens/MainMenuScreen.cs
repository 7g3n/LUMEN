using Lumen.Core.Profiles;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework;

namespace Lumen.Game.Screens;

/// <summary>The hub (spec §14). Player name and Rating sit above the actions.</summary>
public sealed class MainMenuScreen : Screen
{
    private static readonly string[] Actions =
    {
        "PLAY", "SONG SELECT", "EDITOR", "PROFILE", "REPLAYS", "SETTINGS", "EXIT",
    };

    private readonly MenuList _menu = new(Actions);
    private int _lastWidth = 1280;
    private int _lastMenuTop = 320;

    public override void OnEnter() => Context.Session.Refresh();

    public override void OnReveal() => Context.Session.Refresh();

    public override void Update(InputFrame input)
    {
        int activated = _menu.Update(input, MenuArea());
        if (activated < 0)
        {
            return;
        }

        switch (Actions[activated])
        {
            case "PLAY":
                StartPractice();
                break;
            case "SONG SELECT":
                Manager.Push(new PlaceholderScreen("Song Select", "The song library and local rankings arrive in Phase 5."));
                break;
            case "EDITOR":
                Manager.Push(new PlaceholderScreen("Chart Editor", "The chart editor arrives in Phase 6."));
                break;
            case "PROFILE":
                Manager.Push(new ProfileScreen());
                break;
            case "REPLAYS":
                Manager.Push(new PlaceholderScreen("Replays", "Replay recording and playback arrive in Phase 8."));
                break;
            case "SETTINGS":
                Manager.Push(new SettingsScreen());
                break;
            case "EXIT":
                Context.RequestExit();
                break;
        }
    }

    private void StartPractice()
    {
        try
        {
            var installed = Content.TestContent.EnsureInstalled(Context.Paths);
            Manager.Push(new GameplayScreen(installed.ChartPath, installed.AudioPath));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("could not prepare practice track", ex);
            Manager.Push(new ErrorNoticeScreen("Couldn't prepare the practice track", ex.Message));
        }
    }

    private Rectangle MenuArea()
    {
        // Mirrors Draw; kept in one small helper so hit-testing matches the visuals.
        int width = 360;
        int x = (_lastWidth - width) / 2;
        return new Rectangle(x, _lastMenuTop, width, Actions.Length * 44);
    }

    public override void Draw(UiRenderer ui)
    {
        _lastWidth = ui.Width;
        int top = (int)(ui.Height * 0.14f);

        ScreenChrome.Wordmark(ui, Core.GameIdentity.Name, top, Theme.DisplayXl, Theme.Accent);

        Profile? profile = Context.Session.ActiveProfile;
        string name = profile?.DisplayName ?? "player";
        ui.Text(ui.Display(Theme.DisplayM), name,
            new Rectangle(0, top + 78, ui.Width, 28), Theme.Text, TextAlign.Center);

        ProfileSummary summary = profile is not null
            ? Context.Scores.GetSummary(profile)
            : ProfileSummary.Empty(Placeholder());
        string rating = summary.PlayCount == 0 ? "Rating —" : $"Rating {summary.Rating:0.00}";
        ui.Text(ui.Mono(Theme.Label), rating,
            new Rectangle(0, top + 110, ui.Width, 20), Theme.TextMuted, TextAlign.Center);

        _lastMenuTop = top + 168;
        var area = new Rectangle((ui.Width - 360) / 2, _lastMenuTop, 360, Actions.Length * 44);
        _menu.Draw(ui, area, TextAlign.Center);

        ScreenChrome.FooterHint(ui, "arrow keys  ·  Enter to select");
    }

    private static Profile Placeholder() => new()
    {
        PlayerId = Guid.Empty,
        DisplayName = "player",
        CreatedUtc = DateTime.UtcNow,
        LastPlayedUtc = DateTime.UtcNow,
    };
}
