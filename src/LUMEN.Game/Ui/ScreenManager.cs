using Lumen.Core.Diagnostics;
using Microsoft.Xna.Framework;

namespace Lumen.Game.Ui;

/// <summary>
/// A stack of <see cref="Screen"/>s with a short fade-through-black transition on every
/// change. Only the top screen updates and draws.
/// </summary>
public sealed class ScreenManager
{
    private const double FadeSeconds = 0.16;

    private readonly List<Screen> _stack = new();

    private enum Phase { Idle, Out, In }

    private Phase _phase = Phase.Idle;
    private double _phaseTime;
    private Action? _pending;

    public ScreenManager(GameContext context) => Context = context;

    public GameContext Context { get; }

    public Screen? Top => _stack.Count > 0 ? _stack[^1] : null;

    public int Depth => _stack.Count;

    public bool IsTransitioning => _phase != Phase.Idle;

    /// <summary>Fraction of black covering the screen (0 = clear, 1 = opaque).</summary>
    public float FadeAlpha => _phase switch
    {
        Phase.Out => (float)(_phaseTime / FadeSeconds),
        Phase.In => 1f - (float)(_phaseTime / FadeSeconds),
        _ => 0f,
    };

    public void Push(Screen screen) => Begin(() =>
    {
        Top?.OnExit();
        Attach(screen);
        _stack.Add(screen);
        screen.OnEnter();
    });

    public void Replace(Screen screen) => Begin(() =>
    {
        if (_stack.Count > 0)
        {
            _stack[^1].OnExit();
            _stack.RemoveAt(_stack.Count - 1);
        }

        Attach(screen);
        _stack.Add(screen);
        screen.OnEnter();
    });

    public void ReplaceAll(Screen screen) => Begin(() =>
    {
        foreach (Screen s in _stack)
        {
            s.OnExit();
        }

        _stack.Clear();
        Attach(screen);
        _stack.Add(screen);
        screen.OnEnter();
    });

    public void Pop() => Begin(() =>
    {
        if (_stack.Count == 0)
        {
            return;
        }

        _stack[^1].OnExit();
        _stack.RemoveAt(_stack.Count - 1);
        Top?.OnReveal();
    });

    /// <summary>Sets the initial screen with no transition.</summary>
    public void SetRoot(Screen screen)
    {
        _stack.Clear();
        Attach(screen);
        _stack.Add(screen);
        screen.OnEnter();
        _phase = Phase.Idle;
    }

    /// <summary>Hands a window file-drop to the top screen, if it wants one.</summary>
    public void DeliverFileDrop(IReadOnlyList<string> paths)
    {
        if (Top is IFileDropTarget target && paths.Count > 0)
        {
            target.OnFilesDropped(paths);
        }
    }

    public void Update(InputFrame input)
    {
        if (_phase == Phase.Idle)
        {
            Top?.Update(input);
            return;
        }

        _phaseTime += input.DeltaSeconds;
        if (_phaseTime < FadeSeconds)
        {
            if (_phase == Phase.In)
            {
                Top?.Update(input);
            }

            return;
        }

        if (_phase == Phase.Out)
        {
            try
            {
                _pending?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("screen transition failed", ex);
                throw;
            }
            finally
            {
                _pending = null;
            }

            _phase = Phase.In;
            _phaseTime = 0;
        }
        else
        {
            _phase = Phase.Idle;
            _phaseTime = 0;
        }
    }

    public void Draw(UiRenderer ui)
    {
        Screen? top = Top;
        ui.Clear(top?.BackgroundColor ?? Theme.Ground);

        if (top is not null)
        {
            ui.Begin();
            top.Draw(ui);
            float fade = FadeAlpha;
            if (fade > 0f)
            {
                ui.FillRect(ui.Bounds, Color.Black.WithAlpha(MathHelper.Clamp(fade, 0f, 1f)));
            }

            ui.End();
        }
    }

    private void Begin(Action action)
    {
        if (_phase != Phase.Idle)
        {
            // Collapse a change requested mid-transition into the pending action.
            _pending = action;
            return;
        }

        _pending = action;
        _phase = Phase.Out;
        _phaseTime = 0;
    }

    private void Attach(Screen screen) => screen.Manager = this;
}
