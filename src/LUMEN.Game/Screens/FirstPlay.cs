using Lumen.Core.Diagnostics;
using Lumen.Game.Ui;

namespace Lumen.Game.Screens;

/// <summary>
/// The end of the first run: tutorial straight into a first song (spec §81–83).
///
/// The bar the whole game is measured against is that somebody can start it and play
/// without reading anything, and the gap that usually breaks that is the one between
/// "I understand the rules" and "I am playing". So the tutorial does not hand the player
/// back to a menu to go and find a song — it starts one, and the menu is where they land
/// afterwards, by which point they have a score and a reason to read it.
///
/// The bundled practice track is what they play, because it is the one song every install
/// is guaranteed to have.
/// </summary>
internal static class FirstPlay
{
    public static void Start(ScreenManager manager, GameContext context)
    {
        try
        {
            Content.TestContent.Installed practice =
                Content.TestContent.EnsureInstalled(context.Paths);

            Log.Info("first play: starting the practice track");

            manager.ReplaceAll(new GameplayScreen(practice.ChartPath, practice.AudioPath));
        }
        catch (Exception ex)
        {
            // A first run that cannot start a song should still arrive somewhere usable,
            // and should say what went wrong rather than dropping the player on a menu
            // with no explanation.
            Log.Error("first play could not start", ex);
            manager.ReplaceAll(new MainMenuScreen());
            manager.Push(new ErrorNoticeScreen(
                "Couldn't start your first song",
                ex.Message + "\n\nYou can pick a song yourself from PLAY."));
        }
    }
}
