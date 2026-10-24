using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public sealed class World
{
    public const int Width = 32;
    public const int Height = 16;
    public const int Depth = 32;
    public const int GroundHeight = 5;

    private readonly BlockType[,,] _blocks = new BlockType[Width, Height, Depth];
    public int Revision { get; private set; }
    public Vector3 Spawn => new(Width / 2f + 0.5f, GroundHeight, Depth / 2f + 6.5f);

    public World()
    {
        for (int x = 0; x < Width; x++)
        for (int z = 0; z < Depth; z++)
        for (int y = 0; y < GroundHeight; y++)
            _blocks[x, y, z] = y == GroundHeight - 1 ? BlockType.Grass
                : y >= GroundHeight - 3 ? BlockType.Dirt : BlockType.Stone;
    }

    public bool Contains(int x, int y, int z) =>
        x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;

    public BlockType GetBlock(int x, int y, int z) =>
        Contains(x, y, z) ? _blocks[x, y, z] : BlockType.Air;

    public bool IsSolid(int x, int y, int z) => GetBlock(x, y, z) != BlockType.Air;

    public bool SetBlock(int x, int y, int z, BlockType block)
    {
        if (!Contains(x, y, z) || (block != BlockType.Air && !Blocks.IsPlaceable(block))
            || _blocks[x, y, z] == block)
            return false;

        _blocks[x, y, z] = block;
        Revision++;
        return true;
    }

    public bool TryPlace(Point3 cell, BlockType block, BoundingBox playerBounds)
    {
        if (!Blocks.IsPlaceable(block) || !Contains(cell.X, cell.Y, cell.Z)
            || IsSolid(cell.X, cell.Y, cell.Z) || Overlaps(Bounds(cell), playerBounds))
            return false;

        return SetBlock(cell.X, cell.Y, cell.Z, block);
    }

    public static BoundingBox Bounds(Point3 cell) =>
        new(new Vector3(cell.X, cell.Y, cell.Z), new Vector3(cell.X + 1, cell.Y + 1, cell.Z + 1));

    // Strict overlap lets a player stand on or beside a block without intersecting it.
    public static bool Overlaps(BoundingBox a, BoundingBox b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X &&
        a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y &&
        a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

    public bool IntersectsSolid(BoundingBox bounds)
    {
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
            if (IsSolid(x, y, z) && Overlaps(bounds, Bounds(new Point3(x, y, z))))
                return true;

        return false;
    }
}

public readonly record struct Point3(int X, int Y, int Z)
{
    public static Point3 operator +(Point3 a, Point3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}
