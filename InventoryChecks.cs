using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

internal static class InventoryChecks
{
    private static ItemId Dirt => Items.FromBlock(BlockType.Dirt);
    private static ItemId Stone => Items.FromBlock(BlockType.Stone);
    private static ItemStack Stack(ItemId item, int count) => new(item, count);

    public static void Stacking()
    {
        Inventory inventory = Inventory.CreateStarter();
        for (int i = 0; i < Inventory.SlotCount; i++)
            Require(i < 13 ? inventory[i] == Stack(Items.FromBlock(Blocks.Selection[i]), 64)
                : inventory[i].IsEmpty && inventory[i].Item.Value == 0, "Starter slots or empty representation are incorrect.");
        Require(inventory.SelectedSlot == 0 && Inventory.HotbarSlots == 9 && Inventory.SlotCount == 36, "Inventory layout changed.");
        inventory = new Inventory();
        inventory.SetSlot(4, Stack(Dirt, 60));
        Require(inventory.Add(Dirt, 10) == 0 && inventory[4].Count == 64 && inventory[0] == Stack(Dirt, 6), "Existing stack did not take precedence over empties.");
        Require(inventory.Add(Stone, 130) == 0 && inventory[1] == Stack(Stone, 64)
            && inventory[2] == Stack(Stone, 64) && inventory[3] == Stack(Stone, 2), "Overflow or item separation failed.");
        for (int i = 0; i < 36; i++) inventory.SetSlot(i, Stack(Stone, 64));
        inventory.SetSlot(35, Stack(Dirt, 60));
        Require(inventory.Add(Dirt, 10) == 6 && inventory[35].Count == 64, "Partial insertion lost its remainder.");
        Require(inventory.Add(Stone, 10) == 10 && inventory.Add(Dirt, 0) == 0, "Full inventory accepted excess items.");
        Throws<ArgumentException>(() => Stack(Dirt, 65));
        Throws<ArgumentException>(() => Stack(new ItemId(0), 1));
        Throws<ArgumentException>(() => Stack(Stone, 0));
        Throws<ArgumentOutOfRangeException>(() => inventory.SelectedSlot = 9);
    }

    public static void MouseOperations()
    {
        var inventory = new Inventory();
        var mouse = new InventoryInteraction(inventory);
        inventory.SetSlot(0, Stack(Dirt, 27));
        mouse.Click(0, false);
        Require(mouse.Cursor == Stack(Dirt, 27) && inventory[0].IsEmpty, "Whole-stack pickup failed.");
        mouse.Click(1, false);
        Require(mouse.Cursor.IsEmpty && inventory[1] == Stack(Dirt, 27), "Whole-stack placement failed.");
        mouse.Click(1, true);
        Require(mouse.Cursor == Stack(Dirt, 13) && inventory[1] == Stack(Dirt, 14), "Odd split did not round down.");
        mouse.Click(2, true);
        Require(inventory[2] == Stack(Dirt, 1) && mouse.Cursor.Count == 12, "Right-click single placement failed.");
        mouse.Click(1, true);
        Require(inventory[1].Count == 15 && mouse.Cursor.Count == 11, "Compatible right-click insertion failed.");
        inventory.SetSlot(3, Stack(Stone, 5));
        mouse.Click(3, true);
        Require(inventory[3] == Stack(Stone, 5) && mouse.Cursor.Count == 11, "Incompatible right-click changed items.");
        inventory.SetSlot(4, Stack(Dirt, 64));
        mouse.Click(4, true);
        Require(inventory[4].Count == 64 && mouse.Cursor.Count == 11, "Full-stack right click changed items.");
        mouse.Click(3, false);
        Require(inventory[3] == Stack(Dirt, 11) && mouse.Cursor == Stack(Stone, 5), "Different-item swap failed.");
        inventory.SetSlot(5, Stack(Stone, 62));
        mouse.Click(5, false);
        Require(inventory[5].Count == 64 && mouse.Cursor == Stack(Stone, 3), "Left merge lost excess items.");
        mouse.Click(6, false);
        inventory.SetSlot(7, Stack(Dirt, 1));
        mouse.Click(7, true);
        Require(inventory[7].IsEmpty && mouse.Cursor == Stack(Dirt, 1), "Single-item split failed.");
        Require(mouse.TryReturnCursor() && inventory[7] == Stack(Dirt, 1) && mouse.Cursor.IsEmpty, "Cursor did not return to its source.");
    }

    public static void AccountingAndClosing()
    {
        var random = new Random(73);
        var inventory = new Inventory();
        for (int i = 0; i < 36; i++) inventory.SetSlot(i, Stack(Items.FromBlock(Blocks.Selection[i % 13]), random.Next(1, 65)));
        var interaction = new InventoryInteraction(inventory);
        int[] totals = Totals(interaction);
        for (int i = 0; i < 5000; i++)
        {
            interaction.Click(random.Next(-1, 37), random.Next(2) == 0);
            if (i % 17 == 0) interaction.TryReturnCursor();
            Require(totals.AsSpan().SequenceEqual(Totals(interaction)), "Mouse/cursor operation created or lost items.");
        }
        inventory = new Inventory();
        for (int i = 0; i < 36; i++) inventory.SetSlot(i, Stack(Dirt, 64));
        inventory.SetSlot(1, Stack(Stone, 64));
        interaction = new InventoryInteraction(inventory);
        interaction.Click(0, true); interaction.Click(1, false);
        for (int i = 0; i < 3; i++)
            Require(!interaction.TryReturnCursor() && interaction.Cursor == Stack(Stone, 64), "Blocked close discarded or duplicated the cursor.");
        interaction.Click(1, false);
        Require(interaction.TryReturnCursor() && inventory[0].Count == 64 && inventory[1] == Stack(Stone, 64), "Recovering a blocked cursor failed.");
        foreach (var viewport in new[] { new Viewport(0, 0, 320, 240), new Viewport(0, 0, 640, 480), new Viewport(0, 0, 1280, 720) })
            for (int slot = 0; slot < 36; slot++)
            {
                Rectangle bounds = InventoryLayout.Slot(viewport, slot);
                Require(bounds.Left >= 0 && bounds.Right <= viewport.Width && bounds.Top >= 0 && bounds.Bottom <= viewport.Height,
                    "Inventory slots fall outside the viewport.");
            }
    }

    public static void Mining()
    {
        Require(Blocks.Get(BlockType.Grass).MiningSeconds == Blocks.Get(BlockType.Dirt).MiningSeconds
            && Blocks.Get(BlockType.Grass).MiningSeconds > 0, "Grass must take exactly as long to mine as dirt.");
        var world = new World(0, WorldSize.Small, false);
        var drops = new DroppedItems(world);
        var mining = new MiningController();
        var hit = new BlockHit(new Point3(15, 10, 16), new Point3(0, 1, 0), 3);
        foreach (BlockType block in Blocks.Selection)
        {
            world.SetBlock(15, 10, 16, block);
            int before = drops.Entities.Count;
            float duration = Blocks.Get(block).MiningSeconds;
            if (block == BlockType.Bedrock)
            {
                Require(duration < 0 && !mining.Update(world, drops, hit, true, 100) && world.GetBlock(15, 10, 16) == block
                    && drops.Entities.Count == before && mining.Progress == 0, "Bedrock could be mined or dropped an item.");
                continue;
            }
            bool instant = block is BlockType.TallGrass or BlockType.RedFlower or BlockType.YellowFlower;
            Require(instant ? duration == 0 : duration > 0, "Mining duration policy is incorrect.");
            if (!instant) Require(!mining.Update(world, drops, hit, true, duration - 0.01f) && mining.Progress > 0, "Block broke before its duration.");
            Require(mining.Update(world, drops, hit, true, instant ? 0 : 0.02f) && world.GetBlock(15, 10, 16) == BlockType.Air
                && drops.Entities.Count == before + 1 && drops.Entities[^1].Item == Items.FromBlock(block)
                && drops.Entities[^1].Count == 1 && mining.Progress == 0, "Mining did not remove and drop exactly its own block.");
        }
        world.SetBlock(15, 10, 16, BlockType.Stone);
        mining.Update(world, drops, hit, true, 1);
        mining.Update(world, drops, hit, false, 1);
        Require(mining.Target == null && mining.Progress == 0, "Mouse release retained mining progress.");
        Require(!mining.Update(world, drops, hit, true, 2), "Returning to a block resumed discarded progress.");
        var other = new BlockHit(new Point3(16, 10, 16), default, 3);
        world.SetBlock(16, 10, 16, BlockType.Stone);
        mining.Update(world, drops, other, true, 0.5f);
        Require(Math.Abs(mining.Progress - 1f / 6) < 0.001f, "Changing targets retained progress.");
        mining.Update(world, drops, null, true, 1);
        Require(mining.Progress == 0 && mining.Target == null, "Target loss retained progress.");
        mining.Update(world, drops, hit with { Distance = 7 }, true, 100);
        Require(world.GetBlock(15, 10, 16) == BlockType.Stone && mining.Progress == 0, "Out-of-range mining succeeded.");
        world.SetBlock(15, 10, 16, BlockType.Dirt);
        mining.Update(world, drops, hit, true, 0.5f);
        world.SetBlock(15, 10, 16, BlockType.Stone);
        mining.Update(world, drops, hit, true, 0.5f);
        Require(Math.Abs(mining.Progress - 1f / 6) < 0.001f, "A replaced target inherited progress.");
        mining.Reset();
        Require(mining.Target == null && mining.Progress == 0, "Menu/reset retained mining progress.");
        world.SetBlock(15, 10, 16, BlockType.Dirt);
        world.SetBlock(16, 10, 16, BlockType.Dirt);
        Require(mining.Update(world, drops, hit, true, 1.3f) && mining.Update(world, drops, other, true, 1.3f), "Held mining could not continue on subsequent blocks.");
        world.SetBlock(15, 10, 16, BlockType.Grass);
        world.SetBlock(15, 11, 16, BlockType.RedFlower);
        int countBefore = drops.Entities.Count;
        Require(!mining.Update(world, drops, hit, true, 0) && drops.Entities.Count == countBefore,
            "Grass or its supported plant dropped before mining completed.");
        mining.Update(world, drops, hit, true, Blocks.Get(BlockType.Grass).MiningSeconds);
        Require(drops.Entities.Count == countBefore + 2 && world.GetBlock(15, 11, 16) == BlockType.Air, "Removing plant support lost its plant item.");
    }

    public static void PhysicsAndPickup()
    {
        var world = new World(0, WorldSize.Small, false);
        for (int z = 6; z <= 10; z++)
        for (int x = 6; x <= 10; x++) world.SetBlock(x, 3, z, BlockType.Stone);
        var items = new DroppedItems(world);
        items.Spawn(Stone, 1, new Vector3(8.5f, 12, 8.5f));
        var inventory = new Inventory();
        var far = new Player(new Vector3(70, 4, 70));
        int revision = world.Revision;
        for (int i = 0; i < 180; i++) items.Update(far, inventory, 1f / 60);
        Require(items.Entities.Count == 1 && MathF.Abs(items.Entities[0].Position.Y - (4 + DroppedItem.Radius)) < 0.0001f
            && items.Entities[0].VerticalVelocity == 0 && world.Revision == revision, "Item fell through terrain, stayed suspended, or invalidated chunk meshes.");
        var near = new Player(new Vector3(8.5f, 4, 8.5f));
        items.Update(near, inventory, 1f / 60);
        Require(items.Entities.Count == 0 && inventory[0] == Stack(Stone, 1), "Nearby item was not collected.");
        items.Update(near, inventory, 1f / 60);
        Require(inventory[0].Count == 1, "Same item was collected twice.");
        using (var collectedSave = new MemoryStream())
        {
            WorldSerializer.Write(collectedSave, new WorldSave(world, "Collected", near.Position, 0, 0, inventory, items.Capture()));
            collectedSave.Position = 0;
            WorldSave restored = WorldSerializer.Read(collectedSave);
            Require(restored.DroppedItems.Length == 0 && restored.Inventory[0] == Stack(Stone, 1), "Collected item reappeared after saving/loading.");
        }
        var fast = new DroppedItems(world, new[] { new DroppedItemState(Dirt, 1, new Vector3(8.5f, 4.3f, 8.5f), -40, 0) });
        fast.Update(far, inventory, 0.1f);
        Require(fast.Entities[0].Position.Y >= 4 + DroppedItem.Radius, "Fast falling item tunneled through a block.");
        world.SetBlock(8, 3, 8, BlockType.Air);
        fast.Update(far, inventory, 0.1f);
        Require(fast.Entities[0].Position.Y < 4 + DroppedItem.Radius, "Item remained suspended after its support was removed.");
        world.SetBlock(8, 3, 8, BlockType.Stone);
        inventory = new Inventory();
        for (int i = 0; i < 36; i++) inventory.SetSlot(i, Stack(Stone, 64));
        inventory.SetSlot(5, Stack(Dirt, 60));
        items = new DroppedItems(world);
        items.Spawn(Dirt, 10, new Vector3(8.5f, 4.5f, 8.5f));
        for (int i = 0; i < 10; i++) items.Update(near, inventory, 0.1f);
        Require(items.Entities.Count == 1 && items.Entities[0].Count == 6 && inventory[5].Count == 64, "Full inventory lost an uncollected remainder.");
        inventory.SetSlot(6, default);
        items.Update(near, inventory, 0.1f);
        Require(items.Entities.Count == 0 && inventory[6] == Stack(Dirt, 6), "Remaining item quantity did not collect exactly once.");
        items.Spawn(Dirt, 1, new Vector3(8.5f, 4.5f, 8.5f));
        items.Spawn(Dirt, 1, new Vector3(8.5f, 4.5f, 8.5f));
        for (int i = 0; i < 10; i++) items.Update(near, inventory, 0.1f);
        Require(inventory[6].Count == 8 && items.Entities.Count == 0, "Multiple pickup entities duplicated or failed to merge.");
    }

    public static void PlacementAndIsolation()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            var world = new World(0, size, false);
            var inventory = new Inventory();
            inventory.SetSlot(0, Stack(Items.FromBlock(BlockType.Bedrock), 1));
            var cell = new Point3(world.Width - 1, 10, world.Depth - 1);
            var bounds = new Player(new Vector3(40.5f, 10, 40.5f)).Bounds;
            Require(!BlockPlacement.TryPlace(world, inventory, cell with { X = world.Width }, bounds) && inventory[0].Count == 1, "Failed placement consumed an item.");
            Require(BlockPlacement.TryPlace(world, inventory, cell, bounds) && inventory[0].IsEmpty && inventory[0].Item.Value == 0
                && world.GetBlock(cell.X, cell.Y, cell.Z) == BlockType.Bedrock, "Placement did not consume its last item or rejected bedrock.");
            Require(!BlockPlacement.TryPlace(world, inventory, new Point3(8, 10, 8), bounds), "Empty selection placed a block.");
            inventory.SetSlot(0, Stack(Dirt, 2));
            Require(!BlockPlacement.TryPlace(world, inventory, cell, bounds) && inventory[0].Count == 2, "Occupied cell consumed an item.");
            Require(!BlockPlacement.TryPlace(world, inventory, new Point3(40, 10, 40), bounds) && inventory[0].Count == 2, "Player intersection consumed an item.");
            var save = new WorldSave(world, "Isolation", new Vector3(30.5f, 40, 30.5f), 0, 0, Inventory.CreateStarter());
            var first = new GameSession("first", save);
            var second = new GameSession("second", save);
            first.Inventory.SetSlot(0, default); first.Inventory.SelectedSlot = 8;
            Require(second.Inventory[0].Count == 64 && second.Inventory.SelectedSlot == 0 && save.Inventory[0].Count == 64, "Sessions share mutable inventory state.");
            first.InventoryInteraction.Click(1, false);
            Throws<InvalidOperationException>(() => first.Capture());
            first.InventoryInteraction.TryReturnCursor();
            Require(first.Capture().Inventory[0].IsEmpty, "Capture replenished an emptied starter slot.");
        }
    }

    public static void Persistence()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            var world = new World(-91, size, false);
            world.SetBlock(world.Width - 1, 63, world.Depth - 1, BlockType.Leaves);
            var inventory = new Inventory { SelectedSlot = 8 };
            for (int i = 0; i < 36; i++) inventory.SetSlot(i, Stack(Items.FromBlock(Blocks.Selection[i % 13]), i + 1));
            inventory.SetSlot(0, default);
            var drops = new[] { new DroppedItemState(Dirt, 64, new Vector3(30.5f, 30, 30.5f), -12, 0.2f),
                new DroppedItemState(Items.FromBlock(BlockType.RedFlower), 7, new Vector3(world.Width - 0.5f, 1.125f, world.Depth - 0.5f), 0, 0) };
            var save = new WorldSave(world, "Inventory " + size, new Vector3(32.5f, 60, 32.5f), 1.2f, -0.3f, inventory, drops);
            using var encoded = new MemoryStream(); WorldSerializer.Write(encoded, save); encoded.Position = 0;
            WorldSave restored = WorldSerializer.Read(encoded);
            Require(restored.World.Size == size && restored.Inventory.SelectedSlot == 8 && restored.PlayerPosition == save.PlayerPosition
                && restored.Yaw == save.Yaw && restored.Pitch == save.Pitch && restored.DroppedItems.AsSpan().SequenceEqual(drops), "Inventory/drop metadata changed on load.");
            for (int i = 0; i < 36; i++) Require(restored.Inventory[i] == inventory[i], "Saved inventory stack changed.");
            var session = new GameSession("roundtrip", restored);
            Require(session.Items.Entities.Count == 2 && session.Items.Capture().AsSpan().SequenceEqual(drops), "Restoring a session duplicated or lost drops.");
            using var reencoded = new MemoryStream(); WorldSerializer.Write(reencoded, session.Capture());
            Require(encoded.GetBuffer().AsSpan(0, (int)encoded.Length).SequenceEqual(reencoded.GetBuffer().AsSpan(0, (int)reencoded.Length)), "Session save round-trip changed item state.");
            using var old = Legacy(world);
            WorldSave migrated = WorldSerializer.Read(old);
            Require(migrated.World.Size == size && migrated.Inventory[12].Count == 64 && migrated.Inventory[13].IsEmpty
                && migrated.DroppedItems.Length == 0 && migrated.World.GetBlock(world.Width - 1, 63, world.Depth - 1) == BlockType.Leaves,
                "Legacy save was regenerated or not given exactly one starter inventory.");
            migrated.Inventory.SetSlot(0, default); migrated.Inventory.SelectedSlot = 4;
            using var upgraded = new MemoryStream(); WorldSerializer.Write(upgraded, migrated); upgraded.Position = 0;
            WorldSave again = WorldSerializer.Read(upgraded);
            Require(again.Inventory[0].IsEmpty && again.Inventory.SelectedSlot == 4, "Upgraded save replenished starter items.");
        }
    }

    public static void Validation()
    {
        var world = new World(0, WorldSize.Small, false);
        var save = new WorldSave(world, "Validation", new Vector3(64.5f, 30, 64.5f), 0, 0,
            Inventory.CreateStarter(), new[] { new DroppedItemState(Dirt, 1, new Vector3(10.5f, 20, 10.5f), 0, 0) });
        using var stream = new MemoryStream(); WorldSerializer.Write(stream, save);
        byte[] original = stream.ToArray();
        int tail = 52 + Encoding.UTF8.GetByteCount(save.Name) + world.BlockCount;
        void Bad(int offset, byte[] value)
        {
            byte[] changed = (byte[])original.Clone(); value.CopyTo(changed, offset);
            SHA256.HashData(changed.AsSpan(0, changed.Length - 32)).CopyTo(changed, changed.Length - 32);
            Throws<InvalidDataException>(() => WorldSerializer.Read(new MemoryStream(changed)));
        }
        Bad(tail, BitConverter.GetBytes(9));
        Bad(tail + 4, BitConverter.GetBytes(35));
        Bad(tail + 8, BitConverter.GetBytes((ushort)65535));
        Bad(tail + 10, new byte[] { 65 });
        Bad(tail + 8, new byte[] { 0, 0 }); // Nonempty quantity with an empty ID.
        Bad(tail + 10, new byte[] { 0 }); // Valid item ID with zero quantity.
        Bad(tail + 116, BitConverter.GetBytes(-1));
        Bad(tail + 116, BitConverter.GetBytes(DroppedItems.MaximumEntities + 1));
        int drop = tail + 120;
        Bad(drop, new byte[] { 0, 0 });
        Bad(drop + 2, new byte[] { 0 });
        Bad(drop + 2, new byte[] { 65 });
        Bad(drop + 3, BitConverter.GetBytes(float.NaN));
        Bad(drop + 3, BitConverter.GetBytes(129f));
        Bad(drop + 15, BitConverter.GetBytes(41f));
        Bad(drop + 19, BitConverter.GetBytes(-1f));
        Throws<InvalidDataException>(() => WorldSerializer.Write(new MemoryStream(), save with { DroppedItems = new[] { save.DroppedItems[0] with { Count = 65 } } }));
    }

    private static MemoryStream Legacy(World world)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(0x4d53444du); writer.Write(1); writer.Write(world.Width); writer.Write(64); writer.Write(world.Depth); writer.Write(world.Seed);
            byte[] name = Encoding.UTF8.GetBytes("Legacy"); writer.Write(name.Length); writer.Write(name);
            writer.Write(32.5f); writer.Write(60f); writer.Write(32.5f); writer.Write(1.2f); writer.Write(-0.3f); writer.Write(world.BlockCount);
            foreach (Chunk chunk in world.Chunks) writer.Write(chunk.Blocks);
        }
        stream.Write(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
        stream.Position = 0;
        return stream;
    }

    private static int[] Totals(InventoryInteraction interaction)
    {
        var counts = new int[14];
        for (int i = 0; i < 36; i++)
        {
            ItemStack stack = interaction.Inventory[i]; counts[stack.Item.Value] += stack.Count;
            Require(stack.IsEmpty ? stack.Item.Value == 0 : stack.Count >= 1 && stack.Count <= 64, "Invalid stack state.");
        }
        counts[interaction.Cursor.Item.Value] += interaction.Cursor.Count;
        return counts;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
