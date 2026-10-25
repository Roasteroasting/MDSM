using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public sealed class World
{
    public const int Width = 256;
    public const int Height = 64;
    public const int Depth = 256;
    public const int ChunksX = Width / Chunk.Size;
    public const int ChunksZ = Depth / Chunk.Size;
    public const int ChunkCount = ChunksX * ChunksZ;
    public const int BlockCount = Width * Height * Depth;
    public const int DefaultSeed = 173;

    private readonly Chunk[] _chunks = new Chunk[ChunkCount];
    public ReadOnlySpan<Chunk> Chunks => _chunks;
    public int Seed { get; }
    public int Revision { get; private set; }
    public Vector3 Spawn => new(Width / 2f + 0.5f, SurfaceY(Width / 2, Depth / 2) + 1.01f, Depth / 2f + 0.5f);

    public World(int seed = DefaultSeed) : this(seed, true) { }

    // Loading and test fixtures fill empty storage directly without generating terrain.
    internal World(int seed, bool generateTerrain)
    {
        Seed = seed;
        for (int z = 0; z < ChunksZ; z++)
        for (int x = 0; x < ChunksX; x++)
            _chunks[z * ChunksX + x] = new Chunk(x, z);

        if (generateTerrain)
            new TerrainGenerator(seed).Generate(this);
    }

    public Chunk GetChunk(int x, int z)
    {
        if ((uint)x >= ChunksX || (uint)z >= ChunksZ)
            throw new ArgumentOutOfRangeException(nameof(x), "Chunk coordinates must be between 0 and 15.");
        return _chunks[z * ChunksX + x];
    }

    public int SurfaceY(int x, int z)
    {
        if ((uint)x >= Width || (uint)z >= Depth)
            return -1;
        for (int y = Height - 1; y >= 0; y--)
            if (IsSolid(x, y, z))
                return y;
        return -1;
    }

    public bool Contains(int x, int y, int z) =>
        x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;

    public BlockType GetBlock(int x, int y, int z) =>
        Contains(x, y, z) ? _chunks[(z / Chunk.Size) * ChunksX + x / Chunk.Size]
            .GetBlock(x % Chunk.Size, y, z % Chunk.Size) : BlockType.Air;

    public bool IsSolid(int x, int y, int z) => GetBlock(x, y, z) != BlockType.Air;

    public bool SetBlock(int x, int y, int z, BlockType block)
    {
        if (!Contains(x, y, z) || (block != BlockType.Air && !Blocks.IsPlaceable(block))
            || GetBlock(x, y, z) == block)
            return false;

        int chunkX = x / Chunk.Size, chunkZ = z / Chunk.Size;
        int localX = x % Chunk.Size, localZ = z % Chunk.Size;
        Chunk chunk = GetChunk(chunkX, chunkZ);
        chunk.SetBlock(localX, y, localZ, block);
        chunk.MarkDirty();
        // Only face-sharing neighbors can gain or lose an exposed face.
        if (localX == 0) MarkDirty(chunkX - 1, chunkZ);
        if (localX == Chunk.Size - 1) MarkDirty(chunkX + 1, chunkZ);
        if (localZ == 0) MarkDirty(chunkX, chunkZ - 1);
        if (localZ == Chunk.Size - 1) MarkDirty(chunkX, chunkZ + 1);
        Revision++;
        return true;
    }

    private void MarkDirty(int x, int z)
    {
        if ((uint)x < ChunksX && (uint)z < ChunksZ)
            _chunks[z * ChunksX + x].MarkDirty();
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
