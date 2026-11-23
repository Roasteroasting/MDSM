using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Fluid state occupies the existing block byte; no parallel per-block allocation/save tail.
public static class Water
{
    public const int MaximumLevel = 7;
    public static bool IsWater(BlockType block) => block is >= BlockType.WaterSource and <= BlockType.WaterFalling
        or >= BlockType.WaterFalling1 and <= BlockType.WaterFalling7;
    public static bool IsSource(BlockType block) => block == BlockType.WaterSource;
    // Approximate Beta's steady drift within MDSM's swept movement and drag.
    internal const float CurrentSpeed = 1.1f;
    internal static int Decay(BlockType block) => block >= BlockType.WaterFalling1
        ? 9 + (int)block - (int)BlockType.WaterFalling1 : (int)block - (int)BlockType.WaterSource;
    public static int Level(BlockType block) => IsWater(block) ? Decay(block) : -1;
    public static BlockType Flow(int level) => level >= 1 && level <= MaximumLevel
        ? (BlockType)((int)BlockType.WaterSource + level) : throw new ArgumentOutOfRangeException(nameof(level));
    public static float Height(BlockType block) => IsWater(block) ? (8 - (Decay(block) >= 8 ? 0 : Decay(block))) / 9f : 0;
    public static bool Replaceable(BlockType block) => block == BlockType.Air || IsWater(block)
        || Blocks.Get(block).NeedsGrass || block is BlockType.Fire or BlockType.Snow;

    // Each shared corner uses exactly the same four world cells, even across chunk seams.
    public static float CornerHeight(World world,int x,int y,int z)=>CornerHeight(world,world.OriginX+x,y,world.OriginZ+z);
    public static float CornerHeight(World world, long cornerX, int y, long cornerZ) => LiquidCornerHeight(world,cornerX,y,cornerZ,false);
    internal static float LiquidCornerHeight(World world, long cornerX, int y, long cornerZ,bool lava)
    {
        float sum = 0; int weight = 0;
        for (long z = cornerZ - 1; z <= cornerZ; z++)
        for (long x = cornerX - 1; x <= cornerX; x++)
        {
            BlockType block = world.GetBlock(x, y, z);
            if (!(lava ? Lava.IsLava(block) : IsWater(block)))
            {
                if (!Blocks.IsSolid(block) && (!world.IsInfinite || world.IsLoaded(new(x,y,z)))) weight++;
                continue;
            }
            BlockType above=world.GetBlock(x,y+1,z);
            if (lava ? Lava.IsLava(above) : IsWater(above)) return 1;
            int level=lava ? Lava.Decay(block) : Decay(block);
            int w = level==0 || level>=8 ? 11 : 1;
            sum += (8-(level>=8 ? 0 : level))/9f*w; weight += w;
        }
        return weight == 0 ? 0 : sum / weight;
    }

    public static bool Intersects(World world, BoundingBox bounds) => Sample(world,bounds,false,out _);

    internal static bool Intersects(World world, BoundingBox bounds, out Vector3 current) => Sample(world,bounds,true,out current);

    private static Vector3 Normalize(Vector3 value) => value == Vector3.Zero ? value : Vector3.Normalize(value);

    internal static Vector3 FlowVector(World world, Point3 cell, BlockType block)
    {
        int decay = Decay(block), current = decay >= 8 ? 0 : decay;
        Vector3 flow = Vector3.Zero;
        bool wall = false;
        foreach (Point3 direction in FluidSimulation.Horizontal)
        {
            Point3 near = cell + direction;
            BlockType neighbor = world.GetBlock(near.X,near.Y,near.Z);
            bool solid = Blocks.IsSolid(neighbor);
            if (decay >= 8) wall |= solid || Blocks.IsSolid(world.GetBlock(near.X,near.Y+1,near.Z));
            BlockType sample = IsWater(neighbor) ? neighbor : solid ? BlockType.Air : world.GetBlock(near.X,near.Y-1,near.Z);
            if (!IsWater(sample)) continue;
            int depth = Decay(sample);
            int difference = (depth >= 8 ? 0 : depth) - current + (IsWater(neighbor) ? 0 : 8);
            flow += new Vector3(direction.X,0,direction.Z)*difference;
        }
        if (wall) flow = Normalize(flow) - Vector3.Up*6;
        return Normalize(flow);
    }

    private static bool Sample(World world, BoundingBox bounds, bool collectCurrent, out Vector3 current)
    {
        current=Vector3.Zero;
        // Most entities occupy dry chunks. Avoid repeated voxel/height queries there.
        bool wet = false;
        for (int cz = Math.Max(world.LocalMinZ/16, (int)MathF.Floor(bounds.Min.Z / Chunk.Size)); cz <= Math.Min((world.LocalMaxZ-1)/16, (int)MathF.Floor(bounds.Max.Z / Chunk.Size)); cz++)
        for (int cx = Math.Max(world.LocalMinX/16, (int)MathF.Floor(bounds.Min.X / Chunk.Size)); cx <= Math.Min((world.LocalMaxX-1)/16, (int)MathF.Floor(bounds.Max.X / Chunk.Size)); cx++)
            wet |= world.TryGetLocalChunk(cx,cz,out var chunk) && chunk.WaterCount != 0;
        if (!wet) return false;
        bool intersects=false;
        for (int x = Math.Max(world.LocalMinX, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.LocalMaxX - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(world.LocalMinZ, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.LocalMaxZ - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {
            BlockType block=world.GetBlock(x,y,z);
            if (!IsWater(block)) continue;
            float height = (CornerHeight(world, x, y, z) + CornerHeight(world, x + 1, y, z)
                + CornerHeight(world, x, y, z + 1) + CornerHeight(world, x + 1, y, z + 1)) * 0.25f;
            if (!World.Overlaps(bounds,new BoundingBox(new Vector3(x,y,z),new Vector3(x+1,y+height,z+1)))) continue;
            if (!collectCurrent) return true;
            intersects=true;
            current+=FlowVector(world,world.GlobalCell(x,y,z),block);
        }
        current=Normalize(current);
        return intersects;
    }
}
