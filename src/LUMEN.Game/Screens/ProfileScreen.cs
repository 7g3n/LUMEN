using Lumen.Core.Diagnostics;
using Lumen.Core.Profiles;
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

        DrawStats(ui, new Rectangle(column.X, y, column.Width, 160));
        y += 184;

        ui.Text(ui.Body(Theme.Label), "Stats begin after your first play.",
            new Rectangle(column.X, y, column.Width, 18), Theme.TextFaint);
        y += 40;

        if (_renaming)
        {
            DrawRename(ui, new Rectangle(column.X, y, Math.Min(420, column.Width), 56));
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
        (string label, string value)[] cells =
        {
            ("RATING", "—"),
            ("TOTAL PP", "0"),
            ("BEST PP", "0"),
            ("ACCURACY", "—"),
            ("PLAY COUNT", "0"),
            ("FULL COMBOS", "0"),
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
