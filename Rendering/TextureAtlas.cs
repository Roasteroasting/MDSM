using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Rendering;

public sealed class TextureAtlas
{
    public const int TileSize = 16;
    public const int TilesPerRow = 16;
    public const int TileCount = TilesPerRow * TilesPerRow;
    public const int AtlasSize = TileSize * TilesPerRow;

    // Owned by the ContentManager supplied to the constructor, not by this wrapper.
    public Texture2D Texture { get; }
    public Texture2D Porkchop { get; }
    public Texture2D Water { get; }

    public Texture2D ItemTexture(ItemId item) => item == Items.RawPorkchop ? Porkchop : Texture;
    public Rectangle ItemSource(ItemId item) => item == Items.RawPorkchop ? Porkchop.Bounds
        : Items.TryGetBlock(item, out BlockType block) ? Source(Blocks.Tile(block, Vector3.Up)) : Rectangle.Empty;

    public TextureAtlas(ContentManager content)
    {
        Texture = content.Load<Texture2D>("terrain");
        Porkchop = content.Load<Texture2D>("raw-porkchop");
        Water = content.Load<Texture2D>("water");
        if (Water.Width != 16 || Water.Height != 16) throw new ContentLoadException("water.png must be 16x16.");
        if (Porkchop.Width != 16 || Porkchop.Height != 16) throw new ContentLoadException("raw-porkchop.png must be 16x16.");
        if (Texture.Width != AtlasSize || Texture.Height != AtlasSize)
            throw new ContentLoadException($"terrain.png must be {AtlasSize}x{AtlasSize} pixels with {TileSize}x{TileSize} tiles.");
    }

    public static Rectangle Source(int tile)
    {
        if ((uint)tile >= TileCount)
            throw new ArgumentOutOfRangeException(nameof(tile), tile, $"Tile index must be between 0 and {TileCount - 1}.");

        return new Rectangle(tile % TilesPerRow * TileSize, tile / TilesPerRow * TileSize, TileSize, TileSize);
    }

    public static Vector2 Uv(int tile, float u, float v)
    {
        Rectangle source = Source(tile);
        // Sample inside texel centers to keep adjacent atlas tiles from bleeding.
        return new Vector2((source.X + 0.5f + u * (TileSize - 1)) / AtlasSize,
            (source.Y + 0.5f + v * (TileSize - 1)) / AtlasSize);
    }
}
