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
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class MobChecks
{
    private const float Dt = (float)SimulationClock.StepSeconds;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    internal static MobState State(MobSpecies species, Vector3? position = null, int? health = null) =>
        new(species, position ?? new Vector3(20.5f, 1, 20.5f), 0, health ?? MobDefinitions.Get(species).MaximumHealth);

    public static void DefinitionsAndMotion()
    {
        foreach (MobSpecies species in Enum.GetValues<MobSpecies>())
        {
            MobDefinition definition = MobDefinitions.Get(species);
            Require(definition.MaximumHealth == (species == MobSpecies.Chicken ? 6 : 10), "Incorrect species health.");
            World world = PigChecks.Flat(); var mob = new PassiveMob(State(species, new Vector3(40, 5, 40)), 17);
            bool walked = false, idle = false;
            for (int i = 0; i < 1800; i++)
            {
                mob.Update(world, Dt); walked |= mob.Walking; idle |= !mob.Walking;
                Require(!world.IntersectsSolid(mob.Bounds), "Species penetrated terrain.");
            }
            Require(walked && idle && mob.Health == definition.MaximumHealth && mob.Grounded && mob.Position.Y == 1, "Missing passive motion/landing.");
            var damaged = new PassiveMob(State(species, new Vector3(50, 1, 50)), 321);
            var control = new PassiveMob(damaged.CaptureState(), 321); damaged.Damage(2, Vector3.Right); damaged.Update(world, Dt);
            control.Update(world, Dt);
            Require(damaged.Health == definition.MaximumHealth - 2 && damaged.HurtSeconds > 0 && damaged.Position.X > 50 && damaged.Position.Y > 1,
                "Missing health loss, flash, or knockback.");
            for (int i = 0; i < 1440; i++) { damaged.Update(world, Dt); control.Update(world, Dt); Require(damaged.Yaw == control.Yaw, "Damage changed passive AI decisions."); }
            damaged.Damage(int.MaxValue, Vector3.Zero); damaged.Damage(2, Vector3.Zero); Require(damaged.IsDead && damaged.Health == 0, "Death health did not clamp.");
            for (int y = 1; y < 6; y++) for (int z = 17; z < 24; z++) world.SetBlock(22, y, z, BlockType.Stone);
            var wall = new PassiveMob(State(species), 123); wall.Damage(1, Vector3.Right);
            for (int i = 0; i < 120; i++) { wall.Update(world, Dt); Require(!world.IntersectsSolid(wall.Bounds), "Species tunneled through wall."); }
            Require(wall.Position.X <= 22 - definition.HalfWidth + 0.0001f, "Species collision size ignored.");
            var edge = new PassiveMob(State(species, new Vector3(world.Width - definition.HalfWidth, 1, world.Depth - definition.HalfWidth)), 12);
            edge.Damage(1, new Vector3(1, 0, 1));
            for (int i = 0; i < 600; i++) edge.Update(world, Dt);
            Require(edge.Bounds.Min.X >= 0 && edge.Bounds.Min.Z >= 0 && edge.Bounds.Max.X <= world.Width && edge.Bounds.Max.Z <= world.Depth, "Species escaped finite world.");
            Vector3? reference = null;
            foreach (int fps in new[] { 5, 60, 144, 360, 997 })
            {
                var clock = new SimulationClock(); var timed = new PassiveMob(State(species, new Vector3(50, 4, 50)), 99);
                for (int frame = 0; frame < fps * 5; frame++) for (int step = clock.Advance(1d / fps); step > 0; step--) timed.Update(world, Dt);
                Require(reference == null || reference == timed.Position, "Species movement depends on rendered FPS."); reference = timed.Position;
            }
            World water = WaterChecks.Floor();
            for (int z = 35; z <= 50; z++) for (int x = 35; x <= 50; x++) for (int y = 1; y <= 3; y++)
                water.SetGeneratedBlock(x, y, z, x == 35 || z == 35 || x == 50 || z == 50 ? BlockType.Stone : BlockType.WaterSource);
            var swimmer = new PassiveMob(State(species, new Vector3(40, 1, 40)), 123); float peak = swimmer.Position.Y;
            for (int i = 0; i < 480; i++)
            {
                swimmer.Update(water, Dt); peak = Math.Max(peak, swimmer.Position.Y);
                Require(!water.IntersectsSolid(swimmer.Bounds) && swimmer.Position.Y >= 1, "Species penetrated a water tank's bed/wall.");
            }
            Require(peak > 2.4f && swimmer.Health == definition.MaximumHealth, "Species treated water as solid or did not float.");
        }
    }

    public static void Spawning()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            World world = World.CreateNew(173, size);
            MobState[] first = PassiveMobPopulation.Generate(world), same = PassiveMobPopulation.Generate(world);
            Require(first.AsSpan().SequenceEqual(same) && first.Length == PassiveMobPopulation.InitialBudget(world)
                && first.Length == (size.Width() * size.Width() / 2048), "Spawn budget multiplied or repeatability failed.");
            int[] counts = new int[4];
            foreach (MobState state in first)
            {
                PassiveMobPopulation.Validate(state, size); counts[(int)state.Species]++;
                BoundingBox bounds = PassiveMob.BoundsAt(state.Species, state.Position);
                Require(state.Health == MobDefinitions.Get(state.Species).MaximumHealth && !world.IntersectsSolid(bounds) && !Water.Intersects(world, bounds)
                    && world.GetBlock((int)state.Position.X, (int)state.Position.Y - 1, (int)state.Position.Z) == BlockType.Grass
                    && Vector2.Distance(new Vector2(state.Position.X, state.Position.Z), new Vector2(world.Spawn.X, world.Spawn.Z)) >= 12,
                    "Unsuitable/obstructed/wet/spawn-adjacent mob.");
            }
            Require(counts.All(count => count > 0) && counts.Max() - counts.Min() <= 2, "Species mixture is missing/unbalanced.");
            Require(world.Revision == 0 && new PassiveMobPopulation(world).Entities.Count == 0, "Spawning dirtied terrain or empty restoration repopulated.");
            Console.WriteLine($"  {size}: {first.Length} total mobs; pigs {counts[0]}, cows {counts[1]}, sheep {counts[2]}, chickens {counts[3]}.");
        }
        var dry = PigChecks.Flat();
        Throws<InvalidDataException>(() => new PassiveMobPopulation(dry, Enumerable.Repeat(State(MobSpecies.Chicken), PassiveMobPopulation.MaximumMobs + 1).ToArray()));
        Throws<InvalidDataException>(() => new PassiveMobPopulation(dry,
            Enumerable.Repeat(new PigState(new Vector3(20, 1, 20), 0, 10), 128).ToArray(), Enumerable.Repeat(State(MobSpecies.Cow), PassiveMobPopulation.MaximumMobs - 127).ToArray()));
    }

    public static void CombatAndDrops()
    {
        foreach (MobSpecies species in Enum.GetValues<MobSpecies>())
        {
            World world = PigChecks.Flat(); var population = new PassiveMobPopulation(world, new[] { State(species) });
            PassiveMob mob = population.Entities[0]; var drops = new DroppedItems(world);
            Vector3 eye = new(20.5f, 1 + mob.Definition.Height * 0.6f, 25);
            Require(EntityRaycaster.Cast(world, population, eye, Vector3.Forward) == mob, "Species target missed its bounds.");
            int y = (int)eye.Y; world.SetBlock(20, y, 23, BlockType.Stone);
            Require(EntityRaycaster.Cast(world, population, eye, Vector3.Forward) == null, "Species target bypassed terrain occlusion.");
            world.SetBlock(20, y, 23, BlockType.WaterSource);
            Require(EntityRaycaster.Cast(world, population, eye, Vector3.Forward) == mob, "Water incorrectly obstructed mob targeting.");
            world.SetBlock(20, y, 23, BlockType.Air); world.SetBlock(20, y, 18, BlockType.Stone);
            var mining = new MiningController(); var combat = new CombatController();
            BlockHit? target = BlockRaycaster.Cast(world, eye, Vector3.Forward);
            combat.Update(world, population, drops, mining, eye, Vector3.Forward, target, true, Dt);
            int health = mob.Health;
            Require(mob.Definition.MaximumHealth - health is 1 or 2 && mining.Target == null, "Punch did not take mining priority or deal 1-2 damage.");
            for (int i = 0; i < 20; i++) combat.Update(world, population, drops, mining, eye, Vector3.Forward, target, true, Dt);
            Require(mob.Health == health, "Species attack cooldown changed.");
            for (int i = 0; i < 1000 && !mob.IsDead; i++) combat.Update(world, population, drops, mining, eye, Vector3.Forward, target, true, Dt);
            Require(mob.IsDead && population.Entities.Count == 0 && drops.Entities.All(d=>d.Item==mob.Definition.Drop && d.Count<=Items.StackLimit(d.Item))
                && drops.Entities.Sum(d=>d.Count) >= 1 && drops.Entities.Sum(d=>d.Count) <= mob.Definition.MaximumDrop && !population.Punch(mob, eye, drops), "Species death/drop missing, wrong, or duplicated.");
            var harvest=new Inventory();harvest.SetSlot(0,new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe),1));
            for (int i = 0; i < 420; i++) combat.Update(world, population, drops, mining, eye, Vector3.Forward, BlockRaycaster.Cast(world, eye, Vector3.Forward), true, Dt,harvest);
            Require(world.GetBlock(20, y, 18) == BlockType.Air, "Continuous mining did not resume after species death.");
            var amounts = new HashSet<int>();
            for (int i = 0; i < 40; i++)
            {
                // Vary world seeds for independent deterministic combat random streams.
                World seeded = new World(i, WorldSize.Small, false);
                var wounded = new PassiveMobPopulation(seeded, new[] { State(species, health: 1) }); var items = new DroppedItems(seeded);
                Require(wounded.Punch(wounded.Entities[0], eye, items), "Fatal hit failed with available drop capacity."); amounts.Add(items.Entities.Sum(d=>d.Count));
            }
            Require(amounts.Count == mob.Definition.MaximumDrop && amounts.Min() == 1 && amounts.Max() == mob.Definition.MaximumDrop, "Death drop range missing values.");
        }
        World flat = PigChecks.Flat(); var mixed = new PassiveMobPopulation(flat,
            new[] { State(MobSpecies.Cow, new Vector3(20.5f, 1, 20)), State(MobSpecies.Chicken, new Vector3(20.5f, 1, 23)) });
        Require(EntityRaycaster.Cast(flat, mixed, new Vector3(20.5f, 2.1f, 25), Vector3.Forward)?.Species == MobSpecies.Cow
            && EntityRaycaster.Cast(flat, mixed, new Vector3(20.5f, 1.5f, 25), Vector3.Forward)?.Species == MobSpecies.Chicken,
            "Targeting ignored species-specific height or nearest entity.");
    }

    public static void ResourcesAndWool()
    {
        Require(Items.Leather.Value == 257 && Items.Feather.Value == 258 && (byte)BlockType.WhiteWool == 23
            && Blocks.Tile(BlockType.WhiteWool, Vector3.Up) == 15 && Blocks.IsSolid(BlockType.WhiteWool) && Blocks.IsOpaque(BlockType.WhiteWool), "New resource/block IDs or properties invalid.");
        foreach (ItemId item in new[] { Items.Leather, Items.Feather, Items.FromBlock(BlockType.WhiteWool) })
        {
            var inventory = new Inventory(); inventory.SetSlot(5, new ItemStack(item, 60));
            Require(inventory.Add(item, 10) == 0 && inventory[5].Count == 64 && inventory[0].Count == 6, "Resource stack overflow failed.");
            var ui = new InventoryInteraction(inventory); inventory.SetSlot(0, new ItemStack(item, 27)); ui.Click(0, true);
            Require(ui.Cursor.Count == 13 && inventory[0].Count == 14, "Resource odd split lost quantity.");
            ui.Click(1, true); Require(ui.Cursor.Count == 12 && inventory[1].Count == 1 && ui.TryReturnCursor(), "Resource cursor/single placement failed.");
            World world = PigChecks.Flat(); inventory.SelectedSlot = 0;
            var player = new Player(new Vector3(50, 1, 50), 10);
            if (item != Items.FromBlock(BlockType.WhiteWool))
            {
                int before = inventory[0].Count;
                Require(!Items.TryGetBlock(item, out _) && !BlockPlacement.TryPlace(world, inventory, new Point3(20, 1, 20), player.Bounds)
                    && !ItemUse.TryConsume(player, inventory, true, false) && inventory[0].Count == before && player.Health == 10, "Resource placed a block or acted as food.");
                var selected = ViewModelSelection.Select(GameState.Playing, inventory); var geometry = new ViewModelGeometry(); geometry.Build(selected);
                Require(selected.Kind == ViewModelKind.Sprite && geometry.VertexCount == 12, "Resource held sprite missing.");
            }
            var drops = new DroppedItems(world); var full = new Inventory();
            for (int i = 0; i < 36; i++) full.SetSlot(i, new ItemStack(item, 64)); full.SetSlot(0, new ItemStack(item, 63));
            drops.Spawn(item, 4, new Vector3(50, 1.125f, 50), 0); drops.Update(player, full, Dt);
            Require(full[0].Count == 64 && drops.Entities.Count == 1 && drops.Entities[0].Count == 3, "Resource partial pickup lost items.");
        }
        World woolWorld = PigChecks.Flat(); var wool = new Inventory(); wool.SetSlot(0, new ItemStack(Items.FromBlock(BlockType.WhiteWool), 2));
        Require(BlockPlacement.TryPlace(woolWorld, wool, new Point3(15, 1, 15), new BoundingBox()) && wool[0].Count == 1, "Wool placement failed.");
        var mesh = new ChunkMeshBuilder(); mesh.Build(woolWorld, woolWorld.GetChunk(0, 0));
        // Separately build a held cube to check every face uses the wool tile rather than other terrain.
        mesh.Clear(); mesh.AppendItem(BlockType.WhiteWool, Vector3.Zero);
        Require(mesh.VertexCount == 36 && mesh.Vertices.Take(36).All(v => v.TextureCoordinate.X >= 240.5f / 256 && v.TextureCoordinate.Y <= 15.5f / 256), "Wool cube mapping is incorrect.");
        var mine = new MiningController(); var woolDrops = new DroppedItems(woolWorld); var hit = new BlockHit(new Point3(15, 1, 15), new Point3(0, 1, 0), 1);
        mine.Update(woolWorld, woolDrops, hit, true, 0.5f); Require(woolWorld.GetBlock(15, 1, 15) == BlockType.WhiteWool && mine.Progress > 0, "Wool mining is not timed.");
        mine.Update(woolWorld, woolDrops, hit, true, 0.8f);
        Require(woolWorld.GetBlock(15, 1, 15) == BlockType.Air && woolDrops.Entities.Count == 1 && woolDrops.Entities[0].Item == Items.FromBlock(BlockType.WhiteWool), "Wool did not drop itself.");
    }

    public static void StartingAndRespawn()
    {
        Inventory inventory = Inventory.CreateNewWorld();
        Require(Blocks.Selection.Length == 22 && Blocks.Selection[13] == BlockType.WhiteWool && Blocks.Selection[14] == BlockType.CraftingTable,
            "Placeable palette omitted wool/table or reordered existing blocks.");
        for (int i = 0; i < Inventory.SlotCount; i++) Require(inventory[i].IsEmpty, "New worlds must not grant starting items.");
        Require(Inventory.CreateStarter()[13].IsEmpty, "Legacy migration granted new wool.");
        var session = new GameSession("new", new WorldSave(PigChecks.Flat(), "New", new Vector3(40, 1, 40), 0, 0, inventory));
        session.Player.ApplyDamage(20); session.HandleDeath(); session.Respawn();
        Require(Enumerable.Range(0, 36).All(i => session.Inventory[i].IsEmpty) && session.Items.Entities.Count == 0, "Death/respawn replenished new-world stock.");
    }

    internal static byte[] Encode(WorldSave save) { using var stream = new MemoryStream(); WorldSerializer.Write(stream, save); return stream.ToArray(); }
    private static WorldSave Decode(byte[] data) => WorldSerializer.Read(new MemoryStream(data));
    public static void Persistence()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            var world = new World(173, size, false); world.SetBlock(15, 8, 15, Water.Flow(3)); world.SetBlock(16, 7, 15, BlockType.WhiteWool);
            var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.Coal, 23)); inventory.SetSlot(1, new ItemStack(Items.Leather, 7));
            inventory.SetSlot(2, new ItemStack(Items.Feather, 12)); inventory.SetSlot(13, new ItemStack(Items.FromBlock(BlockType.WhiteWool), 9));
            var pigs = new[] { new PigState(new Vector3(40, 1, 40), 1.25f, 3) };
            MobState[] mobs = { State(MobSpecies.Cow, new Vector3(50, 1, 50), 6) with { Yaw = 0.5f },
                State(MobSpecies.Sheep, new Vector3(55, 1, 55), 4), State(MobSpecies.Chicken, new Vector3(60, 1, 60), 2) };
            var drops = new[] { new DroppedItemState(Items.Leather, 4, new Vector3(35, 10, 35), -1, 0.5f), new DroppedItemState(Items.Feather, 2, new Vector3(36, 10, 36), -2, 0.2f) };
            var save = new WorldSave(world, "Mixed persistence", new Vector3(30, 10, 30), 0.1f, 0.2f, inventory, drops, 13, pigs, mobs);
            byte[] bytes = Encode(save); WorldSave restored = Decode(bytes);
            Require(restored.Pigs.AsSpan().SequenceEqual(pigs) && restored.Mobs.AsSpan().SequenceEqual(mobs) && restored.DroppedItems.AsSpan().SequenceEqual(drops)
                && restored.World.GetBlock(15, 8, 15) == Water.Flow(3) && restored.World.GetBlock(16, 7, 15) == BlockType.WhiteWool && restored.Health == 13, "Mixed world/entity data changed on restoration.");
            for (int i = 0; i < 36; i++) Require(inventory[i] == restored.Inventory[i], "Saved inventory modified or food removed.");
            var session = new GameSession("loaded", restored); Require(session.Mobs.Entities.Count == 4 && bytes.AsSpan().SequenceEqual(Encode(session.Capture())), "Session duplicated/repopulated species or changed save bytes.");
            PassiveMob dead = session.Mobs.Entities.Single(m => m.Species == MobSpecies.Chicken);
            while (!dead.IsDead) session.Mobs.Punch(dead, session.Player.Position, session.Items);
            WorldSave killed = Decode(Encode(session.Capture()));
            Require(killed.Mobs.Length == 2 && new GameSession("killed", killed).Mobs.Entities.Count == 3, "Killed species respawned on load.");
            using var metadata = new MemoryStream(bytes); Require(WorldSerializer.ReadMetadata(metadata).Version == 9, "Version 8 metadata listing failed.");
        }
    }

    // Independent pre-update layout: no species tags. This is intentionally not the v5 writer.
    internal static byte[] Version4(WorldSave save)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            writer.Write(0x4d53444d); writer.Write(4); writer.Write(save.World.Width); writer.Write(World.Height); writer.Write(save.World.Depth); writer.Write(save.World.Seed);
            byte[] name = Encoding.UTF8.GetBytes(save.Name); writer.Write(name.Length); writer.Write(name);
            writer.Write(save.PlayerPosition.X); writer.Write(save.PlayerPosition.Y); writer.Write(save.PlayerPosition.Z); writer.Write(save.Yaw); writer.Write(save.Pitch);
            writer.Write(save.World.BlockCount); foreach (Chunk chunk in save.World.Chunks) writer.Write(chunk.Blocks);
            Inventory inv = save.Inventory ?? Inventory.CreateStarter(); writer.Write(inv.SelectedSlot); writer.Write(36);
            for (int i = 0; i < 36; i++) { writer.Write(inv[i].Item.Value); writer.Write((byte)inv[i].Count); }
            DroppedItemState[] drops = save.DroppedItems ?? Array.Empty<DroppedItemState>(); writer.Write(drops.Length);
            foreach (DroppedItemState drop in drops) { writer.Write(drop.Item.Value); writer.Write((byte)drop.Count); writer.Write(drop.Position.X); writer.Write(drop.Position.Y); writer.Write(drop.Position.Z); writer.Write(drop.VerticalVelocity); writer.Write(drop.PickupDelay); }
            writer.Write(save.Health); PigState[] pigs = save.Pigs ?? Array.Empty<PigState>(); writer.Write(pigs.Length);
            foreach (PigState pig in pigs) { writer.Write(pig.Position.X); writer.Write(pig.Position.Y); writer.Write(pig.Position.Z); writer.Write(pig.Yaw); writer.Write(pig.Health); }
        }
        byte[] bytes = payload.ToArray(); payload.Write(SHA256.HashData(bytes)); return payload.ToArray();
    }
    public static void BackwardCompatibilityAndValidation()
    {
        World world = PigChecks.Flat(); world.SetBlock(15, 3, 15, BlockType.WaterSource);
        var oldInventory = Inventory.CreateStarter(); oldInventory.SetSlot(13, ItemStack.ReadLegacyFood(Items.RawPorkchop, 47));
        var save = new WorldSave(world, "Pre-mob expansion", new Vector3(40, 1, 40), 0.2f, 0.3f, oldInventory, Health: 17,
            Pigs: new[] { new PigState(new Vector3(30, 1, 30), -1, 4) });
        WorldSave old = Decode(Version4(save));
        Require(old.Pigs.AsSpan().SequenceEqual(save.Pigs) && old.Mobs.Length == 0 && old.Inventory[13].Count == 1 && old.DroppedItems.Sum(d=>d.Count)+Enumerable.Range(0,36).Where(i=>old.Inventory[i].Item==Items.RawPorkchop).Sum(i=>old.Inventory[i].Count)==47
            && old.World.GetBlock(15, 3, 15) == BlockType.WaterSource && old.Health == 17, "V4 migration changed pig/water/food/health.");
        var session = new GameSession("old", old); WorldSave upgraded = Decode(Encode(session.Capture()));
        Require(session.Mobs.Entities.Count == 1 && upgraded.Mobs.Length == 0 && upgraded.Inventory[13].Count == 1 && upgraded.DroppedItems.Sum(d=>d.Count)+Enumerable.Range(0,36).Where(i=>upgraded.Inventory[i].Item==Items.RawPorkchop).Sum(i=>upgraded.Inventory[i].Count)==47, "An old world was repopulated or inventory refilled.");
        byte[] emptyV4 = Version4(save with { Pigs = Array.Empty<PigState>() });
        Require(new GameSession("oldempty", Decode(emptyV4)).Mobs.Entities.Count == 0, "Killed/empty old population was regenerated.");
        var single = save with { Inventory=old.Inventory, Pigs = Array.Empty<PigState>(), Mobs = new[] { State(MobSpecies.Chicken) } };
        byte[] bytes = Encode(single); int record = SaveLayout.MobRecords(bytes);
        foreach ((int Offset, byte[] Value) bad in new[] { (record, new byte[] { 255 }), (record + 17, BitConverter.GetBytes(7)),
            (record + 1, BitConverter.GetBytes(float.NaN)), (record + 13, BitConverter.GetBytes(float.PositiveInfinity)), (record - 4, BitConverter.GetBytes(257)) })
        {
            byte[] invalid = (byte[])bytes.Clone(); bad.Value.CopyTo(invalid, bad.Offset); SHA256.HashData(invalid.AsSpan(0, invalid.Length - 32)).CopyTo(invalid, invalid.Length - 32);
            Throws<InvalidDataException>(() => Decode(invalid));
        }
        Throws<InvalidDataException>(() => Decode(bytes[..^1]));
        Throws<InvalidDataException>(() => Encode(single with { Mobs = new[] { State(MobSpecies.Cow, health: 11) } }));
        Throws<InvalidDataException>(() => Encode(single with { Mobs = new[] { State(MobSpecies.Pig) } }));
    }

    public static void Geometry()
    {
        World world = PigChecks.Flat();
        foreach (MobSpecies species in Enum.GetValues<MobSpecies>())
        {
            var mob = new PassiveMob(State(species), 17); var builder = new CuboidModelBuilder(64, 16); PassiveMobModels.Append(builder, mob);
            int cuboids = species == MobSpecies.Cow ? 9 : species == MobSpecies.Chicken ? 8 : 7;
            Require(builder.Count == cuboids * 36, "Species silhouette lost cuboids.");
            foreach (var v in builder.Vertices.AsSpan(0, builder.Count))
                Require(v.Color.A == 255 && v.TextureCoordinate.X > 0 && v.TextureCoordinate.X < 1 && v.TextureCoordinate.Y > 0 && v.TextureCoordinate.Y < 1
                    && v.Position.X >= mob.Bounds.Min.X - 0.001f && v.Position.X <= mob.Bounds.Max.X + 0.001f && v.Position.Z >= mob.Bounds.Min.Z - 0.001f
                    && v.Position.Z <= mob.Bounds.Max.Z + 0.001f && v.Position.Y >= mob.Position.Y - 0.001f && v.Position.Y <= mob.Bounds.Max.Y + 0.001f,
                    "Species UVs, opacity, or model/collision proportions invalid.");
            for (int i = 0; i < 1000 && (!mob.Walking || Math.Abs(MathF.Sin(mob.WalkPhase)) < 0.5f); i++) mob.Update(world, Dt);
            Require(mob.Walking, "No walking animation reached."); builder.Clear(); PassiveMobModels.Append(builder, mob); var walking = builder.Vertices.AsSpan(0, builder.Count).ToArray();
            var idle = new PassiveMob(mob.CaptureState(), 17); builder.Clear(); PassiveMobModels.Append(builder, idle);
            Require(!walking.AsSpan().SequenceEqual(builder.Vertices.AsSpan(0, builder.Count)), "Species animation was only translation, not limb motion.");
            mob.Damage(1, Vector3.Right); builder.Clear(); PassiveMobModels.Append(builder, mob);
            Require(builder.Vertices.Take(builder.Count).Any(v => v.Color.R > v.Color.G * 2), "Species hurt tint missing.");
        }
    }
}
