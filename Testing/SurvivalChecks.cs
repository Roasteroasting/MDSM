using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class SurvivalChecks
{
    private static ItemId Dirt => Items.FromBlock(BlockType.Dirt);
    private static ItemId Stone => Items.FromBlock(BlockType.Stone);
    private static ItemStack Stack(ItemId item, int count) => new(item, count);
    public static void HealthAndHearts()
    {
        var player = new Player(Vector3.Zero);
        Require(player.Health == 20 && !player.IsDead, "New player is not at full health.");
        player.ApplyDamage(1); Require(player.Health == 19, "Damage was not integral.");
        player.RestoreHealth(int.MaxValue); Require(player.Health == 20, "Healing overflowed maximum health.");
        player.ApplyDamage(int.MaxValue); Require(player.Health == 0 && player.IsDead, "Damage underflowed or did not cause death.");
        player.ApplyDamage(1); Require(player.Health == 0, "Dead player health became negative.");
        player.RestoreHealth(1); Require(player.Health == 1 && !player.IsDead, "Central health restoration failed.");
        Throws<ArgumentOutOfRangeException>(() => player.ApplyDamage(-1));
        Throws<ArgumentOutOfRangeException>(() => player.RestoreHealth(-1));
        foreach (int health in new[] { 20, 19, 10, 1, 0 })
        {
            int represented = 0, full = 0, half = 0;
            for (int heart = 0; heart < 10; heart++)
            {
                int fill = HealthHud.Fill(health, heart); represented += fill;
                if (fill == 2) full++; if (fill == 1) half++;
            }
            Require(represented == health && full == health / 2 && half == health % 2, "Full/half/empty heart mapping is wrong.");
        }
        foreach (var viewport in new[] { new Viewport(0, 0, 320, 240), new Viewport(0, 0, 640, 480), new Viewport(0, 0, 1280, 720) })
        {
            Rectangle hearts = HealthHud.Bounds(viewport), hotbar = InventoryLayout.HotbarSlot(viewport, 0);
            Require(hearts.Left >= 0 && hearts.Right <= viewport.Width && hearts.Bottom < hotbar.Top && hearts.Top > 0, "Heart HUD overlaps the hotbar or viewport edge.");
        }
    }

    public static void FallDistances()
    {
        var world = Flat();
        foreach (int distance in new[] { 0, 3, 4, 5, 10, 13, 23 })
        {
            int damage = Math.Max(0, distance - 3);
            Require(FallTracker.DamageFor(distance) == damage, "Fall formula is incorrect.");
            var straight = new Player(new Vector3(50.5f, 1 + distance, 50.5f));
            var moving = new Player(straight.Position);
            Land(straight, world, Vector3.Zero); Land(moving, world, new Vector3(1, 0, 1));
            Require(straight.Health == Math.Max(0, 20 - damage) && moving.Health == straight.Health
                && Near(straight.Position.Y, 1) && Near(moving.Position.Y, 1), "Fall damage included horizontal movement or had a threshold error.");
        }
        Require(FallTracker.DamageFor(100) == 97 && FallTracker.DamageFor(3.99999f) == 1
            && FallTracker.DamageFor(3.99f) == 0 && FallTracker.DamageFor(3.00001f) == 0, "Fall threshold tolerance or unclamped formula is wrong.");
        foreach (float velocity in new[] { 0f, -15f, -40f })
        {
            var player = new Player(new Vector3(60.5f, 6, 60.5f));
            typeof(Player).GetField("_verticalSpeed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, velocity);
            Land(player, world, Vector3.Zero);
            Require(player.Health == 18, "Fall damage depends on impact velocity rather than distance.");
        }
    }

    public static void JumpAndLanding()
    {
        var world = Flat();
        var player = new Player(new Vector3(60.5f, 1, 60.5f));
        Step(player, world, 1);
        player.Update(world, Vector3.Zero, true, 1f / 60);
        float peak = player.Position.Y;
        for (int i = 0; i < 90; i++) { player.Update(world, Vector3.Zero, false, 1f / 60); peak = Math.Max(peak, player.Position.Y); }
        Require(peak > 2 && player.Grounded && player.Health == 20, "Ordinary jumping causes fall damage or lost jump height.");
        // Each landing clears descent; several small drops cannot accumulate damage.
        for (int x = 10; x < 18; x++)
        for (int z = 10; z < 13; z++) world.SetBlock(x, 3, z, BlockType.Stone);
        player = new Player(new Vector3(12.5f, 4, 11.5f));
        Land(player, world, Vector3.Zero);
        Step(player, world, 120, Vector3.Right);
        Require(player.Health == 20 && Near(player.Position.Y, 1), "A three-block ledge caused damage.");
        var tracker = new FallTracker(); tracker.Reset(10); tracker.Observe(11.4f);
        Require(tracker.Land(6) == 2 && tracker.Land(6) == 0, "Jump-to-lower-surface descent was not measured from the apex or landing did not reset.");
        // Actual jump off a high platform measures from the reached apex, not takeoff.
        for (int x = 30; x < 33; x++) world.SetBlock(x, 7, 30, BlockType.Stone);
        player = new Player(new Vector3(32.5f, 8, 30.5f)); Step(player, world, 1);
        player.Update(world, Vector3.Right, true, 1f / 60); peak = player.Position.Y;
        for (int i = 0; i < 150; i++) { player.Update(world, Vector3.Right, false, 1f / 60); peak = Math.Max(peak, player.Position.Y); }
        Require(player.Health == 20 - FallTracker.DamageFor(peak - 1), "Jumping off a ledge used takeoff height instead of the apex.");
    }

    public static void DeathAndRespawn()
    {
        var world = Flat();
        var save = new WorldSave(world, "Death", new Vector3(30.5f, 24, 30.5f), 0, 0, Inventory.CreateStarter());
        var session = new GameSession("death", save);
        session.InventoryInteraction.Click(0, true);
        int[] totals = InventoryTotals(session);
        Land(session.Player, world, Vector3.Zero);
        Require(session.Player.IsDead && session.HandleDeath(), "Fatal fall did not cause a death transition.");
        Require(session.InventoryInteraction.Cursor.IsEmpty && InventoryCount(session.Inventory) == 0, "Death retained normal inventory or cursor items.");
        Require(totals.AsSpan().SequenceEqual(ItemTotals(session.Items)), "Death lost or duplicated stack quantities.");
        int entities = session.Items.Entities.Count;
        Require(entities == 14 && !session.HandleDeath() && session.Items.Entities.Count == entities, "Death was not idempotent or spawned individual items instead of stacks.");
        Vector3 death = session.Player.Position;
        foreach (DroppedItem item in session.Items.Entities)
            Require(Vector3.Distance(item.Position, death) < 3 && item.PickupDelay == 1, "Death drops are not near death or lack pickup delay.");
        session.Respawn();
        Require(session.Player.Health == 20 && session.Player.Position == world.Spawn && InventoryCount(session.Inventory) == 0
            && !session.Player.Crouching, "Respawn did not reset health/movement, or replenished items.");
        Step(session.Player, world, 120);
        Require(session.Player.Health == 20, "Respawn inherited fall damage.");
        foreach (DroppedItem item in session.Items.Entities) Require(Vector3.Distance(item.Position, death) < 3, "Respawn teleported death drops.");
        var returning = new Player(new Vector3(death.X, 1, death.Z));
        session.Items.Update(returning, session.Inventory, 1.1f);
        Require(session.Items.Entities.Count == 0 && totals.AsSpan().SequenceEqual(InventoryTotals(session)), "Death drops are not collectible after respawn.");
        // Exceptional lack of clearance must retain ownership, including the cursor.
        var enclosed = new World(0, WorldSize.Small, false);
        for (int y = 0; y <= 7; y++) for (int z = 17; z <= 23; z++) for (int x = 17; x <= 23; x++)
            enclosed.SetBlock(x, y, z, BlockType.Stone);
        session = new GameSession("enclosed", new WorldSave(enclosed, "Enclosed", new Vector3(20.5f, 1, 20.5f), 0, 0, Inventory.CreateStarter()));
        session.InventoryInteraction.Click(0, true); totals = InventoryTotals(session);
        session.Player.ApplyDamage(20); session.HandleDeath();
        Require(session.UndroppedDeathItems && session.Items.Entities.Count == 0 && totals.AsSpan().SequenceEqual(InventoryTotals(session)),
            "Failed death drops discarded items or failed to report retained ownership.");
        session.Respawn();
        Require(totals.AsSpan().SequenceEqual(InventoryTotals(session)), "Exceptional respawn duplicated or discarded retained items.");
    }

    public static void ManualDropAndDelay()
    {
        var world = Flat();
        var inventory = new Inventory(); inventory.SetSlot(0, Stack(Dirt, 27));
        var session = new GameSession("manual", new WorldSave(world, "Manual", new Vector3(60.5f, 1, 60.5f), 0, 0, inventory));
        session.InventoryInteraction.Click(0, false);
        Require(session.DropCursor() && session.InventoryInteraction.Cursor.IsEmpty && session.Items.Entities.Count == 1
            && session.Items.Entities[0].Count == 27 && session.Items.Entities[0].PickupDelay == 1, "Whole cursor stack did not drop atomically.");
        DroppedItem item = session.Items.Entities[0];
        Require(!world.IntersectsSolid(new BoundingBox(item.Position - new Vector3(DroppedItem.Radius), item.Position + new Vector3(DroppedItem.Radius)))
            && !World.Overlaps(session.Player.Bounds, new BoundingBox(item.Position - new Vector3(DroppedItem.Radius), item.Position + new Vector3(DroppedItem.Radius))),
            "Manual drop spawned inside terrain or the player.");
        Require(!session.DropCursor(), "Empty cursor generated a drop.");
        session.Items.Update(session.Player, session.Inventory, 0.6f);
        Require(session.Items.Entities.Count == 1 && InventoryCount(session.Inventory) == 0 && Near(item.PickupDelay, 0.4f), "Pickup delay used frames/clamped physics time or collected too early.");
        session.Items.Update(session.Player, session.Inventory, 0.5f);
        Require(session.Items.Entities.Count == 0 && InventoryCount(session.Inventory) == 27, "Elapsed pickup delay never expired.");
        // Obstruct all positions outside the body: failure must retain the held stack.
        session.InventoryInteraction.Click(0, false);
        for (int y = 1; y <= 5; y++)
        for (int z = 57; z <= 63; z++)
        for (int x = 57; x <= 63; x++) world.SetBlock(x, y, z, BlockType.Stone);
        Require(!session.DropCursor() && session.InventoryInteraction.Cursor.Count == 27, "Failed spawn discarded the cursor.");
    }

    public static void Merging()
    {
        var world = Flat(); var far = new Player(new Vector3(90.5f, 1, 90.5f));
        DroppedItems Make(int a, int b, ItemId second, float delay = 1) => new(world, new[]
        {
            new DroppedItemState(Dirt, a, new Vector3(8.9f, 1.125f, 8.9f), 0, 0),
            new DroppedItemState(second, b, new Vector3(9.1f, 1.125f, 9.1f), 0, delay)
        });
        var items = Make(20, 30, Dirt); items.Update(far, new Inventory(), 0.25f);
        Require(items.Entities.Count == 1 && items.Entities[0].Count == 50 && Near(items.Entities[0].PickupDelay, 0.75f), "Merge lost quantities, missed a cell boundary, or bypassed delay.");
        items = Make(40, 40, Dirt); items.Update(far, new Inventory(), 0.25f);
        Require(items.Entities.Count == 2 && items.Entities[0].Count == 64 && items.Entities[1].Count == 16
            && Near(items.Entities[0].PickupDelay, 0.75f) && Near(items.Entities[1].PickupDelay, 0.75f), "Overflow merge or conservative delay failed.");
        items = Make(20, 30, Stone); items.Update(far, new Inventory(), 0.25f);
        Require(items.Entities.Count == 2, "Unrelated types merged.");
        items = new DroppedItems(world); items.Spawn(Dirt, 20, new Vector3(5, 2, 5)); items.Spawn(Dirt, 30, new Vector3(7, 2, 5));
        items.Update(far, new Inventory(), 0.25f); Require(items.Entities.Count == 2, "Distant items merged.");
        items = new DroppedItems(world);
        for (int i = 0; i < 1000; i++) items.Spawn(i % 2 == 0 ? Dirt : Stone, 1, new Vector3(8.5f, 1.125f, 8.5f), 1);
        items.Update(far, new Inventory(), 0.25f);
        int[] totals = ItemTotals(items);
        Require(totals[Dirt.Value] == 500 && totals[Stone.Value] == 500 && items.Entities.Count == 16, "Dense merging violated quantities or stack limits.");
    }

    public static void HealthPersistence()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            var world = new World(7, size, false); world.SetBlock(world.Width / 2, 0, world.Depth / 2, BlockType.Stone);
            var drops = new[] { new DroppedItemState(Dirt, 17, new Vector3(12.5f, 10, 12.5f), -2, 0.8f) };
            var inventory = new Inventory(); inventory.SetSlot(4, Stack(Stone, 9)); inventory.SelectedSlot = 4;
            var save = new WorldSave(world, "Health", new Vector3(30.5f, 30, 30.5f), 0.4f, -0.2f, inventory, drops, 7);
            WorldSave loaded = Decode(Encode(save));
            Require(loaded.Health == 7 && loaded.World.Size == size && loaded.Inventory[4].Count == 9
                && loaded.DroppedItems.AsSpan().SequenceEqual(drops) && new GameSession("alive", loaded).Player.Health == 7, "Health/items did not round-trip per world.");
            var first = new GameSession("first", loaded); var second = new GameSession("second", loaded);
            first.Player.ApplyDamage(3);
            Require(first.Player.Health == 4 && second.Player.Health == 7 && loaded.Health == 7, "Health leaked between sessions or into saved state.");
            WorldSave dead = Decode(Encode(save with { Health = 0, Inventory = new Inventory() }));
            var session = new GameSession("dead", dead);
            Require(dead.Health == 0 && session.Player.Health == 20 && session.Player.Position == session.World.Spawn
                && InventoryCount(session.Inventory) == 0 && session.Items.Entities.Count == 1 && !session.HandleDeath(), "Dead load failed automatic respawn or duplicated death drops.");
            // V2 is exactly V3 without the final health integer; construct it independently.
            byte[] bytes = PigChecks.ToVersion3(Encode(save with { DroppedItems = Array.Empty<DroppedItemState>() }));
            byte[] old = new byte[bytes.Length - 4]; bytes.AsSpan(0, bytes.Length - 36).CopyTo(old);
            BitConverter.GetBytes(2).CopyTo(old, 4); Sign(old);
            WorldSave v2 = Decode(old);
            Require(v2.Health == 20 && v2.Inventory[4].Count == 9 && v2.Inventory[0].IsEmpty && v2.World.Size == size, "Milestone 5 health default or inventory compatibility failed.");
            int prefix = 52 + Encoding.UTF8.GetByteCount(save.Name) + world.BlockCount;
            byte[] v1 = new byte[prefix + 32]; bytes.AsSpan(0, prefix).CopyTo(v1); BitConverter.GetBytes(1).CopyTo(v1, 4); Sign(v1);
            Require(Decode(v1).Health == 20 && Decode(v1).Inventory[0].Count == 64, "V1 migration lost full health or starter stock.");
            foreach (int bad in new[] { -1, 21 })
            {
                bytes = PigChecks.ToVersion3(Encode(save)); BitConverter.GetBytes(bad).CopyTo(bytes, bytes.Length - 36); Sign(bytes);
                Throws<InvalidDataException>(() => Decode(bytes));
                Throws<InvalidDataException>(() => Encode(save with { Health = bad }));
            }
            foreach (float delay in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
            {
                bytes = Encode(save);
                int delayOffset = 52 + Encoding.UTF8.GetByteCount(save.Name) + world.BlockCount + 120 + 19;
                BitConverter.GetBytes(delay).CopyTo(bytes, delayOffset); Sign(bytes);
                Throws<InvalidDataException>(() => Decode(bytes));
                Throws<InvalidDataException>(() => Encode(save with { DroppedItems = new[] { drops[0] with { PickupDelay = delay } } }));
            }
        }
    }

    public static void ManagedDeathSave()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-survival-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var manager = new WorldSaveManager(root); var world = Flat();
            var session = new GameSession("fixture", new WorldSave(world, "Managed", new Vector3(20.5f, 1, 20.5f), 0, 0, Inventory.CreateStarter()));
            string id = manager.Create(session.Capture()); string path = Path.Combine(root, id, WorldSaveManager.FileName);
            byte[] alive = File.ReadAllBytes(path); session.Player.ApplyDamage(20); session.HandleDeath();
            if (OperatingSystem.IsWindows())
            {
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Throws<IOException>(() => manager.Save(id, session.Capture()));
                Require(alive.AsSpan().SequenceEqual(File.ReadAllBytes(path)) && session.Items.Entities.Count == 13, "Failed death save discarded the session or prior valid save.");
            }
            manager.Save(id, session.Capture()); manager.Rename(id, "Renamed dead"); WorldSave saved = manager.Load(id);
            var restored = new GameSession(id, saved);
            Require(saved.Health == 0 && saved.Name == "Renamed dead" && restored.Player.Health == 20 && InventoryCount(restored.Inventory) == 0
                && restored.Items.Entities.Count == 13, "Managed death save/rename/load changed health, inventory, or drops.");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Test cleanup escaped root.");
            Directory.Delete(root, true);
        }
    }

    public static void CrouchMovement()
    {
        var world = Flat(); var normal = new Player(new Vector3(20.5f, 1, 20.5f)); var crouch = new Player(normal.Position);
        Step(normal, world, 60, Vector3.Right); Step(crouch, world, 60, Vector3.Right, true);
        Require(Near(normal.Position.X - 20.5f, 5) && Near(crouch.Position.X - 20.5f, 1.75f)
            && Near(crouch.EyePosition.Y - crouch.Position.Y, Player.CrouchingEyeHeight), "Crouch speed or camera height is wrong.");
        crouch.Update(world, Vector3.Zero, false, 1f / 60);
        Require(!crouch.Crouching && Near(crouch.EyePosition.Y - crouch.Position.Y, 1.62f), "Releasing crouch did not restore eye height.");
        normal = new Player(new Vector3(20.5f, 1, 20.5f)); crouch = new Player(normal.Position);
        Step(normal, world, 1); Step(crouch, world, 1);
        normal.Update(world, Vector3.Zero, true, 1f / 60); crouch.Update(world, Vector3.Zero, true, 1f / 60, true);
        float normalPeak = 0, crouchPeak = 0;
        for (int i = 0; i < 90; i++)
        {
            normal.Update(world, Vector3.Zero, false, 1f / 60); crouch.Update(world, Vector3.Zero, false, 1f / 60, true);
            normalPeak = Math.Max(normalPeak, normal.Position.Y); crouchPeak = Math.Max(crouchPeak, crouch.Position.Y);
        }
        Require(Near(normalPeak, crouchPeak) && normalPeak >= 2, "Crouching reduced jump height.");
        world.SetBlock(12, 1, 10, BlockType.Stone); crouch = new Player(new Vector3(11.2f, 1, 10.5f)); Step(crouch, world, 1);
        crouch.Update(world, Vector3.Right, true, 1f / 60, true); bool landed = false;
        for (int i = 0; i < 60; i++) { crouch.Update(world, Vector3.Right, false, 1f / 60, true); landed |= crouch.Grounded && Near(crouch.Position.Y, 2); }
        Require(landed, "Crouch jumping cannot reach a one-block-higher surface.");
    }

    public static void CrouchEdges()
    {
        var world = Flat();
        for (int x = 40; x <= 44; x++) for (int z = 40; z <= 44; z++) world.SetBlock(x, 5, z, BlockType.Stone);
        foreach (Vector3 movement in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Backward, new Vector3(1, 0, 1), new Vector3(-1, 0, -1), new Vector3(1, 0, -1) })
        {
            var player = new Player(new Vector3(42.5f, 6, 42.5f)); Step(player, world, 1); Step(player, world, 480, movement, true);
            Require(Near(player.Position.Y, 6) && player.Grounded && Vector3.Distance(player.Position, new Vector3(42.5f, 6, 42.5f)) > 2,
                "Crouch edge protection failed or disabled horizontal approach.");
        }
        var releasing = new Player(new Vector3(44.9f, 6, 42.5f)); Step(releasing, world, 1); Step(releasing, world, 90, Vector3.Right);
        Require(releasing.Position.Y < 6, "Releasing Shift still prevented walking off.");
        var jumping = new Player(new Vector3(44.9f, 6, 42.5f)); Step(jumping, world, 1);
        jumping.Update(world, Vector3.Right, true, 1f / 60, true); Step(jumping, world, 150, Vector3.Right, true);
        Require(jumping.Position.X > 45.3f && jumping.Position.Y < 6, "Intentional crouch jump could not leave an edge.");
        var unsupported = new Player(new Vector3(44.9f, 6, 44.9f)); Step(unsupported, world, 1);
        world.SetBlock(44, 5, 44, BlockType.Air); Step(unsupported, world, 60, Vector3.Zero, true);
        Require(unsupported.Position.Y < 6, "Crouch edge protection suspended the player after support removal.");
        var sliding = new Player(new Vector3(41.5f, 6, 41.5f)); Step(sliding, world, 1);
        for (int y = 6; y < 9; y++) for (int z = 40; z <= 44; z++) world.SetBlock(42, y, z, BlockType.Stone);
        Step(sliding, world, 60, new Vector3(1, 0, 1), true);
        Require(sliding.Position.X <= 42 - Player.HalfWidth + 0.001f && sliding.Position.Z > 42 && !world.IntersectsSolid(sliding.Bounds), "Crouching broke wall collision/sliding.");
    }

    public static void Scrolling()
    {
        var inventory = Inventory.CreateStarter(); var scroll = new HotbarScroll();
        int[] before = InventoryTotals(new GameSession("scroll", new WorldSave(new World(0, WorldSize.Small, false), "Scroll", new Vector3(30, 30, 30), 0, 0, inventory)));
        inventory.SelectedSlot = 8; scroll.Apply(inventory, 120); Require(inventory.SelectedSlot == 0, "Forward wheel failed to wrap 9 to 1.");
        scroll.Apply(inventory, -120); Require(inventory.SelectedSlot == 8, "Backward wheel failed to wrap 1 to 9.");
        scroll.Apply(inventory, 120 * 20); Require(inventory.SelectedSlot == 1, "Multiple positive wheel steps were lost.");
        scroll.Apply(inventory, -120 * 20); Require(inventory.SelectedSlot == 8, "Multiple negative wheel steps were lost.");
        scroll.Apply(inventory, 60); Require(inventory.SelectedSlot == 8, "Partial wheel step selected too early.");
        scroll.Apply(inventory, 60); Require(inventory.SelectedSlot == 0, "Partial deltas did not accumulate.");
        scroll.Apply(inventory, 60); scroll.Reset(); scroll.Apply(inventory, 60); Require(inventory.SelectedSlot == 0, "Menu reset retained an old partial wheel step.");
        inventory.SelectedSlot = 6;
        var copy = new GameSession("copy", new WorldSave(new World(0, WorldSize.Small, false), "Copy", new Vector3(30, 30, 30), 0, 0, inventory));
        Require(before.AsSpan().SequenceEqual(InventoryTotals(copy)), "Scrolling changed inventory contents.");
    }

    private static World Flat()
    {
        var world = new World(0, WorldSize.Small, false);
        foreach (Chunk chunk in world.Chunks)
        for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) chunk.SetBlock(x, 0, z, BlockType.Stone);
        return world;
    }
    private static void Step(Player player, World world, int frames, Vector3 movement = default, bool crouch = false)
    { for (int i = 0; i < frames; i++) player.Update(world, movement, false, 1f / 60, crouch); }
    private static void Land(Player player, World world, Vector3 movement)
    { for (int i = 0; i < 600 && !player.IsDead; i++) { player.Update(world, movement, false, 1f / 60); if (player.Grounded) break; } }
    private static int InventoryCount(Inventory inventory) { int total = 0; for (int i = 0; i < 36; i++) total += inventory[i].Count; return total; }
    private static int[] InventoryTotals(GameSession session)
    {
        var counts = new int[14]; for (int i = 0; i < 36; i++) counts[session.Inventory[i].Item.Value] += session.Inventory[i].Count;
        counts[session.InventoryInteraction.Cursor.Item.Value] += session.InventoryInteraction.Cursor.Count; return counts;
    }
    private static int[] ItemTotals(DroppedItems items)
    {
        var counts = new int[14]; foreach (DroppedItem item in items.Entities) { Require(item.Count is >= 1 and <= 64, "Invalid entity stack size."); counts[item.Item.Value] += item.Count; } return counts;
    }
    private static byte[] Encode(WorldSave save) { using var stream = new MemoryStream(); WorldSerializer.Write(stream, save); return stream.ToArray(); }
    private static WorldSave Decode(byte[] bytes) => WorldSerializer.Read(new MemoryStream(bytes));
    private static void Sign(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.002f;
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
