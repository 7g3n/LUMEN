using Lumen.Core.Tournaments;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens.Tournaments;

/// <summary>
/// What a tournament does, in the game rather than in a manual (spec: Tournament Rules).
///
/// The one thing here that genuinely needs saying is the separation: people are reasonably
/// wary of entering a competition on a chart somebody else chose when they have a rating
/// they care about. Saying plainly that it cannot touch that rating is the difference
/// between a feature they try and one they avoid.
/// </summary>
public sealed class TournamentRulesScreen : Screen
{
    public override void Update(InputFrame input)
    {
        if (input.Pressed(Keys.Escape) || input.Pressed(Keys.Enter))
        {
            Manager.Pop();
        }
    }

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 88);
        int y = ScreenChrome.Header(ui, column, "How Tournaments Work");

        y = Paragraph(ui, column, y,
            "Your rating is safe",
            "Tournament results are recorded separately from your normal play. They never " +
            "change your Rating, your PP or your personal bests — a competition on somebody " +
            "else's chart choice should not move the number you have been building.");

        y = Paragraph(ui, column, y,
            "Everybody plays on this machine",
            "Pick the profiles on this computer to enter. Players take turns at the keyboard, " +
            "and the screen says whose go it is.");

        y = Paragraph(ui, column, y,
            "The rules are enforced, not suggested",
            "An official tournament allows one attempt, no retry and no pause, and the game " +
            "refuses them rather than trusting nobody to try. A casual one allows three " +
            "attempts and lets you pause.");

        y = Paragraph(ui, column, y,
            "A result is not final until it is confirmed",
            "A play is recorded when it finishes, but it decides nothing until the organiser " +
            "confirms it. That is what stops a match being settled by whoever played first.");

        y = Paragraph(ui, column, y,
            "Everything is written down",
            "Every chart chosen, every play, every confirmation and every disqualification " +
            "goes into the tournament's log, with the build and the rules that were in force. " +
            "Afterwards you can say not only who won but how the event got there.");

        Formats(ui, column, y);

        ScreenChrome.FooterHint(ui, "Enter or Esc to go back");
    }

    private static int Paragraph(UiRenderer ui, Rectangle column, int y, string heading, string body)
    {
        ui.Text(ui.Display(Theme.DisplayM), heading,
            new Rectangle(column.X, y, column.Width, 24), Theme.Text);

        int used = Wrap(ui, body, new Rectangle(column.X, y + 28, column.Width, 20));
        return y + 34 + used;
    }

    private static void Formats(UiRenderer ui, Rectangle column, int y)
    {
        ui.Text(ui.Mono(Theme.Label), "FORMATS",
            new Rectangle(column.X, y, column.Width, 16), Theme.Accent);

        (string Name, string Blurb)[] formats =
        {
            ("SINGLE ELIM", "Lose once and you are out."),
            ("DOUBLE ELIM", "A losers' bracket: one bad match is not the end of a run."),
            ("SCORE ATTACK", "Everybody plays the same charts; the ranking decides."),
        };

        int rowY = y + 24;
        foreach ((string name, string blurb) in formats)
        {
            ui.Text(ui.Mono(Theme.Label), name,
                new Rectangle(column.X, rowY, 150, 18), Theme.TextMuted);
            ui.Text(ui.Body(Theme.Label), blurb,
                new Rectangle(column.X + 160, rowY, column.Width - 160, 18), Theme.TextFaint);
            rowY += 22;
        }
    }

    /// <summary>
    /// Wraps to the column width, measuring rather than guessing at a character count —
    /// the body face is proportional, so a fixed wrap would be ragged at best.
    /// </summary>
    private static int Wrap(UiRenderer ui, string text, Rectangle area)
    {
        var font = ui.Body(Theme.Label);
        string[] words = text.Split(' ');

        var line = new System.Text.StringBuilder();
        int y = area.Y;

        foreach (string word in words)
        {
            string candidate = line.Length == 0 ? word : line + " " + word;

            if (ui.Measure(font, candidate).X > area.Width && line.Length > 0)
            {
                ui.Text(font, line.ToString(),
                    new Rectangle(area.X, y, area.Width, area.Height), Theme.TextMuted);
                y += 20;
                line.Clear().Append(word);
            }
            else
            {
                line.Clear().Append(candidate);
            }
        }

        if (line.Length > 0)
        {
            ui.Text(font, line.ToString(),
                new Rectangle(area.X, y, area.Width, area.Height), Theme.TextMuted);
            y += 20;
        }

        return y - area.Y;
    }
}
