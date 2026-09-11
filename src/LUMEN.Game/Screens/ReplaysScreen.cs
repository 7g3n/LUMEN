using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Core.Library;
using Lumen.Core.Replays;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// The replay list (spec §41).
///
/// Every finished play is here without having been asked for, because a replay the player
/// had to arrange in advance is never there for the run that turned out to matter. Watching
/// one feeds the recorded input back through the ordinary gameplay screen — nothing is
/// scored, and nothing about the play is re-derived.
/// </summary>
public sealed class ReplaysScreen : Screen
{
    private const int RowHeight = 44;

    private IReadOnlyList<ReplaySummary> _replays = Array.Empty<ReplaySummary>();
    private int _index;
    private int _scroll;
    private int _visibleRows = 10;
    private string? _error;
    private Rectangle _listArea = Rectangle.Empty;

    public override void OnEnter() => Load();

    public override void OnReveal() => Load();

    private void Load()
    {
        _replays = Context.Replays.ListForPlayer(Context.Session.ActivePlayerId, 200);
        Clamp();
    }

    private ReplaySummary? Selected =>
        _replays.Count == 0 ? null : _replays[Math.Clamp(_index, 0, _replays.Count - 1)];

    private void Clamp()
    {
        _index = _replays.Count == 0 ? 0 : Math.Clamp(_index, 0, _replays.Count - 1);

        if (_index < _scroll)
        {
            _scroll = _index;
        }
        else if (_index >= _scroll + _visibleRows)
        {
            _scroll = _index - _visibleRows + 1;
        }

        _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, _replays.Count - _visibleRows)));
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        if (input.Pressed(Keys.Down))
        {
            _index++;
        }
        else if (input.Pressed(Keys.Up))
        {
            _index--;
        }
        else if (input.Pressed(Keys.PageDown))
        {
            _index += _visibleRows;
        }
        else if (input.Pressed(Keys.PageUp))
        {
            _index -= _visibleRows;
        }

        if (input.ScrollDelta != 0)
        {
            _index -= Math.Sign(input.ScrollDelta);
        }

        if (input.MouseClicked && _listArea.Contains(input.MousePosition))
        {
            int row = (input.MousePosition.Y - _listArea.Y) / RowHeight;
            int target = _scroll + row;
            if (target >= 0 && target < _replays.Count)
            {
                bool again = target == _index;
                _index = target;
                if (again)
                {
                    Watch();
                }
            }
        }

        if (input.Pressed(Keys.Enter))
        {
            Watch();
        }
        else if (input.Pressed(Keys.Delete))
        {
            DeleteSelected();
        }

        Clamp();
    }

    private void Watch()
    {
        if (Selected is not { } summary)
        {
            return;
        }

        Replay? replay = Context.Replays.Load(summary.ReplayId);
        if (replay is null)
        {
            _error = "That replay's data file is missing.";
            Load();
            return;
        }

        // The chart has to still be in the library, and be the same chart: the chart key
        // folds in the note count, so an edited chart no longer matches and the recorded
        // input would land on notes that are not there any more.
        LibraryChart? chart = Context.Library.Charts.All()
            .FirstOrDefault(c => c.ChartKey == replay.ChartKey);

        if (chart is null)
        {
            _error = "The chart this replay was recorded on is no longer in your library.";
            return;
        }

        if (!File.Exists(chart.AudioPath))
        {
            _error = $"The audio for this chart is missing ({chart.Meta.AudioFile}).";
            return;
        }

        _error = null;
        Manager.Push(new GameplayScreen(chart.ChartPath, chart.AudioPath, watch: replay));
    }

    private void DeleteSelected()
    {
        if (Selected is not { } summary)
        {
            return;
        }

        try
        {
            Context.Replays.Delete(summary.ReplayId);
            Load();
        }
        catch (Exception ex)
        {
            Log.Warn("replay could not be deleted", ex);
            _error = ex.Message;
        }
    }

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 84);
        int y = ScreenChrome.Header(ui, column, "Replays",
            _replays.Count == 0
                ? null
                : $"{_replays.Count} recorded");

        if (_replays.Count == 0)
        {
            ui.Text(ui.Body(Theme.Body),
                "Nothing recorded yet. Every play you finish is saved here automatically.",
                new Rectangle(column.X, y + 20, column.Width, 24), Theme.TextFaint);
            ScreenChrome.FooterHint(ui, "Esc to go back");
            return;
        }

        _listArea = new Rectangle(column.X, y, column.Width, column.Bottom - y - 30);
        _visibleRows = Math.Max(1, _listArea.Height / RowHeight);
        Clamp();

        int rowY = _listArea.Y;
        for (int i = _scroll; i < _replays.Count && rowY + RowHeight <= _listArea.Bottom; i++)
        {
            ReplaySummary replay = _replays[i];
            bool selected = i == _index;
            var row = new Rectangle(_listArea.X, rowY, _listArea.Width, RowHeight);

            if (selected)
            {
                ui.FillRect(row, Theme.SurfaceRaised);
                ui.FillRect(new Rectangle(row.X, row.Y, 3, row.Height), Theme.Accent);
            }

            const int GradeWidth = 96;
            const int ScoreWidth = 92;
            const int AccuracyWidth = 76;
            int textWidth = row.Width - (GradeWidth + ScoreWidth + AccuracyWidth + 40);

            ui.Text(ui.Body(Theme.Body), Truncate(replay.Title, 26),
                new Rectangle(row.X + 14, row.Y + 5, textWidth, 20),
                selected ? Theme.Text : Theme.TextMuted);

            ui.Text(ui.Mono(Theme.Label),
                $"{replay.DifficultyName} {replay.DifficultyLevel:0.0}   ·   " +
                $"{replay.RecordedUtc.ToLocalTime():yyyy-MM-dd HH:mm}   ·   " +
                $"{replay.EventCount:N0} inputs",
                new Rectangle(row.X + 14, row.Y + 24, textWidth, 16), Theme.TextFaint);

            int gradeX = row.Right - GradeWidth - 14;
            int scoreX = gradeX - ScoreWidth;
            int accuracyX = scoreX - AccuracyWidth;

            ui.Text(ui.Mono(Theme.Mono), $"{replay.Accuracy:0.00}%",
                new Rectangle(accuracyX, row.Y, AccuracyWidth, row.Height),
                Theme.TextMuted, TextAlign.Right);

            ui.Text(ui.Mono(Theme.Mono), replay.Score.ToString("N0"),
                new Rectangle(scoreX, row.Y, ScoreWidth, row.Height),
                Theme.Text, TextAlign.Right);

            ui.Text(ui.Display(Theme.DisplayM), replay.Grade,
                new Rectangle(gradeX, row.Y, GradeWidth, row.Height),
                replay.FullCombo ? Theme.Accent : Theme.TextMuted, TextAlign.Right);

            rowY += RowHeight;
        }

        if (_error is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _error,
                new Rectangle(column.X, column.Bottom - 24, column.Width, 18), Theme.Danger);
        }

        ScreenChrome.FooterHint(ui, "↑↓ select  ·  Enter watch  ·  Delete remove  ·  Esc back");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..Math.Max(1, max - 1)] + "…";
}
