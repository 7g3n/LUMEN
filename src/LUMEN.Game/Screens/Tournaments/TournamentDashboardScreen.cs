using Lumen.Core.Diagnostics;
using Lumen.Core.Tournaments;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens.Tournaments;

/// <summary>
/// A tournament in progress (spec: Tournament Dashboard / Tournament Bracket UI).
///
/// The bracket and the organiser's controls on one screen, because they are the same job:
/// you look at where the event has got to in order to decide what to do next. Splitting
/// them would mean paging back and forth between a picture and a list that describe the
/// same thing.
/// </summary>
public sealed class TournamentDashboardScreen : Screen
{
    private readonly Guid _tournamentId;

    private Tournament? _tournament;
    private IReadOnlyList<TournamentParticipant> _participants = Array.Empty<TournamentParticipant>();
    private IReadOnlyList<TournamentRound> _rounds = Array.Empty<TournamentRound>();
    private IReadOnlyList<TournamentMatch> _matches = Array.Empty<TournamentMatch>();
    private IReadOnlyList<StandingEntry> _standings = Array.Empty<StandingEntry>();

    private List<TournamentMatch> _playable = new();
    private int _selected;
    private string? _error;
    private string? _status;

    public TournamentDashboardScreen(Guid tournamentId) => _tournamentId = tournamentId;

    public override void OnEnter() => Refresh();

    public override void OnReveal() => Refresh();

    private void Refresh()
    {
        try
        {
            _tournament = Context.Tournaments.Get(_tournamentId);
            if (_tournament is null)
            {
                _error = "That tournament is no longer there.";
                return;
            }

            _participants = Context.Tournaments.Participants(_tournamentId);
            _rounds = Context.Tournaments.Rounds(_tournamentId);
            _matches = Context.Tournaments.Matches(_tournamentId);

            _standings = _tournament.Format == TournamentFormat.ScoreAttack
                ? Context.Tournaments.Standings(_tournamentId)
                : Array.Empty<StandingEntry>();

            // The matches somebody could sit down and play right now. That is the only list
            // an organiser actually acts on, so it is the one the keyboard drives.
            _playable = _matches
                .Where(m => !MatchFlow.IsTerminal(m.Status)
                            && (m.HasBothPlayers || _tournament.Format == TournamentFormat.ScoreAttack))
                .ToList();

            _selected = Math.Clamp(_selected, 0, Math.Max(0, _playable.Count - 1));
            _error = null;
        }
        catch (Exception ex)
        {
            Log.Error("could not read the tournament", ex);
            _error = "This tournament could not be read.";
        }
    }

    private string NameOf(Guid? playerId) =>
        playerId is null
            ? "—"
            : _participants.FirstOrDefault(p => p.PlayerId == playerId)?.DisplayName ?? "unknown";

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        if (input.Pressed(Keys.R))
        {
            Refresh();
            return;
        }

        if (input.Pressed(Keys.E))
        {
            Export();
            return;
        }

        if (_tournament?.Format == TournamentFormat.ScoreAttack
            && input.Pressed(Keys.F)
            && _tournament.Status == TournamentStatus.Running)
        {
            FinishScoreAttack();
            return;
        }

        if (_playable.Count == 0)
        {
            return;
        }

        if (input.Pressed(Keys.Down))
        {
            _selected = Math.Min(_selected + 1, _playable.Count - 1);
        }
        else if (input.Pressed(Keys.Up))
        {
            _selected = Math.Max(_selected - 1, 0);
        }
        else if (input.Pressed(Keys.Enter))
        {
            Manager.Push(new TournamentMatchScreen(_tournamentId, _playable[_selected].Id));
        }
    }

    /// <summary>
    /// Writes the tournament out as one file — the bracket, the results and the log of how
    /// it got there — so it can be sent to somebody or opened on another machine.
    /// </summary>
    private void Export()
    {
        if (_tournament is null)
        {
            return;
        }

        try
        {
            string safe = string.Join("_", _tournament.Name.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(Context.Paths.Exports,
                $"{safe}.{Lumen.Data.Tournaments.TournamentArchive.Extension}");

            Lumen.Data.Tournaments.TournamentArchive.Export(
                Context.TournamentStore, _tournament.Id, path);

            _status = $"Exported to {path}";
            _error = null;
        }
        catch (Exception ex)
        {
            Log.Warn("the tournament could not be exported", ex);
            _error = ex.Message;
        }
    }

    private void FinishScoreAttack()
    {
        try
        {
            IReadOnlyList<StandingEntry> standings = Context.Tournaments.FinishScoreAttack(_tournamentId);
            _status = standings.Count > 0
                ? $"Finished. {NameOf(standings[0].PlayerId)} wins."
                : "Finished, with nothing to rank.";
            Refresh();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    // --- drawing ---

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 80);

        if (_tournament is null)
        {
            ScreenChrome.Header(ui, column, "Tournament");
            ui.Text(ui.Body(Theme.Body), _error ?? "Loading…",
                new Rectangle(column.X, 160, column.Width, 24), Theme.Bad);
            ScreenChrome.FooterHint(ui, "Esc to go back");
            return;
        }

        int y = ScreenChrome.Header(ui, column, _tournament.Name);

        DrawSummary(ui, column, y);
        y += 56;

        // The bracket on the left, the things to do on the right.
        int split = column.X + (int)(column.Width * 0.58);

        if (_tournament.Format == TournamentFormat.ScoreAttack)
        {
            DrawStandings(ui, new Rectangle(column.X, y, split - column.X - 20, 320));
        }
        else
        {
            DrawBracket(ui, new Rectangle(column.X, y, split - column.X - 20, 340));
        }

        DrawQueue(ui, new Rectangle(split, y, column.Right - split, 340));

        if (_tournament.WinnerPlayerId is { } champion)
        {
            ui.Text(ui.Display(Theme.DisplayM), $"{NameOf(champion)} wins the tournament",
                new Rectangle(column.X, y + 356, column.Width, 28), Theme.Perfect);
        }

        string? message = _error ?? _status;
        if (message is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), message,
                new Rectangle(column.X, y + 390, column.Width, 20),
                _error is not null ? Theme.Bad : Theme.TextMuted);
        }

        ScreenChrome.FooterHint(ui, Hint());
    }

    private string Hint()
    {
        if (_tournament?.Status == TournamentStatus.Complete)
        {
            return "E to export  ·  R to refresh  ·  Esc to go back";
        }

        return _tournament?.Format == TournamentFormat.ScoreAttack
            ? "Enter to play  ·  F to finish and rank  ·  E to export  ·  Esc to go back"
            : "arrows  ·  Enter to play  ·  E to export  ·  R to refresh  ·  Esc to go back";
    }

    private void DrawSummary(UiRenderer ui, Rectangle column, int y)
    {
        (string Label, string Value)[] cells =
        {
            ("FORMAT", TournamentHubScreen.FormatName(_tournament!.Format)),
            ("STATUS", TournamentHubScreen.StatusName(_tournament.Status)),
            ("PLAYERS", _participants.Count.ToString()),
            ("RULES", _tournament.Rules.MaxAttempts == 1 ? "OFFICIAL" : "CASUAL"),
            ("BUILD", _tournament.GameVersion),
        };

        int gap = 10;
        int width = (column.Width - gap * (cells.Length - 1)) / cells.Length;

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new Rectangle(column.X + i * (width + gap), y, width, 44);
            ui.FillRect(cell, Theme.Surface);
            ui.StrokeRect(cell, Theme.Border);

            ui.Text(ui.Mono(Theme.Label), cells[i].Label,
                new Rectangle(cell.X + 10, cell.Y + 6, cell.Width - 20, 14), Theme.TextFaint);
            ui.Text(ui.Mono(16), cells[i].Value,
                new Rectangle(cell.X + 10, cell.Y + 22, cell.Width - 20, 18), Theme.Text);
        }
    }

    /// <summary>
    /// The bracket as columns, one per round, with a line from each pair to where its
    /// winner goes. Drawn rather than listed because the shape is the information: who a
    /// player would meet, and how far there is left to go.
    /// </summary>
    private void DrawBracket(UiRenderer ui, Rectangle area)
    {
        ui.Text(ui.Mono(Theme.Label), "BRACKET",
            new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);

        var winners = _rounds.Where(r => r.Side != BracketSide.Losers).OrderBy(r => r.Index).ToList();
        if (winners.Count == 0)
        {
            return;
        }

        int top = area.Y + 24;
        int columnWidth = area.Width / Math.Max(1, winners.Count);
        const int RowHeight = 40;

        for (int c = 0; c < winners.Count; c++)
        {
            TournamentRound round = winners[c];
            var inRound = _matches.Where(m => m.RoundId == round.Id).OrderBy(m => m.Slot).ToList();

            int x = area.X + c * columnWidth;

            ui.Text(ui.Mono(Theme.Label), round.Name.ToUpperInvariant(),
                new Rectangle(x, top, columnWidth - 8, 14), Theme.TextFaint);

            // Each round has half as many matches as the one before, so spacing doubles —
            // which is what makes the pairs line up with the match they feed.
            int spacing = RowHeight * (1 << c);
            int offset = spacing / 2 - RowHeight / 2;

            for (int i = 0; i < inRound.Count; i++)
            {
                TournamentMatch match = inRound[i];
                int matchY = top + 20 + offset + i * spacing;

                if (matchY + 34 > area.Bottom)
                {
                    break;
                }

                DrawMatchBox(ui, new Rectangle(x, matchY, columnWidth - 12, 34), match);
            }
        }
    }

    private void DrawMatchBox(UiRenderer ui, Rectangle box, TournamentMatch match)
    {
        bool done = match.Status == MatchStatus.Complete;
        bool live = _playable.Count > 0 && _playable.ElementAtOrDefault(_selected)?.Id == match.Id;

        ui.FillRect(box, Theme.Surface);
        ui.StrokeRect(box, live ? Theme.AccentBright : done ? Theme.Border : Theme.Border);

        if (live)
        {
            ui.FillRect(new Rectangle(box.X, box.Y, 3, box.Height), Theme.AccentBright);
        }

        DrawSeat(ui, new Rectangle(box.X + 8, box.Y + 2, box.Width - 16, 15),
            match.Player1Id, match.WinnerPlayerId);
        DrawSeat(ui, new Rectangle(box.X + 8, box.Y + 17, box.Width - 16, 15),
            match.Player2Id, match.WinnerPlayerId);
    }

    private void DrawSeat(UiRenderer ui, Rectangle row, Guid? player, Guid? winner)
    {
        bool won = player is not null && player == winner;

        ui.Text(ui.Mono(Theme.Label), NameOf(player), row,
            player is null ? Theme.TextFaint : won ? Theme.Perfect : Theme.TextMuted);
    }

    private void DrawStandings(UiRenderer ui, Rectangle area)
    {
        ui.Text(ui.Mono(Theme.Label), "STANDINGS",
            new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);

        if (_standings.Count == 0)
        {
            ui.Text(ui.Mono(Theme.Label), "Nothing confirmed yet.",
                new Rectangle(area.X, area.Y + 26, area.Width, 18), Theme.TextFaint);
            return;
        }

        int y = area.Y + 26;
        foreach (StandingEntry entry in _standings.Take(10))
        {
            var row = new Rectangle(area.X, y, area.Width, 26);

            ui.Text(ui.Mono(Theme.Label), $"#{entry.Rank}",
                new Rectangle(row.X, row.Y, 40, row.Height),
                entry.Rank == 1 ? Theme.Perfect : Theme.TextFaint);

            ui.Text(ui.Body(Theme.Label), NameOf(entry.PlayerId),
                new Rectangle(row.X + 44, row.Y, row.Width - 200, row.Height), Theme.Text);

            ui.Text(ui.Mono(Theme.Label), entry.Best.Score.ToString("N0"),
                new Rectangle(row.Right - 150, row.Y, 150, row.Height),
                Theme.TextMuted, TextAlign.Right);

            y += 28;
        }
    }

    /// <summary>What can be played right now, which is the list the organiser acts on.</summary>
    private void DrawQueue(UiRenderer ui, Rectangle area)
    {
        ui.Text(ui.Mono(Theme.Label), "UP NEXT",
            new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);

        if (_playable.Count == 0)
        {
            ui.Text(ui.Mono(Theme.Label),
                _tournament?.Status == TournamentStatus.Complete
                    ? "The tournament is over."
                    : "Waiting on earlier matches.",
                new Rectangle(area.X, area.Y + 26, area.Width, 18), Theme.TextFaint);
            return;
        }

        int y = area.Y + 26;

        foreach ((TournamentMatch match, int index) in _playable.Take(8).Select((m, i) => (m, i)))
        {
            bool selected = index == _selected;
            var row = new Rectangle(area.X, y, area.Width, 42);

            ui.FillRect(row, selected ? Theme.Surface : Theme.Ground);
            if (selected)
            {
                ui.FillRect(new Rectangle(row.X, row.Y, 3, row.Height), Theme.Accent);
            }

            string title = _tournament?.Format == TournamentFormat.ScoreAttack
                ? NameOf(match.Player1Id)
                : $"{NameOf(match.Player1Id)}  vs  {NameOf(match.Player2Id)}";

            ui.Text(ui.Body(Theme.Label), title,
                new Rectangle(row.X + 12, row.Y + 4, row.Width - 24, 18),
                selected ? Theme.Text : Theme.TextMuted);

            ui.Text(ui.Mono(Theme.Label), RoundNameOf(match) + "  ·  " + StatusWord(match.Status),
                new Rectangle(row.X + 12, row.Y + 22, row.Width - 24, 14), Theme.TextFaint);

            y += 44;
        }
    }

    private string RoundNameOf(TournamentMatch match) =>
        _rounds.FirstOrDefault(r => r.Id == match.RoundId)?.Name ?? "";

    private static string StatusWord(MatchStatus status) => status switch
    {
        MatchStatus.Pending => "waiting for players",
        MatchStatus.Waiting => "needs a chart",
        MatchStatus.SongSelected => "chart chosen",
        MatchStatus.Ready => "ready to play",
        MatchStatus.Playing => "in progress",
        MatchStatus.ResultPending => "waiting to be confirmed",
        MatchStatus.Complete => "finished",
        _ => "",
    };
}
