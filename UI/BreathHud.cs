using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM.UI;

internal static class BreathHud
{
    public static Rectangle Bounds(Viewport viewport, int armorPoints)
    {
        Rectangle area=ClassicHudLayout.Armor(viewport);int scale=ClassicHudLayout.Scale(viewport);
        return new(area.X,area.Y-(armorPoints>20?20:10)*scale,81*scale,8*scale);
    }
    public static void Draw(SpriteBatch sprites, Texture2D pixel, Viewport viewport, int armorPoints, int bubbles)
    {
        Rectangle area = Bounds(viewport, armorPoints); int scale = area.Height / 8;
        for (int i = 0; i < 10; i++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            int dx = x * 2 - 7, dy = y * 2 - 7, radius = dx * dx + dy * dy;
            if (radius > 49) continue;
            Color color = radius >= 25 ? new Color(52, 143, 157) : i < bubbles ? new Color(134, 219, 214) : new Color(35, 61, 71);
            if (i < bubbles && x == 2 && y is 2 or 3) color = Color.White;
            sprites.Draw(pixel, new Rectangle(area.X + (i * 8 + x) * scale, area.Y + y * scale, scale, scale), color);
        }
    }
}
