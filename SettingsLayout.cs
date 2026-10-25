using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

internal static class SettingsLayout
{
    public static Rectangle Slider(Viewport viewport) => new((viewport.Width - Width(viewport)) / 2, viewport.Height / 2 - 50, Width(viewport), 32);
    public static Rectangle Toggle(Viewport viewport) => new(Slider(viewport).X, Slider(viewport).Y + 58, Width(viewport), 36);
    public static Rectangle Back(Viewport viewport) => new((viewport.Width - Math.Min(300, Width(viewport))) / 2,
        Slider(viewport).Y + 124, Math.Min(300, Width(viewport)), 42);
    private static int Width(Viewport viewport) => Math.Min(520, Math.Max(32, viewport.Width - 32));
    public static int ValueAt(Rectangle slider, int x) => Math.Clamp(GameSettings.MinimumFps + (int)Math.Round(
        (x - slider.Left - 8) / (double)Math.Max(1, slider.Width - 17) * (GameSettings.MaximumFps - GameSettings.MinimumFps)),
        GameSettings.MinimumFps, GameSettings.MaximumFps);
    public static int ThumbX(Rectangle slider, int value) => slider.Left + 8 + (int)Math.Round(
        (value - GameSettings.MinimumFps) / (double)(GameSettings.MaximumFps - GameSettings.MinimumFps) * (slider.Width - 17));
}
