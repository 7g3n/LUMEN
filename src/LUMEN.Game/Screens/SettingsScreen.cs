using System.Diagnostics;
using System.IO;
using System.Linq;
using Lumen.Data.Backup;
using Lumen.Core.Diagnostics;
using Lumen.Core.Settings;
using Lumen.Game.Config;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Settings (spec §65). Per-profile values persist through <see cref="ISettingsRepository"/>;
/// display values persist through <see cref="DisplayConfig"/>. Changes save immediately.
/// Key bindings and the calibration screen arrive in Phase 3 / Phase 10.
/// </summary>
public sealed class SettingsScreen : Screen
{
    private static readonly int[] FpsCapChoices = { -1, 60, 120, 144, 240, 0 };
    private static readonly string[] FpsCapLabels = { "Follow refresh", "60", "120", "144", "240", "Uncapped" };

    private readonly List<SettingRow> _rows = new();
    private int _selected;
    private float _scroll;

    public override void OnEnter() => BuildRows();

    private void BuildRows()
    {
        _rows.Clear();
        ISettingsRepository s = Context.Settings;
        Guid id = Context.Session.ActivePlayerId;
        DisplayConfig d = Context.Display;

        SliderRow Volume(string label, string key, double def) => new()
        {
            Label = label,
            Get = () => s.GetDouble(id, key, def),
            Set = v => s.SetDouble(id, key, v),
            Min = 0, Max = 100, Step = 5,
            Format = v => $"{v:0}%",
        };

        _rows.Add(new SectionRow { Label = "Audio" });
        _rows.Add(Volume("Master Volume", SettingKeys.MasterVolume, 80));
        _rows.Add(Volume("Music Volume", SettingKeys.MusicVolume, 100));
        _rows.Add(Volume("SE Volume", SettingKeys.SeVolume, 100));

        _rows.Add(new SectionRow { Label = "Gameplay" });
        _rows.Add(new SliderRow
        {
            Label = "Note Speed",
            Get = () => s.GetDouble(id, SettingKeys.NoteSpeed, 6.0),
            Set = v => s.SetDouble(id, SettingKeys.NoteSpeed, v),
            Min = 1.0, Max = 12.0, Step = 0.5,
            Format = v => v.ToString("0.0"),
        });
        _rows.Add(Volume("Background Dim", SettingKeys.BackgroundDim, 60));
        _rows.Add(Volume("Effect Intensity", SettingKeys.EffectIntensity, 100));

        _rows.Add(new SectionRow { Label = "Timing" });
        _rows.Add(OffsetRow("Input Offset", SettingKeys.InputOffsetMs, s, id));
        _rows.Add(OffsetRow("Audio Offset", SettingKeys.AudioOffsetMs, s, id));

        _rows.Add(new SectionRow { Label = "Controls" });
        // The bindings are real and stored per profile; the screen to change them from
        // here arrives in Phase 10, so show what they currently are rather than a
        // placeholder that reads like the feature is missing.
        _rows.Add(new InfoRow
        {
            Label = "Key Bindings",
            Value = string.Join(" ", Input.KeyBindings
                .Load(Context.Settings, Context.Session.ActivePlayerId).Lanes),
        });

        _rows.Add(new SectionRow { Label = "Display" });
        _rows.Add(new ToggleRow
        {
            Label = "Fullscreen",
            Get = () => d.Fullscreen,
            Set = v => { d.Fullscreen = v; d.Save(Context.Paths.Settings); },
            Note = "restart to apply",
        });
        _rows.Add(new ToggleRow
        {
            Label = "VSync",
            Get = () => d.Vsync,
            Set = v => { d.Vsync = v; d.Save(Context.Paths.Settings); },
            Note = "restart to apply",
        });
        _rows.Add(new ChoiceRow
        {
            Label = "FPS Cap",
            Options = FpsCapLabels,
            GetIndex = () => Math.Max(0, Array.IndexOf(FpsCapChoices, d.FpsCap)),
            SetIndex = i => { d.FpsCap = FpsCapChoices[i]; d.Save(Context.Paths.Settings); },
            Note = "restart to apply",
        });
        _rows.Add(new ToggleRow
        {
            Label = "Performance Overlay",
            Get = () => d.ShowPerfOverlay,
            Set = v => { d.ShowPerfOverlay = v; d.Save(Context.Paths.Settings); },
        });

        _rows.Add(new SectionRow { Label = "Data" });
        _rows.Add(new ActionRow
        {
            Label = "Create Backup",
            ActionText = "BACK UP",
            OnActivate = CreateBackup,
        });
        _rows.Add(new ActionRow
        {
            Label = "Restore Backup",
            ActionText = LatestBackupLabel(),
            OnActivate = RestoreLatestBackup,
        });
        _rows.Add(new ActionRow
        {
            Label = "Export Data",
            ActionText = "EXPORT",
            OnActivate = ExportData,
        });
        _rows.Add(new ActionRow
        {
            Label = "Import Data",
            ActionText = ImportableLabel(),
            OnActivate = ImportData,
        });
        _rows.Add(new ActionRow { Label = "Open Data Folder", OnActivate = OpenDataFolder });
        _rows.Add(new ActionRow
        {
            Label = "Open Backups Folder",
            OnActivate = () => OpenFolder(Context.Paths.Backups),
        });
        _rows.Add(new ActionRow
        {
            Label = "Open Charts Folder",
            OnActivate = () => OpenFolder(Path.GetDirectoryName(Context.Paths.ChartsLocal)!),
        });

        if (_dataMessage is { Length: > 0 })
        {
            _rows.Add(new InfoRow { Label = "", Value = _dataMessage });
        }

        _selected = FirstInteractive(0, 1);
    }

    private static SliderRow OffsetRow(string label, string key, ISettingsRepository s, Guid id) => new()
    {
        Label = label,
        Get = () => s.GetDouble(id, key, 0),
        Set = v => s.SetDouble(id, key, v),
        Min = -100, Max = 100, Step = 1,
        Format = v => $"{(v > 0 ? "+" : "")}{v:0} ms",
    };

    private string? _dataMessage;

    /// <summary>
    /// Backup and restore act on the newest file in the backups folder rather than through
    /// a file picker: the toolkit has no dialog yet, and "restore the most recent backup"
    /// is the action a player actually wants nine times out of ten. Anything else is one
    /// drag away — export writes to the exports folder, and import reads from it.
    /// </summary>
    private string LatestBackupLabel()
    {
        BackupService.BackupInfo? latest = Context.Backups.List().FirstOrDefault();
        return latest is null ? "NONE YET" : latest.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm");
    }

    private string ImportableLabel()
    {
        return Importable() is { } file ? Path.GetFileName(file) : "NONE FOUND";
    }

    /// <summary>The newest backup sitting in the exports folder, if any.</summary>
    private string? Importable()
    {
        if (!Directory.Exists(Context.Paths.Exports))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(Context.Paths.Exports, $"*.{Core.GameIdentity.BackupExtension}")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void CreateBackup()
    {
        Run(() =>
        {
            BackupService.BackupInfo info = Context.Backups.Create();
            return $"Saved {info.FileName} ({info.SizeBytes / 1024:N0} KB).";
        });
    }

    private void RestoreLatestBackup()
    {
        BackupService.BackupInfo? latest = Context.Backups.List().FirstOrDefault();
        if (latest is null)
        {
            _dataMessage = "There are no backups yet.";
            BuildRows();
            return;
        }

        Run(() =>
        {
            BackupService.RestoreResult result = Context.Backups.Restore(latest.Path);
            Context.Session.Refresh();
            return $"Restored {result.Rows:N0} rows and {result.FilesRestored} files " +
                   $"from {latest.FileName}.";
        });
    }

    private void ExportData()
    {
        Run(() =>
        {
            Directory.CreateDirectory(Context.Paths.Exports);
            string path = Path.Combine(
                Context.Paths.Exports, BackupService.FileNameFor(DateTime.Now));

            BackupService.BackupInfo info = Context.Backups.Create(path);
            return $"Exported {info.FileName} to the exports folder.";
        });
    }

    private void ImportData()
    {
        if (Importable() is not { } file)
        {
            _dataMessage =
                $"Put a .{Core.GameIdentity.BackupExtension} file in the exports folder first.";
            BuildRows();
            return;
        }

        Run(() =>
        {
            BackupService.RestoreResult result = Context.Backups.Restore(file);
            Context.Session.Refresh();
            return $"Imported {result.Rows:N0} rows and {result.FilesRestored} files " +
                   $"from {Path.GetFileName(file)}.";
        });
    }

    /// <summary>
    /// Runs a data action and turns whatever happens into one line the player can read.
    /// These touch every file the game owns, so none of them may take the game down.
    /// </summary>
    private void Run(Func<string> action)
    {
        try
        {
            _dataMessage = action();
        }
        catch (Exception ex)
        {
            Log.Error("data action failed", ex);
            _dataMessage = ex.Message;
        }

        BuildRows();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"could not open {path}", ex);
            _dataMessage = "That folder could not be opened.";
            BuildRows();
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Context.Paths.EnsureCreated();
            Process.Start(new ProcessStartInfo(Context.Paths.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("could not open data folder", ex);
        }
    }

    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape))
        {
            Manager.Pop();
            return;
        }

        if (input.Pressed(Keys.Down) || input.Pressed(Keys.S))
        {
            _selected = FirstInteractive(_selected + 1, 1);
        }
        else if (input.Pressed(Keys.Up) || input.Pressed(Keys.W))
        {
            _selected = FirstInteractive(_selected - 1, -1);
        }

        SettingRow row = _rows[_selected];
        if (input.Pressed(Keys.Right) || input.Pressed(Keys.D))
        {
            row.Adjust(1);
        }
        else if (input.Pressed(Keys.Left) || input.Pressed(Keys.A))
        {
            row.Adjust(-1);
        }
        else if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space))
        {
            row.Activate();
        }
    }

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 72, bottom: 56);
        int headerBottom = ScreenChrome.Header(ui, column, "Settings");

        var viewport = new Rectangle(column.X, headerBottom, column.Width, column.Bottom - headerBottom);

        const int rowH = 40;
        int selectedY = RowYof(_selected, rowH);
        float targetScroll = Math.Max(0, selectedY - viewport.Height / 2f);
        _scroll += (targetScroll - _scroll) * 0.2f;

        for (int i = 0; i < _rows.Count; i++)
        {
            int y = viewport.Y + RowYof(i, rowH) - (int)_scroll;
            var rowRect = new Rectangle(viewport.X, y, viewport.Width, rowH);
            if (rowRect.Bottom < viewport.Y || rowRect.Y > viewport.Bottom)
            {
                continue;
            }

            DrawRow(ui, _rows[i], rowRect, i == _selected);
        }

        ScreenChrome.FooterHint(ui, "arrows to navigate  ·  ←/→ to change  ·  Esc to go back");
    }

    private static int RowYof(int index, int rowH) => index * rowH;

    private void DrawRow(UiRenderer ui, SettingRow row, Rectangle rect, bool selected)
    {
        if (row is SectionRow)
        {
            ui.Text(ui.Mono(Theme.Label), row.Label.ToUpperInvariant(),
                new Rectangle(rect.X, rect.Y + 10, rect.Width, 18), Theme.Accent);
            ui.FillRect(new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 1), Theme.Border);
            return;
        }

        if (selected)
        {
            ui.FillRect(rect, Theme.Surface);
            ui.FillRect(new Rectangle(rect.X - 14, rect.Y + 6, 3, rect.Height - 12), Theme.Accent);
        }

        Color labelColor = row.Interactive ? Theme.Text : Theme.TextFaint;
        ui.Text(ui.Body(Theme.Body), row.Label,
            new Rectangle(rect.X + 6, rect.Y, rect.Width / 2, rect.Height), labelColor);

        var valueArea = new Rectangle(rect.X + rect.Width / 2, rect.Y, rect.Width / 2 - 6, rect.Height);

        if (row is SliderRow slider)
        {
            int trackW = 120;
            var track = new Rectangle(valueArea.Right - trackW - 70, rect.Y + rect.Height / 2 - 2, trackW, 4);
            ui.FillRect(track, Theme.Border);
            ui.FillRect(new Rectangle(track.X, track.Y, (int)(track.Width * Math.Clamp(slider.Fraction, 0, 1)), 4), Theme.Accent);
            ui.Text(ui.Mono(Theme.Mono), row.ValueText,
                new Rectangle(valueArea.Right - 66, rect.Y, 66, rect.Height), Theme.Text, TextAlign.Right);
        }
        else
        {
            Color vc = row is ActionRow || row is ChoiceRow ? Theme.Accent
                : row is ToggleRow t && t.Get() ? Theme.Accent
                : Theme.TextMuted;
            ui.Text(ui.Mono(Theme.Mono), row.ValueText, valueArea, vc, TextAlign.Right);
        }

        string? note = row switch
        {
            ToggleRow tr => tr.Note,
            ChoiceRow cr => cr.Note,
            _ => null,
        };
        if (selected && note is { Length: > 0 })
        {
            ui.Text(ui.Mono(Theme.Label), note,
                new Rectangle(rect.X + 6, rect.Bottom - 14, rect.Width, 12), Theme.TextFaint);
        }
    }

    private int FirstInteractive(int start, int direction)
    {
        for (int step = 0; step < _rows.Count; step++)
        {
            int i = ((start + direction * step) % _rows.Count + _rows.Count) % _rows.Count;
            if (_rows[i].Interactive)
            {
                return i;
            }
        }

        return Math.Clamp(start, 0, _rows.Count - 1);
    }
}
