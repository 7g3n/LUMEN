using Lumen.Core.Gameplay;
using Lumen.Core.Scores;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Result screen (spec §36–38). Accuracy / grade / score / judgements, then the PP
/// earned and the Rating change, revealed and counted up in stages. Record badges per §37.
/// </summary>
public sealed class ResultScreen : Screen
{
    private readonly PlayResult _result;
    private readonly ScoreSaveOutcome? _outcome;
    private readonly Action _onDismiss;
    private double _time;

    public ResultScreen(PlayResult result, ScoreSaveOutcome? outcome, Action onDismiss)
    {
        _result = result;
        _outcome = outcome;
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
        int top = 56;

        ui.Text(ui.Mono(Theme.Label), $"{_result.Chart.Title.ToUpperInvariant()}   {_result.Chart.DifficultyName}",
            new Rectangle(0, top, ui.Width, 18), Theme.TextMuted, TextAlign.Center);

        ui.Text(ui.Display(60), $"{_result.Accuracy:0.00}%",
            new Rectangle(0, top + 30, ui.Width, 68), Theme.Text, TextAlign.Center);
        ui.Text(ui.Display(36), _result.Grade,
            new Rectangle(0, top + 106, ui.Width, 44), GradeColor(_result.Grade), TextAlign.Center);
        ui.Text(ui.Mono(19), _result.Score.ToString("N0"),
            new Rectangle(0, top + 160, ui.Width, 26), Theme.Accent, TextAlign.Center);
        ui.Text(ui.Mono(Theme.Label), $"{_result.MaxCombo:N0} COMBO",
            new Rectangle(0, top + 190, ui.Width, 18), Theme.TextMuted, TextAlign.Center);

        var rows = new (string label, int value, Color color)[]
        {
            ("PERFECT", _result.Perfect, Theme.Perfect),
            ("GREAT", _result.Great, Theme.Great),
            ("GOOD", _result.Good, Theme.Good),
            ("BAD", _result.Bad, Theme.Bad),
            ("MISS", _result.Miss, Theme.Miss),
        };

        int panelW = 300;
        int y = top + 234;
        foreach ((string label, int value, Color color) in rows)
        {
            var row = new Rectangle(cx - panelW / 2, y, panelW, 28);
            ui.Text(ui.Mono(Theme.Mono), label, row, color);
            ui.Text(ui.Mono(Theme.Mono), value.ToString("N0"), row, Theme.Text, TextAlign.Right);
            y += 30;
        }

        y += 18;
        DrawPpAndRating(ui, new Rectangle(cx - 220, y, 440, 120));
        y += 132;

        foreach ((string text, Color c) in Badges())
        {
            ui.Text(ui.Display(Theme.DisplayM), text, new Rectangle(0, y, ui.Width, 26), c, TextAlign.Center);
            y += 28;
        }

        ScreenChrome.FooterHint(ui, "Enter to continue");
    }

    private void DrawPpAndRating(UiRenderer ui, Rectangle area)
    {
        if (_outcome is null)
        {
            ui.Text(ui.Mono(Theme.Label), "practice — not saved",
                new Rectangle(area.X, area.Y, area.Width, 18), Theme.TextFaint, TextAlign.Center);
            return;
        }

        // Stage 1 (t>0.3): PP counts up. Stage 2 (t>1.1): rating counts up.
        double ppT = Ease((_time - 0.3) / 0.8);
        double pp = _outcome.PpBreakdown.FinalPp * ppT;
        ui.Text(ui.Display(30), $"+{pp:0} PP",
            new Rectangle(area.X, area.Y, area.Width, 34), Theme.Accent, TextAlign.Center);

        double ratT = Ease((_time - 1.1) / 0.9);
        double rating = _outcome.Before.Rating + _outcome.RatingDelta * ratT;
        ui.Text(ui.Mono(16), $"Rating {rating:0.00}",
            new Rectangle(area.X, area.Y + 42, area.Width, 22), Theme.Text, TextAlign.Center);

        if (_outcome.RatingDelta > 0.001)
        {
            ui.Text(ui.Mono(Theme.Label), $"+{_outcome.RatingDelta:0.00}",
                new Rectangle(area.X, area.Y + 64, area.Width, 16), Theme.Good, TextAlign.Center);
        }

        double totalPp = _outcome.After.TotalPp;
        ui.Text(ui.Mono(Theme.Label), $"Total PP {totalPp:N0}",
            new Rectangle(area.X, area.Y + 84, area.Width, 16), Theme.TextMuted, TextAlign.Center);
    }

    private IEnumerable<(string, Color)> Badges()
    {
        if (_time < 1.8)
        {
            yield break;
        }

        if (_result.AllPerfect) yield return ("ALL PERFECT", Theme.Perfect);
        else if (_result.FullCombo) yield return ("FULL COMBO", Theme.Accent);

        if (_outcome is { IsPersonalBest: true, IsFirstPlayOnChart: false })
            yield return ("NEW PERSONAL BEST", Theme.AccentBright);
        if (_outcome is { IsPpRecord: true })
            yield return ("NEW PP RECORD", Theme.Perfect);
        if (_outcome is { IsRatingRecord: true })
            yield return ("NEW RATING RECORD", Theme.Great);
    }

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
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
