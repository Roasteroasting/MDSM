using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed class World
{
    public const int Height = 128;
    public WorldSize Size { get; }
    public int Width => Size.Width();
    public int Depth => Width;
    public int ChunksX => Width / Chunk.Size;
    public int ChunksZ => Depth / Chunk.Size;
    public int ChunkCount => ChunksX * ChunksZ;
    public int BlockCount => Width * Height * Depth;
    public const int DefaultSeed = 173;

    private readonly Chunk[] _chunks;
    public ReadOnlySpan<Chunk> Chunks => _chunks;
    public int Seed { get; }
    public int Revision { get; private set; }
    public FluidSimulation Fluids { get; }
    public FallingBlockSimulation Gravity { get; }
    public WaterFeatureCounts GeneratedWaterFeatures { get; private set; }
    public Vector3 Spawn => new(Width / 2f + 0.5f, SurfaceY(Width / 2, Depth / 2) + 1.01f, Depth / 2f + 0.5f);

    public World(int seed = DefaultSeed, WorldSize size = WorldSize.Normal) : this(seed, size, true) { }

    // Loading and test fixtures fill empty storage directly without generating terrain.
    internal World(int seed, bool generateTerrain) : this(seed, WorldSize.Normal, generateTerrain) { }

    internal World(int seed, WorldSize size, bool generateTerrain)
    {
        Seed = seed;
        Fluids = new FluidSimulation(this);
        Gravity = new FallingBlockSimulation(this);
        _ = size.Width(); // Validate before allocating storage.
        Size = size;
        _chunks = new Chunk[ChunkCount];
        for (int z = 0; z < ChunksZ; z++)
        for (int x = 0; x < ChunksX; x++)
            _chunks[z * ChunksX + x] = new Chunk(x, z);

        if (generateTerrain)
        {
            new TerrainGenerator(seed).Generate(this);
            new WorldDecorator(seed).Decorate(this);
        }
    }

    // Explicit new-world pipeline. Base-terrain fixtures and deserialization never add lakes.
    public static World CreateNew(int seed, WorldSize size = WorldSize.Normal, Action<string> progress = null)
    {
        progress?.Invoke("ALLOCATING WORLD");
        var world = new World(seed, size, false);
        progress?.Invoke("GENERATING TERRAIN");
        new TerrainGenerator(seed).Generate(world);
        progress?.Invoke("CARVING PONDS, RIVERS AND LAKES");
        var water = new LakeGenerator(seed); water.Generate(world); world.GeneratedWaterFeatures = water.Counts;
        progress?.Invoke("PLACING TREES AND VEGETATION");
        new WorldDecorator(seed).Decorate(world);
        progress?.Invoke("INITIALIZING WATER");
        world.Fluids.Initialize();
        return world;
    }

    public Chunk GetChunk(int x, int z)
    {
        if ((uint)x >= ChunksX || (uint)z >= ChunksZ)
            throw new ArgumentOutOfRangeException(nameof(x), "Chunk coordinates must be inside this world.");
        return _chunks[z * ChunksX + x];
    }

    public int SurfaceY(int x, int z)
    {
        if ((uint)x >= Width || (uint)z >= Depth)
            return -1;
        return GetChunk(x / Chunk.Size, z / Chunk.Size).SurfaceY(x % Chunk.Size, z % Chunk.Size);
    }

    public bool Contains(int x, int y, int z) =>
        x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;

    public BlockType GetBlock(int x, int y, int z) =>
        Contains(x, y, z) ? _chunks[(z / Chunk.Size) * ChunksX + x / Chunk.Size]
            .GetBlock(x % Chunk.Size, y, z % Chunk.Size) : BlockType.Air;

    public bool IsSolid(int x, int y, int z) => Blocks.IsSolid(GetBlock(x, y, z));

    public bool SetBlock(int x, int y, int z, BlockType block)
    {
        if (!Contains(x, y, z) || !Blocks.IsDefined(block)
            || GetBlock(x, y, z) == block)
            return false;

        int chunkX = x / Chunk.Size, chunkZ = z / Chunk.Size;
        int localX = x % Chunk.Size, localZ = z % Chunk.Size;
        Chunk chunk = GetChunk(chunkX, chunkZ);
        BlockType previous = chunk.GetBlock(localX, y, localZ);
        chunk.SetBlock(localX, y, localZ, block);
        // Air/fluid both expose terrain faces; changing a flow level only invalidates water.
        bool terrainChanged = !(previous == BlockType.Air || Water.IsWater(previous))
            || !(block == BlockType.Air || Water.IsWater(block));
        if (terrainChanged)
        {
            chunk.MarkDirty();
            // Only face-sharing neighbors can gain or lose an exposed face.
            if (localX == 0) MarkDirty(chunkX - 1, chunkZ);
            if (localX == Chunk.Size - 1) MarkDirty(chunkX + 1, chunkZ);
            if (localZ == 0) MarkDirty(chunkX, chunkZ - 1);
            if (localZ == Chunk.Size - 1) MarkDirty(chunkX, chunkZ + 1);
        }
        // Shared water corners also depend on diagonally adjacent chunks.
        for (int cz = chunkZ - (localZ == 0 ? 1 : 0); cz <= chunkZ + (localZ == Chunk.Size - 1 ? 1 : 0); cz++)
        for (int cx = chunkX - (localX == 0 ? 1 : 0); cx <= chunkX + (localX == Chunk.Size - 1 ? 1 : 0); cx++)
            if ((uint)cx < ChunksX && (uint)cz < ChunksZ) GetChunk(cx, cz).MarkWaterDirty();
        Revision++;
        Fluids.Changed(new Point3(x, y, z), previous, block);
        Gravity.Changed(x, z, previous, block);
        if (!Blocks.SupportsPlant(block) && Blocks.Get(GetBlock(x, y + 1, z)).NeedsGrass)
            SetBlock(x, y + 1, z, BlockType.Air);
        return true;
    }

    // Used only before meshes exist. Decorations span chunk edges without invalidation work.
    internal void SetGeneratedBlock(int x, int y, int z, BlockType block)
    {
        if (!Contains(x, y, z)) throw new ArgumentOutOfRangeException(nameof(x));
        if (Blocks.Falls(block) && (y == 0 || !IsSolid(x, y - 1, z)))
            throw new InvalidOperationException("Generated Sand and Gravel require solid support.");
        _chunks[(z / Chunk.Size) * ChunksX + x / Chunk.Size].SetBlock(x % Chunk.Size, y, z % Chunk.Size, block);
    }

    private void MarkDirty(int x, int z)
    {
        if ((uint)x < ChunksX && (uint)z < ChunksZ)
            _chunks[z * ChunksX + x].MarkDirty();
    }

    public bool TryPlace(Point3 cell, BlockType block, BoundingBox playerBounds)
    {
        BlockDefinition definition = Blocks.Get(block);
        if (!definition.Placeable || !Contains(cell.X, cell.Y, cell.Z)
            || (GetBlock(cell.X, cell.Y, cell.Z) != BlockType.Air
                && !(definition.Solid && Water.IsWater(GetBlock(cell.X, cell.Y, cell.Z))))
            || (definition.Solid && Overlaps(Bounds(cell), playerBounds))
            || (definition.Solid && Gravity.Intersects(Bounds(cell)))
            || (definition.NeedsGrass && !Blocks.SupportsPlant(GetBlock(cell.X, cell.Y - 1, cell.Z))))
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
