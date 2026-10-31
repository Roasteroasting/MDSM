using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

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
    YellowFlower = 13,
    WaterSource = 14,
    WaterFlow1 = 15, WaterFlow2 = 16, WaterFlow3 = 17, WaterFlow4 = 18,
    WaterFlow5 = 19, WaterFlow6 = 20, WaterFlow7 = 21, WaterFalling = 22,
    WhiteWool = 23,
    CraftingTable = 24,
    Glass = 25, IronOre = 26, CoalOre = 27, DiamondOre = 28, Furnace = 29, Chest = 30
}

public enum BlockShape { None, Cube, Cross, Fluid }

public readonly record struct BlockDefinition(string Name, bool Solid, bool Opaque,
    BlockShape Shape, int SideTile, int TopTile = -1, int BottomTile = -1, bool Placeable = true, float MiningSeconds = 2, int FrontTile = -1)
{
    public bool Renderable => Shape != BlockShape.None;
    public bool NeedsGrass => Shape == BlockShape.Cross;
    public int Tile(Vector3 normal) => normal.Y > 0 && TopTile >= 0 ? TopTile
        : normal.Y < 0 && BottomTile >= 0 ? BottomTile : normal.Z<0 && FrontTile>=0 ? FrontTile : SideTile;
}

public static class Blocks
{
    private static readonly BlockDefinition[] Definitions =
    {
        new("AIR", false, false, BlockShape.None, 0, Placeable: false, MiningSeconds: -1),
        new("GRASS", true, true, BlockShape.Cube, 1, 0, 2, MiningSeconds: 1.25f),
        new("DIRT", true, true, BlockShape.Cube, 2, MiningSeconds: 1.25f),
        new("STONE", true, true, BlockShape.Cube, 3, MiningSeconds: 3),
        new("WOOD LOG", true, true, BlockShape.Cube, 4, 5, 5, MiningSeconds: 2.5f),
        new("LEAVES", true, false, BlockShape.Cube, 6, MiningSeconds: 0.4f),
        new("WOODEN PLANKS", true, true, BlockShape.Cube, 7, MiningSeconds: 1.5f),
        new("COBBLESTONE", true, true, BlockShape.Cube, 8, MiningSeconds: 3),
        new("SAND", true, true, BlockShape.Cube, 9, MiningSeconds: 1),
        new("GRAVEL", true, true, BlockShape.Cube, 10, MiningSeconds: 1),
        new("BEDROCK", true, true, BlockShape.Cube, 11, MiningSeconds: -1),
        new("TALL GRASS", false, false, BlockShape.Cross, 12, MiningSeconds: 0),
        new("RED FLOWER", false, false, BlockShape.Cross, 13, MiningSeconds: 0),
        new("YELLOW FLOWER", false, false, BlockShape.Cross, 14, MiningSeconds: 0),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WATER", false, false, BlockShape.Fluid, 0, Placeable: false, MiningSeconds: -1),
        new("WHITE WOOL", true, true, BlockShape.Cube, 15, MiningSeconds: 1.25f),
        new("CRAFTING TABLE", true, true, BlockShape.Cube, 17, 16, 7, MiningSeconds: 2.5f),
        new("GLASS",true,false,BlockShape.Cube,18,MiningSeconds:.45f),
        new("IRON ORE",true,true,BlockShape.Cube,19,MiningSeconds:4.5f),
        new("COAL ORE",true,true,BlockShape.Cube,20,MiningSeconds:4.5f),
        new("DIAMOND ORE",true,true,BlockShape.Cube,21,MiningSeconds:4.5f),
        new("FURNACE",true,true,BlockShape.Cube,23,22,22,MiningSeconds:5.25f,FrontTile:24),
        new("CHEST",true,true,BlockShape.Cube,26,25,25,MiningSeconds:3.75f,FrontTile:27)
    };
    private static readonly BlockType[] Palette =
    {
        BlockType.Grass, BlockType.Dirt, BlockType.Stone, BlockType.WoodLog, BlockType.Leaves,
        BlockType.WoodenPlanks, BlockType.Cobblestone, BlockType.Sand, BlockType.Gravel,
        BlockType.Bedrock, BlockType.TallGrass, BlockType.RedFlower, BlockType.YellowFlower, BlockType.WhiteWool, BlockType.CraftingTable,
        BlockType.Glass,BlockType.IronOre,BlockType.CoalOre,BlockType.DiamondOre,BlockType.Furnace,BlockType.Chest
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
    public static bool Falls(BlockType block) => block is BlockType.Sand or BlockType.Gravel;
}
