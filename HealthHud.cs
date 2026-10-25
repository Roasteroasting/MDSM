using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

internal static class HealthHud
{
    public const int HeartCount = Player.MaximumHealth / 2;
    // 1 = dark outline, 2 = fill; rendered with the existing crisp UI pixel.
    private static readonly string[] Pixels =
    {
        "011101110", "122212221", "122222221", "122222221",
        "012222210", "001222100", "000121000", "000010000"
    };
    public static int Fill(int health, int heart) => Math.Clamp(health - heart * 2, 0, 2);
    public static Rectangle Bounds(Viewport viewport)
    {
        Rectangle slot = InventoryLayout.HotbarSlot(viewport, 0);
        int scale = slot.Width >= 42 ? 2 : 1;
        return new Rectangle(slot.Left, slot.Top - 8 * scale - 4, HeartCount * 10 * scale, 8 * scale);
    }
    public static void Draw(SpriteBatch sprites, Texture2D pixel, Viewport viewport, int health)
    {
        Rectangle bounds = Bounds(viewport);
        int scale = bounds.Height / 8;
        for (int heart = 0; heart < HeartCount; heart++)
        {
            int fill = Fill(health, heart);
            for (int y = 0; y < Pixels.Length; y++)
            for (int x = 0; x < Pixels[y].Length; x++)
            {
                char part = Pixels[y][x];
                if (part == '0') continue;
                bool red = fill == 2 || (fill == 1 && x <= 4);
                Color color = part == '1' ? new Color(30, 13, 15) : red ? new Color(221, 43, 54) : new Color(76, 58, 60);
                if (part == '2' && red && y == 2 && x == 2) color = new Color(255, 164, 161);
                sprites.Draw(pixel, new Rectangle(bounds.X + (heart * 10 + x) * scale, bounds.Y + y * scale, scale, scale), color);
            }
        }
    }
}
