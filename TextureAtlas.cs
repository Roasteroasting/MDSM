using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

public sealed class TextureAtlas : IDisposable
{
    public const int TileSize = 16;
    public const int TileCount = 4;
    public Texture2D Texture { get; }

    public TextureAtlas(GraphicsDevice device)
    {
        Texture = new Texture2D(device, TileSize * TileCount, TileSize, false, SurfaceFormat.Color);
        var pixels = new Color[Texture.Width * Texture.Height];
        var random = new Random(173);
        for (int tile = 0; tile < TileCount; tile++)
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
        {
            Color color = tile switch
            {
                0 => new Color(92, 151, 53),
                1 when y < 3 || (y < 6 && (x * 7 % 5) > y - 3) => new Color(92, 151, 53),
                1 or 2 => new Color(131, 91, 57),
                _ => new Color(126, 128, 131)
            };
            int noise = random.Next(-19, 20);
            if (tile == 3 && (x + y * 3) % 11 == 0)
                noise -= 20;
            pixels[y * Texture.Width + tile * TileSize + x] = new Color(
                Math.Clamp(color.R + noise, 0, 255), Math.Clamp(color.G + noise, 0, 255), Math.Clamp(color.B + noise, 0, 255));
        }
        Texture.SetData(pixels);
    }

    public static Rectangle Source(int tile) => new(tile * TileSize, 0, TileSize, TileSize);

    public static Vector2 Uv(int tile, float u, float v) => new(
        // Sample inside texel centers to keep adjacent atlas tiles from bleeding.
        (tile * TileSize + 0.5f + u * (TileSize - 1)) / (TileSize * TileCount),
        (0.5f + v * (TileSize - 1)) / TileSize);

    public void Dispose() => Texture.Dispose();
}
