using Lumen.Core.Accessibility;
using Lumen.Core.Diagnostics;
using Lumen.Game.Input;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lumen.Game.Screens;

/// <summary>
/// How to play (spec §82–83).
///
/// The quality bar for LUMEN is that somebody can play without reading a manual, so this
/// is not a manual either: each step asks for the thing it is teaching and will not move
/// on until the player has done it. Nobody has to remember which key is which lane,
/// because they have already pressed all four by the time the tutorial ends.
///
/// It is reachable from the menu as well as on a first run, so a player who skipped it —
/// or who comes back after a long gap — is never locked out of it.
/// </summary>
public sealed class TutorialScreen : Screen
{
    private enum Step
    {
        Lanes,      // press each lane key once
        HitLine,    // what the line means
        Timing,     // judgement tiers
        Ready,      // start playing
    }

    private readonly Action _onFinished;

    private KeyBindings _bindings = KeyBindings.Default;
    private bool[] _pressed = new bool[4];
    private Step _step = Step.Lanes;
    private double _stepTime;

    /// <summary>The lane that most recently lit, for the flash under it.</summary>
    private int _lastLane = -1;
    private double _lastLaneAt = double.NegativeInfinity;

    /// <param name="onFinished">
    /// Where to go when the tutorial is done or skipped. The tutorial does not decide
    /// this: on a first run it leads into a first play, from the menu it goes back to the
    /// menu, and the screen itself should not have to know which.
    /// </param>
    public TutorialScreen(Action onFinished) => _onFinished = onFinished;

    public override void OnEnter()
    {
        _bindings = KeyBindings.Load(Context.Settings, Context.Session.ActivePlayerId);
        _pressed = new bool[_bindings.LaneCount];
        Log.Info("tutorial started");
    }

    private bool AllLanesPressed()
    {
        foreach (bool pressed in _pressed)
        {
            if (!pressed)
            {
                return false;
            }
        }

        return true;
    }

    private int RemainingLanes()
    {
        int remaining = 0;
        foreach (bool pressed in _pressed)
        {
            if (!pressed)
            {
                remaining++;
            }
        }

        return remaining;
    }

    public override void Update(InputFrame input)
    {
        _stepTime += input.DeltaSeconds;

        // Escape leaves at any point. A tutorial nobody can get out of is worse than none.
        if (input.Pressed(Keys.Escape))
        {
            Log.Info($"tutorial skipped at {_step}");
            _onFinished();
            return;
        }

        switch (_step)
        {
            case Step.Lanes:
                UpdateLanes(input);
                break;

            default:
                if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space))
                {
                    Advance();
                }

                break;
        }
    }

    private void UpdateLanes(InputFrame input)
    {
        for (int lane = 0; lane < _bindings.LaneCount; lane++)
        {
            if (!input.Pressed(_bindings[lane]))
            {
                continue;
            }

            _pressed[lane] = true;
            _lastLane = lane;
            _lastLaneAt = _stepTime;
        }

        // A short beat after the last key, so the player sees the fourth one light up
        // rather than the screen changing out from under their finger.
        if (AllLanesPressed() && _stepTime - _lastLaneAt > 0.45)
        {
            Advance();
        }
    }

    private void Advance()
    {
        if (_step == Step.Ready)
        {
            Log.Info("tutorial finished");
            _onFinished();
            return;
        }

        _step++;
        _stepTime = 0;
    }

    // --- drawing ---

    public override void Draw(UiRenderer ui)
    {
        Rectangle column = ScreenChrome.Column(ui, top: 88);
        int y = ScreenChrome.Header(ui, column, "How To Play");

        (string headline, string detail) = _step switch
        {
            Step.Lanes => ("Four lanes, four keys.",
                           "Press each one to see which lane it belongs to."),
            Step.HitLine => ("Notes fall towards the line.",
                             "Press a lane's key as its note reaches the line."),
            Step.Timing => ("The closer to the line, the better the hit.",
                            "Perfect, Great, Good, Bad — or a Miss if you are too far off."),
            _ => ("That's everything.",
                  "The rest you learn by playing. Your first song is ready."),
        };

        ui.Text(ui.Display(Theme.DisplayM), headline,
            new Rectangle(column.X, y, column.Width, 30), Theme.Text);
        ui.Text(ui.Body(Theme.Body), detail,
            new Rectangle(column.X, y + 36, column.Width, 24), Theme.TextMuted);

        DrawLanes(ui, new Rectangle(column.X, y + 84, column.Width, 260));

        ScreenChrome.FooterHint(ui, _step switch
        {
            Step.Lanes => AllLanesPressed()
                ? "Esc to skip"
                : $"{RemainingLanes()} more to go  ·  Esc to skip",
            Step.Ready => "Enter to play  ·  Esc to skip",
            _ => "Enter to continue  ·  Esc to skip",
        });
    }

    /// <summary>
    /// A miniature playfield. It is the same shape as the real one on purpose: what the
    /// player learns here has to be recognisable the moment the song starts.
    /// </summary>
    private void DrawLanes(UiRenderer ui, Rectangle area)
    {
        int lanes = Math.Max(1, _bindings.LaneCount);
        int laneWidth = Math.Min(96, area.Width / lanes);
        int fieldWidth = laneWidth * lanes;
        int fieldX = area.X + (area.Width - fieldWidth) / 2;

        // The field stops at the hit line, and the keycaps sit below it in clear air: the
        // line is the thing being taught, so nothing may read as continuing past it.
        int hitLineY = area.Bottom - 112;
        int fieldHeight = hitLineY - area.Y + 8;

        ui.FillRect(new Rectangle(fieldX, area.Y, fieldWidth, fieldHeight),
            Theme.Surface.WithAlpha(0.5f));

        for (int i = 0; i <= lanes; i++)
        {
            ui.FillRect(new Rectangle(fieldX + i * laneWidth, area.Y, 1, fieldHeight),
                Theme.Border);
        }

        // The hit line is highlighted from the step that introduces it onwards.
        bool emphasise = _step >= Step.HitLine;
        ui.FillRect(new Rectangle(fieldX, hitLineY, fieldWidth, emphasise ? 3 : 2),
            emphasise ? Theme.AccentBright : Theme.Accent.WithAlpha(0.8f));

        if (_step == Step.HitLine)
        {
            ui.Text(ui.Mono(Theme.Label), "HIT LINE",
                new Rectangle(fieldX + fieldWidth + 12, hitLineY - 8, 90, 16),
                Theme.AccentBright);
        }

        DrawFallingNote(ui, fieldX, laneWidth, area.Y, hitLineY);

        for (int lane = 0; lane < lanes; lane++)
        {
            DrawLaneKey(ui, lane, fieldX + lane * laneWidth, laneWidth, hitLineY);
        }

        if (_step == Step.Timing)
        {
            DrawTimingTiers(ui, new Rectangle(area.X, hitLineY + 76, area.Width, 20));
        }
    }

    private void DrawLaneKey(UiRenderer ui, int lane, int x, int width, int hitLineY)
    {
        bool done = lane < _pressed.Length && _pressed[lane];

        // While teaching the lanes, the one just pressed flashes. Under reduced motion the
        // key simply stays marked, which carries the same information without the flash (§67).
        bool flashing = !Context.Accessibility.ReducedMotion
                        && _step == Step.Lanes
                        && lane == _lastLane
                        && _stepTime - _lastLaneAt < 0.35;

        if (done || flashing)
        {
            ui.FillRect(new Rectangle(x + 1, hitLineY - 3, width - 1, 6),
                flashing ? Theme.AccentBright : Theme.Accent.WithAlpha(0.5f));
        }

        var cap = new Rectangle(x + width / 2 - 22, hitLineY + 24, 44, 44);
        ui.FillRect(cap, done ? Theme.AccentSoft : Theme.Surface);
        ui.StrokeRect(cap, done ? Theme.Accent : Theme.Border);

        ui.Text(ui.Display(Theme.DisplayM), KeyLabel(_bindings[lane]), cap,
            done ? Theme.AccentBright : Theme.TextMuted, TextAlign.Center);
    }

    /// <summary>
    /// A single note, falling on a loop, so "notes fall towards the line" is shown rather
    /// than asserted. With reduced motion it is parked just above the line instead.
    /// </summary>
    private void DrawFallingNote(UiRenderer ui, int fieldX, int laneWidth, int top, int hitLineY)
    {
        if (_step < Step.HitLine)
        {
            return;
        }

        AccessibilityOptions a11y = Context.Accessibility;
        int lane = Math.Min(1, Math.Max(0, _bindings.LaneCount - 1));

        double travel = a11y.ReducedMotion ? 0.88 : _stepTime % 1.6 / 1.6;
        int y = (int)(top + (hitLineY - top) * travel);

        int x = fieldX + lane * laneWidth + 4;
        int width = laneWidth - 8;

        ui.FillRect(new Rectangle(x, y - 6, width, 12), Theme.Text);
        ui.FillRect(new Rectangle(x, y - 6, width, 2), Theme.Accent);
    }

    private void DrawTimingTiers(UiRenderer ui, Rectangle area)
    {
        (string Label, Color Colour)[] tiers =
        {
            ("PERFECT", Theme.Perfect),
            ("GREAT", Theme.Great),
            ("GOOD", Theme.Good),
            ("BAD", Theme.Bad),
            ("MISS", Theme.Miss),
        };

        int cellWidth = area.Width / tiers.Length;
        for (int i = 0; i < tiers.Length; i++)
        {
            var cell = new Rectangle(area.X + i * cellWidth, area.Y, cellWidth, 18);
            ui.Text(ui.Mono(Theme.Label),
                JudgementLabelFor(i, Context.Accessibility.ShapeCues, tiers[i].Label),
                cell, tiers[i].Colour, TextAlign.Center);
        }
    }

    private static string JudgementLabelFor(int index, bool shapeCues, string fallback) =>
        index < 5 ? JudgementShapes.Label((Core.Judgement)index, shapeCues) : fallback;

    /// <summary>
    /// What is printed on the key, rather than what the enum is called: "D1" on a keycap
    /// helps nobody find the 1 key.
    /// </summary>
    public static string KeyLabel(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => key.ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "N" + (char)('0' + (key - Keys.NumPad0)),
        Keys.Space => "SPC",
        Keys.OemComma => ",",
        Keys.OemPeriod => ".",
        Keys.OemSemicolon => ";",
        Keys.OemQuestion => "/",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.LeftShift or Keys.RightShift => "SHFT",
        Keys.LeftControl or Keys.RightControl => "CTRL",
        Keys.None => "—",
        _ => key.ToString().ToUpperInvariant(),
    };
}
