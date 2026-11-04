using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;

namespace MDSM.UI;

internal static class SettingsLayout
{
    public static Rectangle Slider(Viewport viewport) => new((viewport.Width - Width(viewport)) / 2,
        viewport.Height<300 ? 64 : Math.Max(42, Math.Min(viewport.Height / 2 - 64, viewport.Height - 210)), Width(viewport), viewport.Height<300?20:32);
    public static Rectangle Toggle(Viewport viewport) => new(Slider(viewport).X,
        Slider(viewport).Bottom + (viewport.Height<300?4:viewport.Height < 400 ? 4 : 16), Width(viewport), viewport.Height<300?24:viewport.Height<400?28:36);
    public static Rectangle Bobbing(Viewport viewport)=>new(Toggle(viewport).X,Toggle(viewport).Bottom+(viewport.Height<300?4:6),Width(viewport),viewport.Height<300?24:viewport.Height<400?28:36);
    public static Rectangle Choice(Viewport viewport, int index)
    {
        int width = (Width(viewport) - 12) / 2;
        return new(Slider(viewport).X + index * (width + 12), Bobbing(viewport).Bottom + (viewport.Height<300?4:6), width, viewport.Height<300?24:viewport.Height < 400 ? 28 : 36);
    }
    public static Rectangle Information(Viewport viewport, int index)
    {
        int width = (Width(viewport) - 12) / 2;
        return new Rectangle(Slider(viewport).X + index * (width + 12),
            Choice(viewport, 0).Bottom + (viewport.Height<300?4:8), width, viewport.Height<300?24:viewport.Height < 400 ? 28 : 42);
    }
    public static Rectangle Back(Viewport viewport) => new((viewport.Width - Math.Min(300, Width(viewport))) / 2,
        Information(viewport, 0).Bottom + (viewport.Height<300?4:8), Math.Min(300, Width(viewport)), viewport.Height<300?28:viewport.Height < 400 ? 32 : 42);
    private static int Width(Viewport viewport) => Math.Min(520, Math.Max(32, viewport.Width - 32));
    public static int ValueAt(Rectangle slider, int x) => Math.Clamp(GameSettings.MinimumFps + (int)Math.Round(
        (x - slider.Left - 8) / (double)Math.Max(1, slider.Width - 17) * (GameSettings.MaximumFps - GameSettings.MinimumFps)),
        GameSettings.MinimumFps, GameSettings.MaximumFps);
    public static int ThumbX(Rectangle slider, int value) => slider.Left + 8 + (int)Math.Round(
        (value - GameSettings.MinimumFps) / (double)(GameSettings.MaximumFps - GameSettings.MinimumFps) * (slider.Width - 17));
}
