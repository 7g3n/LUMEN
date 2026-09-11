using Lumen.Core.Accessibility;
using Lumen.Core.Gameplay;
using Lumen.Core.Scores;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// Result screen (spec §36–38). Accuracy / grade / score / judgements, then the PP
/// earned and the Rating change, revealed and counted up in stages. Record badges per §37.
///
/// The staging is the point of the screen, not decoration on top of it: a result is read
/// in an order — how did I do, what grade was that, what did it earn me — and showing it
/// all at once makes the player find that order for themselves. Every stage is a single
/// <see cref="Stage"/> call against one clock, so the sequence can be read down the draw
/// method in the order the player sees it.
///
/// The player is never made to wait for it. The first key press finishes the animation
/// immediately; the second dismisses. Reduced motion skips straight to that end state,
/// which is the same code path rather than a special case (§67).
/// </summary>
public sealed class ResultScreen : Screen
{
    private readonly PlayResult _result;
    private readonly ScoreSaveOutcome? _outcome;
    private readonly Action _onDismiss;

    private readonly List<(string Text, Color Colour)> _badges = new();

    /// <summary>
    /// The judgement breakdown, built once for the same reason the badges are: the counts
    /// are fixed the moment the play ends, and rebuilding the rows on every frame would be
    /// work done purely to arrive at the same answer.
    /// </summary>
    private (string Label, int Value, Color Colour)[] _judgementRows =
        Array.Empty<(string, int, Color)>();

    private double _time;
    private bool _settled;

    /// <summary>When the last stage has finished, so the screen knows it is done.</summary>
    private const double FinishedAt = 2.6;

    public ResultScreen(PlayResult result, ScoreSaveOutcome? outcome, Action onDismiss)
    {
        _result = result;
        _outcome = outcome;
        _onDismiss = onDismiss;
    }

    public override void OnEnter()
    {
        _judgementRows = new (string, int, Color)[]
        {
            ("PERFECT", _result.Perfect, Theme.Perfect),
            ("GREAT", _result.Great, Theme.Great),
            ("GOOD", _result.Good, Theme.Good),
            ("BAD", _result.Bad, Theme.Bad),
            ("MISS", _result.Miss, Theme.Miss),
        };

        BuildBadges();

        // Reduced motion means the end state from the first frame (§67).
        if (Context.Accessibility.ReducedMotion)
        {
            Settle();
        }
    }

    private void Settle()
    {
        _settled = true;
        _time = FinishedAt;
    }

    public override void Update(InputFrame input)
    {
        if (!_settled)
        {
            _time += input.DeltaSeconds;
            if (_time >= FinishedAt)
            {
                Settle();
            }
        }

        bool confirm = input.Pressed(Keys.Enter) || input.Pressed(Keys.Space);

        // Escape leaves at once: somebody who wants out should not have to watch a
        // count-up first.
        if (input.Pressed(Keys.Escape))
        {
            _onDismiss();
            return;
        }

        if (!confirm)
        {
            return;
        }

        if (_settled)
        {
            _onDismiss();
        }
        else
        {
            Settle();
        }
    }

    // --- staging ---

    /// <summary>
    /// How far into a stage the screen is: 0 before it starts, 1 once it has finished,
    /// eased in between. Reduced motion is handled by the clock rather than here, so a
    /// caller never has to ask about it.
    /// </summary>
    private double Stage(double startSeconds, double durationSeconds = 0.45) =>
        Ease(Math.Clamp((_time - startSeconds) / durationSeconds, 0, 1));

    /// <summary>A stage's opacity and the small rise that goes with it.</summary>
    private (float Alpha, int Rise) Reveal(double startSeconds, double durationSeconds = 0.45)
    {
        double t = Stage(startSeconds, durationSeconds);
        return ((float)t, (int)Math.Round((1 - t) * 10));
    }

    public override void Draw(UiRenderer ui)
    {
        int cx = ui.Width / 2;
        int top = 56;

        (float titleA, int titleRise) = Reveal(0);
        ui.Text(ui.Mono(Theme.Label),
            $"{_result.Chart.Title.ToUpperInvariant()}   {_result.Chart.DifficultyName}",
            new Rectangle(0, top + titleRise, ui.Width, 18),
            Theme.TextMuted.WithAlpha(titleA), TextAlign.Center);

        // The headline number counts, because it is the one the player is waiting for.
        double accuracyT = Stage(0.15, 0.75);
        (float accA, int accRise) = Reveal(0.15, 0.75);
        ui.Text(ui.Display(60), $"{_result.Accuracy * accuracyT:0.00}%",
            new Rectangle(0, top + 30 + accRise, ui.Width, 68),
            Theme.Text.WithAlpha(accA), TextAlign.Center);

        // The grade lands after it, as the verdict on the number just shown.
        (float gradeA, int gradeRise) = Reveal(0.85, 0.35);
        ui.Text(ui.Display(36), _result.Grade,
            new Rectangle(0, top + 106 + gradeRise, ui.Width, 44),
            GradeColor(_result.Grade).WithAlpha(gradeA), TextAlign.Center);

        (float scoreA, int scoreRise) = Reveal(1.0, 0.35);
        ui.Text(ui.Mono(19), _result.Score.ToString("N0"),
            new Rectangle(0, top + 160 + scoreRise, ui.Width, 26),
            Theme.Accent.WithAlpha(scoreA), TextAlign.Center);
        ui.Text(ui.Mono(Theme.Label), $"{_result.MaxCombo:N0} COMBO",
            new Rectangle(0, top + 190 + scoreRise, ui.Width, 18),
            Theme.TextMuted.WithAlpha(scoreA), TextAlign.Center);

        DrawJudgements(ui, cx, top + 234);

        DrawPpAndRating(ui, new Rectangle(cx - 220, top + 252 + 5 * 30, 440, 120));

        DrawBadges(ui, top + 384 + 5 * 30);

        // The hint says what the key will do right now, which changes once the screen has
        // finished revealing itself.
        ScreenChrome.FooterHint(ui, _settled ? "Enter to continue" : "Enter to skip");
    }

    private void DrawJudgements(UiRenderer ui, int cx, int startY)
    {
        const int PanelWidth = 300;
        int y = startY;
        (string Label, int Value, Color Colour)[] rows = _judgementRows;

        for (int i = 0; i < rows.Length; i++)
        {
            // Staggered, so the breakdown reads top to bottom rather than arriving as a block.
            (float alpha, int rise) = Reveal(1.15 + i * 0.06, 0.3);
            var row = new Rectangle(cx - PanelWidth / 2, y + rise, PanelWidth, 28);

            ui.Text(ui.Mono(Theme.Mono), rows[i].Label, row, rows[i].Colour.WithAlpha(alpha));
            ui.Text(ui.Mono(Theme.Mono), rows[i].Value.ToString("N0"), row,
                Theme.Text.WithAlpha(alpha), TextAlign.Right);

            y += 30;
        }
    }

    private void DrawPpAndRating(UiRenderer ui, Rectangle area)
    {
        if (_outcome is null)
        {
            (float practiceA, _) = Reveal(1.5, 0.3);
            ui.Text(ui.Mono(Theme.Label), "practice — not saved",
                new Rectangle(area.X, area.Y, area.Width, 18),
                Theme.TextFaint.WithAlpha(practiceA), TextAlign.Center);
            return;
        }

        double ppT = Stage(1.5, 0.7);
        ui.Text(ui.Display(30), $"+{_outcome.PpBreakdown.FinalPp * ppT:0} PP",
            new Rectangle(area.X, area.Y, area.Width, 34),
            Theme.Accent.WithAlpha((float)Stage(1.5, 0.25)), TextAlign.Center);

        double ratingT = Stage(2.0, 0.6);
        double rating = _outcome.Before.Rating + _outcome.RatingDelta * ratingT;
        ui.Text(ui.Mono(16), $"Rating {rating:0.00}",
            new Rectangle(area.X, area.Y + 42, area.Width, 22),
            Theme.Text.WithAlpha((float)Stage(2.0, 0.25)), TextAlign.Center);

        if (_outcome.RatingDelta > 0.001)
        {
            ui.Text(ui.Mono(Theme.Label), $"+{_outcome.RatingDelta:0.00}",
                new Rectangle(area.X, area.Y + 64, area.Width, 16),
                Theme.Good.WithAlpha((float)ratingT), TextAlign.Center);
        }

        ui.Text(ui.Mono(Theme.Label), $"Total PP {_outcome.After.TotalPp:N0}",
            new Rectangle(area.X, area.Y + 84, area.Width, 16),
            Theme.TextMuted.WithAlpha((float)Stage(2.2, 0.3)), TextAlign.Center);
    }

    private void DrawBadges(UiRenderer ui, int startY)
    {
        int y = startY;

        for (int i = 0; i < _badges.Count; i++)
        {
            // Last, and one after another: these are the part worth pausing on.
            (float alpha, int rise) = Reveal(2.3 + i * 0.12, 0.3);
            if (alpha <= 0)
            {
                break;
            }

            ui.Text(ui.Display(Theme.DisplayM), _badges[i].Text,
                new Rectangle(0, y + rise, ui.Width, 26),
                _badges[i].Colour.WithAlpha(alpha), TextAlign.Center);

            y += 28;
        }
    }

    /// <summary>
    /// Worked out once when the screen opens rather than on every frame — the outcome
    /// cannot change while the screen is up, and an iterator in a draw loop is an
    /// allocation per frame for an answer that never moves.
    /// </summary>
    private void BuildBadges()
    {
        if (_result.AllPerfect)
        {
            _badges.Add(("ALL PERFECT", Theme.Perfect));
        }
        else if (_result.FullCombo)
        {
            _badges.Add(("FULL COMBO", Theme.Accent));
        }

        if (_outcome is { IsPersonalBest: true, IsFirstPlayOnChart: false })
        {
            _badges.Add(("NEW PERSONAL BEST", Theme.AccentBright));
        }

        if (_outcome is { IsPpRecord: true })
        {
            _badges.Add(("NEW PP RECORD", Theme.Perfect));
        }

        if (_outcome is { IsRatingRecord: true })
        {
            _badges.Add(("NEW RATING RECORD", Theme.Great));
        }
    }

    /// <summary>Decelerating: fast to almost-there, then settling. </summary>
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
