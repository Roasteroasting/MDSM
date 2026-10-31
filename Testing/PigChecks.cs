using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class PigChecks
{
    private const float Dt = (float)SimulationClock.StepSeconds;
    internal static World Flat(WorldSize size = WorldSize.Small)
    {
        var world = new World(173, size, false);
        for (int z = 0; z < world.Depth; z++)
        for (int x = 0; x < world.Width; x++) world.SetGeneratedBlock(x, 0, z, BlockType.Grass);
        return world;
    }
    private static PigState State(float x = 10.5f, float y = 1, float z = 10.5f, int health = 10) => new(new Vector3(x, y, z), 0, health);

    public static void Spawning()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            World world = Flat(size);
            MobState[] first = PassiveMobPopulation.Generate(world, MobSpecies.Pig), second = PassiveMobPopulation.Generate(world, MobSpecies.Pig);
            Require(first.Length == world.Width * world.Depth / 2048 && first.AsSpan().SequenceEqual(second), "Pig count or seeded spawn repeatability failed.");
            foreach (MobState pig in first)
            {
                PassiveMobPopulation.Validate(pig, size);
                Require(pig.Species == MobSpecies.Pig && pig.Health == 10 && !world.IntersectsSolid(PassiveMob.BoundsAt(MobSpecies.Pig, pig.Position))
                    && world.GetBlock((int)pig.Position.X, (int)pig.Position.Y - 1, (int)pig.Position.Z) == BlockType.Grass
                    && Vector3.DistanceSquared(pig.Position, world.Spawn) >= 144, "Invalid/obstructed/spawn-adjacent pig.");
            }
            Require(world.Revision == 0 && world.Chunks[0].Revision == 0, "Pig generation modified terrain or dirtied chunk meshes.");
            Require(new PassiveMobPopulation(world).Entities.Count == 0, "Loading empty population spawned pigs.");
        }
        var terrain = new World(173, WorldSize.Small);
        Require(PassiveMobPopulation.Generate(terrain, MobSpecies.Pig).Length == 32, "Generated terrain did not support a modest pig population.");
    }

    public static void MovementAndDamage()
    {
        World world = Flat();
        var pig = new PassiveMob(State(y: 5).ToMobState(), 17);
        for (int i = 0; i < 600; i++) pig.Update(world, Dt);
        Require(pig.Health == 10 && pig.Grounded && Math.Abs(pig.Position.Y - 1) < 0.0001f, "Pig gravity/landing/health failed.");
        bool walked = false, idled = false;
        for (int i = 0; i < 3600; i++)
        {
            pig.Update(world, Dt); walked |= pig.Walking; idled |= !pig.Walking;
            Require(!world.IntersectsSolid(pig.Bounds), "Wandering pig entered solid terrain.");
        }
        Require(walked && idled && pig.WalkPhase > 0, "Idle/wander alternation or walking animation missing.");
        var damaged = new PassiveMob(State().ToMobState(), 123); var undamaged = new PassiveMob(State().ToMobState(), 123);
        damaged.Damage(2, Vector3.Right);
        Require(damaged.Health == 8 && damaged.HurtSeconds > 0 && damaged.Yaw == undamaged.Yaw, "Damage changed AI facing, health, or flash failed.");
        damaged.Update(world, Dt);
        Require(damaged.Position.X > 10.5f && damaged.Position.Y > 1, "Missing backward/upward knockback.");
        for (int i = 0; i < 100; i++) damaged.Update(world, Dt);
        Require(damaged.HurtSeconds == 0, "Hurt flash never cleared.");
        var passive = new PassiveMob(State(50, 1, 50).ToMobState(), 321); var control = new PassiveMob(State(50, 1, 50).ToMobState(), 321);
        passive.Damage(1, Vector3.Right);
        for (int i = 0; i < 1440; i++)
        {
            passive.Update(world, Dt); control.Update(world, Dt);
            Require(passive.Yaw == control.Yaw && control.Health == 10, "Damage triggered fleeing, pursuit, or an AI decision change.");
        }
        damaged.Damage(int.MaxValue, Vector3.Zero); damaged.Damage(2, Vector3.Zero);
        Require(damaged.Health == 0 && damaged.IsDead, "Pig health escaped 0-10 or dead pig received repeated damage.");
        var wallPig = new PassiveMob(State().ToMobState(), 123);
        for (int z = 8; z <= 12; z++) for (int y = 1; y < 4; y++) world.SetBlock(12, y, z, BlockType.Stone);
        wallPig.Damage(1, Vector3.Right);
        for (int i = 0; i < 120; i++) wallPig.Update(world, Dt);
        Require(wallPig.Position.X <= 12 - 0.8f + 0.0001f && !world.IntersectsSolid(wallPig.Bounds), "Pig tunneled through a wall.");
        var edge = new PassiveMob(State(world.Width - 0.8f).ToMobState(), 12); edge.Damage(1, Vector3.Right);
        for (int i = 0; i < 500; i++) edge.Update(world, Dt);
        Require(edge.Position.X <= world.Width - 0.8f && edge.Position.X >= 0.8f
            && edge.Position.Z >= 0.8f && edge.Position.Z <= world.Depth - 0.8f, "Pig left finite world bounds.");
        Vector3? reference = null;
        foreach (int fps in new[] { 5, 30, 60, 144, 240, 360, 997 })
        {
            var clock = new SimulationClock(); var ratePig = new PassiveMob(State(50, 4, 50).ToMobState(), 99);
            for (int frame = 0; frame < fps * 5; frame++)
                for (int step = clock.Advance(1.0 / fps); step > 0; step--) ratePig.Update(world, Dt);
            Require(!reference.HasValue || reference == ratePig.Position, "Pig motion depends on rendering FPS.");
            reference = ratePig.Position;
        }
        Require(world.Revision == 15, "Moving pigs changed chunk revisions."); // Only the wall edits above.
    }

    public static void Combat()
    {
        World world = Flat(); var pigs = new PassiveMobPopulation(world, new[] { State() }, default);
        var drops = new DroppedItems(world); PassiveMob pig = pigs.Entities[0];
        Vector3 eye = new(10.5f, 1.5f, 15);
        Require(EntityRaycaster.Cast(world, pigs, eye, Vector3.Forward) == pig, "Pig targeting missed within reach.");
        Require(EntityRaycaster.Cast(world, pigs, eye + new Vector3(0, 0, 5), Vector3.Forward) == null, "Pig targeted out of reach.");
        world.SetBlock(10, 1, 13, BlockType.Stone);
        Require(EntityRaycaster.Cast(world, pigs, eye, Vector3.Forward) == null, "Pig was targetable through solid terrain.");
        world.SetBlock(10, 1, 13, BlockType.Air);
        world.SetBlock(10, 1, 13, BlockType.TallGrass);
        Require(EntityRaycaster.Cast(world, pigs, eye, Vector3.Forward) == pig, "Non-solid vegetation incorrectly occluded a pig.");
        world.SetBlock(10, 1, 13, BlockType.Air);
        world.SetBlock(10, 1, 8, BlockType.Stone);
        var mining = new MiningController(); var combat = new CombatController();
        BlockHit? block = BlockRaycaster.Cast(world, eye, Vector3.Forward);
        combat.Update(world, pigs, drops, mining, eye, Vector3.Forward, block, true, Dt);
        int afterPunch = pig.Health;
        Require(afterPunch is 8 or 9 && mining.Target == null, "Punch damage or mining priority failed.");
        for (int i = 0; i < 20; i++) combat.Update(world, pigs, drops, mining, eye, Vector3.Forward, block, true, Dt);
        Require(pig.Health == afterPunch, "Attack cooldown allowed per-frame damage.");
        while (pigs.Entities.Count > 0) combat.Update(world, pigs, drops, mining, eye, Vector3.Forward, block, true, Dt);
        Require(pig.IsDead && drops.Entities.Count is 1 or 2 && drops.Entities.All(d=>d.Item==Items.RawPorkchop && d.Count==1)
            && !pigs.Punch(pig, eye, drops), "Death/drop was missed or duplicated.");
        Require(pigs.CapturePigs().Length == 0, "Killed pig remained in saved population.");
        var harvest=new Inventory();harvest.SetSlot(0,new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe),1));
        for (int i = 0; i < 400; i++) combat.Update(world, pigs, drops, mining, eye, Vector3.Forward,
            BlockRaycaster.Cast(world, eye, Vector3.Forward), true, Dt,harvest);
        Require(world.GetBlock(10, 1, 8) == BlockType.Air, "Continuous mining did not resume after pig died.");
        foreach (int fps in new[] { 30, 60, 144, 360 })
        {
            var population = new PassiveMobPopulation(world, new[] { State() }, default); var input = new CombatController();
            var clock = new SimulationClock(); var items = new DroppedItems(world); var mine = new MiningController();
            for (int frame = 0; frame < fps * 10; frame++)
                for (int step = clock.Advance(1.0 / fps); step > 0; step--)
                    input.Update(world, population, items, mine, eye, Vector3.Forward, null, true, Dt);
            Require(population.Entities.Count == 0 && items.Entities.Count is 1 or 2 && items.Entities.All(d=>d.Count==1), "Combat depends on rendering FPS.");
        }
    }

    public static void Food()
    {
        Require(Items.RawPorkchop.Value == 256 && Items.IsDefined(Items.RawPorkchop)
            && !Items.TryGetBlock(Items.RawPorkchop, out _), "Food item ID conflicts with blocks or is placeable.");
        var inventory = new Inventory(); inventory.SetSlot(4, new ItemStack(Items.RawPorkchop, 1));
        Require(inventory.Add(Items.RawPorkchop, 10) == 0 && Enumerable.Range(0,36).Sum(i=>inventory[i].Count)==11
            && Enumerable.Range(0,36).All(i=>inventory[i].Count<=1), "Nonstackable food overflow failed.");
        var interaction = new InventoryInteraction(inventory);
        interaction.Click(0, true); Require(interaction.Cursor.Count == 1 && inventory[0].IsEmpty, "Single food pickup failed.");
        interaction.Click(1, true); Require(interaction.Cursor.Count == 1 && inventory[1].Count == 1, "Nonstackable food merged.");
        Require(interaction.TryReturnCursor(), "Food cursor return failed.");
        foreach (int health in new[] { 10, 18, 19, 20 })
        {
            var player = new Player(new Vector3(10, 1, 10), health);
            inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 1));
            bool used = ItemUse.TryConsume(player, inventory, true, false);
            Require(player.Health == Math.Min(20, health + 2) && used == (health < 20)
                && inventory[0].Count == (health < 20 ? 0 : 1), "Healing cap or consumption quantity failed.");
            Require(!ItemUse.TryConsume(player, inventory, true, true) && !ItemUse.TryConsume(player, inventory, false, true), "Held/released RMB consumed food.");
        }
        var wounded = new Player(new Vector3(10, 1, 10), 10);
        inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 1));
        Require(ItemUse.TryConsume(wounded, inventory, true, false) && inventory[0].IsEmpty, "Last porkchop left an invalid slot.");
        inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 1));
        var dead = new Player(new Vector3(10, 1, 10), 0);
        Require(!ItemUse.TryConsume(dead, inventory, true, false) && dead.Health == 0 && inventory[0].Count == 1, "Eating revived a dead player.");
        inventory = Inventory.CreateNewWorld();
        for (int i = 0; i < Inventory.SlotCount; i++) Require(inventory[i].IsEmpty, "New-world survival inventory is not empty.");
        Require(Inventory.CreateStarter()[0].Count == 64 && Inventory.CreateStarter()[13].IsEmpty, "Legacy migration stock changed.");
        World world = Flat();
        var session = new GameSession("food", new WorldSave(world, "Food", new Vector3(20, 1, 20), 0, 0, inventory));
        session.Player.ApplyDamage(20); session.HandleDeath(); session.Respawn();
        Require(session.Inventory[13].IsEmpty && session.Items.Entities.Count == 0 && session.Player.Health == 20, "Death/respawn replenished food or blocks.");
        var drops = new DroppedItems(world); inventory = new Inventory();
        drops.Spawn(Items.RawPorkchop, 2, new Vector3(20, 1.125f, 20), 0);
        drops.Update(new Player(new Vector3(20, 1, 20)), inventory, Dt);
        Require(drops.Entities.Count == 0 && inventory[0].Count == 1 && inventory[1].Count==1, "Non-block drop could not be picked up.");
        for (int slot = 0; slot < Inventory.SlotCount; slot++) inventory.SetSlot(slot, new ItemStack(Items.RawPorkchop, 1));
        inventory.SetSlot(0, default);
        drops.Spawn(Items.RawPorkchop, 2, new Vector3(20, 1.125f, 20), 0);
        drops.Update(new Player(new Vector3(20, 1, 20)), inventory, Dt);
        Require(inventory[0].Count == 1 && drops.Entities.Count == 1 && drops.Entities[0].Count == 1,
            "Full inventory lost the uncollected porkchop remainder.");
        Require(!BlockPlacement.TryPlace(world, inventory, new Point3(20, 1, 21), new Player(new Vector3(10, 1, 10)).Bounds), "Food placed a terrain block.");
    }

    public static void Persistence()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            World world = new(173, size, false);
            var save = new WorldSave(world, "Pig persistence", new Vector3(20, 10, 20), 0.1f, 0.2f, Inventory.CreateNewWorld(),
                new[] { new DroppedItemState(Items.RawPorkchop, 1, new Vector3(30, 5, 30), -1, 0.5f),new DroppedItemState(Items.RawPorkchop,1,new Vector3(30,5,30),-1,.5f) }, 12,
                new[] { State(50, 5, 50, 3) with { Yaw = 1.25f } });
            byte[] bytes = Encode(save); WorldSave restored = Decode(bytes);
            Require(restored.Pigs.AsSpan().SequenceEqual(save.Pigs) && restored.DroppedItems.AsSpan().SequenceEqual(save.DroppedItems)
                && restored.Inventory[13] == save.Inventory[13] && restored.Health == 12, "Pig/food/player persistence failed.");
            var session = new GameSession("pigs", restored);
            Require(session.Mobs.Entities.Count == 1 && session.Mobs.Entities[0].Health == 3 && session.Mobs.Entities[0].Yaw == 1.25f,
                "Session regenerated pigs or reset saved health/yaw.");
            Require(bytes.AsSpan().SequenceEqual(Encode(session.Capture())), "Saving/loading duplicated or changed entities.");
            var empty = save with { Pigs = Array.Empty<PigState>(), Inventory = Inventory.CreateStarter(), DroppedItems = Array.Empty<DroppedItemState>() };
            byte[] v3 = ToVersion3(Encode(empty));
            WorldSave legacy = Decode(v3);
            Require(legacy.Pigs.Length == 0 && legacy.Inventory[13].IsEmpty && new GameSession("legacy", legacy).Mobs.Entities.Count == 0,
                "Old world unexpectedly received pigs or porkchops.");
            Require(Decode(Encode(legacy)).Pigs.Length == 0, "Upgraded old world repopulated pigs.");
            byte[] killed = Encode(save with { Pigs = Array.Empty<PigState>() });
            Require(Decode(killed).Pigs.Length == 0, "Empty/killed population regenerated.");
            foreach (PigState bad in new[] { State(health: 0), State(health: 11), State(x: -1), State(y: float.NaN), State() with { Yaw = float.PositiveInfinity } })
                Throws<InvalidDataException>(() => Encode(save with { Pigs = new[] { bad } }));
            int pigOffset = SaveLayout.MobRecords(bytes) + 1;
            foreach (int offset in new[] { pigOffset - 5, pigOffset + 16 })
            {
                byte[] bad = (byte[])bytes.Clone(); BitConverter.GetBytes(-1).CopyTo(bad, offset);
                SHA256.HashData(bad.AsSpan(0, bad.Length - 32)).CopyTo(bad, bad.Length - 32);
                Throws<InvalidDataException>(() => Decode(bad));
            }
            foreach (int badHealth in new[] { -1, 21 })
            {
                byte[] bad = (byte[])bytes.Clone(); BitConverter.GetBytes(badHealth).CopyTo(bad, pigOffset - 9);
                SHA256.HashData(bad.AsSpan(0, bad.Length - 32)).CopyTo(bad, bad.Length - 32);
                Throws<InvalidDataException>(() => Decode(bad));
            }
            byte[] oversized = (byte[])bytes.Clone(); BitConverter.GetBytes(PassiveMobPopulation.MaximumMobs + 1).CopyTo(oversized, pigOffset - 5);
            SHA256.HashData(oversized.AsSpan(0, oversized.Length - 32)).CopyTo(oversized, oversized.Length - 32);
            Throws<InvalidDataException>(() => Decode(oversized));
            Throws<InvalidDataException>(() => Decode(bytes[..^1]));
            using var header = new MemoryStream(bytes); Require(WorldSerializer.ReadMetadata(header).Size == size, "Pig data broke metadata listing.");
        }
    }

    public static void Geometry()
    {
        World world = Flat(); var pig = new PassiveMob(State().ToMobState(), 17);
        var mesh = new CuboidModelBuilder(64, 16); PassiveMobModels.Append(mesh, pig);
        Require(mesh.Count == 7 * 36, "Pig lost cuboid body/head/snout/four legs.");
        var idle = mesh.Vertices.AsSpan(0, mesh.Count).ToArray();
        foreach (var vertex in idle) Require(vertex.TextureCoordinate.X > 0 && vertex.TextureCoordinate.X < 1
            && vertex.TextureCoordinate.Y > 0 && vertex.TextureCoordinate.Y < 1 && vertex.Color.A == 255, "Pig UV/vertex alpha invalid.");
        pig.Damage(1, Vector3.Right); mesh.Clear(); PassiveMobModels.Append(mesh, pig);
        Require(mesh.Vertices.Take(mesh.Count).Any(v => v.Color.R > v.Color.G * 2), "Pig damage does not tint red.");
        for (int i = 0; i < 600 && !pig.Walking; i++) pig.Update(world, Dt);
        mesh.Clear(); PassiveMobModels.Append(mesh, pig);
        Require(pig.Walking && !idle.AsSpan().SequenceEqual(mesh.Vertices.AsSpan(0, mesh.Count)), "Walking model did not animate.");
        var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 1));
        var selection = ViewModelSelection.Select(GameState.Playing, inventory); var geometry = new ViewModelGeometry(); geometry.Build(selection);
        Require(selection.Kind == ViewModelKind.Sprite && geometry.VertexCount == 12, "Food held model missing.");
        foreach (GameState state in Enum.GetValues<GameState>())
            if (state != GameState.Playing) Require(ViewModelSelection.Select(state, inventory).Kind == ViewModelKind.Hidden, "Held food leaked into menu.");
    }

    // V3 has no pig section. Used by the existing independent V1/V2 fixtures too.
    internal static byte[] ToVersion3(byte[] version4)
    {
        if (BitConverter.ToInt32(version4, SaveLayout.MobCount(version4)) != 0) throw new InvalidOperationException("Expected an empty pig section.");
        int end = SaveLayout.Health(version4) + 4;
        byte[] v3 = new byte[end + 32]; version4.AsSpan(0, end).CopyTo(v3);
        BitConverter.GetBytes(3).CopyTo(v3, 4);
        SHA256.HashData(v3.AsSpan(0, v3.Length - 32)).CopyTo(v3, v3.Length - 32);
        return v3;
    }
    private static byte[] Encode(WorldSave save) { using var stream = new MemoryStream(); WorldSerializer.Write(stream, save); return stream.ToArray(); }
    private static WorldSave Decode(byte[] bytes) => WorldSerializer.Read(new MemoryStream(bytes));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
