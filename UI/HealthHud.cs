using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Gameplay;

namespace MDSM.UI;

internal static class HealthHud
{
    public const int HeartCount = Player.MaximumHealth / 2;
    // 1 = dark outline, 2 = fill; rendered with the existing crisp UI pixel.
    private static readonly string[] Pixels =
    {
        "001101100", "012212210", "122222221", "122222221",
        "012222210", "001222100", "000121000", "000010000"
    };
    public static int Fill(int health, int heart) => Math.Clamp(health - heart * 2, 0, 2);
    public static Rectangle Bounds(Viewport viewport)
    {
        Rectangle bar=ClassicHudLayout.Bar(viewport);int scale=ClassicHudLayout.Scale(viewport);
        return new Rectangle(bar.Left+scale,bar.Top-10*scale,81*scale,9*scale);
    }
    public static void Draw(SpriteBatch sprites, Texture2D pixel, Viewport viewport, int health)
    {
        Rectangle bounds = Bounds(viewport);
        int scale = ClassicHudLayout.Scale(viewport);
        for (int heart = 0; heart < HeartCount; heart++)
        {
            int fill = Fill(health, heart);
            for (int y = 0; y < Pixels.Length; y++)
            for (int x = 0; x < Pixels[y].Length; x++)
            {
                char part = Pixels[y][x];
                if (part == '0') continue;
                bool red = fill == 2 || (fill == 1 && x <= 4);
                Color color = part == '1' ? MdsmUiStyle.Edge : red ? new Color(234, 111, 101) : new Color(67, 52, 56);
                if (part == '2' && red && y == 1 && x is 2 or 6) color = new Color(255, 210, 167);
                sprites.Draw(pixel, new Rectangle(bounds.X + (heart * 8 + x) * scale, bounds.Y + y * scale, scale, scale), color);
            }
        }
    }
}
