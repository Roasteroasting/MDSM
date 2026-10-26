using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Fluid state occupies the existing block byte; no parallel per-block allocation/save tail.
public static class Water
{
    public const int MaximumLevel = 7;
    public static bool IsWater(BlockType block) => block >= BlockType.WaterSource && block <= BlockType.WaterFalling;
    public static bool IsSource(BlockType block) => block == BlockType.WaterSource;
    public static int Level(BlockType block) => IsSource(block) || block == BlockType.WaterFalling ? 0
        : IsWater(block) ? (int)block - (int)BlockType.WaterSource : 8;
    public static BlockType Flow(int level) => level >= 1 && level <= MaximumLevel
        ? (BlockType)((int)BlockType.WaterSource + level) : throw new ArgumentOutOfRangeException(nameof(level));
    public static float Height(BlockType block) => block == BlockType.WaterFalling ? 1
        : IsWater(block) ? (8 - Level(block)) / 9f : 0;
    public static bool Replaceable(BlockType block) => block == BlockType.Air || IsWater(block)
        || Blocks.Get(block).Shape == BlockShape.Cross;

    // Each shared corner uses exactly the same four world cells, even across chunk seams.
    public static float CornerHeight(World world, int cornerX, int y, int cornerZ)
    {
        float sum = 0; int weight = 0;
        for (int z = cornerZ - 1; z <= cornerZ; z++)
        for (int x = cornerX - 1; x <= cornerX; x++)
        {
            BlockType block = world.GetBlock(x, y, z);
            if (!IsWater(block)) continue;
            if (IsWater(world.GetBlock(x, y + 1, z))) return 1;
            int w = IsSource(block) ? 4 : 1;
            sum += Height(block) * w; weight += w;
        }
        return weight == 0 ? 0 : sum / weight;
    }

    public static bool Intersects(World world, BoundingBox bounds)
    {
        // Most entities occupy dry chunks. Avoid repeated voxel/height queries there.
        bool wet = false;
        for (int cz = Math.Max(0, (int)MathF.Floor(bounds.Min.Z / Chunk.Size)); cz <= Math.Min(world.ChunksZ - 1, (int)MathF.Floor(bounds.Max.Z / Chunk.Size)); cz++)
        for (int cx = Math.Max(0, (int)MathF.Floor(bounds.Min.X / Chunk.Size)); cx <= Math.Min(world.ChunksX - 1, (int)MathF.Floor(bounds.Max.X / Chunk.Size)); cx++)
            wet |= world.GetChunk(cx, cz).WaterCount != 0;
        if (!wet) return false;
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {
            if (!IsWater(world.GetBlock(x, y, z))) continue;
            float height = (CornerHeight(world, x, y, z) + CornerHeight(world, x + 1, y, z)
                + CornerHeight(world, x, y, z + 1) + CornerHeight(world, x + 1, y, z + 1)) * 0.25f;
            if (World.Overlaps(bounds, new BoundingBox(new Vector3(x, y, z), new Vector3(x + 1, y + height, z + 1)))) return true;
        }
        return false;
    }
}
