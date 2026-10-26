using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WorldUpdateChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Kill(PassiveMobPopulation population, PassiveMob mob, DroppedItems drops)
    { while (!mob.IsDead) Require(population.Punch(mob, new Vector3(60, 1, 60), drops), "Could not kill mob."); }
    private static int[] Counts(PassiveMobPopulation population)
    {
        var counts = new int[4]; foreach (PassiveMob mob in population.Entities) counts[(int)mob.Species]++; return counts;
    }
    public static void TargetsAndTimers()
    {
        World world = PigChecks.Flat();
        MobState[] states = { MobChecks.State(MobSpecies.Pig, health: 1), MobChecks.State(MobSpecies.Pig, new Vector3(25,1,25), 1),
            MobChecks.State(MobSpecies.Cow, new Vector3(30,1,30), 1), MobChecks.State(MobSpecies.Cow, new Vector3(35,1,35), 1),
            MobChecks.State(MobSpecies.Sheep, new Vector3(40,1,40), 1), MobChecks.State(MobSpecies.Chicken, new Vector3(45,1,45), 1) };
        var population = new PassiveMobPopulation(world, states); var drops = new DroppedItems(world);
        int[] targets = Counts(population); PassiveMob[] original = population.Entities.ToArray();
        foreach (PassiveMob mob in original) Kill(population, mob, drops);
        Require(population.Entities.Count == 0 && population.PendingRespawns.Count == states.Length && drops.Entities.Count == states.Length,
            "Deaths did not create exactly one replacement and drop apiece.");
        Require(population.PendingRespawns.All(p => p.RemainingSeconds >= 30 && p.RemainingSeconds <= 60)
            && population.PendingRespawns.Select(p => p.RemainingSeconds).Distinct().Count() == states.Length, "Respawn delays are not independent 30-60 second values.");
        foreach (PassiveMob mob in original) Require(!population.Punch(mob, Vector3.Zero, drops), "Dead mob scheduled twice.");
        for (int i = 0; i < 4; i++) Require(population.Target((MobSpecies)i) == targets[i], "Death reduced the species target.");
        population.Update(29); Require(population.Entities.Count == 0, "Replacement spawned before 30 seconds.");
        MobRespawnState[] paused = population.PendingRespawns.ToArray();
        population.Update(0); population.Update(float.NaN); population.Update(-1);
        Require(paused.AsSpan().SequenceEqual(population.PendingRespawns.ToArray()), "No gameplay time still advanced respawns.");
        var player = new Player(new Vector3(64.5f, 1, 64.5f)); var seen = new HashSet<PassiveMob>();
        for (int i = 0; i < 60 * 32; i++)
        {
            population.Update(1f / 60, player.Position, player.Bounds, drops);
            Require(population.LastSpawnAttempts <= PassiveMobPopulation.MaximumSpawnAttemptsPerUpdate, "Spawn work exceeded its bound.");
            foreach (PassiveMob mob in population.Entities)
            {
                if (!seen.Add(mob)) continue;
                Require(mob.Health == mob.Definition.MaximumHealth && !world.IntersectsSolid(mob.Bounds) && !Water.Intersects(world, mob.Bounds)
                    && !World.Overlaps(mob.Bounds, player.Bounds) && Vector2.Distance(new Vector2(mob.Position.X, mob.Position.Z), new Vector2(player.Position.X, player.Position.Z)) >= 12,
                    "Replacement had invalid health, clearance, water, or player distance.");
                foreach (PassiveMob other in population.Entities) if (other != mob) Require(!World.Overlaps(mob.Bounds, other.Bounds), "Replacement intersected another mob.");
            }
            int[] counts = Counts(population); for (int species = 0; species < 4; species++) Require(counts[species] <= targets[species], "Species target exceeded.");
        }
        Require(Counts(population).AsSpan().SequenceEqual(targets) && population.PendingRespawns.Count == 0, "Population was not replenished by 60 seconds.");
        for (int i = 0; i < 600; i++) population.Update(1f / 60);
        Require(population.Entities.Count == states.Length && drops.Entities.Count == states.Length && world.Revision == 0, "Replenishment duplicated mobs/drops or edited terrain.");
    }
    public static void RetryAndClearance()
    {
        var empty = new World(173, WorldSize.Small, false);
        var population = new PassiveMobPopulation(empty, new[] { MobChecks.State(MobSpecies.Cow, health: 1) }); var drops = new DroppedItems(empty);
        Kill(population, population.Entities[0], drops); population.Update(60);
        Require(population.Entities.Count == 0 && population.PendingRespawns.Count == 1 && population.PendingRespawns[0].RemainingSeconds == 5
            && population.LastSpawnAttempts == 32, "Failed spawn abandoned work, placed invalid mob, or exceeded budget.");
        population.Update(1); Require(population.LastSpawnAttempts == 0 && population.PendingRespawns[0].RemainingSeconds == 4, "Failure retried every frame.");
        for (int z = 0; z < empty.Depth; z++) for (int x = 0; x < empty.Width; x++) empty.SetGeneratedBlock(x, 0, z, BlockType.Grass);
        population.Update(4); Require(population.Entities.Count == 1 && population.PendingRespawns.Count == 0, "Failed replenishment never recovered after terrain became suitable.");
        World world = PigChecks.Flat();
        var reference = new PassiveMobPopulation(world, new[] { MobChecks.State(MobSpecies.Cow, health: 1) }); var referenceDrops = new DroppedItems(world);
        Kill(reference, reference.Entities[0], referenceDrops); reference.Update(60); Vector3 candidate = reference.Entities[0].Position;
        var occupied = new PassiveMobPopulation(world, new[] { MobChecks.State(MobSpecies.Cow, health: 1), MobChecks.State(MobSpecies.Pig, candidate) });
        var occupiedDrops = new DroppedItems(world); Kill(occupied, occupied.Entities[0], occupiedDrops); occupied.Update(60);
        Require(!World.Overlaps(occupied.Entities[0].Bounds, occupied.Entities[1].Bounds), "Spawn ignored an occupied candidate.");
        var nearPlayer = new PassiveMobPopulation(world, new[] { MobChecks.State(MobSpecies.Cow, health: 1) });
        var player = new Player(candidate); var playerDrops = new DroppedItems(world); Kill(nearPlayer, nearPlayer.Entities[0], playerDrops);
        nearPlayer.Update(60, player.Position, player.Bounds);
        Require(Vector3.Distance(nearPlayer.Entities[0].Position, candidate) >= 12, "Spawn ignored player clearance.");
        var itemBlocked = new PassiveMobPopulation(world, new[] { MobChecks.State(MobSpecies.Cow, health: 1) }); var itemDrops = new DroppedItems(world);
        Kill(itemBlocked, itemBlocked.Entities[0], itemDrops); itemDrops.Spawn(Items.Leather, 1, candidate + Vector3.Up * 0.125f, 0);
        itemBlocked.Update(60, items: itemDrops);
        Require(!World.Overlaps(itemBlocked.Entities[0].Bounds, new BoundingBox(candidate, candidate + new Vector3(0.2f))), "Replacement overlapped an item entity.");
        // No valid dry grassy location: keep every replacement pending, bounded across a large simultaneous batch.
        var water = PigChecks.Flat();
        for (int z = 0; z < water.Depth; z++) for (int x = 0; x < water.Width; x++) water.SetGeneratedBlock(x, 1, z, BlockType.WaterSource);
        var wet = new PassiveMobPopulation(water, Enumerable.Range(0, 20).Select(_ => MobChecks.State(MobSpecies.Chicken, health: 1)).ToArray());
        var wetDrops = new DroppedItems(water); foreach (PassiveMob mob in wet.Entities.ToArray()) Kill(wet, mob, wetDrops);
        for (int i = 0; i < 30; i++) { wet.Update(i == 0 ? 60 : 1f / 60); Require(wet.LastSpawnAttempts <= 32, "Simultaneous retries exceeded shared budget."); }
        Require(wet.Entities.Count == 0 && wet.PendingRespawns.Count == 20, "Invalid underwater spawning or lost failed respawns.");
    }
    internal static byte[] Version5(WorldSave save)
    {
        byte[] old = MobChecks.Version4(save); int prefix = SaveLayout.MobCount(old);
        using var stream = new MemoryStream(); stream.Write(old, 0, prefix);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            PigState[] pigs = save.Pigs ?? Array.Empty<PigState>(); MobState[] others = save.Mobs ?? Array.Empty<MobState>(); writer.Write(pigs.Length + others.Length);
            foreach (PigState pig in pigs) Record(new MobState(MobSpecies.Pig, pig.Position, pig.Yaw, pig.Health));
            foreach (MobState mob in others) Record(mob);
            void Record(MobState mob) { writer.Write((byte)mob.Species); writer.Write(mob.Position.X); writer.Write(mob.Position.Y); writer.Write(mob.Position.Z); writer.Write(mob.Yaw); writer.Write(mob.Health); }
        }
        byte[] payload = stream.ToArray(); BitConverter.GetBytes(5).CopyTo(payload, 4);
        using var result = new MemoryStream(); result.Write(payload); result.Write(SHA256.HashData(payload)); return result.ToArray();
    }
    public static void PopulationPersistence()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            World world = PigChecks.Flat(size);
            var states = new[] { MobChecks.State(MobSpecies.Pig, health: 1), MobChecks.State(MobSpecies.Cow, new Vector3(30,1,30), 1), MobChecks.State(MobSpecies.Chicken, new Vector3(40,1,40)) };
            var population = new PassiveMobPopulation(world, states); var drops = new DroppedItems(world);
            Kill(population, population.Entities[0], drops); Kill(population, population.Entities[0], drops); population.Update(5);
            var save = new WorldSave(world, "Pending " + size, new Vector3(64,1,64), 0, 0, Inventory.CreateNewWorld(), drops.Capture(), 17,
                population.CapturePigs(), population.CaptureOthers(), population.CapturePopulation());
            byte[] bytes = MobChecks.Encode(save); WorldSave loaded = WorldSerializer.Read(new MemoryStream(bytes));
            var session = new GameSession("pending", loaded);
            Require(session.Mobs.Target(MobSpecies.Pig) == 1 && session.Mobs.Target(MobSpecies.Cow) == 1 && session.Mobs.Target(MobSpecies.Chicken) == 1
                && session.Mobs.Entities.Count == 1 && session.Mobs.PendingRespawns.SequenceEqual(population.PendingRespawns), "Save/load reduced targets or reset pending delays.");
            Require(bytes.AsSpan().SequenceEqual(MobChecks.Encode(session.Capture())), "Repeated save/load changed population metadata or duplicated animals.");
            session.Mobs.Update(60, session.Player.Position, session.Player.Bounds, session.Items);
            Require(session.Mobs.Entities.Count == 3 && session.Mobs.PendingRespawns.Count == 0, "Saved pending animals did not return.");
            WorldSave again = WorldSerializer.Read(new MemoryStream(MobChecks.Encode(session.Capture())));
            Require(new GameSession("again", again).Mobs.Entities.Count == 3 && again.Population.Pending.Length == 0, "Completed replacement was scheduled again after loading.");
        }
    }
    public static void LegacyPopulationAndCorruption()
    {
        World world = PigChecks.Flat(); var inventory = Inventory.CreateNewWorld(); inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 23));
        var save = new WorldSave(world, "Legacy survivors", new Vector3(60,1,60), 0.1f, 0.2f, inventory, Health: 13,
            Pigs: new[] { new PigState(new Vector3(30,1,30), 1, 4) }, Mobs: new[] { MobChecks.State(MobSpecies.Cow, new Vector3(40,1,40), 7) });
        WorldSave old = WorldSerializer.Read(new MemoryStream(Version5(save))); var session = new GameSession("legacy", old);
        Require(session.Mobs.Target(MobSpecies.Pig) == 1 && session.Mobs.Target(MobSpecies.Cow) == 1 && session.Mobs.Target(MobSpecies.Sheep) == 0
            && session.Mobs.PendingRespawns.Count == 0 && session.Inventory[0].Count == 23 && session.Player.Health == 13, "V5 migration changed survivors or invented targets/resources.");
        session.Mobs.Update(60); Require(session.Mobs.Entities.Count == 2, "Migration spawned missing historical species.");
        WorldSave v4 = WorldSerializer.Read(new MemoryStream(MobChecks.Version4(save)));
        Require(v4.Population.Targets.AsSpan().SequenceEqual(new[] { 1,0,0,0 }), "V4 pig-only policy invented other species.");
        var empty = save with { Pigs = Array.Empty<PigState>(), Mobs = Array.Empty<MobState>() };
        var none = new GameSession("none", WorldSerializer.Read(new MemoryStream(Version5(empty)))); none.Mobs.Update(60);
        Require(none.Mobs.Entities.Count == 0 && none.Mobs.CapturePopulation().Targets.All(t => t == 0), "Empty legacy world was repopulated.");
        var waiting = new PassiveMobPopulation(world, new[] { MobChecks.State(MobSpecies.Chicken, health: 1) }); var items = new DroppedItems(world);
        Kill(waiting, waiting.Entities[0], items);
        var pendingSave = empty with { Population = waiting.CapturePopulation() }; byte[] bytes = MobChecks.Encode(pendingSave);
        int start = SaveLayout.Population(bytes);
        foreach ((int Offset, byte[] Value) bad in new[] { (start, BitConverter.GetBytes(-1)), (start + 12, BitConverter.GetBytes(2)),
            (start + 16, BitConverter.GetBytes(PassiveMobPopulation.MaximumMobs + 1)), (start + 20, new byte[] { 255 }), (start + 21, BitConverter.GetBytes(double.NaN)),
            (start + 21, BitConverter.GetBytes(-1d)), (start + 21, BitConverter.GetBytes(61d)) })
        {
            byte[] invalid = (byte[])bytes.Clone(); bad.Value.CopyTo(invalid, bad.Offset); SHA256.HashData(invalid.AsSpan(0, invalid.Length - 32)).CopyTo(invalid, invalid.Length - 32);
            Throws<InvalidDataException>(() => WorldSerializer.Read(new MemoryStream(invalid)));
        }
        Throws<InvalidDataException>(() => MobChecks.Encode(empty with { Population = new MobPopulationState(new[] { 256,1,0,0 }, Array.Empty<MobRespawnState>()) }));
    }
    private static int FallingCount(World world)
    {
        int result = 0; foreach (Chunk chunk in world.Chunks) foreach (byte block in chunk.Blocks) if (Blocks.Falls((BlockType)block)) result++; return result;
    }
    internal static void SettleGravity(World world)
    {
        for (int step = 0; step < 2400 && (world.Gravity.PendingCount > 0 || world.Gravity.Active.Count > 0); step++)
            world.Gravity.Update(1d / 120);
        Require(world.Gravity.PendingCount == 0 && world.Gravity.Active.Count == 0, "Falling blocks did not finish settling.");
    }
    public static void GravityStacksAndBoundaries()
    {
        var world = new World(0, WorldSize.Small, false);
        foreach (int x in new[] { 0,15,16,127 })
        {
            world.SetBlock(x, 0, 15, BlockType.Stone); world.SetBlock(x, 8, 15, BlockType.Sand);
            world.SetBlock(x, 9, 15, BlockType.Gravel); world.SetBlock(x, 10, 15, BlockType.Sand);
        }
        int count = FallingCount(world), revision = world.GetChunk(1, 0).Revision; world.Gravity.Tick();
        Require(world.Gravity.LastMovedBlocks == 12 && FallingCount(world) + world.Gravity.Active.Count == count && world.Gravity.Active.Count == 12,
            "Launching a stack lost or duplicated falling blocks.");
        SettleGravity(world);
        Require(FallingCount(world) == count && world.GetChunk(1, 0).Revision > revision
            && world.GetChunk(3, 3).Revision == 0 && world.Gravity.PendingCount == 0, "Stack fall lost blocks, failed boundary invalidation, or dirtied unrelated chunks.");
        foreach (int x in new[] { 0,15,16,127 })
        {
            Require(world.GetBlock(x, 1, 15) == BlockType.Sand && world.GetBlock(x, 2, 15) == BlockType.Gravel && world.GetBlock(x, 3, 15) == BlockType.Sand
                && world.GetBlock(x, 8, 15) == BlockType.Air && world.GetBlock(x, 9, 15) == BlockType.Air && world.GetBlock(x, 10, 15) == BlockType.Air, "Stack order or settlement incorrect.");
        }
        int stable = world.Revision; world.Gravity.Tick(); Require(world.Revision == stable, "Supported stack kept moving.");
        world.SetBlock(0, 0, 15, BlockType.Air); world.Gravity.Tick(); SettleGravity(world);
        Require(world.GetBlock(0, 0, 15) == BlockType.Sand && FallingCount(world) == count && world.GetBlock(0, -1, 15) == BlockType.Air, "Falling material escaped the world bottom.");
        world.SetBlock(20, 0, 20, BlockType.Stone); world.SetBlock(20, 4, 20, BlockType.Dirt); world.SetBlock(20, 5, 20, BlockType.Sand); world.SetBlock(20, 6, 20, BlockType.Gravel);
        world.Gravity.Tick(); Require(world.GetBlock(20, 5, 20) == BlockType.Sand, "Sand fell through solid support.");
        var mine = new MiningController(); var items = new DroppedItems(world);
        mine.Update(world, items, new BlockHit(new Point3(20,4,20), new Point3(1,0,0), 1), true, 2);
        world.Gravity.Tick(); SettleGravity(world); Require(world.GetBlock(20, 1, 20) == BlockType.Sand && world.GetBlock(20, 2, 20) == BlockType.Gravel && items.Entities.Count == 1,
            "Mining support did not schedule the whole stack, or generated extra drops.");
    }
    public static void GravityWaterAndPersistence()
    {
        var world = new World(173, WorldSize.Small, false);
        for (int z = 13; z <= 19; z++) for (int x = 12; x <= 18; x++)
        {
            world.SetBlock(x, 0, z, BlockType.Stone);
            for (int y = 1; y <= 6; y++)
                world.SetBlock(x, y, z, x == 12 || x == 18 || z == 13 || z == 19 ? BlockType.Stone : y <= 5 ? BlockType.WaterSource : BlockType.Air);
        }
        world.SetBlock(15, 10, 16, BlockType.Sand); world.Gravity.Tick(); SettleGravity(world);
        Require(world.GetBlock(15, 1, 16) == BlockType.Sand && Enumerable.Range(2,4).All(y => world.GetBlock(15,y,16) == BlockType.WaterSource), "Falling sand erased water along its path.");
        WaterChecks.Settle(world); Require(world.GetBlock(15, 1, 16) == BlockType.Sand && FallingCount(world) == 1, "Settled water corrupted falling-block accounting.");
        world.SetBlock(16, 12, 16, BlockType.Gravel);
        var save = new WorldSave(world, "Queued gravity", new Vector3(60,1,60), 0, 0);
        WorldSave loaded = WorldSerializer.Read(new MemoryStream(MobChecks.Encode(save)));
        Require(loaded.World.GetBlock(16, 12, 16) == BlockType.Gravel && loaded.World.Gravity.PendingCount > 0, "Save/load prematurely moved or forgot an unsupported block.");
        loaded.World.Gravity.Update(0.05);
        Require(loaded.World.Gravity.Active.Count == 1 && loaded.World.Gravity.Active[0].Y > 11, "Queued saved gravity skipped its falling animation.");
        SettleGravity(loaded.World); Require(loaded.World.GetBlock(16, 1, 16) == BlockType.Gravel && FallingCount(loaded.World) == 2, "Saved gravity did not resume exactly once.");
        var old = new World(1, WorldSize.Small, false); old.SetBlock(20, 0, 20, BlockType.Stone); old.SetBlock(20, 8, 20, BlockType.Sand);
        WorldSave legacy = WorldSerializer.Read(new MemoryStream(Version5(new WorldSave(old, "Floating legacy", new Vector3(60,1,60), 0,0))));
        legacy.World.Gravity.Update(1); Require(legacy.World.GetBlock(20, 8, 20) == BlockType.Sand && legacy.World.Gravity.PendingCount == 0, "Loading retroactively changed legacy terrain.");
        legacy = WorldSerializer.Read(new MemoryStream(MobChecks.Encode(legacy))); legacy.World.Gravity.Update(1);
        Require(legacy.World.GetBlock(20, 8, 20) == BlockType.Sand, "Upgrading a legacy save started an unwanted global cascade.");
        legacy.World.SetBlock(20, 0, 20, BlockType.Air); legacy.World.Gravity.Tick(); SettleGravity(legacy.World);
        Require(legacy.World.GetBlock(20, 0, 20) == BlockType.Sand, "A subsequent legacy-world edit failed to enable gravity.");
        Throws<InvalidDataException>(() => old.Gravity.Restore(new[] { old.Width * old.Depth }));
        Throws<InvalidDataException>(() => old.Gravity.Restore(new[] { 1,1 }));
    }
    public static void GenerationAndBudgets()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            World world = World.CreateNew(173, size); int count = 0;
            foreach (Chunk chunk in world.Chunks) for (int y = 0; y < World.Height; y++) for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                if (Blocks.Falls(chunk.GetBlock(x,y,z)))
                {
                    count++; Require(y > 0 && world.IsSolid(chunk.OriginX+x, y-1, chunk.OriginZ+z), "Generation placed floating Sand/Gravel.");
                }
            Require(count > 0 && world.Gravity.PendingCount == 0, "Generated shores missing or initialized a gravity cascade.");
            int revision = world.Revision; world.Gravity.Tick(); Require(revision == world.Revision, "Stable new world moved on first gravity tick.");
        }
        var busy = new World(0, WorldSize.Small, false);
        for (int x = 0; x < 100; x++) busy.SetBlock(x, 8, 8, BlockType.Sand);
        busy.Gravity.Tick(); Require(busy.Gravity.LastProcessedColumns == 32 && busy.Gravity.PendingCount == 68, "Gravity column budget is unbounded.");
        while (busy.Gravity.PendingCount > 0) busy.Gravity.Tick();
        Require(busy.Gravity.Active.Count == 100, "Bounded gravity activation lost blocks.");
        SettleGravity(busy); Require(FallingCount(busy) == 100, "Bounded gravity processing lost blocks.");
        Throws<InvalidOperationException>(() => busy.SetGeneratedBlock(110, 10, 10, BlockType.Gravel));
    }
    public static void TimingAndScroll()
    {
        MobState[] reference = null;
        foreach (int fps in new[] { 5,30,60,144,240,360,997 })
        {
            World world = PigChecks.Flat(); var mobs = new PassiveMobPopulation(world, new[] { MobChecks.State(MobSpecies.Sheep, health: 1) }); var items = new DroppedItems(world);
            Kill(mobs, mobs.Entities[0], items); var clock = new SimulationClock();
            world.SetBlock(15, 8, 15, BlockType.Sand);
            for (int frame = 0; frame < fps * 65; frame++) for (int step = clock.Advance(1d/fps); step > 0; step--)
            { mobs.Update((float)SimulationClock.StepSeconds); world.Gravity.Update(SimulationClock.StepSeconds); }
            MobState[] actual = mobs.CaptureAll(); Require(actual.Length == 1 && (reference == null || reference.AsSpan().SequenceEqual(actual)), "Respawn timing/motion depends on rendering FPS."); reference = actual;
            Require(world.GetBlock(15, 1, 15) == BlockType.Sand && world.Gravity.PendingCount == 0, "Gravity depends on rendered FPS.");
        }
        var inventory = Inventory.CreateNewWorld(); var scroll = new HotbarScroll();
        scroll.Apply(inventory, -120); Require(inventory.SelectedSlot == 1, "Scroll down did not move right.");
        scroll.Apply(inventory, 120); Require(inventory.SelectedSlot == 0, "Scroll up did not move left.");
        scroll.Apply(inventory, 120); Require(inventory.SelectedSlot == 8, "Scroll up did not wrap 1 to 9.");
        scroll.Apply(inventory, -120); Require(inventory.SelectedSlot == 0, "Scroll down did not wrap 9 to 1.");
    }
}
