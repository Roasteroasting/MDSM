using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;

namespace MDSM.UI;

internal static class SettingsLayout
{
    public static Rectangle Slider(Viewport viewport) => new((viewport.Width - Width(viewport)) / 2, viewport.Height / 2 - 50, Width(viewport), 32);
    public static Rectangle Toggle(Viewport viewport) => new(Slider(viewport).X,
        Slider(viewport).Y + (viewport.Height < 400 ? 42 : 58), Width(viewport), 36);
    public static Rectangle Information(Viewport viewport, int index)
    {
        int width = (Width(viewport) - 12) / 2;
        return new Rectangle(Slider(viewport).X + index * (width + 12),
            Toggle(viewport).Bottom + 8, width, viewport.Height < 400 ? 28 : 42);
    }
    public static Rectangle Back(Viewport viewport) => new((viewport.Width - Math.Min(300, Width(viewport))) / 2,
        Information(viewport, 0).Bottom + 8, Math.Min(300, Width(viewport)), viewport.Height < 400 ? 32 : 42);
    private static int Width(Viewport viewport) => Math.Min(520, Math.Max(32, viewport.Width - 32));
    public static int ValueAt(Rectangle slider, int x) => Math.Clamp(GameSettings.MinimumFps + (int)Math.Round(
        (x - slider.Left - 8) / (double)Math.Max(1, slider.Width - 17) * (GameSettings.MaximumFps - GameSettings.MinimumFps)),
        GameSettings.MinimumFps, GameSettings.MaximumFps);
    public static int ThumbX(Rectangle slider, int value) => slider.Left + 8 + (int)Math.Round(
        (value - GameSettings.MinimumFps) / (double)(GameSettings.MaximumFps - GameSettings.MinimumFps) * (slider.Width - 17));
}
