using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace MDSM;

internal static class PersistenceChecks
{
    public static void RoundTrip()
    {
        var world = new World(-8128);
        foreach (Chunk chunk in world.Chunks)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            int worldX = chunk.OriginX + x, worldZ = chunk.OriginZ + z;
            world.SetBlock(worldX, world.SurfaceY(worldX, worldZ), worldZ, BlockType.Air);
            world.SetBlock(worldX, 60, worldZ, (x + z) % 2 == 0 ? BlockType.Dirt : BlockType.Stone);
        }
        world.GetChunk(0, 0).RestoreBlocks(new byte[Chunk.BlockCount]);
        var filled = new byte[Chunk.BlockCount];
        Array.Fill(filled, (byte)BlockType.Stone);
        world.GetChunk(15, 15).RestoreBlocks(filled);
        var original = new WorldSave(world, "Restoration Caf\u00e9", new Vector3(128.5f, 70, 128.5f), 1.2345f, -0.765f);
        byte[] bytes = Encode(original);
        WorldSave restored = WorldSerializer.Read(new MemoryStream(bytes));
        Require(restored.Name == original.Name && restored.World.Seed == original.World.Seed, "World metadata changed.");
        Require(restored.PlayerPosition == original.PlayerPosition && restored.Yaw == original.Yaw && restored.Pitch == original.Pitch, "Player pose changed.");
        for (int i = 0; i < world.ChunkCount; i++)
            Require(world.Chunks[i].Blocks.SequenceEqual(restored.World.Chunks[i].Blocks), "Edited block bytes changed or were regenerated.");
        Require(bytes.AsSpan().SequenceEqual(Encode(restored)), "Restoring and reserializing was not deterministic.");
        var session = new GameSession(Guid.NewGuid().ToString("N"), restored);
        Require(session.Player.Position == original.PlayerPosition && session.Camera.Yaw == original.Yaw
            && session.Camera.Pitch == original.Pitch && session.Camera.Position == session.Player.EyePosition, "Session did not restore player and camera.");
    }

    public static void CorruptData()
    {
        byte[] original = Encode(Fixture());
        Reject(Array.Empty<byte>());
        Reject(original[..^1]);
        Reject(new byte[WorldSerializer.MaximumFileSize + 1]);
        byte[] damaged = (byte[])original.Clone();
        damaged[1000] ^= 1;
        Reject(damaged);

        // Recompute the checksum to exercise structural validation independently.
        ChangeInt(original, 0, 0);                         // magic
        ChangeInt(original, 4, 999);                       // version
        ChangeInt(original, 8, 128);                       // width
        ChangeInt(original, 12, 128);                      // height
        ChangeInt(original, 16, 128);                      // depth
        ChangeInt(original, 24, int.MaxValue);             // unbounded name length
        int nameLength = Encoding.UTF8.GetByteCount("Test");
        ChangeInt(original, 28 + nameLength, BitConverter.SingleToInt32Bits(float.NaN));
        ChangeInt(original, 40 + nameLength, BitConverter.SingleToInt32Bits(float.PositiveInfinity));
        ChangeInt(original, 44 + nameLength, BitConverter.SingleToInt32Bits(2));
        ChangeInt(original, 48 + nameLength, WorldSize.Normal.BlockCount() - 1);
        damaged = (byte[])original.Clone();
        damaged[52 + nameLength] = 255;
        Sign(damaged);
        Reject(damaged);
        damaged = (byte[])original.Clone();
        damaged[28] = 0xff;                                // invalid UTF-8
        Sign(damaged);
        Reject(damaged);
        damaged = (byte[])original.Clone();
        damaged[28] = (byte)'/';                           // unsafe display name
        Sign(damaged);
        Reject(damaged);

        damaged = new byte[original.Length + 1];
        original.AsSpan(0, original.Length - 32).CopyTo(damaged);
        Sign(damaged);
        Reject(damaged);                                   // valid checksum but extra block byte
    }

    public static void WorldDirectories()
    {
        WithStore((manager, root) =>
        {
            var save = Fixture() with { Name = "CON" }; // A reserved Windows name is metadata, never a directory name.
            string first = manager.Create(save), second = manager.Create(save);
            Require(first != second && Guid.TryParseExact(first, "N", out _), "Independent directory IDs were not assigned.");
            Require(manager.ListWorlds().Count == 2, "Multiple worlds were not listed.");
            Require(manager.Load(first).Name == "CON", "Display name did not round-trip.");
            Require(File.Exists(Path.Combine(root, first, WorldSaveManager.FileName)), "Save is outside the documented directory.");
            foreach (string invalid in new[] { "..", "../escape", "C:/escape", first + "/.." })
                Throws<ArgumentException>(() => manager.Load(invalid));
            foreach (string invalid in new[] { "", " ", "..", "../escape", "A/B", "A\\B", "A:B", "A\0B", new string('A', 49) })
                Throws<ArgumentException>(() => WorldSerializer.ValidateName(invalid));

            string broken = Guid.NewGuid().ToString("N"), missing = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(root, broken));
            Directory.CreateDirectory(Path.Combine(root, missing));
            Directory.CreateDirectory(Path.Combine(root, "ignored-folder"));
            File.WriteAllBytes(Path.Combine(root, broken, WorldSaveManager.FileName), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(root, first, "world.dat.interrupted.tmp"), "incomplete save");
            var entries = manager.ListWorlds();
            Require(entries.Count == 4, "World list lost corrupt/missing entries or included unrelated folders.");
            int errors = 0;
            foreach (WorldEntry entry in entries) if (entry.Error != null) errors++;
            Require(errors == 2, "Corrupt or missing saves were not marked unavailable.");
            Throws<InvalidDataException>(() => manager.Load(broken));
            Throws<IOException>(() => manager.Load(missing));
            Require(manager.Load(first).World.Seed == save.World.Seed, "Interrupted temporary file replaced a valid save.");
        });
    }

    public static void AtomicSaving()
    {
        WithStore((manager, root) =>
        {
            WorldSave save = Fixture();
            string id = manager.Create(save);
            string path = Path.Combine(root, id, WorldSaveManager.FileName);
            byte[] previous = File.ReadAllBytes(path);
            save.World.GetChunk(0, 0).SetBlock(0, 0, 0, (BlockType)250);
            Throws<InvalidDataException>(() => manager.Save(id, save));
            Require(previous.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Failed serialization damaged the previous save.");
            save.World.GetChunk(0, 0).SetBlock(0, 0, 0, BlockType.Air);
            save.World.SetBlock(16, 63, 16, BlockType.Stone);

            if (OperatingSystem.IsWindows())
            {
                // Force failure after the temporary file is completely written and flushed.
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Throws<IOException>(() => manager.Save(id, save));
                Require(previous.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Failed replacement damaged the previous save.");
            }
            Require(Directory.GetFiles(Path.Combine(root, id), "*.tmp").Length == 0, "Failed save left temporary files behind.");
            manager.Save(id, save with { PlayerPosition = new Vector3(32.5f, 65, 40.5f), Yaw = -1.2f });
            WorldSave updated = manager.Load(id);
            Require(updated.World.GetBlock(16, 63, 16) == BlockType.Stone && updated.PlayerPosition.X == 32.5f && updated.Yaw == -1.2f,
                "Atomic replacement failed to persist the new state.");
        });
    }

    private static WorldSave Fixture() => new(new World(123, false), "Test", new Vector3(128.5f, 30, 128.5f), 0, -0.3f);
    private static byte[] Encode(WorldSave save)
    {
        using var stream = new MemoryStream();
        WorldSerializer.Write(stream, save);
        return stream.ToArray();
    }
    private static void ChangeInt(byte[] original, int offset, int value)
    {
        byte[] changed = (byte[])original.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(offset, 4), value);
        Sign(changed);
        Reject(changed);
    }
    private static void Sign(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
    private static void Reject(byte[] bytes) => Throws<InvalidDataException>(() => WorldSerializer.Read(new MemoryStream(bytes)));
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void WithStore(Action<WorldSaveManager, string> check)
    {
        // Disposable test fixtures stay under the test build, never in the user's real saves.
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-persistence-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { check(new WorldSaveManager(root), root); }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                throw new InvalidOperationException("Test cleanup escaped its directory.");
            Directory.Delete(root, true);
        }
    }
}
