using Lumen.Core.Tournaments;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens.Tournaments;

/// <summary>
/// The way into tournaments (spec: Tournament Mode UI).
///
/// Lists what is already running before it offers to start something new, because on any
/// evening after the first that is what somebody is here for: an event in progress they
/// want to get back to.
/// </summary>
public sealed class TournamentHubScreen : Screen, IFileDropTarget
{
    private static readonly string[] Actions = { "CREATE TOURNAMENT", "HOW TOURNAMENTS WORK" };

    private readonly MenuList _menu = new(Actions);
    private IReadOnlyList<Tournament> _tournaments = Array.Empty<Tournament>();
    private int _selected;
    private bool _onList = true;
    private string? _error;

    public override void OnEnter() => Refresh();

    public override void OnReveal() => Refresh();

    private void Refresh()
    {
        try
        {
            _tournaments = Context.Tournaments.All();
            _selected = Math.Clamp(_selected, 0, Math.Max(0, _tournaments.Count - 1));

            // If there is anything in the list, that is what somebody came here for —
            // including on the way back from one they were just running. Leaving focus on
            // the actions meant returning from a tournament and finding the keys doing
            // nothing to it.
            _onList = _tournaments.Count > 0;
            _error = null;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("could not read tournaments", ex);
            _error = "The tournament list could not be read.";
            _tournaments = Array.Empty<Tournament>();
        }
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        // Tab moves between the list of existing tournaments and the actions below it.
        if (input.Pressed(Keys.Tab) && _tournaments.Count > 0)
        {
            _onList = !_onList;
            return;
        }

        if (_onList && _tournaments.Count > 0)
        {
            UpdateList(input);
            return;
        }

        int activated = _menu.Update(input, MenuArea());
        if (activated < 0)
        {
            return;
        }

        switch (Actions[activated])
        {
            case "CREATE TOURNAMENT":
                Manager.Push(new CreateTournamentScreen());
                break;
            case "HOW TOURNAMENTS WORK":
                Manager.Push(new TournamentRulesScreen());
                break;
        }
    }

    private void UpdateList(InputFrame input)
    {
        if (input.Pressed(Keys.Down))
        {
            _selected = Math.Min(_selected + 1, _tournaments.Count - 1);
        }
        else if (input.Pressed(Keys.Up))
        {
            if (_selected == 0)
            {
                _onList = false;
            }
            else
            {
                _selected--;
            }
        }
        else if (input.Pressed(Keys.Enter))
        {
            Manager.Push(new TournamentDashboardScreen(_tournaments[_selected].Id));
        }
        else if (input.Pressed(Keys.Delete))
        {
            Delete(_tournaments[_selected]);
        }
    }

    /// <summary>
    /// Only a draft is deletable. One that has been run is cancelled instead, because the
    /// record of what happened stays true even when the event does not.
    /// </summary>
    private void Delete(Tournament tournament)
    {
        try
        {
            if (tournament.Status == TournamentStatus.Draft)
            {
                Context.TournamentStore.Delete(tournament.Id);
            }
            else
            {
                Context.Tournaments.Cancel(tournament.Id, "cancelled by the organiser");
            }

            Refresh();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    /// <summary>
    /// A tournament file dropped on the window is imported — the same gesture that imports
    /// a song package, for the same reason: it is what somebody handed a file will try.
    /// </summary>
    public void OnFilesDropped(IReadOnlyList<string> paths)
    {
        string? file = paths.FirstOrDefault(p =>
            Path.GetExtension(p).TrimStart('.')
                .Equals(Lumen.Data.Tournaments.TournamentArchive.Extension,
                        StringComparison.OrdinalIgnoreCase));

        if (file is null)
        {
            _error = $"That is not a tournament file " +
                     $"(.{Lumen.Data.Tournaments.TournamentArchive.Extension}).";
            return;
        }

        try
        {
            Tournament imported = Lumen.Data.Tournaments.TournamentArchive.Import(
                Context.TournamentStore, file);

            _error = null;
            Refresh();
            _selected = Math.Max(0, _tournaments.ToList().FindIndex(t => t.Id == imported.Id));
            _onList = true;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn("a tournament could not be imported", ex);
            _error = ex.Message;
        }
    }

    // Mirrors Draw, so mouse hit-testing lands on what was drawn.
    private int _menuLeft = 160;
    private int _menuTop = 520;

    private Rectangle MenuArea() => new(_menuLeft, _menuTop, 360, Actions.Length * 40);

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 88);
        int y = ScreenChrome.Header(ui, column, "Tournament");

        ui.Text(ui.Body(Theme.Body),
            "Run a competition on this machine. Tournament results are kept apart from your rating.",
            new Rectangle(column.X, y, column.Width, 22), Theme.TextMuted);

        y += 44;
        y = DrawList(ui, column, y);

        _menuLeft = column.X;
        _menuTop = y + 16;
        _menu.Draw(ui, MenuArea());

        if (_error is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _error,
                new Rectangle(column.X, _menuTop + Actions.Length * 40 + 12, column.Width, 18),
                Theme.Bad);
        }

        ScreenChrome.FooterHint(ui, _tournaments.Count == 0
            ? "Enter to select  ·  Esc to go back"
            : "arrows  ·  Enter to open  ·  Tab to switch  ·  Del to remove  ·  Esc to go back");

        ui.Text(ui.Mono(Theme.Label),
            $"drop a .{Lumen.Data.Tournaments.TournamentArchive.Extension} file to import one",
            new Rectangle(column.X, column.Bottom - 20, column.Width, 16), Theme.TextFaint);
    }

    private int DrawList(UiRenderer ui, Rectangle column, int y)
    {
        if (_tournaments.Count == 0)
        {
            ui.Text(ui.Mono(Theme.Label), "No tournaments yet.",
                new Rectangle(column.X, y, column.Width, 18), Theme.TextFaint);
            return y + 32;
        }

        ui.Text(ui.Mono(Theme.Label), "YOUR TOURNAMENTS",
            new Rectangle(column.X, y, column.Width, 16), Theme.Accent);
        y += 24;

        foreach ((Tournament tournament, int index) in _tournaments.Take(8).Select((t, i) => (t, i)))
        {
            bool selected = _onList && index == _selected;
            var row = new Rectangle(column.X, y, column.Width, 34);

            if (selected)
            {
                ui.FillRect(row, Theme.Surface);
                ui.FillRect(new Rectangle(row.X, row.Y, 3, row.Height), Theme.Accent);
            }

            ui.Text(ui.Body(Theme.Body), tournament.Name,
                new Rectangle(row.X + 14, row.Y, row.Width - 320, row.Height),
                selected ? Theme.Text : Theme.TextMuted);

            ui.Text(ui.Mono(Theme.Label), FormatName(tournament.Format),
                new Rectangle(row.Right - 300, row.Y, 150, row.Height), Theme.TextFaint);

            ui.Text(ui.Mono(Theme.Label), StatusName(tournament.Status),
                new Rectangle(row.Right - 150, row.Y, 150, row.Height),
                StatusColour(tournament.Status), TextAlign.Right);

            y += 36;
        }

        return y + 8;
    }

    public static string FormatName(TournamentFormat format) => format switch
    {
        TournamentFormat.SingleElimination => "SINGLE ELIM",
        TournamentFormat.DoubleElimination => "DOUBLE ELIM",
        TournamentFormat.ScoreAttack => "SCORE ATTACK",
        _ => format.ToString().ToUpperInvariant(),
    };

    public static string StatusName(TournamentStatus status) => status switch
    {
        TournamentStatus.Draft => "DRAFT",
        TournamentStatus.Running => "RUNNING",
        TournamentStatus.Complete => "FINISHED",
        TournamentStatus.Cancelled => "CANCELLED",
        _ => "",
    };

    public static Color StatusColour(TournamentStatus status) => status switch
    {
        TournamentStatus.Draft => Theme.TextFaint,
        TournamentStatus.Running => Theme.AccentBright,
        TournamentStatus.Complete => Theme.Perfect,
        _ => Theme.TextFaint,
    };
}
