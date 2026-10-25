using Microsoft.Xna.Framework.Input;

namespace MDSM.Input;

// Presentation toggles never alter gameplay input or the saved world.
internal sealed class PresentationInput
{
    public bool HudVisible { get; private set; } = true;
    public bool DebugEnabled { get; private set; }
    public bool DebugVisible => HudVisible && DebugEnabled;
    private bool _screenshotPending;

    public void Update(KeyboardState keyboard, KeyboardState previous, bool active, bool hasWorld)
    {
        if (!active) return;
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && previous.IsKeyUp(key);
        if (hasWorld && Pressed(Keys.F1)) HudVisible = !HudVisible;
        if (hasWorld && Pressed(Keys.F3)) DebugEnabled = !DebugEnabled;
        if (Pressed(Keys.F2)) _screenshotPending = true;
    }

    public bool TakeScreenshotRequest()
    {
        bool requested = _screenshotPending;
        _screenshotPending = false;
        return requested;
    }
}
