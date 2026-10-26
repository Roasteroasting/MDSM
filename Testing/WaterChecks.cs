using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WaterChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static World Floor(int seed = 173, WorldSize size = WorldSize.Small)
    {
        var world = new World(seed, size, false);
        for (int z = 0; z < world.Depth; z++)
        for (int x = 0; x < world.Width; x++) world.SetGeneratedBlock(x, 0, z, BlockType.Stone);
        return world;
    }
    internal static void Settle(World world, int maximum = 300)
    {
        for (int i = 0; i < maximum && world.Fluids.PendingCount > 0; i++)
        {
            world.Fluids.Tick();
            Require(world.Fluids.LastProcessedCount <= FluidSimulation.MaximumUpdatesPerTick, "Water exceeded its tick budget.");
        }
        Require(world.Fluids.PendingCount == 0, "Water did not reach a stable, idle frontier.");
    }
    public static void StatesAndPlacement()
    {
        Require((int)BlockType.YellowFlower == 13 && (int)BlockType.WaterSource == 14 && (int)BlockType.WaterFalling == 22, "Persistent IDs changed.");
        for (int id = 14; id <= 22; id++)
        {
            BlockType block = (BlockType)id;
            Require(Blocks.IsDefined(block) && Water.IsWater(block) && !Blocks.IsSolid(block) && !Blocks.IsOpaque(block)
                && !Blocks.IsPlaceable(block) && !Items.IsDefined(new ItemId((ushort)id)) && Blocks.Get(block).MiningSeconds < 0, "Water became a solid/inventory/mineable block.");
        }
        for (int level = 1; level <= 7; level++) Require(Water.Level(Water.Flow(level)) == level
            && Water.Height(Water.Flow(level)) < Water.Height(level == 1 ? BlockType.WaterSource : Water.Flow(level - 1)), "Flow heights are not decreasing.");
        var world = Floor(); world.SetBlock(15, 1, 15, BlockType.WaterSource); world.SetBlock(16, 1, 15, BlockType.Stone);
        var hit = BlockRaycaster.Cast(world, new Vector3(14.5f, 1.5f, 15.5f), Vector3.Right);
        Require(hit?.Cell == new Point3(16, 1, 15), "Water obstructs targeting solid terrain.");
        var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.FromBlock(BlockType.Dirt), 2));
        Require(BlockPlacement.TryPlace(world, inventory, new Point3(15, 1, 15), new BoundingBox()), "Solid placement failed to displace water.");
        Require(inventory.Selected.Count == 1 && world.GetBlock(15, 1, 15) == BlockType.Dirt, "Displacement lost/duplicated inventory.");
        Require(!world.TryPlace(new Point3(5, 1, 5), BlockType.WaterSource, new BoundingBox()), "Water can be placed from inventory.");
    }
    public static void Propagation()
    {
        var world = Floor(); world.SetBlock(15, 5, 15, BlockType.WaterSource);
        world.Fluids.Tick();
        Require(world.GetBlock(15, 4, 15) == BlockType.WaterFalling && world.GetBlock(16, 5, 15) == BlockType.Air, "Downward flow did not take priority.");
        Settle(world);
        Require(world.GetBlock(15, 1, 15) == BlockType.WaterFalling && world.GetBlock(16, 1, 15) == Water.Flow(1), "Waterfall did not spread at its foot.");
        Require(world.GetBlock(22, 1, 15) == Water.Flow(7) && world.GetBlock(23, 1, 15) == BlockType.Air, "Horizontal reach is not seven blocks.");
        Require(world.GetBlock(15, 6, 15) == BlockType.Air && world.GetBlock(16, 2, 15) == BlockType.Air, "Water flowed upward or through an airborne side.");
        var corners = Floor();
        for (int z = 18; z <= 22; z++) corners.SetBlock(22, 1, z, BlockType.Stone);
        corners.SetBlock(20, 1, 20, BlockType.WaterSource); Settle(corners);
        Require(Water.IsWater(corners.GetBlock(23, 1, 18)) && corners.GetBlock(22, 1, 20) == BlockType.Stone,
            "Water failed to flow around a wall within its finite reach.");
        int sources = 0;
        foreach (Chunk chunk in corners.Chunks) foreach (byte block in chunk.Blocks) if (Water.IsSource((BlockType)block)) sources++;
        Require(sources == 1, "A lone source multiplied.");
    }
    public static void RecessionAndRegeneration()
    {
        var world = Floor(); world.SetBlock(15, 8, 15, BlockType.WaterSource); Settle(world);
        world.SetBlock(15, 8, 15, BlockType.Air); Settle(world);
        foreach (Chunk chunk in world.Chunks) Require(chunk.WaterCount == 0, "Unsupported water persisted after source removal.");
        world.SetBlock(30, 1, 30, BlockType.WaterSource); world.SetBlock(32, 1, 30, BlockType.WaterSource); Settle(world);
        Require(world.GetBlock(31, 1, 30) == BlockType.WaterSource, "Supported two-source regeneration failed.");
        world.SetBlock(31, 1, 30, BlockType.Air); Settle(world);
        Require(world.GetBlock(31, 1, 30) == BlockType.WaterSource, "Regenerating a removed source failed.");
        var unsupported = Floor(); unsupported.SetBlock(30, 4, 30, BlockType.WaterSource); unsupported.SetBlock(32, 4, 30, BlockType.WaterSource);
        Settle(unsupported);
        Require(!Water.IsSource(unsupported.GetBlock(31, 4, 30)), "Two airborne sources regenerated an unsupported source.");
        var supplied = Floor(); supplied.SetBlock(20, 1, 20, BlockType.WaterSource); supplied.SetBlock(24, 1, 20, BlockType.WaterSource); Settle(supplied);
        supplied.SetBlock(20, 1, 20, BlockType.Air); Settle(supplied);
        Require(Water.IsWater(supplied.GetBlock(21, 1, 20)) && !Water.IsSource(supplied.GetBlock(21, 1, 20)), "A valid remaining upstream supply was discarded.");
        // Opening a floor invalidates horizontal recipients even if its upstream source stays unchanged.
        var floor = Floor(); floor.SetBlock(20, 3, 20, BlockType.Stone); floor.SetBlock(20, 4, 20, BlockType.WaterSource); Settle(floor);
        Require(Water.IsWater(floor.GetBlock(21, 4, 20)), "Raised platform did not spill.");
        floor.SetBlock(20, 3, 20, BlockType.Air); Settle(floor);
        Require(floor.GetBlock(21, 4, 20) == BlockType.Air && Water.IsWater(floor.GetBlock(20, 1, 20)), "Removing a source's floor left suspended side flows.");
    }
    public static void BoundariesAndScheduling()
    {
        var world = Floor(); world.SetBlock(15, 1, 15, BlockType.WaterSource); Settle(world);
        Require(Water.IsWater(world.GetBlock(16, 1, 16)), "Fluid did not cross diagonal chunk seams.");
        Require(world.GetChunk(0, 0).Revision == 0 && world.GetChunk(1, 1).Revision == 0,
            "Air/fluid state changes rebuilt opaque terrain.");
        world.SetBlock(0, 1, 0, BlockType.WaterSource); world.SetBlock(127, 63, 127, BlockType.WaterSource); Settle(world, 1000);
        Require(world.GetBlock(-1, 1, 0) == BlockType.Air && world.GetBlock(128, 1, 127) == BlockType.Air, "Fluid escaped finite storage.");
        var a = Floor(); a.SetBlock(15, 4, 15, BlockType.WaterSource);
        // Compare an active frontier at one second, not just an eventual settled configuration.
        for (int i = 0; i < 60; i++) a.Fluids.Update(1d / 60);
        foreach (int rate in new[] { 5, 30, 144, 240, 360, 997 })
        {
            var b = Floor(); b.SetBlock(15, 4, 15, BlockType.WaterSource);
            for (int i = 0; i < rate; i++) b.Fluids.Update(1d / rate);
            Equal(a, b); Require(a.Fluids.PendingCount == b.Fluids.PendingCount, "Water timing depends on render rate.");
        }
        var busy = Floor();
        for (int z = 2; z < 126; z += 3) for (int x = 2; x < 126; x += 3) busy.SetBlock(x, 3, z, BlockType.WaterSource);
        Require(busy.Fluids.PendingCount > FluidSimulation.MaximumUpdatesPerTick, "Budget fixture was too small.");
        busy.Fluids.Tick(); Require(busy.Fluids.LastProcessedCount == FluidSimulation.MaximumUpdatesPerTick && busy.Fluids.PendingCount > 0, "Unbounded fluid update processing.");
        var idle = Floor(); idle.Fluids.Initialize(); idle.Fluids.Tick(); Require(idle.Fluids.LastProcessedCount == 0, "Dry world performed fluid work.");
    }
    public static void Geometry()
    {
        var world = new World(0, WorldSize.Small, false);
        world.SetBlock(15, 4, 15, BlockType.WaterSource);
        var faces = new List<WaterFace>(); WaterMeshBuilder.Build(world, world.GetChunk(0, 0), faces);
        Require(faces.Count == 6, "Isolated water omitted a face.");
        float height = faces[0].A.Y;
        Require(MathF.Abs(height - 4 - Water.Height(BlockType.WaterSource)) < 0.001f, "Source surface height is incorrect.");
        var opaque = new ChunkMeshBuilder(); opaque.Build(world, world.GetChunk(0, 0)); Require(opaque.VertexCount == 0, "Water entered the opaque pass.");
        world.SetBlock(16, 4, 15, Water.Flow(7));
        WaterMeshBuilder.Build(world, world.GetChunk(0, 0), faces); Require(faces.Count == 5, "Internal water face remained at seam.");
        float shared = Water.CornerHeight(world, 16, 4, 15);
        Require(shared > Water.Height(Water.Flow(7)) && shared < Water.Height(BlockType.WaterSource), "Corner heights did not blend levels.");
        Require(faces[0].B.Y == 4 + Water.CornerHeight(world, 16, 4, 16), "Shared mesh corner differs across chunk boundary.");
        world.SetBlock(15, 5, 15, BlockType.WaterFalling); WaterMeshBuilder.Build(world, world.GetChunk(0, 0), faces);
        Require(Water.CornerHeight(world, 15, 4, 15) == 1 && faces.Count == 9, "Water column contains internal top/bottom faces.");
        world.SetBlock(14, 4, 15, BlockType.Stone); WaterMeshBuilder.Build(world, world.GetChunk(0, 0), faces);
        Require(faces.Count == 8, "Water face against opaque terrain was not culled.");
        var vertices = new Microsoft.Xna.Framework.Graphics.VertexPositionColorTexture[6]; faces[0].CopyTo(vertices, 0);
        foreach (var vertex in vertices) Require(vertex.Color.A > 0 && vertex.Color.A < 255 && vertex.TextureCoordinate.X > 0
            && vertex.TextureCoordinate.X < 1 && vertex.TextureCoordinate.Y > 0 && vertex.TextureCoordinate.Y < 1, "Water opacity or point-sampled UVs are invalid.");
    }
    public static void Swimming()
    {
        var world = Floor();
        for (int z = 10; z <= 35; z++) for (int x = 10; x <= 35; x++) for (int y = 1; y <= 5; y++) world.SetGeneratedBlock(x, y, z, BlockType.WaterSource);
        var dry = new Player(new Vector3(50, 1, 50)); var wet = new Player(new Vector3(15, 1, 15));
        for (int i = 0; i < 120; i++) { dry.Update(world, Vector3.Right, false, 1f / 120); wet.Update(world, Vector3.Right, false, 1f / 120); }
        Require(MathF.Abs(dry.Position.X - 55) < 0.001f && MathF.Abs(wet.Position.X - 17.25f) < 0.001f, "Water speed reduction changed normal movement.");
        for (int i = 0; i < 180; i++) wet.Update(world, Vector3.Zero, false, 1f / 120, swimUp: true);
        Require(wet.Position.Y > 4.5f && wet.Health == 20 && !world.IntersectsSolid(wet.Bounds), "Space swimming failed or collided with water.");
        var fall = new Player(new Vector3(20, 30, 20));
        for (int i = 0; i < 1200; i++) fall.Update(world, Vector3.Zero, false, 1f / 120);
        Require(fall.Health == 20 && fall.Grounded && fall.Position.Y == 1, "Water did not cancel fall damage/resist descent or terrain collision failed.");
        var pig = new Pig(new PigState(new Vector3(25, 1, 25), 0, 10), 123);
        for (int i = 0; i < 1200; i++) { pig.Update(world, 1f / 120); Require(!world.IntersectsSolid(pig.Bounds) && pig.Position.Y >= 1, "Pig penetrated lake bed."); }
        Require(pig.Position.Y > 3 && pig.Health == 10 && pig.Position.X >= Pig.HalfWidth, "Pig did not float safely.");
        var shore = Floor();
        for (int z = 5; z < 50; z++) for (int x = 5; x < 50; x++)
            shore.SetGeneratedBlock(x, 1, z, x >= 20 && x <= 25 && z >= 20 && z <= 25 ? BlockType.WaterSource : BlockType.Grass);
        var stranded = new Pig(new PigState(new Vector3(22.5f, 1, 22.5f), 0, 10), 123);
        bool escaped = false;
        for (int i = 0; i < 24000 && !escaped; i++)
        {
            stranded.Update(shore, 1f / 120);
            Require(!shore.IntersectsSolid(stranded.Bounds), "Swimming pig tunneled through a shore.");
            escaped = stranded.Grounded && stranded.Position.Y >= 2;
        }
        Require(escaped, $"A pig could not wander out of a one-block shoreline; final feet {stranded.Position}.");
    }
    public static void Lakes()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            World a = World.CreateNew(173, size), b = World.CreateNew(173, size); Equal(a, b);
            int water = 0, sand = 0, gravel = 0, surface = 0;
            var positions = new HashSet<Point3>();
            for (int z = 0; z < a.Depth; z++)
            for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < World.Height; y++)
            {
                BlockType block = a.GetBlock(x, y, z);
                if (block == BlockType.Sand) sand++;
                if (block == BlockType.Gravel) gravel++;
                if (!Water.IsWater(block)) continue;
                water++; positions.Add(new Point3(x, y, z));
                Require(Water.IsSource(block) && x > 0 && z > 0 && x < a.Width - 1 && z < a.Depth - 1 && y > 0, "Lake uses unstable flow or escaped world bounds.");
                Require(Vector2.Distance(new Vector2(x, z), new Vector2(a.Spawn.X, a.Spawn.Z)) > 20, "Lake obstructs spawn.");
                if (!Water.IsWater(a.GetBlock(x, y + 1, z))) surface++;
                Require(Blocks.Get(a.GetBlock(x, y + 1, z)).Shape != BlockShape.Cross, "Vegetation generated underwater.");
            }
            Require(water > 0 && sand > 0 && gravel > 0 && surface < a.Width * a.Depth / 25, "Missing lakes/beaches or excessive lake coverage.");
            int count = 0;
            var queue = new Queue<Point3>();
            while (positions.Count > 0)
            {
                Point3 first = default; foreach (Point3 p in positions) { first = p; break; }
                positions.Remove(first); queue.Enqueue(first); count++;
                int minX = first.X, maxX = first.X, minZ = first.Z, maxZ = first.Z;
                while (queue.Count > 0)
                {
                    Point3 p = queue.Dequeue(); minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
                    foreach (Point3 offset in Directions) { Point3 next = p + offset; if (positions.Remove(next)) queue.Enqueue(next); }
                }
                Require(maxX - minX <= LakeGenerator.MaximumRadius(size) * 2 && maxZ - minZ <= LakeGenerator.MaximumRadius(size) * 2, "A generated lake exceeded its size limit.");
            }
            Require(count > 0 && count <= LakeGenerator.MaximumLakes(size), "Lake population exceeded size budget.");
            int revision = a.Revision; Settle(a, 200); Require(a.Revision == revision, "Natural lakes drained/flooded without interaction.");
            Require(!Water.Intersects(a, new Player(a.Spawn).Bounds), "Spawn is underwater.");
            Console.WriteLine($"  {size}: {count} lakes, {surface} surface cells, {water} source cells; stable frontier idle.");
        }
    }
    private static readonly Point3[] Directions = { new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1) };
    public static void LakeEditsAndSeeds()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        foreach (int seed in new[] { int.MinValue, -1, 0, 174, int.MaxValue })
        {
            var a = new World(seed, size, false); new TerrainGenerator(seed).Generate(a);
            var b = new World(seed, size, false); new TerrainGenerator(seed).Generate(b);
            int lakes = new LakeGenerator(seed).Generate(a), same = new LakeGenerator(seed).Generate(b);
            Require(lakes > 0 && lakes == same && lakes <= LakeGenerator.MaximumLakes(size), "An extreme seed failed to produce deterministic restrained lakes.");
            Equal(a, b); a.Fluids.Initialize(); int revision = a.Revision; Settle(a);
            Require(a.Revision == revision, "A seeded lake had an unsealed shoreline/bottom.");
        }
        World world = World.CreateNew(173, WorldSize.Small);
        Point3 bank = default, bed = default;
        foreach (Chunk chunk in world.Chunks)
        {
            if (chunk.WaterCount == 0) continue;
            for (int y = 1; y < World.Height; y++)
            for (int z = chunk.OriginZ; z < chunk.OriginZ + 16; z++)
            for (int x = chunk.OriginX; x < chunk.OriginX + 16; x++)
            {
                if (!Water.IsWater(world.GetBlock(x, y, z))) continue;
                if (world.IsSolid(x, y - 1, z)) bed = new Point3(x, y - 1, z);
                if (Water.IsWater(world.GetBlock(x, y + 1, z))) continue;
                foreach (Point3 offset in Directions)
                {
                    if (offset.Y != 0) continue;
                    Point3 p = new(x + offset.X, y, z + offset.Z);
                    if (world.IsSolid(p.X, p.Y, p.Z)) bank = p;
                }
            }
        }
        Require(bank != default && bed != default, "Generated lake had no solid shoreline/bed.");
        Require(world.SetBlock(bank.X, bank.Y, bank.Z, BlockType.Air), "Digging lake bank failed."); Settle(world);
        Require(Water.IsWater(world.GetBlock(bank.X, bank.Y, bank.Z)), "A dug shoreline did not admit water.");
        world.SetBlock(bed.X, bed.Y, bed.Z, BlockType.Air); Settle(world);
        Require(Water.IsWater(world.GetBlock(bed.X, bed.Y, bed.Z)), "A dug lake bottom did not admit downward flow.");
        Require(world.TryPlace(bank, BlockType.Stone, new BoundingBox()), "A solid could not displace lake water."); Settle(world);
        Require(world.GetBlock(bank.X, bank.Y, bank.Z) == BlockType.Stone, "Flow overwrote a solid replacement block.");
    }
    private static void Equal(World a, World b)
    {
        Require(a.Size == b.Size, "World sizes differ.");
        for (int i = 0; i < a.ChunkCount; i++) Require(a.Chunks[i].Blocks.SequenceEqual(b.Chunks[i].Blocks), "Seeded water/world bytes differ.");
    }
    public static void Persistence()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            var original = Floor(173, size); original.SetBlock(15, 6, 15, BlockType.WaterSource);
            for (int i = 0; i < 8; i++) original.Fluids.Tick(); // Save a moving waterfall/front, not just an idle pool.
            using var bytes = new MemoryStream();
            var inventory = Inventory.CreateNewWorld(); inventory.SelectedSlot = 5;
            var pigs = new[] { new PigState(new Vector3(40, 1, 40), 0.5f, 7) };
            var drops = new[] { new DroppedItemState(Items.RawPorkchop, 3, new Vector3(30, 1, 30), -1, 0.7f) };
            WorldSerializer.Write(bytes, new WorldSave(original, "Water round trip", new Vector3(45, 1, 45), 0.4f, 0.2f, inventory, drops, 13, pigs));
            bytes.Position = 0; WorldSave saved = WorldSerializer.Read(bytes); Equal(original, saved.World);
            Require(saved.Health == 13 && saved.Pigs[0] == pigs[0] && saved.DroppedItems[0] == drops[0]
                && saved.Inventory.SelectedSlot == 5 && saved.Inventory[13] == inventory[13] && saved.World.Fluids.PendingCount > 0,
                "Water restoration changed existing player/entity data or failed to resume flow.");
            Settle(original); Settle(saved.World); Equal(original, saved.World);
            original.SetBlock(15, 6, 15, BlockType.Air); saved.World.SetBlock(15, 6, 15, BlockType.Air); Settle(original); Settle(saved.World); Equal(original, saved.World);
            foreach (Chunk chunk in saved.World.Chunks) Require(chunk.WaterCount == 0, "Restored unsupported flow did not recede.");
            // A pre-water v4 layout remains byte-for-byte compatible; loading never generates lakes.
            bytes.SetLength(0); var dry = new World(12, size);
            bytes.Write(MobChecks.Version4(new WorldSave(dry, "Existing dry world", dry.Spawn, 0, 0))); bytes.Position = 0;
            WorldSave old = WorldSerializer.Read(bytes); Equal(dry, old.World);
            Require(old.World.Fluids.PendingCount == 0 && WorldSerializer.FormatVersion == 7, "Loading old terrain introduced water or changed the expected save version.");
        }
    }
}
