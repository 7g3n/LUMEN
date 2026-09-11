using Lumen.Core.Diagnostics;
using Lumen.Core.Library;
using Lumen.Core.Profiles;
using Lumen.Core.Tournaments;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens.Tournaments;

/// <summary>
/// Setting a tournament up (spec: Tournament作成).
///
/// One screen rather than a wizard. Everything a tournament needs is a short list, and
/// somebody organising an evening wants to see the whole of it at once — how many people
/// are in, which charts are in the pool, what the rules are — not to page back and forth
/// checking.
///
/// Entrants come from the profiles already on this machine, which is what makes a local
/// tournament work: four people who play on the same computer already have four profiles.
/// </summary>
public sealed class CreateTournamentScreen : Screen
{
    private enum Field { Name, Format, Rules, Players, Songs, Start }

    private readonly TextField _name = new() { MaxGraphemes = 40, Value = "Tournament" };

    private Field _field = Field.Name;
    private TournamentFormat _format = TournamentFormat.SingleElimination;
    private bool _casual;

    private IReadOnlyList<Profile> _profiles = Array.Empty<Profile>();
    private IReadOnlyList<LibraryChart> _charts = Array.Empty<LibraryChart>();

    private readonly HashSet<Guid> _entrants = new();
    private readonly HashSet<string> _pool = new();

    private int _playerCursor;
    private int _songCursor;
    private string? _error;

    private TournamentRules Rules => _casual ? TournamentRules.Casual : TournamentRules.Official;

    public override void OnEnter()
    {
        _name.Focused = true;

        try
        {
            _profiles = Context.Profiles.GetAll();
            _charts = Context.Library.Charts.All();

            // The person setting it up is almost certainly playing in it.
            if (Context.Session.HasProfile)
            {
                _entrants.Add(Context.Session.ActivePlayerId);
            }
        }
        catch (Exception ex)
        {
            Log.Error("could not load profiles or charts for a tournament", ex);
            _error = "Profiles and charts could not be read.";
        }
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        if (_field == Field.Name)
        {
            _name.Update(input);
        }

        if (input.Pressed(Keys.Tab) || input.Pressed(Keys.Down))
        {
            Move(1);
            return;
        }

        if (input.Pressed(Keys.Up))
        {
            Move(-1);
            return;
        }

        switch (_field)
        {
            case Field.Format when input.Pressed(Keys.Left) || input.Pressed(Keys.Right):
                _format = Cycle(_format, input.Pressed(Keys.Right) ? 1 : -1);
                break;

            case Field.Rules when input.Pressed(Keys.Left) || input.Pressed(Keys.Right):
                _casual = !_casual;
                break;

            case Field.Players:
                UpdatePicker(input, _profiles.Count, ref _playerCursor,
                    () => Toggle(_entrants, _profiles[_playerCursor].PlayerId));
                break;

            case Field.Songs:
                UpdatePicker(input, _charts.Count, ref _songCursor,
                    () => Toggle(_pool, _charts[_songCursor].ChartKey));
                break;

            case Field.Start when input.Pressed(Keys.Enter):
                CreateAndStart();
                break;
        }
    }

    private void Move(int delta)
    {
        var values = Enum.GetValues<Field>();
        _field = values[(Array.IndexOf(values, _field) + delta + values.Length) % values.Length];
        _name.Focused = _field == Field.Name;
    }

    private static void UpdatePicker(InputFrame input, int count, ref int cursor, Action toggle)
    {
        if (count == 0)
        {
            return;
        }

        if (input.Pressed(Keys.Right))
        {
            cursor = Math.Min(cursor + 1, count - 1);
        }
        else if (input.Pressed(Keys.Left))
        {
            cursor = Math.Max(cursor - 1, 0);
        }
        else if (input.Pressed(Keys.Space) || input.Pressed(Keys.Enter))
        {
            toggle();
        }
    }

    private static void Toggle<T>(HashSet<T> set, T id)
    {
        if (!set.Add(id))
        {
            set.Remove(id);
        }
    }

    private static TournamentFormat Cycle(TournamentFormat format, int delta)
    {
        var values = Enum.GetValues<TournamentFormat>();
        return values[(Array.IndexOf(values, format) + delta + values.Length) % values.Length];
    }

    /// <summary>
    /// Creates the tournament and draws its bracket in one go.
    ///
    /// Deliberately not two steps. A draft with nobody in it is not a useful thing to be
    /// able to leave lying around, and the organiser's intent when they press this is to
    /// start playing.
    /// </summary>
    private void CreateAndStart()
    {
        try
        {
            _error = null;

            if (_entrants.Count < BracketBuilder.MinimumParticipants)
            {
                _error = $"Pick at least {BracketBuilder.MinimumParticipants} players.";
                return;
            }

            if (_pool.Count == 0)
            {
                _error = "Pick at least one chart.";
                return;
            }

            Tournament tournament = Context.Tournaments.Create(
                _name.Value.Trim().Length > 0 ? _name.Value.Trim() : "Tournament",
                _format,
                Rules,
                Context.Session.ActiveProfile?.DisplayName ?? "");

            int seed = 1;
            foreach (Profile profile in _profiles.Where(p => _entrants.Contains(p.PlayerId)))
            {
                Context.Tournaments.AddParticipant(
                    tournament.Id, profile.PlayerId, profile.DisplayName, seed++);
            }

            foreach (LibraryChart chart in _charts.Where(c => _pool.Contains(c.ChartKey)))
            {
                Context.Tournaments.AddSong(tournament.Id, new TournamentSong
                {
                    TournamentId = tournament.Id,
                    ChartKey = chart.ChartKey,

                    // Title and difficulty are snapshots: a chart renamed later must not
                    // relabel a match that was already played on it.
                    Title = chart.Meta.Title,
                    DifficultyName = chart.Meta.DifficultyName,
                    Level = chart.Level,

                    // The key already folds in the chart's contents, so it is the hash:
                    // edit the chart and it stops matching what the pool recorded.
                    ChartHash = chart.ChartKey,
                });
            }

            Context.Tournaments.Start(tournament.Id);
            Manager.Replace(new TournamentDashboardScreen(tournament.Id));
        }
        catch (Exception ex)
        {
            Log.Warn("could not create the tournament", ex);
            _error = ex.Message;
        }
    }

    // --- drawing ---

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 80);
        int y = ScreenChrome.Header(ui, column, "New Tournament");

        y = DrawName(ui, column, y);
        y = DrawChoice(ui, column, y, Field.Format, "FORMAT",
            TournamentHubScreen.FormatName(_format), FormatBlurb(_format));
        y = DrawChoice(ui, column, y, Field.Rules, "RULES",
            _casual ? "CASUAL" : "OFFICIAL",
            _casual
                ? "Retries and pauses allowed, three attempts."
                : "One attempt, no retry, no pause, no modifiers.");

        y = DrawPicker(ui, column, y, Field.Players, "PLAYERS",
            _profiles.Select(p => p.DisplayName).ToList(),
            _profiles.Select(p => _entrants.Contains(p.PlayerId)).ToList(),
            _playerCursor, "No profiles on this machine yet.");

        y = DrawPicker(ui, column, y, Field.Songs, "SONG POOL",
            _charts.Select(c => $"{c.Meta.Title} [{c.Meta.DifficultyName}]").ToList(),
            _charts.Select(c => _pool.Contains(c.ChartKey)).ToList(),
            _songCursor, "No charts in your library yet.");

        DrawStart(ui, column, y);

        ScreenChrome.FooterHint(ui,
            "arrows to move  ·  Space to pick  ·  Tab for the next field  ·  Esc to go back");
    }

    private int DrawName(UiRenderer ui, Rectangle column, int y)
    {
        Label(ui, column, y, "NAME", _field == Field.Name);
        _name.Draw(ui, new Rectangle(column.X, y + 18, 420, 34));
        return y + 66;
    }

    private int DrawChoice(UiRenderer ui, Rectangle column, int y, Field field,
                           string label, string value, string blurb)
    {
        bool active = _field == field;
        Label(ui, column, y, label, active);

        ui.Text(ui.Display(Theme.DisplayM), value,
            new Rectangle(column.X, y + 18, 300, 26),
            active ? Theme.AccentBright : Theme.Text);

        ui.Text(ui.Mono(Theme.Label), blurb,
            new Rectangle(column.X + 310, y + 22, column.Width - 310, 18), Theme.TextFaint);

        if (active)
        {
            ui.Text(ui.Mono(Theme.Label), "< >",
                new Rectangle(column.X - 34, y + 20, 30, 18), Theme.Accent);
        }

        return y + 58;
    }

    private int DrawPicker(UiRenderer ui, Rectangle column, int y, Field field, string label,
                           IReadOnlyList<string> names, IReadOnlyList<bool> chosen,
                           int cursor, string empty)
    {
        bool active = _field == field;
        int picked = chosen.Count(c => c);

        Label(ui, column, y, $"{label}   {picked} picked", active);

        if (names.Count == 0)
        {
            ui.Text(ui.Mono(Theme.Label), empty,
                new Rectangle(column.X, y + 20, column.Width, 18), Theme.Bad);
            return y + 52;
        }

        // A horizontal strip of chips: a tournament is a handful of people and a handful of
        // charts, and a scrolling list would be more machinery than the job needs.
        int x = column.X;
        int rowY = y + 20;

        for (int i = 0; i < names.Count; i++)
        {
            string text = names[i];
            var size = ui.Measure(ui.Mono(Theme.Label), text);
            int width = (int)size.X + 22;

            if (x + width > column.Right)
            {
                x = column.X;
                rowY += 28;
            }

            var chip = new Rectangle(x, rowY, width, 24);

            ui.FillRect(chip, chosen[i] ? Theme.AccentSoft : Theme.Surface);
            ui.StrokeRect(chip, active && i == cursor ? Theme.AccentBright
                : chosen[i] ? Theme.Accent : Theme.Border);

            ui.Text(ui.Mono(Theme.Label), text, chip,
                chosen[i] ? Theme.AccentBright : Theme.TextMuted, TextAlign.Center);

            x += width + 8;
        }

        return rowY + 44;
    }

    private void DrawStart(UiRenderer ui, Rectangle column, int y)
    {
        bool active = _field == Field.Start;
        bool ready = _entrants.Count >= BracketBuilder.MinimumParticipants && _pool.Count > 0;

        var button = new Rectangle(column.X, y, 260, 44);
        ui.FillRect(button, ready ? Theme.AccentSoft : Theme.Surface);
        ui.StrokeRect(button, active ? Theme.AccentBright : Theme.Border, active ? 2 : 1);

        ui.Text(ui.Display(Theme.DisplayM), "DRAW THE BRACKET", button,
            ready ? Theme.AccentBright : Theme.TextFaint, TextAlign.Center);

        if (_error is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _error,
                new Rectangle(column.X + 280, y + 12, column.Width - 280, 20), Theme.Bad);
        }
        else if (ready)
        {
            ui.Text(ui.Mono(Theme.Label),
                $"{_entrants.Count} players  ·  {_pool.Count} charts",
                new Rectangle(column.X + 280, y + 14, column.Width - 280, 18), Theme.TextFaint);
        }
    }

    private static void Label(UiRenderer ui, Rectangle column, int y, string text, bool active) =>
        ui.Text(ui.Mono(Theme.Label), text,
            new Rectangle(column.X, y, column.Width, 16),
            active ? Theme.Accent : Theme.TextFaint);

    private static string FormatBlurb(TournamentFormat format) => format switch
    {
        TournamentFormat.SingleElimination => "Lose once and you are out.",
        TournamentFormat.DoubleElimination => "A losers' bracket: one bad match is not the end.",
        TournamentFormat.ScoreAttack => "Everybody plays the same charts; the ranking decides.",
        _ => "",
    };
}
