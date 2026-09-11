using Lumen.Core.Library;
using Lumen.Core.Scores;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Song Select (spec §35, §39, §62–63).
///
/// Three columns of information, in the order a player needs them: what they can play,
/// what they have already done on it, and who else has played it. The difficulty row is
/// always visible because the level is the number every other system in the game is built
/// on, and the local ranking is on the same screen rather than behind a menu — a board
/// nobody looks at does not create the competition the spec is asking for.
///
/// Selection survives leaving and coming back, so finishing a play returns the player to
/// the chart they just played rather than to the top of the list.
/// </summary>
public sealed class SongSelectScreen : Screen
{
    private const int RankingRows = 5;

    private IReadOnlyList<LibraryChart> _charts = Array.Empty<LibraryChart>();
    private IReadOnlyCollection<string> _favorites = Array.Empty<string>();
    private IReadOnlyDictionary<string, ChartStats> _stats = new Dictionary<string, ChartStats>();
    private IReadOnlyList<SongGroup> _groups = Array.Empty<SongGroup>();

    private IReadOnlyList<ChartRankingEntry> _ranking = Array.Empty<ChartRankingEntry>();
    private int? _selfRank;
    private string _rankedChartKey = "";

    private SongSort _sort = SongSort.Title;
    private string _search = "";
    private bool _favoritesOnly;

    private int _songIndex;
    private int _diffIndex;
    private int _scroll;
    private int _visibleRows = 10;

    // Remembered so the mouse hit-test uses exactly the rectangle that was drawn.
    private Rectangle _listArea = Rectangle.Empty;
    private Rectangle[] _difficultyChips = Array.Empty<Rectangle>();
    private string? _error;

    public override void OnEnter()
    {
        Rescan();
        Reload();
    }

    public override void OnReveal()
    {
        // A play may have set a new best, and the editor may have added a chart.
        Rescan();
        Reload();
    }

    // --- data ---

    private void Rescan()
    {
        try
        {
            Context.Library.Scan();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn("library scan failed", ex);
        }
    }

    private void Reload()
    {
        Guid player = Context.Session.ActivePlayerId;
        _charts = Context.Library.Charts.All();
        _favorites = Context.Library.Charts.Favorites(player);
        _stats = Context.Scores.GetChartStats(player);
        Refilter();
    }

    private void Refilter()
    {
        LibraryChart? previous = SelectedChart;

        var filter = new SongFilter
        {
            Search = _search,
            FavoritesOnly = _favoritesOnly,
        };

        _groups = LibraryQuery.Apply(_charts, filter, _sort, reversed: false, _favorites, _stats);

        // Keep the player on the chart they were looking at whenever it survived the
        // filter; a list that jumps to the top every keystroke is unusable for searching.
        if (previous is null || !TrySelect(previous.ChartKey))
        {
            SelectSong(0);
        }

        Clamp();
        LoadRanking();
    }

    private bool TrySelect(string chartKey)
    {
        for (int s = 0; s < _groups.Count; s++)
        {
            for (int d = 0; d < _groups[s].Charts.Count; d++)
            {
                if (_groups[s].Charts[d].ChartKey == chartKey)
                {
                    _songIndex = s;
                    _diffIndex = d;
                    return true;
                }
            }
        }

        return false;
    }

    private void LoadRanking()
    {
        LibraryChart? chart = SelectedChart;
        if (chart is null)
        {
            _ranking = Array.Empty<ChartRankingEntry>();
            _selfRank = null;
            _rankedChartKey = "";
            return;
        }

        if (chart.ChartKey == _rankedChartKey)
        {
            return;
        }

        Guid player = Context.Session.ActivePlayerId;
        _ranking = Context.Scores.GetChartRanking(chart.ChartKey, RankingRows, player);
        _selfRank = Context.Scores.GetChartRank(chart.ChartKey, player);
        _rankedChartKey = chart.ChartKey;
    }

    private SongGroup? SelectedSong =>
        _groups.Count == 0 ? null : _groups[Math.Clamp(_songIndex, 0, _groups.Count - 1)];

    private LibraryChart? SelectedChart
    {
        get
        {
            SongGroup? song = SelectedSong;
            if (song is null || song.Charts.Count == 0)
            {
                return null;
            }

            return song.Charts[Math.Clamp(_diffIndex, 0, song.Charts.Count - 1)];
        }
    }

    private ChartStats StatsFor(LibraryChart chart) =>
        _stats.TryGetValue(chart.ChartKey, out ChartStats? s) ? s : ChartStats.None;

    /// <summary>
    /// Which difficulty to land on when the player moves to a song: the hardest one they
    /// have actually played, falling back to the easiest. Landing on a chart they have a
    /// score on means the "your best" and ranking panels say something the moment they
    /// arrive, instead of showing "not played yet" for a song they have played for hours.
    /// </summary>
    private int PreferredDifficulty(SongGroup song)
    {
        int preferred = 0;
        for (int i = 0; i < song.Charts.Count; i++)
        {
            if (StatsFor(song.Charts[i]).Played)
            {
                preferred = i;
            }
        }

        return preferred;
    }

    /// <summary>Moves to a song and picks a sensible difficulty on it.</summary>
    private void SelectSong(int index)
    {
        _songIndex = index;
        Clamp();
        _diffIndex = SelectedSong is { } song ? PreferredDifficulty(song) : 0;
        Clamp();
    }

    private void Clamp()
    {
        _songIndex = _groups.Count == 0 ? 0 : Math.Clamp(_songIndex, 0, _groups.Count - 1);
        SongGroup? song = SelectedSong;
        int diffCount = song?.Charts.Count ?? 0;
        _diffIndex = diffCount == 0 ? 0 : Math.Clamp(_diffIndex, 0, diffCount - 1);

        // Keep the selection inside the scrolled viewport.
        if (_songIndex < _scroll)
        {
            _scroll = _songIndex;
        }
        else if (_songIndex >= _scroll + _visibleRows)
        {
            _scroll = _songIndex - _visibleRows + 1;
        }

        _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, _groups.Count - _visibleRows)));
    }

    // --- input ---

    public override void Update(InputFrame input)
    {
        HandleSearchTyping(input);

        if (input.Pressed(Keys.Escape))
        {
            if (_search.Length > 0)
            {
                _search = "";
                Refilter();
            }
            else
            {
                Manager.Pop();
            }

            return;
        }

        int before = _songIndex;

        if (input.Pressed(Keys.Down))
        {
            SelectSong(_songIndex + 1);
        }
        else if (input.Pressed(Keys.Up))
        {
            SelectSong(_songIndex - 1);
        }
        else if (input.Pressed(Keys.PageDown))
        {
            SelectSong(_songIndex + _visibleRows);
        }
        else if (input.Pressed(Keys.PageUp))
        {
            SelectSong(_songIndex - _visibleRows);
        }
        else if (input.Pressed(Keys.Right))
        {
            _diffIndex++;
        }
        else if (input.Pressed(Keys.Left))
        {
            _diffIndex--;
        }

        if (input.ScrollDelta != 0)
        {
            SelectSong(_songIndex - Math.Sign(input.ScrollDelta));
        }

        HandleMouse(input);

        // Search is live, so a printable key is only a shortcut while the box is empty;
        // once the player is typing, "f" is an f. Tab and the function keys are never
        // printable, so they work either way.
        bool searching = _search.Length > 0;

        if (input.Pressed(Keys.Tab))
        {
            CycleSort();
        }
        else if (input.Pressed(Keys.F3))
        {
            _favoritesOnly = !_favoritesOnly;
            Refilter();
        }
        else if (!searching && input.Pressed(Keys.F))
        {
            ToggleFavorite();
        }
        else if (!searching && input.Pressed(Keys.E))
        {
            EditSelected();
        }
        else if (input.Pressed(Keys.Enter) || (!searching && input.Pressed(Keys.Space)))
        {
            Play();
            return;
        }

        Clamp();
        if (_songIndex != before || SelectedChart?.ChartKey != _rankedChartKey)
        {
            LoadRanking();
        }
    }

    /// <summary>
    /// Search is always live: anything printable goes straight into the box with no mode
    /// to enter first, which is what makes finding a chart in a large library quick. The
    /// shortcuts that share a printable key stand down while the box has content.
    /// </summary>
    private void HandleSearchTyping(InputFrame input)
    {
        bool changed = false;

        if (input.Pressed(Keys.Back) && _search.Length > 0)
        {
            _search = _search[..^1];
            changed = true;
        }

        foreach (char c in input.TypedText)
        {
            // A leading space would be invisible and would swallow the play shortcut.
            if (c == ' ' && _search.Length == 0)
            {
                continue;
            }

            if (!char.IsControl(c))
            {
                _search += c;
                changed = true;
            }
        }

        if (changed)
        {
            Refilter();
        }
    }

    private void HandleMouse(InputFrame input)
    {
        if (!input.MouseClicked)
        {
            return;
        }

        Point mouse = input.MousePosition;

        for (int i = 0; i < _difficultyChips.Length; i++)
        {
            if (_difficultyChips[i].Contains(mouse))
            {
                _diffIndex = i;
                LoadRanking();
                return;
            }
        }

        if (_listArea.Contains(mouse))
        {
            int row = (mouse.Y - _listArea.Y) / RowHeight;
            int index = _scroll + row;
            if (index >= 0 && index < _groups.Count)
            {
                bool same = index == _songIndex;
                SelectSong(index);
                LoadRanking();

                // Click to select, click again to play — the same gesture as double-click
                // but forgiving of how fast the player does it.
                if (same)
                {
                    Play();
                }
            }
        }
    }

    private void CycleSort()
    {
        _sort = _sort switch
        {
            SongSort.Title => SongSort.Artist,
            SongSort.Artist => SongSort.Level,
            SongSort.Level => SongSort.Recent,
            SongSort.Recent => SongSort.BestPp,
            SongSort.BestPp => SongSort.Accuracy,
            _ => SongSort.Title,
        };
        Refilter();
    }

    private void ToggleFavorite()
    {
        if (SelectedChart is not { } chart)
        {
            return;
        }

        Guid player = Context.Session.ActivePlayerId;
        bool next = !_favorites.Contains(chart.ChartKey);
        Context.Library.Charts.SetFavorite(player, chart.ChartKey, next);
        _favorites = Context.Library.Charts.Favorites(player);

        if (_favoritesOnly)
        {
            Refilter();
        }
    }

    /// <summary>Opens the selected chart in the editor (spec §59: the library is editable).</summary>
    private void EditSelected()
    {
        if (SelectedChart is not { } chart)
        {
            return;
        }

        if (!File.Exists(chart.ChartPath))
        {
            _error = "That chart's file is missing. The library will forget it on the next scan.";
            return;
        }

        _error = null;
        Manager.Push(new Editor.EditorScreen(chart.ChartPath));
    }

    private void Play()
    {
        if (SelectedChart is not { } chart)
        {
            return;
        }

        if (!File.Exists(chart.ChartPath))
        {
            _error = "That chart's file is missing. The library will forget it on the next scan.";
            return;
        }

        if (chart.AudioPath.Length == 0 || !File.Exists(chart.AudioPath))
        {
            _error = $"The audio for this chart is missing ({chart.Meta.AudioFile}).";
            return;
        }

        _error = null;
        Manager.Push(new GameplayScreen(chart.ChartPath, chart.AudioPath));
    }

    // --- drawing ---

    private const int RowHeight = 46;

    public override void Draw(UiRenderer ui)
    {
        int margin = 48;
        int top = 74;
        var content = new Rectangle(margin, top, ui.Width - margin * 2, ui.Height - top - 64);

        DrawHeader(ui, content);

        int listWidth = (int)(content.Width * 0.52f);
        var list = new Rectangle(content.X, content.Y + 78, listWidth, content.Height - 78);
        var detail = new Rectangle(list.Right + 28, list.Y, content.Right - list.Right - 28, list.Height);

        _visibleRows = Math.Max(1, list.Height / RowHeight);
        _listArea = list;
        Clamp();

        DrawList(ui, list);
        DrawDetail(ui, detail);

        ScreenChrome.FooterHint(ui,
            _search.Length > 0
                ? "↑↓ song  ·  ←→ difficulty  ·  Enter play  ·  Tab sort  ·  Esc clears the search"
                : "↑↓ song  ·  ←→ difficulty  ·  Enter play  ·  E edit  ·  F favourite  ·  Tab sort  ·  type to search  ·  Esc back");
    }

    private void DrawHeader(UiRenderer ui, Rectangle content)
    {
        ui.Text(ui.Mono(Theme.Label), "SONG SELECT",
            new Rectangle(content.X, content.Y, 300, 20), Theme.Accent);

        string counts = $"{_groups.Count} song{(_groups.Count == 1 ? "" : "s")}  ·  {_charts.Count} charts";
        ui.Text(ui.Mono(Theme.Label), counts,
            new Rectangle(content.X, content.Y, content.Width, 20), Theme.TextFaint, TextAlign.Right);

        // Search box, always present so it never has to be discovered.
        var box = new Rectangle(content.X, content.Y + 30, Math.Min(420, content.Width / 2), 34);
        ui.FillRect(box, Theme.Surface);
        ui.StrokeRect(box, _search.Length > 0 ? Theme.Accent : Theme.Border);

        string shown = _search.Length > 0 ? _search : "type to search title, artist or charter";
        Color color = _search.Length > 0 ? Theme.Text : Theme.TextFaint;
        ui.Text(ui.Body(Theme.Body), shown,
            new Rectangle(box.X + 12, box.Y, box.Width - 24, box.Height), color);

        string mode = $"SORT {SortLabel(_sort)}" + (_favoritesOnly ? "   ·   FAVOURITES ONLY" : "");
        ui.Text(ui.Mono(Theme.Label), mode,
            new Rectangle(box.Right + 20, box.Y, content.Right - box.Right - 20, box.Height),
            Theme.TextMuted, TextAlign.Right);
    }

    private void DrawList(UiRenderer ui, Rectangle area)
    {
        if (_groups.Count == 0)
        {
            ui.Text(ui.Body(Theme.Body),
                _charts.Count == 0
                    ? "No charts yet. Make one in the editor, or drop a chart into the charts folder."
                    : "Nothing matches that search.",
                new Rectangle(area.X, area.Y + 40, area.Width, 24), Theme.TextFaint);
            return;
        }

        int y = area.Y;
        for (int i = _scroll; i < _groups.Count && y + RowHeight <= area.Bottom; i++)
        {
            SongGroup song = _groups[i];
            bool selected = i == _songIndex;
            var row = new Rectangle(area.X, y, area.Width, RowHeight);

            if (selected)
            {
                ui.FillRect(row, Theme.SurfaceRaised);
                ui.FillRect(new Rectangle(row.X, row.Y, 3, row.Height), Theme.Accent);
            }

            bool favorite = song.Charts.Any(c => _favorites.Contains(c.ChartKey));
            int textX = row.X + 16;

            ui.Text(ui.Body(Theme.Body), Truncate(song.Title, 34),
                new Rectangle(textX, row.Y + 6, row.Width - 150, 20),
                selected ? Theme.Text : Theme.TextMuted);

            ui.Text(ui.Mono(Theme.Label), Truncate(song.Artist, 40),
                new Rectangle(textX, row.Y + 24, row.Width - 150, 16), Theme.TextFaint);

            if (favorite)
            {
                ui.Text(ui.Mono(Theme.Label), "★",
                    new Rectangle(row.Right - 120, row.Y, 20, row.Height), Theme.Accent);
            }

            // Level range, so scanning the list tells you what you are in for.
            string levels = song.Charts.Count == 1
                ? DifficultyBands.Precise(song.MinLevel)
                : $"{DifficultyBands.Precise(song.MinLevel)}–{DifficultyBands.Precise(song.MaxLevel)}";
            ui.Text(ui.Mono(Theme.Mono), levels,
                new Rectangle(row.X, row.Y, row.Width - 16, row.Height),
                selected ? Theme.Accent : Theme.TextFaint, TextAlign.Right);

            y += RowHeight;
        }

        if (_groups.Count > _visibleRows)
        {
            DrawScrollbar(ui, area);
        }
    }

    private void DrawScrollbar(UiRenderer ui, Rectangle area)
    {
        int trackX = area.Right - 3;
        ui.FillRect(new Rectangle(trackX, area.Y, 2, area.Height), Theme.Border);

        float visible = (float)_visibleRows / _groups.Count;
        int thumbHeight = Math.Max(24, (int)(area.Height * visible));
        float progress = _groups.Count <= _visibleRows
            ? 0
            : (float)_scroll / (_groups.Count - _visibleRows);
        int thumbY = area.Y + (int)((area.Height - thumbHeight) * progress);
        ui.FillRect(new Rectangle(trackX, thumbY, 2, thumbHeight), Theme.BorderStrong);
    }

    private void DrawDetail(UiRenderer ui, Rectangle area)
    {
        SongGroup? song = SelectedSong;
        LibraryChart? chart = SelectedChart;
        if (song is null || chart is null)
        {
            _difficultyChips = Array.Empty<Rectangle>();
            return;
        }

        int y = area.Y;

        ui.Text(ui.Display(Theme.DisplayM), Truncate(song.Title, 30),
            new Rectangle(area.X, y, area.Width, 28), Theme.Text);
        y += 32;

        ui.Text(ui.Mono(Theme.Label),
            $"{Truncate(song.Artist, 28)}   ·   {FormatDuration(chart.DurationMs)}   ·   {chart.NoteCount:N0} notes",
            new Rectangle(area.X, y, area.Width, 18), Theme.TextFaint);
        y += 30;

        ui.FillRect(new Rectangle(area.X, y, area.Width, 1), Theme.Border);
        y += 20;

        y = DrawDifficulties(ui, area, y, song);
        y += 10;

        ui.Text(ui.Mono(Theme.Label),
            $"charted by {(chart.Meta.Creator.Length > 0 ? chart.Meta.Creator : "unknown")}" +
            (chart.Source == ChartSource.Imported ? "   ·   imported" : ""),
            new Rectangle(area.X, y, area.Width, 16), Theme.TextFaint);
        y += 28;

        y = DrawYourBest(ui, area, y, chart);
        y += 16;

        DrawRanking(ui, new Rectangle(area.X, y, area.Width, area.Bottom - y));

        if (_error is { Length: > 0 })
        {
            ui.Text(ui.Body(Theme.Label), _error,
                new Rectangle(area.X, area.Bottom - 22, area.Width, 18), Theme.Danger);
        }
    }

    private int DrawDifficulties(UiRenderer ui, Rectangle area, int y, SongGroup song)
    {
        var chips = new Rectangle[song.Charts.Count];
        int x = area.X;
        const int chipHeight = 44;

        for (int i = 0; i < song.Charts.Count; i++)
        {
            LibraryChart c = song.Charts[i];
            string name = c.Meta.DifficultyName.ToUpperInvariant();
            string level = DifficultyBands.Precise(c.Level);

            int width = Math.Max(96, (int)ui.Measure(ui.Mono(Theme.Label), name).X + 44);
            if (x + width > area.Right)
            {
                x = area.X;
                y += chipHeight + 8;
            }

            var chip = new Rectangle(x, y, width, chipHeight);
            chips[i] = chip;

            bool selected = i == _diffIndex;
            Color accent = BandColor(c.Level);

            ui.FillRect(chip, selected ? Theme.SurfaceRaised : Theme.Surface);
            ui.StrokeRect(chip, selected ? accent : Theme.Border);

            ui.Text(ui.Mono(Theme.Label), name,
                new Rectangle(chip.X, chip.Y + 6, chip.Width, 14), selected ? accent : Theme.TextFaint,
                TextAlign.Center);
            ui.Text(ui.Display(Theme.DisplayM), level,
                new Rectangle(chip.X, chip.Y + 18, chip.Width, 22),
                selected ? Theme.Text : Theme.TextMuted, TextAlign.Center);

            if (_favorites.Contains(c.ChartKey))
            {
                ui.Text(ui.Mono(Theme.Label), "★",
                    new Rectangle(chip.Right - 16, chip.Y + 4, 12, 12), Theme.Accent);
            }

            x += width + 8;
        }

        _difficultyChips = chips;
        return y + chipHeight;
    }

    private int DrawYourBest(UiRenderer ui, Rectangle area, int y, LibraryChart chart)
    {
        ui.Text(ui.Mono(Theme.Label), "YOUR BEST",
            new Rectangle(area.X, y, area.Width, 16), Theme.Accent);

        string who = Context.Session.ActiveProfile?.DisplayName ?? "";
        ui.Text(ui.Mono(Theme.Label), who,
            new Rectangle(area.X, y, area.Width, 16), Theme.TextFaint, TextAlign.Right);
        y += 24;

        ChartStats stats = StatsFor(chart);
        if (!stats.Played)
        {
            ui.Text(ui.Body(Theme.Label), "You have not played this chart yet.",
                new Rectangle(area.X, y, area.Width, 20), Theme.TextFaint);
            return y + 24;
        }

        (string Label, string Value)[] cells =
        {
            ("SCORE", stats.BestScore.ToString("N0")),
            ("ACCURACY", $"{stats.BestAccuracy:0.00}%"),
            ("PP", stats.BestPp.ToString("0")),
            ("PLAYS", stats.PlayCount.ToString("N0")),
        };

        int gap = 10;
        int cw = (area.Width - gap * (cells.Length - 1)) / cells.Length;
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new Rectangle(area.X + i * (cw + gap), y, cw, 52);
            ui.FillRect(cell, Theme.Surface);
            ui.StrokeRect(cell, Theme.Border);
            ui.Text(ui.Mono(Theme.Label), cells[i].Label,
                new Rectangle(cell.X + 10, cell.Y + 8, cell.Width - 20, 14), Theme.TextFaint);
            ui.Text(ui.Display(Theme.DisplayM), cells[i].Value,
                new Rectangle(cell.X + 10, cell.Y + 24, cell.Width - 20, 22), Theme.Text);
        }

        return y + 56;
    }

    private void DrawRanking(UiRenderer ui, Rectangle area)
    {
        ui.Text(ui.Mono(Theme.Label), "LOCAL RANKING",
            new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);

        if (_selfRank is { } rank)
        {
            ui.Text(ui.Mono(Theme.Label), $"YOU #{rank}",
                new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent, TextAlign.Right);
        }

        int y = area.Y + 24;

        if (_ranking.Count == 0)
        {
            ui.Text(ui.Body(Theme.Label), "No scores on this chart yet. Set the first one.",
                new Rectangle(area.X, y, area.Width, 20), Theme.TextFaint);
            return;
        }

        foreach (ChartRankingEntry entry in _ranking)
        {
            if (y + 26 > area.Bottom)
            {
                break;
            }

            var row = new Rectangle(area.X, y, area.Width, 24);
            if (entry.IsSelf)
            {
                ui.FillRect(row, Theme.AccentSoft);
            }

            Color name = entry.IsSelf ? Theme.Accent : Theme.TextMuted;
            ui.Text(ui.Mono(Theme.Label), $"#{entry.Rank}",
                new Rectangle(row.X + 8, row.Y, 36, row.Height), Theme.TextFaint);
            ui.Text(ui.Body(Theme.Label), Truncate(entry.PlayerName, 16),
                new Rectangle(row.X + 48, row.Y, row.Width - 220, row.Height), name);
            ui.Text(ui.Mono(Theme.Label), $"{entry.Accuracy:0.00}%",
                new Rectangle(row.X, row.Y, row.Width - 96, row.Height), Theme.TextFaint, TextAlign.Right);
            ui.Text(ui.Mono(Theme.Label), entry.Score.ToString("N0"),
                new Rectangle(row.X, row.Y, row.Width - 10, row.Height), Theme.Text, TextAlign.Right);

            y += 26;
        }

        // A player outside the visible top N still sees where they stand.
        if (_selfRank is { } selfRank && selfRank > _ranking.Count && y + 26 <= area.Bottom)
        {
            ui.Text(ui.Mono(Theme.Label), "…",
                new Rectangle(area.X + 8, y, area.Width, 20), Theme.TextFaint);
        }
    }

    // --- helpers ---

    private static Color BandColor(double level) => DifficultyBands.For(level) switch
    {
        DifficultyBand.Introductory => Theme.Good,
        DifficultyBand.Basic => Theme.Great,
        DifficultyBand.Advanced => Theme.Accent,
        DifficultyBand.Expert => Theme.Perfect,
        DifficultyBand.Master => Theme.Bad,
        _ => Theme.Miss,
    };

    private static string SortLabel(SongSort sort) => sort switch
    {
        SongSort.Artist => "ARTIST",
        SongSort.Level => "LEVEL",
        SongSort.Recent => "RECENT",
        SongSort.BestPp => "BEST PP",
        SongSort.Accuracy => "ACCURACY",
        _ => "TITLE",
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..Math.Max(1, max - 1)] + "…";

    private static string FormatDuration(double ms)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }
}
