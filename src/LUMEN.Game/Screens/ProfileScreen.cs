using Lumen.Core.Diagnostics;
using Lumen.Core.Profiles;
using Lumen.Core.Rating;
using Lumen.Core.Scores;
using Lumen.Game.Ui;
using Lumen.Game.Ui.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Profile overview and rename (spec §6, §7). Renaming changes only the display name;
/// the UUID and every stored score stay attached.
/// </summary>
public sealed class ProfileScreen : Screen
{
    private bool _renaming;
    private TextField _rename = new();
    private PlayerNameError _renameError = PlayerNameError.None;

    private ProfileSummary _summary = null!;
    private PlayerStatistics _stats = PlayerStatistics.Empty;
    private SkillAxes _skill = SkillAxes.Zero;
    private IReadOnlyList<BestPerformance> _best = System.Array.Empty<BestPerformance>();
    private IReadOnlyList<Lumen.Core.Achievements.AchievementState> _achievements =
        System.Array.Empty<Lumen.Core.Achievements.AchievementState>();
    private Lumen.Core.Achievements.AchievementStats _achievementStats =
        Lumen.Core.Achievements.AchievementStats.Empty;

    public override void OnEnter() => Load();

    public override void OnReveal() => Load();

    private void Load()
    {
        Profile profile = Context.Session.ActiveProfile!;
        _summary = Context.Scores.GetSummary(profile);
        _stats = Context.Scores.GetStatistics(profile.PlayerId);
        _skill = Context.Scores.GetSkillProfile(profile.PlayerId);
        _best = Context.Scores.GetBestPerformances(profile.PlayerId, 5);
        _achievements = Context.Achievements.All(profile.PlayerId);
        // Read once here, not once per locked achievement per frame.
        _achievementStats = Context.Achievements.StatsFor(profile.PlayerId);
    }

    public override void Update(InputFrame input)
    {
        if (_renaming)
        {
            UpdateRename(input);
            return;
        }

        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
        }
        else if (input.Pressed(Keys.Enter) || input.Pressed(Keys.F2))
        {
            BeginRename();
        }
    }

    private void BeginRename()
    {
        Profile profile = Context.Session.ActiveProfile!;
        _rename = new TextField { Value = profile.DisplayName, Focused = true };
        _renameError = PlayerNameError.None;
        _renaming = true;
    }

    private void UpdateRename(InputFrame input)
    {
        _rename.Update(input);

        if (input.Pressed(Keys.Escape))
        {
            _renaming = false;
            return;
        }

        if (input.Pressed(Keys.Enter))
        {
            PlayerNameResult result = PlayerName.Validate(_rename.Value);
            if (!result.IsValid)
            {
                _renameError = result.Error;
                return;
            }

            Guid id = Context.Session.ActivePlayerId;
            Context.Profiles.Rename(id, result.Normalized);
            Context.Session.Refresh();
            Load();
            Log.Info($"profile renamed to {result.Normalized} (id unchanged: {id})");
            _renaming = false;
        }
    }

    public override void Draw(UiRenderer ui)
    {
        Profile profile = Context.Session.ActiveProfile!;
        Rectangle column = ScreenChrome.Column(ui);
        int y = ScreenChrome.Header(ui, column, "Profile", profile.DisplayName);

        // Identity line
        ui.Text(ui.Mono(Theme.Label),
            $"id {Short(profile.PlayerId)}   ·   created {profile.CreatedUtc.ToLocalTime():yyyy-MM-dd}   ·   " +
            $"play time {FormatDuration(profile.TotalPlayTimeMs)}",
            new Rectangle(column.X, y, column.Width, 18), Theme.TextFaint);
        y += 40;

        DrawStats(ui, new Rectangle(column.X, y, column.Width, 132));
        y += 148;

        if (_stats.PlayCount == 0)
        {
            ui.Text(ui.Body(Theme.Label), "Stats begin after your first play.",
                new Rectangle(column.X, y, column.Width, 18), Theme.TextFaint);
            y += 34;
        }
        else
        {
            int half = (column.Width - 24) / 2;
            DrawBestPerformances(ui, new Rectangle(column.X, y, half, 132));
            DrawSkill(ui, new Rectangle(column.X + half + 24, y, half, 132));
            y += 140;
        }

        // The rename control is anchored to the bottom of the column and the achievements
        // take whatever is left above it, so neither can push the other off the screen
        // however tall the window is.
        int controlsTop = column.Bottom - 56;
        DrawAchievements(ui, new Rectangle(column.X, y, column.Width, Math.Max(0, controlsTop - y - 12)));
        y = controlsTop;

        if (_renaming)
        {
            DrawRename(ui, new Rectangle(column.X, y, Math.Min(420, column.Width), 46));
        }
        else
        {
            var button = new Rectangle(column.X, y, 220, 46);
            ui.FillRect(button, Theme.Surface);
            ui.StrokeRect(button, Theme.BorderStrong);
            ui.Text(ui.Display(Theme.DisplayM), "CHANGE NAME", button, Theme.Text, TextAlign.Center);
            ScreenChrome.FooterHint(ui, "Enter to change name  ·  Esc to go back");
        }
    }

    private void DrawStats(UiRenderer ui, Rectangle area)
    {
        bool played = _stats.PlayCount > 0;
        (string label, string value)[] cells =
        {
            ("RATING", played ? _summary.Rating.ToString("0.00") : "—"),
            ("TOTAL PP", _summary.TotalPp.ToString("N0")),
            ("BEST PP", _summary.BestPp.ToString("0")),
            ("ACCURACY", played ? $"{_stats.AverageAccuracy:0.00}%" : "—"),
            ("PLAY COUNT", _stats.PlayCount.ToString("N0")),
            ("FULL COMBOS", _stats.FullCombos.ToString("N0")),
        };

        const int cols = 3;
        int gap = 12;
        int cw = (area.Width - gap * (cols - 1)) / cols;
        int ch = (area.Height - gap) / 2;

        for (int i = 0; i < cells.Length; i++)
        {
            int r = i / cols;
            int c = i % cols;
            var cell = new Rectangle(area.X + c * (cw + gap), area.Y + r * (ch + gap), cw, ch);

            ui.FillRect(cell, Theme.Surface);
            ui.StrokeRect(cell, Theme.Border);
            ui.Text(ui.Mono(Theme.Label), cells[i].label,
                new Rectangle(cell.X + 14, cell.Y + 12, cell.Width - 28, 16), Theme.TextFaint);
            ui.Text(ui.Display(Theme.DisplayM), cells[i].value,
                new Rectangle(cell.X + 14, cell.Y + 30, cell.Width - 28, 30), Theme.Text);
        }
    }

    private void DrawBestPerformances(UiRenderer ui, Rectangle area)
    {
        ui.Text(ui.Mono(Theme.Label), "BEST PERFORMANCES",
            new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);
        int y = area.Y + 24;

        int rank = 1;
        foreach (BestPerformance b in _best)
        {
            var row = new Rectangle(area.X, y, area.Width, 24);
            ui.Text(ui.Mono(Theme.Label), $"#{rank}", row, Theme.TextFaint);
            ui.Text(ui.Body(Theme.Label), Truncate(b.Chart.Title, 18),
                new Rectangle(area.X + 28, y, area.Width - 90, 24), Theme.Text);
            ui.Text(ui.Mono(Theme.Label), $"{b.Pp:0}pp", row, Theme.Accent, TextAlign.Right);
            y += 22;
            rank++;
        }
    }

    private void DrawSkill(UiRenderer ui, Rectangle area)
    {
        ui.Text(ui.Mono(Theme.Label), "SKILL", new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);
        int y = area.Y + 24;

        (string label, double value)[] axes =
        {
            ("Speed", _skill.Speed),
            ("Technical", _skill.Technical),
            ("Reading", _skill.Reading),
            ("Stamina", _skill.Stamina),
            ("Accuracy", _skill.Accuracy),
        };

        foreach ((string label, double value) in axes)
        {
            ui.Text(ui.Body(Theme.Label), label, new Rectangle(area.X, y, 90, 20), Theme.TextMuted);
            int barX = area.X + 96;
            int barW = area.Width - 140;
            ui.FillRect(new Rectangle(barX, y + 8, barW, 3), Theme.Border);
            ui.FillRect(new Rectangle(barX, y + 8, (int)(barW * Math.Clamp(value / 20.0, 0, 1)), 3), Theme.Accent);
            ui.Text(ui.Mono(Theme.Label), value.ToString("0.0"),
                new Rectangle(area.X, y, area.Width, 20), Theme.Text, TextAlign.Right);
            y += 22;
        }
    }

    /// <summary>
    /// Unlocked achievements first, then whatever the player is closest to earning — the
    /// list is there to suggest what to do next, not to be an alphabet (spec §64).
    /// </summary>
    private void DrawAchievements(UiRenderer ui, Rectangle area)
    {
        int unlocked = _achievements.Count(a => a.Unlocked);
        ui.Text(ui.Mono(Theme.Label), $"ACHIEVEMENTS   {unlocked} / {_achievements.Count}",
            new Rectangle(area.X, area.Y, area.Width, 16), Theme.Accent);

        int y = area.Y + 24;
        int half = (area.Width - 24) / 2;

        int rows = Math.Max(0, (area.Height - 24) / 26);
        int shown = Math.Min(rows * 2, _achievements.Count);

        for (int i = 0; i < shown; i++)
        {
            Lumen.Core.Achievements.AchievementState a = _achievements[i];
            int column = i % 2;
            int rowY = y + i / 2 * 26;
            var row = new Rectangle(area.X + column * (half + 24), rowY, half, 22);

            ui.Text(ui.Body(Theme.Label), Truncate(a.Definition.Name, 20),
                new Rectangle(row.X, row.Y, row.Width - 70, row.Height),
                a.Unlocked ? Theme.Text : Theme.TextFaint);

            if (a.Unlocked)
            {
                ui.Text(ui.Mono(Theme.Label), "UNLOCKED",
                    row, Theme.Accent, TextAlign.Right);
            }
            else
            {
                // Progress rather than a blank: a locked achievement should say how close.
                double fraction = a.Definition.Fraction(_achievementStats);
                ui.FillRect(new Rectangle(row.Right - 64, row.Y + 10, 60, 3), Theme.Border);
                ui.FillRect(new Rectangle(row.Right - 64, row.Y + 10, (int)(60 * fraction), 3),
                    Theme.AccentSoft);
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void DrawRename(UiRenderer ui, Rectangle box)
    {
        ui.Text(ui.Mono(Theme.Label), "NEW NAME",
            new Rectangle(box.X, box.Y - 24, box.Width, 16), Theme.TextMuted);
        _rename.Draw(ui, box);

        if (_renameError != PlayerNameError.None)
        {
            string msg = new PlayerNameResult(false, _rename.Value, _renameError).Message;
            ui.Text(ui.Body(Theme.Label), msg,
                new Rectangle(box.X, box.Bottom + 8, box.Width, 16), Theme.Danger);
        }

        ScreenChrome.FooterHint(ui, "Enter to save  ·  Esc to cancel  ·  your scores stay attached");
    }

    private static string Short(Guid id) => id.ToString("D")[..8];

    private static string FormatDuration(long ms)
    {
        var span = TimeSpan.FromMilliseconds(ms);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";
    }
}
