using Lumen.Core.Gameplay;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Result screen (spec §36). Score / accuracy / combo / judgements, revealed in stages.
/// PP and the Rating change land in Phase 4.
/// </summary>
public sealed class ResultScreen : Screen
{
    private readonly PlayResult _result;
    private readonly Action _onDismiss;
    private double _time;

    public ResultScreen(PlayResult result, Action onDismiss)
    {
        _result = result;
        _onDismiss = onDismiss;
    }

    public override void Update(InputFrame input)
    {
        _time += input.DeltaSeconds;
        if (_time > 0.4 && (input.Pressed(Keys.Enter) || input.Pressed(Keys.Escape) || input.Pressed(Keys.Space)))
        {
            _onDismiss();
        }
    }

    public override void Draw(UiRenderer ui)
    {
        int cx = ui.Width / 2;
        int top = 70;

        float reveal = (float)Math.Clamp(_time / 0.25, 0, 1);

        ui.Text(ui.Mono(Theme.Label), _result.Chart.Title.ToUpperInvariant() + "   " + _result.Chart.DifficultyName,
            new Rectangle(0, top, ui.Width, 18), Theme.TextMuted, TextAlign.Center);

        // Accuracy + grade
        ui.Text(ui.Display(64), $"{_result.Accuracy:0.00}%",
            new Rectangle(0, top + 34, ui.Width, 72), Theme.Text, TextAlign.Center);
        ui.Text(ui.Display(40), _result.Grade,
            new Rectangle(0, top + 116, ui.Width, 48), GradeColor(_result.Grade), TextAlign.Center);

        ui.Text(ui.Mono(20), _result.Score.ToString("N0"),
            new Rectangle(0, top + 176, ui.Width, 28), Theme.Accent, TextAlign.Center);
        ui.Text(ui.Mono(Theme.Label), $"{_result.MaxCombo:N0} COMBO",
            new Rectangle(0, top + 208, ui.Width, 20), Theme.TextMuted, TextAlign.Center);

        // Judgement breakdown
        var rows = new (string label, int value, Color color)[]
        {
            ("PERFECT", _result.Perfect, Theme.Perfect),
            ("GREAT", _result.Great, Theme.Great),
            ("GOOD", _result.Good, Theme.Good),
            ("BAD", _result.Bad, Theme.Bad),
            ("MISS", _result.Miss, Theme.Miss),
        };

        int panelW = 320;
        int y = top + 260;
        for (int i = 0; i < rows.Length; i++)
        {
            if (reveal < (i + 1) / (float)rows.Length)
            {
                // staged reveal
            }

            var row = new Rectangle(cx - panelW / 2, y, panelW, 30);
            ui.Text(ui.Mono(Theme.Mono), rows[i].label, row, rows[i].color);
            ui.Text(ui.Mono(Theme.Mono), rows[i].value.ToString("N0"), row, Theme.Text, TextAlign.Right);
            y += 32;
        }

        y += 16;
        var badges = new List<(string, Color)>();
        if (_result.AllPerfect) badges.Add(("ALL PERFECT", Theme.Perfect));
        else if (_result.FullCombo) badges.Add(("FULL COMBO", Theme.Accent));
        foreach ((string text, Color c) in badges)
        {
            ui.Text(ui.Display(Theme.DisplayM), text, new Rectangle(0, y, ui.Width, 28), c, TextAlign.Center);
            y += 30;
        }

        ui.Text(ui.Mono(Theme.Label), "+PP and Rating change arrive in Phase 4",
            new Rectangle(0, y + 12, ui.Width, 16), Theme.TextFaint, TextAlign.Center);

        ScreenChrome.FooterHint(ui, "Enter to continue");
    }

    private static Color GradeColor(string grade) => grade switch
    {
        "PERFECT" => Theme.Perfect,
        "S+" or "S" => Theme.Accent,
        "A+" or "A" => Theme.Good,
        "B" => Theme.Great,
        _ => Theme.TextMuted,
    };
}
