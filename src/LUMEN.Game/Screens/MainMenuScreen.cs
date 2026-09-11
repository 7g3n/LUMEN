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
        "PLAY", "SONG SELECT", "EDITOR", "PROFILE", "REPLAYS", "HOW TO PLAY", "SETTINGS", "EXIT",
    };

    private readonly MenuList _menu = new(Actions);
    private int _lastWidth = 1280;
    private int _lastMenuTop = 320;

    /// <summary>
    /// The name and rating under the wordmark. Read when the screen appears rather than
    /// while drawing it: the summary is a database query, and asking for it once a frame
    /// is a few hundred queries a second for a number that changes once a play.
    /// </summary>
    private ProfileSummary _summary = ProfileSummary.Empty(Placeholder());

    public override void OnEnter() => Refresh();

    public override void OnReveal() => Refresh();

    private void Refresh()
    {
        Context.Session.Refresh();

        Profile? profile = Context.Session.ActiveProfile;
        _summary = profile is not null
            ? Context.Scores.GetSummary(profile)
            : ProfileSummary.Empty(Placeholder());
    }

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
            case "SONG SELECT":
                OpenSongSelect();
                break;
            case "EDITOR":
                Manager.Push(new Editor.EditorScreen());
                break;
            case "PROFILE":
                Manager.Push(new ProfileScreen());
                break;
            case "REPLAYS":
                Manager.Push(new ReplaysScreen());
                break;
            case "HOW TO PLAY":
                Manager.Push(new TutorialScreen(onFinished: () => Manager.Pop()));
                break;
            case "SETTINGS":
                Manager.Push(new SettingsScreen());
                break;
            case "EXIT":
                Context.RequestExit();
                break;
        }
    }

    /// <summary>
    /// Both PLAY and SONG SELECT land here: the spec's flow is "PLAY → pick a song", so
    /// the two are the same door with different labels rather than two different places.
    /// The bundled practice track is installed first, so a fresh profile never meets an
    /// empty library.
    /// </summary>
    private void OpenSongSelect()
    {
        try
        {
            Content.TestContent.EnsureInstalled(Context.Paths);
            Manager.Push(new SongSelectScreen());
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("could not open song select", ex);
            Manager.Push(new ErrorNoticeScreen("Couldn't open the song library", ex.Message));
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

        string rating = _summary.PlayCount == 0 ? "Rating —" : $"Rating {_summary.Rating:0.00}";
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
