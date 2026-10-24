using Microsoft.Xna.Framework;

namespace MDSM;

public enum BlockType : byte
{
    Air,
    Grass,
    Dirt,
    Stone
}

public static class Blocks
{
    // Atlas tiles: grass top, grass side, dirt, stone.
    public static int Tile(BlockType block, Vector3 normal) => block switch
    {
        BlockType.Grass => normal.Y > 0 ? 0 : normal.Y < 0 ? 2 : 1,
        BlockType.Dirt => 2,
        BlockType.Stone => 3,
        _ => 2
    };

    public static bool IsPlaceable(BlockType block) =>
        block is BlockType.Grass or BlockType.Dirt or BlockType.Stone;
}
