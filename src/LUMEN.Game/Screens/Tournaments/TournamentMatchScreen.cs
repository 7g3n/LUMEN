using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Core.Gameplay;
using Lumen.Core.Library;
using Lumen.Core.Tournaments;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens.Tournaments;

/// <summary>
/// One match, from picking a chart to confirming the result (spec: Tournament Match).
///
/// The match is played here in turns: each player sits down, plays, and hands the keyboard
/// over. That is what a local tournament actually looks like — four people around one
/// machine — and it is why the screen tracks whose go it is rather than assuming the
/// person at the keyboard is the profile that happens to be signed in.
/// </summary>
public sealed class TournamentMatchScreen : Screen
{
    private readonly Guid _tournamentId;
    private readonly Guid _matchId;

    private Tournament? _tournament;
    private TournamentMatch? _match;
    private IReadOnlyList<TournamentParticipant> _participants = Array.Empty<TournamentParticipant>();
    private IReadOnlyList<TournamentSong> _pool = Array.Empty<TournamentSong>();
    private IReadOnlyList<TournamentMatchResult> _results = Array.Empty<TournamentMatchResult>();

    private int _songCursor;
    private string? _error;
    private string? _status;

    public TournamentMatchScreen(Guid tournamentId, Guid matchId)
    {
        _tournamentId = tournamentId;
        _matchId = matchId;
    }

    public override void OnEnter() => Refresh();

    public override void OnReveal() => Refresh();

    private void Refresh()
    {
        try
        {
            _tournament = Context.Tournaments.Get(_tournamentId);
            _match = Context.Tournaments.Matches(_tournamentId).FirstOrDefault(m => m.Id == _matchId);
            _participants = Context.Tournaments.Participants(_tournamentId);
            _pool = Context.Tournaments.Songs(_tournamentId);
            _results = Context.Tournaments.ResultsForMatch(_matchId);
            _error = null;
        }
        catch (Exception ex)
        {
            Log.Error("could not read the match", ex);
            _error = "This match could not be read.";
        }
    }

    private string NameOf(Guid? playerId) =>
        playerId is null
            ? "—"
            : _participants.FirstOrDefault(p => p.PlayerId == playerId)?.DisplayName ?? "unknown";

    /// <summary>
    /// Whose turn it is: the player in this match who has not submitted a result for the
    /// current game yet. Null when both have played.
    /// </summary>
    private Guid? NextToPlay()
    {
        if (_match is null)
        {
            return null;
        }

        int game = CurrentGame();

        foreach (Guid? seat in new[] { _match.Player1Id, _match.Player2Id })
        {
            if (seat is { } player && !_results.Any(r => r.PlayerId == player && r.GameIndex == game))
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>Which chart of a best-of series the match is on.</summary>
    private int CurrentGame()
    {
        if (_match is null || _results.Count == 0)
        {
            return 0;
        }

        int needed = _match.HasBothPlayers ? 2 : 1;

        for (int game = 0; game < Math.Max(1, _match.BestOf); game++)
        {
            if (_results.Count(r => r.GameIndex == game) < needed)
            {
                return game;
            }
        }

        return Math.Max(0, _match.BestOf - 1);
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        if (_match is null || _tournament is null)
        {
            return;
        }

        if (input.Pressed(Keys.Left))
        {
            _songCursor = Math.Max(0, _songCursor - 1);
        }
        else if (input.Pressed(Keys.Right))
        {
            _songCursor = Math.Min(Math.Max(0, _pool.Count - 1), _songCursor + 1);
        }
        else if (input.Pressed(Keys.Enter))
        {
            Advance();
        }
        else if (input.Pressed(Keys.C) && _results.Any(r => !r.IsConfirmed))
        {
            ConfirmAll();
        }
    }

    /// <summary>
    /// The one key that moves the match on, doing whatever the match needs next. An
    /// organiser running an evening should not have to remember which of five verbs applies
    /// at which moment.
    /// </summary>
    private void Advance()
    {
        try
        {
            _error = null;

            switch (_match!.Status)
            {
                case MatchStatus.Waiting or MatchStatus.Pending:
                    ChooseChart();
                    break;

                case MatchStatus.SongSelected:
                    Context.Tournaments.MarkReady(_matchId);
                    Refresh();
                    break;

                case MatchStatus.Ready or MatchStatus.Playing:
                    PlayNext();
                    break;

                case MatchStatus.ResultPending:
                    ConfirmAll();
                    break;

                case MatchStatus.Complete:
                    Manager.Pop();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("tournament match could not be advanced", ex);
            _error = ex.Message;
        }
    }

    private void ChooseChart()
    {
        if (_pool.Count == 0)
        {
            _error = "This tournament has no charts in its pool.";
            return;
        }

        TournamentSong chosen = _pool[Math.Clamp(_songCursor, 0, _pool.Count - 1)];
        Context.Tournaments.SelectSongs(_matchId, new[] { chosen.ChartKey });
        _status = $"Playing {chosen.Title}.";
        Refresh();
    }

    /// <summary>
    /// Hands the next player the chart, under the tournament's rules.
    /// </summary>
    private void PlayNext()
    {
        Guid? player = NextToPlay();
        if (player is null)
        {
            _status = "Both players have played. Press C to confirm the results.";
            return;
        }

        TournamentSong song = SongFor(CurrentGame());
        LibraryChart? chart = Context.Library.Charts.All()
            .FirstOrDefault(c => c.ChartKey == song.ChartKey);

        if (chart is null)
        {
            _error = $"\"{song.Title}\" is no longer in your library.";
            return;
        }

        // The chart is hashed by its contents, so a chart edited since the pool was built
        // no longer matches — and a tournament result set on a different chart than the one
        // everybody agreed on is not a result anybody can stand behind.
        if (song.ChartHash.Length > 0 && chart.ChartKey != song.ChartHash)
        {
            _error = $"\"{song.Title}\" has been edited since this tournament started.";
            return;
        }

        if (_match!.Status == MatchStatus.Ready)
        {
            Context.Tournaments.StartMatch(_matchId, player);
            Refresh();
        }

        Guid playing = player.Value;
        int game = CurrentGame();

        Manager.Push(new GameplayScreen(
            chart.ChartPath,
            chart.AudioPath,
            autoPlay: false,
            onComplete: result => Submit(playing, game, song, result),
            tournamentRules: _tournament!.Rules));
    }

    private TournamentSong SongFor(int game)
    {
        IReadOnlyList<string> chosen = _match!.SelectedChartKeys;

        string key = chosen.Count == 0
            ? _pool[0].ChartKey
            : chosen[Math.Min(game, chosen.Count - 1)];

        return _pool.FirstOrDefault(s => s.ChartKey == key) ?? _pool[0];
    }

    /// <summary>
    /// Records what the player just scored. Submitted, not accepted — the organiser
    /// confirms it, which is what stops a match being settled by whoever plays first.
    /// </summary>
    private void Submit(Guid playerId, int game, TournamentSong song, PlayResult result)
    {
        try
        {
            Context.Tournaments.SubmitResult(new TournamentMatchResult
            {
                Id = Guid.NewGuid(),
                MatchId = _matchId,
                PlayerId = playerId,
                ChartKey = song.ChartKey,
                GameIndex = game,
                Score = result.Score,
                Accuracy = result.Accuracy,
                MaxCombo = result.MaxCombo,
                Perfect = result.Perfect,
                Great = result.Great,
                Good = result.Good,
                Bad = result.Bad,
                Miss = result.Miss,
                FullCombo = result.FullCombo,
                AllPerfect = result.AllPerfect,
                ChartHash = song.ChartHash,
            });

            _status = $"{NameOf(playerId)}: {result.Score:N0} ({result.Accuracy:0.00}%)";
        }
        catch (Exception ex)
        {
            Log.Warn("a tournament result could not be recorded", ex);
            _error = ex.Message;
        }
        finally
        {
            Manager.Pop();
        }
    }

    private void ConfirmAll()
    {
        try
        {
            MatchOutcome outcome = default;

            foreach (TournamentMatchResult result in _results.Where(r => !r.IsConfirmed))
            {
                outcome = Context.Tournaments.ConfirmResult(result.Id, _matchId);
            }

            Refresh();

            _status = outcome.IsDecided
                ? $"{NameOf(outcome.WinnerPlayerId)} wins {outcome.WinnerGames}-{outcome.LoserGames}."
                : outcome.Reason;
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

        if (_match is null || _tournament is null)
        {
            ScreenChrome.Header(ui, column, "Match");
            ui.Text(ui.Body(Theme.Body), _error ?? "Loading…",
                new Rectangle(column.X, 160, column.Width, 24), Theme.Bad);
            ScreenChrome.FooterHint(ui, "Esc to go back");
            return;
        }

        int y = ScreenChrome.Header(ui, column, _tournament.Name);

        DrawPlayers(ui, column, y);
        y += 110;

        y = DrawChartPicker(ui, column, y);
        y = DrawResults(ui, column, y);

        DrawNextStep(ui, column, y);

        string? message = _error ?? _status;
        if (message is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), message,
                new Rectangle(column.X, column.Bottom - 26, column.Width, 20),
                _error is not null ? Theme.Bad : Theme.TextMuted);
        }

        ScreenChrome.FooterHint(ui,
            "Enter for the next step  ·  arrows to pick a chart  ·  C to confirm  ·  Esc to go back");
    }

    private void DrawPlayers(UiRenderer ui, Rectangle column, int y)
    {
        int half = column.Width / 2 - 30;

        DrawPlayer(ui, new Rectangle(column.X, y, half, 88), _match!.Player1Id);

        ui.Text(ui.Display(Theme.DisplayM), "vs",
            new Rectangle(column.X + half, y + 30, 60, 26), Theme.TextFaint, TextAlign.Center);

        DrawPlayer(ui, new Rectangle(column.Right - half, y, half, 88), _match.Player2Id);
    }

    private void DrawPlayer(UiRenderer ui, Rectangle box, Guid? playerId)
    {
        bool won = playerId is not null && playerId == _match!.WinnerPlayerId;
        bool theirTurn = playerId is not null && playerId == NextToPlay();

        ui.FillRect(box, Theme.Surface);
        ui.StrokeRect(box, won ? Theme.Perfect : theirTurn ? Theme.AccentBright : Theme.Border,
            theirTurn || won ? 2 : 1);

        ui.Text(ui.Display(Theme.DisplayM), NameOf(playerId),
            new Rectangle(box.X + 14, box.Y + 14, box.Width - 28, 28),
            won ? Theme.Perfect : Theme.Text);

        int game = CurrentGame();
        TournamentMatchResult? played = _results
            .FirstOrDefault(r => r.PlayerId == playerId && r.GameIndex == game);

        string line = won ? "WINNER"
            : played is not null ? $"{played.Score:N0}  ·  {played.Accuracy:0.00}%"
            : theirTurn ? "TO PLAY"
            : "waiting";

        ui.Text(ui.Mono(Theme.Label), line,
            new Rectangle(box.X + 14, box.Y + 50, box.Width - 28, 18),
            won ? Theme.Perfect : theirTurn ? Theme.AccentBright : Theme.TextFaint);
    }

    private int DrawChartPicker(UiRenderer ui, Rectangle column, int y)
    {
        ui.Text(ui.Mono(Theme.Label), "CHART",
            new Rectangle(column.X, y, column.Width, 16), Theme.Accent);

        if (_pool.Count == 0)
        {
            ui.Text(ui.Mono(Theme.Label), "This tournament has no charts.",
                new Rectangle(column.X, y + 22, column.Width, 18), Theme.Bad);
            return y + 56;
        }

        bool locked = _match!.SelectedChartKeys.Count > 0;

        TournamentSong shown = locked
            ? SongFor(CurrentGame())
            : _pool[Math.Clamp(_songCursor, 0, _pool.Count - 1)];

        ui.Text(ui.Display(Theme.DisplayM), shown.Title,
            new Rectangle(column.X, y + 20, column.Width - 200, 26),
            locked ? Theme.Text : Theme.AccentBright);

        ui.Text(ui.Mono(Theme.Label),
            $"{shown.DifficultyName}  {shown.Level:0.0}" + (locked ? "   (locked in)" : ""),
            new Rectangle(column.X, y + 48, column.Width - 200, 16), Theme.TextFaint);

        if (!locked && _pool.Count > 1)
        {
            ui.Text(ui.Mono(Theme.Label), $"{_songCursor + 1} / {_pool.Count}   < >",
                new Rectangle(column.Right - 200, y + 26, 200, 16), Theme.TextMuted, TextAlign.Right);
        }

        return y + 78;
    }

    private int DrawResults(UiRenderer ui, Rectangle column, int y)
    {
        if (_results.Count == 0)
        {
            return y;
        }

        ui.Text(ui.Mono(Theme.Label), "RESULTS",
            new Rectangle(column.X, y, column.Width, 16), Theme.Accent);

        int rowY = y + 22;

        foreach (TournamentMatchResult result in _results.OrderBy(r => r.GameIndex).Take(6))
        {
            var row = new Rectangle(column.X, rowY, column.Width, 22);

            ui.Text(ui.Mono(Theme.Label), $"G{result.GameIndex + 1}",
                new Rectangle(row.X, row.Y, 40, row.Height), Theme.TextFaint);

            ui.Text(ui.Mono(Theme.Label), NameOf(result.PlayerId),
                new Rectangle(row.X + 44, row.Y, 200, row.Height), Theme.TextMuted);

            ui.Text(ui.Mono(Theme.Label), result.Score.ToString("N0"),
                new Rectangle(row.X + 250, row.Y, 140, row.Height), Theme.Text, TextAlign.Right);

            ui.Text(ui.Mono(Theme.Label), $"{result.Accuracy:0.00}%",
                new Rectangle(row.X + 400, row.Y, 100, row.Height), Theme.TextMuted, TextAlign.Right);

            ui.Text(ui.Mono(Theme.Label), result.IsConfirmed ? "CONFIRMED" : "unconfirmed",
                new Rectangle(row.Right - 140, row.Y, 140, row.Height),
                result.IsConfirmed ? Theme.Good : Theme.TextFaint, TextAlign.Right);

            rowY += 24;
        }

        return rowY + 12;
    }

    private void DrawNextStep(UiRenderer ui, Rectangle column, int y)
    {
        string step = _match!.Status switch
        {
            MatchStatus.Pending or MatchStatus.Waiting => "Enter — lock in this chart",
            MatchStatus.SongSelected => "Enter — both players ready",
            MatchStatus.Ready or MatchStatus.Playing =>
                NextToPlay() is { } next ? $"Enter — {NameOf(next)} plays" : "C — confirm the results",
            MatchStatus.ResultPending => "C — confirm the results",
            MatchStatus.Complete => $"{NameOf(_match.WinnerPlayerId)} won. Enter to go back.",
            _ => "",
        };

        var button = new Rectangle(column.X, y, 380, 40);
        ui.FillRect(button, Theme.AccentSoft);
        ui.StrokeRect(button, Theme.Accent);
        ui.Text(ui.Body(Theme.Body), step, button, Theme.AccentBright, TextAlign.Center);
    }
}
