using System;
using Microsoft.Xna.Framework;
using MDSM.Audio;

namespace MDSM.Worlds;

public sealed partial class World
{
    public const int Height = 128;
    public WorldSize Size { get; }
    public bool IsInfinite=>Size==WorldSize.Infinite;
    public int Width => Size.Width();
    public int Depth => Width;
    public int ChunksX => Width / Chunk.Size;
    public int ChunksZ => Depth / Chunk.Size;
    public int ChunkCount => IsInfinite?_residentChunks.Length:ChunksX * ChunksZ;
    public int BlockCount => Width * Height * Depth;
    public const int DefaultSeed = 173;

    private readonly Chunk[] _chunks;
    public ReadOnlySpan<Chunk> Chunks => IsInfinite?_residentChunks:_chunks;
    public int Seed { get; }
    public int Revision { get; private set; }
    internal SoundEvents Sounds { get; set; }
    public FluidSimulation Fluids { get; }
    public FluidSimulation LavaFlow { get; }
    public FallingBlockSimulation Gravity { get; }
    public BlockEntities Containers { get; }
    public VegetationSimulation Vegetation { get; }
    public WorldTime Time { get; } = new();
    public BlockLighting Lighting { get; }
    public BlockParticles Particles { get; }=new();
    public Climate Climate {get;}
    public Weather Weather {get;}
    public FireSimulation Fire {get;}
    public MDSM.Core.GameMode? SavedGameMode { get; internal set; } = MDSM.Core.GameMode.Survival;
    public MDSM.Core.Difficulty SavedDifficulty { get; internal set; } = MDSM.Core.Difficulty.Normal;
    public bool AllowCheats { get; internal set; } = true; // Legacy worlds/test fixtures retain command access.
    public WaterFeatureCounts GeneratedWaterFeatures { get; private set; }
    public OreGenerationStats GeneratedOres { get; private set; }
    public System.Collections.Generic.IReadOnlyList<Point3> GeneratedDungeons {get;private set;}=Array.Empty<Point3>();
    public System.Collections.Generic.IReadOnlyList<NaturalLake> GeneratedLakes { get; private set; } = Array.Empty<NaturalLake>();
    public Vector3 Spawn => IsInfinite?LocalPosition(InfiniteSpawn):new(Width / 2f + 0.5f, SurfaceY(Width / 2, Depth / 2) + 1.01f, Depth / 2f + 0.5f);

    public World(int seed = DefaultSeed, WorldSize size = WorldSize.Normal) : this(seed, size, true) { }

    // Loading and test fixtures fill empty storage directly without generating terrain.
    internal World(int seed, bool generateTerrain) : this(seed, WorldSize.Normal, generateTerrain) { }

    internal World(int seed, WorldSize size, bool generateTerrain)
    {
        Seed = seed;
        Climate=new(seed);Weather=new(this);Fire=new(this);
        Lighting = new BlockLighting(this);
        Farming=new FarmingSimulation(this);
        Vegetation=new VegetationSimulation(this);
        Containers=new BlockEntities(this);
        Fluids = new FluidSimulation(this);
        LavaFlow=new FluidSimulation(this,true);
        Gravity = new FallingBlockSimulation(this);
        Size = size;
        if(IsInfinite)
        {
            if(generateTerrain)throw new InvalidOperationException("Infinite terrain must be requested through the chunk lifecycle.");
            _chunks=Array.Empty<Chunk>();return;
        }
        _ = size.Width(); // Validate before allocating storage.
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
        world.Climate.VariedGeneration=true;
        world.SavedGameMode=MDSM.Core.GameMode.Survival;
        world.AllowCheats=false;
        progress?.Invoke("GENERATING TERRAIN");
        new TerrainGenerator(seed,true,world.Width).Generate(world);
        progress?.Invoke("CARVING PONDS, RIVERS AND LAKES");
        var water = new LakeGenerator(seed); water.Generate(world); world.GeneratedWaterFeatures = water.Counts;
        world.GeneratedLakes = new System.Collections.Generic.List<NaturalLake>(water.Lakes);
        progress?.Invoke("CARVING CAVES AND LAVA POOLS");
        world.GeneratedCaves=CaveGenerator.Generate(world,water.Footprints);
        progress?.Invoke("BUILDING CAVE DUNGEONS");
        world.GeneratedDungeons=DungeonGenerator.Generate(world);
        progress?.Invoke("PLACING TREES AND VEGETATION");
        new WorldDecorator(seed).Decorate(world);
        DesertVegetation.Generate(world);
        world.Climate.SnowCover(world);
        progress?.Invoke("PLACING COAL, IRON AND DIAMONDS");
        UndergroundDeposits.Generate(world);
        world.GeneratedOres=OreGenerator.Generate(world);
        world.GeneratedCaves=world.GeneratedCaves with {ExposedOres=CaveGenerator.ExposedOres(world)};
        progress?.Invoke("INITIALIZING WATER");
        world.Fluids.Initialize();
        world.LavaFlow.Initialize();
        return world;
    }

    public Chunk GetChunk(long x, long z)
    {
        if(IsInfinite)return _resident.TryGetValue(new(x,z),out var loaded)?loaded:throw new InvalidOperationException("Chunk is not resident.");
        if (x<0 || x>=ChunksX || z<0 || z>=ChunksZ)
            throw new ArgumentOutOfRangeException(nameof(x), "Chunk coordinates must be inside this world.");
        return _chunks[checked((int)(z * ChunksX + x))];
    }

    public int SurfaceY(long x, long z)
    {
        if(IsInfinite)return TryGetChunkAt(x,z,out var loaded)?loaded.SurfaceY(Streaming.Coordinates.Mod(x,16),Streaming.Coordinates.Mod(z,16)):-1;
        if (x<0 || x>=Width || z<0 || z>=Depth)
            return -1;
        return GetChunk(x / Chunk.Size, z / Chunk.Size).SurfaceY((int)(x % Chunk.Size), (int)(z % Chunk.Size));
    }

    public bool Contains(long x, int y, long z) =>
        y>=0 && y<Height && (IsInfinite?Streaming.Coordinates.Valid(x,z):x>=0 && x<Width && z>=0 && z<Depth);

    public BlockType GetBlock(long x, int y, long z)
    {
        if(!Contains(x,y,z))return BlockType.Air;
        if(IsInfinite)return TryGetChunkAt(x,z,out var chunk)?chunk.GetBlock(Streaming.Coordinates.Mod(x,16),y,Streaming.Coordinates.Mod(z,16)):BlockType.Bedrock;
        return _chunks[checked((int)((z / Chunk.Size) * ChunksX + x / Chunk.Size))].GetBlock((int)(x % Chunk.Size),y,(int)(z % Chunk.Size));
    }

    public bool IsSolid(long x, int y, long z) => Blocks.IsSolid(GetBlock(x, y, z));

    public bool SetBlock(long x, int y, long z, BlockType block)
        => ChangeWithSupportDrops(new(x,y,z),block,LiveDrops,0,false,false);

    private bool SetBlockCore(long x,int y,long z,BlockType block)
    {
        if (!Contains(x, y, z) || IsInfinite && !TryGetChunkAt(x,z,out _) || !Blocks.IsDefined(block)
            || !_plantEdit && block==BlockType.DoubleTallGrassLower && (!Contains(x,y+1,z) || GetBlock(x,y+1,z)!=BlockType.Air || GetBlock(x,y-1,z)!=BlockType.Grass)
            || !_plantEdit && block==BlockType.DoubleTallGrassUpper && GetBlock(x,y-1,z)!=BlockType.DoubleTallGrassLower
            || block==BlockType.Sapling && !Vegetation.CanPlant
            || block==BlockType.Fire && !Fire.CanAdd
            || block==BlockType.Cactus && !CanCactus(new(x,y,z))
            || !CanDetailChange(new(x,y,z),GetBlock(x,y,z),block)
            || GetBlock(x, y, z) == block || !Containers.CanChange(new Point3(x,y,z),block))
            return false;

        long chunkX = Streaming.Coordinates.FloorDiv(x,Chunk.Size), chunkZ = Streaming.Coordinates.FloorDiv(z,Chunk.Size);
        int localX = Streaming.Coordinates.Mod(x,Chunk.Size), localZ = Streaming.Coordinates.Mod(z,Chunk.Size);
        Chunk chunk = GetChunk(chunkX, chunkZ);
        BlockType previous = chunk.GetBlock(localX, y, localZ);
        chunk.SetBlock(localX, y, localZ, block);
        if(IsInfinite)MarkStreamedDirty(chunkX,chunkZ);
        Lighting.Changed(new(x,y,z),previous,block);
        Point3 changedCell=new(x,y,z);
        if(IsInfinite)ReconcileDormantBlock(changedCell,previous,block);
        if(block==BlockType.Air && Blocks.IsRenderable(previous) && !Lava.Fluid(previous)
            && _blockChangeRoot!=changedCell && _doorPartnerEdit!=changedCell && !(_plantEdit && Blocks.DoubleGrass(previous)) && !(_bedEdit && Blocks.Bed(previous)))
            Sounds?.Block(SoundKind.Break,previous,Center(changedCell));
        Containers.Changed(new Point3(x,y,z),block);
        Vegetation.Changed(new Point3(x,y,z),previous,block);
        DetailChanged(new Point3(x,y,z),previous,block);
        Fire.Changed(new(x,y,z),previous,block);
        PairedPlantChanged(new Point3(x,y,z),previous,block);
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
        for (long cz = chunkZ - (localZ == 0 ? 1 : 0); cz <= chunkZ + (localZ == Chunk.Size - 1 ? 1 : 0); cz++)
        for (long cx = chunkX - (localX == 0 ? 1 : 0); cx <= chunkX + (localX == Chunk.Size - 1 ? 1 : 0); cx++)
            if(IsInfinite)
            {if(TryGetChunkAt(cx*16,cz*16,out var neighbor)){neighbor.MarkWaterDirty();if(Lava.IsLava(previous)||Lava.IsLava(block))neighbor.MarkDirty();}}
            else if (cx>=0 && cx<ChunksX && cz>=0 && cz<ChunksZ)
            {GetChunk(cx, cz).MarkWaterDirty();if(Lava.IsLava(previous)||Lava.IsLava(block))GetChunk(cx,cz).MarkDirty();}
        Revision++;
        Fluids.Changed(new Point3(x, y, z), previous, block);
        LavaFlow.Changed(new Point3(x,y,z),previous,block);
        if(Lava.Fluid(block))ResolveFluidContact(new Point3(x,y,z));
        Gravity.Changed(x, z, previous, block);
        if (!Blocks.SupportsPlant(block,GetBlock(x,y+1,z)) && Blocks.Get(GetBlock(x, y + 1, z)).NeedsGrass)
            SetBlock(x, y + 1, z, BlockType.Air);
        return true;
    }

    // Used only before meshes exist. Decorations span chunk edges without invalidation work.
    internal void SetGeneratedBlock(long x, int y, long z, BlockType block)
    {
        if (!Contains(x, y, z)) throw new ArgumentOutOfRangeException(nameof(x));
        if (Blocks.Falls(block) && (y == 0 || !IsSolid(x, y - 1, z)))
            throw new InvalidOperationException("Generated Sand and Gravel require solid support.");
        if(IsInfinite)
        {
            if(!TryGetChunkAt(x,z,out var chunk))throw new InvalidOperationException("Generation cannot write into a non-resident chunk.");
            chunk.SetBlock(Streaming.Coordinates.Mod(x,16),y,Streaming.Coordinates.Mod(z,16),block);MarkStreamedDirty(chunk.X,chunk.Z);
        }
        else _chunks[checked((int)((z / Chunk.Size) * ChunksX + x / Chunk.Size))].SetBlock((int)(x % Chunk.Size), y, (int)(z % Chunk.Size), block);
        if(block==BlockType.Leaves)Vegetation.MarkNatural(new Point3(x,y,z));
    }

    private void MarkDirty(long x, long z)
    {
        if(IsInfinite){if(TryGetChunkAt(x*16,z*16,out var chunk))chunk.MarkDirty();return;}
        if (x>=0 && x<ChunksX && z>=0 && z<ChunksZ)
            _chunks[checked((int)(z * ChunksX + x))].MarkDirty();
    }

    public bool TryPlace(Point3 cell, BlockType block, BoundingBox playerBounds,Point3? support=null)
        => TryPlaceBlock(cell,block,playerBounds,support,null);

    private bool TryPlaceBlock(Point3 cell,BlockType block,BoundingBox playerBounds,Point3? support,StructureState? proposed)
    {
        if(block==BlockType.Bed)return PlaceBed(cell,0,playerBounds);
        BlockDefinition definition = Blocks.Get(block);
        if(block==BlockType.Chest&&!Containers.CanPlaceChest(cell))return false;
        if (!definition.Placeable || !Contains(cell.X, cell.Y, cell.Z)
            || support.HasValue && support.Value.Y>0 && Blocks.RejectsUnderside(block)
            || (GetBlock(cell.X, cell.Y, cell.Z) != BlockType.Air
                && !(definition.Solid && Water.IsWater(GetBlock(cell.X, cell.Y, cell.Z))))
            || (definition.Solid && PlacementOverlaps(cell,block,playerBounds,proposed))
            || (definition.Solid && PlacementBlockedByMobs(cell,block,proposed))
            || (definition.Solid && Gravity.Intersects(LocalBounds(cell)))
            || (definition.NeedsGrass && !Blocks.SupportsPlant(GetBlock(cell.X, cell.Y - 1, cell.Z),block)))
            return false;

        if(!SetBlock(cell.X, cell.Y, cell.Z, block))return false;
        Sounds?.Block(SoundKind.Place,block,Center(cell));return true;
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
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for (int x = Math.Max(LocalMinX, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(LocalMaxX - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)-1); y <= Math.Min(Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(LocalMinZ, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(LocalMaxZ - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {int count=BlockBoxes(GlobalCell(x,y,z),boxes,true);for(int i=0;i<count;i++)if(Overlaps(bounds,boxes[i]))return true;}

        return false;
    }
}

public readonly record struct Point3(long X, int Y, long Z)
{
    public static Point3 operator +(Point3 a, Point3 b) => new(checked(a.X + b.X), a.Y + b.Y, checked(a.Z + b.Z));
}
