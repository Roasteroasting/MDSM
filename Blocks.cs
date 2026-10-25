using System;
using Microsoft.Xna.Framework;

namespace MDSM;

// Explicit values are persistent on-disk IDs. Never reorder or reuse them.
public enum BlockType : byte
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Stone = 3,
    WoodLog = 4,
    Leaves = 5,
    WoodenPlanks = 6,
    Cobblestone = 7,
    Sand = 8,
    Gravel = 9,
    Bedrock = 10,
    TallGrass = 11,
    RedFlower = 12,
    YellowFlower = 13
}

public enum BlockShape { None, Cube, Cross }

public readonly record struct BlockDefinition(string Name, bool Solid, bool Opaque,
    BlockShape Shape, int SideTile, int TopTile = -1, int BottomTile = -1, bool Placeable = true)
{
    public bool Renderable => Shape != BlockShape.None;
    public bool NeedsGrass => Shape == BlockShape.Cross;
    public int Tile(Vector3 normal) => normal.Y > 0 && TopTile >= 0 ? TopTile
        : normal.Y < 0 && BottomTile >= 0 ? BottomTile : SideTile;
}

public static class Blocks
{
    private static readonly BlockDefinition[] Definitions =
    {
        new("AIR", false, false, BlockShape.None, 0, Placeable: false),
        new("GRASS", true, true, BlockShape.Cube, 1, 0, 2),
        new("DIRT", true, true, BlockShape.Cube, 2),
        new("STONE", true, true, BlockShape.Cube, 3),
        new("WOOD LOG", true, true, BlockShape.Cube, 4, 5, 5),
        new("LEAVES", true, false, BlockShape.Cube, 6),
        new("WOODEN PLANKS", true, true, BlockShape.Cube, 7),
        new("COBBLESTONE", true, true, BlockShape.Cube, 8),
        new("SAND", true, true, BlockShape.Cube, 9),
        new("GRAVEL", true, true, BlockShape.Cube, 10),
        new("BEDROCK", true, true, BlockShape.Cube, 11),
        new("TALL GRASS", false, false, BlockShape.Cross, 12),
        new("RED FLOWER", false, false, BlockShape.Cross, 13),
        new("YELLOW FLOWER", false, false, BlockShape.Cross, 14)
    };
    private static readonly BlockType[] Palette =
    {
        BlockType.Grass, BlockType.Dirt, BlockType.Stone, BlockType.WoodLog, BlockType.Leaves,
        BlockType.WoodenPlanks, BlockType.Cobblestone, BlockType.Sand, BlockType.Gravel,
        BlockType.Bedrock, BlockType.TallGrass, BlockType.RedFlower, BlockType.YellowFlower
    };
    public static ReadOnlySpan<BlockType> Selection => Palette;
    public static bool IsDefined(BlockType block) => (int)block < Definitions.Length;
    public static BlockDefinition Get(BlockType block) => IsDefined(block) ? Definitions[(int)block] : Definitions[0];
    public static bool IsSolid(BlockType block) => Get(block).Solid;
    public static bool IsOpaque(BlockType block) => Get(block).Opaque;
    public static bool IsRenderable(BlockType block) => Get(block).Renderable;
    public static bool IsPlaceable(BlockType block) => Get(block).Placeable;
    public static int Tile(BlockType block, Vector3 normal) => Get(block).Tile(normal);
    public static bool SupportsPlant(BlockType block) => block == BlockType.Grass;
}
