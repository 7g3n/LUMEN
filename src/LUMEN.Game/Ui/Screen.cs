using Microsoft.Xna.Framework;

namespace Lumen.Game.Ui;

/// <summary>Base class for every full-screen view (setup, menu, profile, gameplay, editor...).</summary>
public abstract class Screen
{
    protected internal ScreenManager Manager { get; internal set; } = null!;

    protected GameContext Context => Manager.Context;

    public virtual Color BackgroundColor => Theme.Ground;

    /// <summary>Called when the screen becomes the active top of the stack.</summary>
    public virtual void OnEnter() { }

    /// <summary>Called when the screen is popped or covered by a replacement.</summary>
    public virtual void OnExit() { }

    /// <summary>Called when a covering screen is popped and this one is revealed again.</summary>
    public virtual void OnReveal() { }

    public abstract void Update(InputFrame input);

    public abstract void Draw(UiRenderer ui);
}
