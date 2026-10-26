using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class FallingBlockChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static World Falling(int x = 15, int z = 16, int y = 12)
    {
        var world = new World(173, WorldSize.Small, false);
        world.SetBlock(x, 0, z, BlockType.Stone); world.SetBlock(x, y, z, BlockType.Sand); world.Gravity.Tick();
        return world;
    }
    public static void ContinuousMotion()
    {
        World world = Falling(); FallingBlock block = world.Gravity.Active.Single();
        Require(world.GetBlock(15, 12, 16) == BlockType.Air && block.Block == BlockType.Sand && block.Y == 12,
            "Fall did not transfer the source block to exactly one temporary object.");
        world.Gravity.Update(0.1); double first = block.Y;
        Require(Math.Abs(first - 11.88) < 1e-9 && Math.Abs(block.DownwardSpeed - 2.4) < 1e-9, "Continuous acceleration is incorrect.");
        int revision = world.Revision; world.Gravity.Update(0.1);
        Require(Math.Abs(block.Y - 11.52) < 1e-9 && first - block.Y > 12 - first && world.Revision == revision,
            "Block failed to accelerate smoothly or rebuilt terrain during motion.");
        Require(Math.Abs(block.RenderPosition(0.5f).Y - 11.7f) < 1e-5 && block.RenderPosition(0).Y > block.RenderPosition(1).Y,
            "High-FPS interpolation snapped to voxel positions.");
        FallingBlockState state = block.Capture(); world.Gravity.Update(0); world.Gravity.Update(double.NaN); world.Gravity.Update(-1);
        Require(state == block.Capture(), "Invalid/no elapsed time moved a block.");
        var clock = new SimulationClock(); clock.Advance(SimulationClock.StepSeconds * 0.5);
        Require(Math.Abs(clock.Fraction - 0.5f) < 1e-6, "Render interpolation did not use the fixed-step remainder.");
        WorldUpdateChecks.SettleGravity(world);
        Require(world.GetBlock(15, 1, 16) == BlockType.Sand && world.Gravity.Active.Count == 0, "Sand did not become an ordinary block on landing.");
        var builder = new ChunkMeshBuilder(); builder.AppendCube(BlockType.Sand, new Vector3(15, 7.25f, 16));
        Require(builder.VertexCount == 36 && builder.Vertices.Take(36).Min(v => v.Position.Y) == 7.25f
            && builder.Vertices.Take(36).Max(v => v.Position.Y) == 8.25f, "Falling cube is not full-size continuous geometry.");
    }
    public static void SweepsAndStacks()
    {
        foreach (double elapsed in new[] { 1d / 30, 0.5, 5, 1000 })
        {
            World world = Falling(y: 63); world.SetBlock(15, 7, 16, BlockType.Stone);
            for (int step = 0; step < 300 && world.Gravity.Active.Count > 0; step++) world.Gravity.Update(elapsed);
            Require(world.GetBlock(15, 8, 16) == BlockType.Sand && world.GetBlock(15, 1, 16) == BlockType.Air,
                "Swept collision tunneled through the first solid surface.");
        }
        World stack = Falling(y: 40); stack.SetBlock(15, 41, 16, BlockType.Gravel); stack.SetBlock(15, 42, 16, BlockType.Sand); stack.Gravity.Tick();
        Require(stack.Gravity.Active.Count == 3, "Unsupported upper stack remained suspended.");
        for (int step = 0; step < 120; step++)
        {
            stack.Gravity.Update(1d / 60);
            var ordered = stack.Gravity.Active.OrderBy(b => b.Y).ToArray();
            for (int i = 1; i < ordered.Length; i++) Require(ordered[i].Y >= ordered[i - 1].Y + 1 - 1e-8, "Active stack overlapped or changed order.");
            Require(ordered.All(b => b.DownwardSpeed <= FallingBlockSimulation.TerminalSpeed), "Terminal speed exceeded.");
        }
        WorldUpdateChecks.SettleGravity(stack);
        Require(stack.GetBlock(15, 1, 16) == BlockType.Sand && stack.GetBlock(15, 2, 16) == BlockType.Gravel
            && stack.GetBlock(15, 3, 16) == BlockType.Sand, "Stack types/order were lost while settling.");
        World later = Falling(y: 20); later.Gravity.Update(0.3); later.SetBlock(15, 23, 16, BlockType.Gravel); later.Gravity.Tick();
        WorldUpdateChecks.SettleGravity(later);
        Require(later.GetBlock(15, 1, 16) == BlockType.Sand && later.GetBlock(15, 2, 16) == BlockType.Gravel, "Later-started block passed through an earlier fall.");
    }
    public static void FrameRatesAndPlacement()
    {
        double? reference = null;
        foreach (int fps in new[] { 30, 60, 144, 360 })
        {
            World world = Falling(y: 40);
            for (int frame = 0; frame < fps / 2; frame++) world.Gravity.Update(1d / fps);
            FallingBlock block = world.Gravity.Active.Single();
            Require(Math.Abs(block.Y - 37) < 1e-8 && Math.Abs(block.DownwardSpeed - 12) < 1e-8
                && (!reference.HasValue || Math.Abs(block.Y - reference.Value) < 1e-8), "Falling speed changed with rendered FPS.");
            reference = block.Y;
            WorldUpdateChecks.SettleGravity(world);
        }
        World occupied = Falling(); occupied.Gravity.Update(0.2);
        var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.FromBlock(BlockType.Stone), 2));
        var player = new BoundingBox(new Vector3(50, 1, 50), new Vector3(51, 3, 51));
        Require(!BlockPlacement.TryPlace(occupied, inventory, new Point3(15, 11, 16), player) && inventory[0].Count == 2,
            "Placement overwrote an active cube or consumed a rejected item.");
        Require(BlockPlacement.TryPlace(occupied, inventory, new Point3(15, 5, 16), player) && inventory[0].Count == 1, "Could not place new support below a fall.");
        WorldUpdateChecks.SettleGravity(occupied);
        Require(occupied.GetBlock(15, 6, 16) == BlockType.Sand && occupied.GetBlock(15, 5, 16) == BlockType.Stone, "New support was missed by an active block.");
    }
    public static void Persistence()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            var world = new World(173, size, false); world.SetBlock(15, 0, 16, BlockType.Stone);
            world.SetBlock(15, 25, 16, BlockType.Sand); world.SetBlock(15, 26, 16, BlockType.Gravel); world.Gravity.Update(0.2);
            var save = new WorldSave(world, "Mid fall", new Vector3(60, 1, 60), 0, 0);
            byte[] bytes = MobChecks.Encode(save); WorldSave restored = WorldSerializer.Read(new MemoryStream(bytes));
            Require(restored.World.Gravity.CaptureActive().AsSpan().SequenceEqual(world.Gravity.CaptureActive())
                && bytes.AsSpan().SequenceEqual(MobChecks.Encode(restored)), "Mid-fall save lost type/height/velocity or duplicated blocks.");
            world.Gravity.Update(0.1); restored.World.Gravity.Update(0.1);
            Require(world.Gravity.CaptureActive().AsSpan().SequenceEqual(restored.World.Gravity.CaptureActive()), "Saved fall did not resume the same motion.");
            WorldUpdateChecks.SettleGravity(restored.World);
            Require(restored.World.GetBlock(15, 1, 16) == BlockType.Sand && restored.World.GetBlock(15, 2, 16) == BlockType.Gravel,
                "Restored fall failed to settle once.");
        }
        // Independent v6 fixture has queued voxel blocks and no active-object tail.
        var queued = new World(173, WorldSize.Small, false); queued.SetBlock(15, 0, 16, BlockType.Stone); queued.SetBlock(15, 12, 16, BlockType.Gravel);
        var legacySave = new WorldSave(queued, "Version six", new Vector3(60, 1, 60), 0, 0);
        byte[] modern = MobChecks.Encode(legacySave), version6 = new byte[modern.Length - 4];
        modern.AsSpan(0, modern.Length - 36).CopyTo(version6); BitConverter.GetBytes(6).CopyTo(version6, 4); Sign(version6);
        WorldSave old = WorldSerializer.Read(new MemoryStream(version6));
        Require(old.World.Gravity.Active.Count == 0 && old.World.Gravity.PendingCount == 1, "V6 queue migration spawned or lost a block.");
        WorldUpdateChecks.SettleGravity(old.World); Require(old.World.GetBlock(15, 1, 16) == BlockType.Gravel, "V6 pending block did not animate and settle.");
        World falling = Falling(); falling.Gravity.Update(0.2); byte[] active = MobChecks.Encode(legacySave with { World = falling });
        int record = active.Length - 32 - 25;
        foreach ((int Offset, byte[] Value) invalid in new[] { (record, new byte[] { (byte)BlockType.Stone }), (record+1, BitConverter.GetBytes(-1)),
            (record+9, BitConverter.GetBytes(double.NaN)), (record+9, BitConverter.GetBytes(64d)), (record+17, BitConverter.GetBytes(-1d)),
            (record+17, BitConverter.GetBytes(21d)), (record-4, BitConverter.GetBytes(FallingBlockSimulation.MaximumActiveBlocks+1)) })
        {
            byte[] bad = (byte[])active.Clone(); invalid.Value.CopyTo(bad, invalid.Offset); Sign(bad);
            try { WorldSerializer.Read(new MemoryStream(bad)); } catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Invalid active-fall save was accepted.");
        }
    }
    private static void Sign(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);

    public static void CapacityAndAccounting()
    {
        var world = new World(173, WorldSize.Small, false);
        const int columns = 70, stackHeight = 60, total = columns * stackHeight;
        for (int x = 0; x < columns; x++)
        {
            world.SetBlock(x, 0, 16, BlockType.Stone);
            for (int y = 4; y < 4 + stackHeight; y++) world.SetBlock(x, y, 16, (y & 1) == 0 ? BlockType.Sand : BlockType.Gravel);
        }
        for (int tick = 0; tick < 3; tick++) world.Gravity.Tick();
        Require(world.Gravity.Active.Count == FallingBlockSimulation.MaximumActiveBlocks && world.Gravity.PendingCount > 0,
            "Active-fall capacity failed to defer excess blocks.");
        int Count()
        {
            int count = world.Gravity.Active.Count;
            foreach (Chunk chunk in world.Chunks) foreach (byte block in chunk.Blocks) if (Blocks.Falls((BlockType)block)) count++;
            return count;
        }
        Require(Count() == total, "Capacity limit discarded deferred blocks.");
        // Persistence includes both active objects and the unconverted, queued portion of the same stack.
        var save = new WorldSave(world, "Bounded falls", new Vector3(100, 1, 100), 0, 0);
        world = WorldSerializer.Read(new MemoryStream(MobChecks.Encode(save))).World;
        Require(Count() == total, "Saving at the active limit duplicated or lost blocks.");
        WorldUpdateChecks.SettleGravity(world);
        Require(Count() == total && world.Gravity.Active.Count == 0, "Deferred falling stacks did not eventually settle intact.");
        for (int x = 0; x < columns; x++) for (int y = 1; y <= stackHeight; y++)
            Require(world.GetBlock(x, y, 16) == ((y & 1) == 1 ? BlockType.Sand : BlockType.Gravel), "Capacity scheduling reordered a mixed stack.");
    }
}
