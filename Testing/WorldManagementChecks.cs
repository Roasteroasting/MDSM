using System;
using System.IO;
using Microsoft.Xna.Framework;

using MDSM.Gameplay;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WorldManagementChecks
{
    public static void SizesAndBoundaries()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            var world = new World(173, size, false);
            int width = size.Width();
            Require(world.Width == width && world.Depth == width && World.Height == 128, "Wrong world dimensions.");
            Require(world.ChunkCount == width / 16 * (width / 16) && world.Chunks.Length == world.ChunkCount, "Wrong chunk count.");
            int bytes = 0;
            foreach (Chunk chunk in world.Chunks)
            {
                bytes += chunk.Blocks.Length;
                Require(ReferenceEquals(world.GetChunk(chunk.X, chunk.Z), chunk), "Chunk lookup aliases another chunk.");
                foreach (int local in new[] { 0, 15 })
                {
                    int x = chunk.OriginX + local, z = chunk.OriginZ + local;
                    Require(world.SetBlock(x, 63, z, BlockType.Gravel), "Valid edge coordinate rejected.");
                    Require(chunk.GetBlock(local, 63, local) == BlockType.Gravel, "World/local indexing disagrees.");
                }
            }
            Require(bytes == world.BlockCount && bytes == width * 128 * width, "Raw storage is not one byte per block.");
            Require(world.GetChunk(world.ChunksX - 1, world.ChunksZ - 1).Bounds.Max == new Vector3(width, 128, width), "Last chunk bounds are wrong.");
            foreach (Point3 cell in new[] { new Point3(-1, 0, 0), new(width, 0, 0), new(0, 0, width), new(0, 128, 0) })
                Require(world.GetBlock(cell.X, cell.Y, cell.Z) == BlockType.Air
                    && !world.SetBlock(cell.X, cell.Y, cell.Z, BlockType.Stone)
                    && !world.TryPlace(cell, BlockType.Stone, new BoundingBox()), "Invalid world access succeeded.");
            var player = new Player(new Vector3(width - 0.5f, 40, width - 0.5f));
            for (int i = 0; i < 30; i++) player.Update(world, new Vector3(1, 0, 1), false, 1f / 60);
            Require(player.Bounds.Max.X <= width && player.Bounds.Max.Z <= width, "Player escaped selected world bounds.");
            var seam = new Point3(width - 16, 45, width - 16);
            world.SetBlock(seam.X, seam.Y, seam.Z, BlockType.Stone);
            BlockHit? hit = BlockRaycaster.Cast(world, new Vector3(seam.X - 1.5f, 45.5f, seam.Z + 0.5f), Vector3.Right);
            Require(hit.HasValue && hit.Value.Cell == seam, "Raycast failed near selected world's last seam.");
            int own = world.GetChunk(world.ChunksX - 1, world.ChunksZ - 1).Revision;
            int diagonal = world.GetChunk(world.ChunksX - 2, world.ChunksZ - 2).Revision;
            world.SetBlock(seam.X, seam.Y, seam.Z, BlockType.Air);
            Require(world.GetChunk(world.ChunksX - 1, world.ChunksZ - 1).Revision == own + 1
                && world.GetChunk(world.ChunksX - 2, world.ChunksZ - 2).Revision == diagonal, "Edit rebuilt unrelated chunks.");
            var camera = new Camera { Position = new Vector3(width / 2f + 0.5f, 40, width / 2f + 0.5f) };
            var frustum = new BoundingFrustum(camera.View * camera.Projection(16f / 9));
            Require(frustum.Intersects(world.GetChunk(world.ChunksX / 2, world.ChunksZ / 2 - 2).Bounds)
                && !frustum.Intersects(world.GetChunk(world.ChunksX / 2, world.ChunksZ - 1).Bounds), "Size-dependent frustum bounds failed.");
        }
        Throws<ArgumentOutOfRangeException>(() => new World(0, (WorldSize)99));
    }

    public static void Generation()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            var world = new World(-907, size);
            var same = new World(-907, size);
            foreach (Chunk chunk in world.Chunks)
                Require(chunk.Blocks.SequenceEqual(same.GetChunk(chunk.X, chunk.Z).Blocks)
                    && chunk.Revision == 0, "Generation is nondeterministic or dirties initial meshes.");
            var terrain = new TerrainGenerator(world.Seed);
            int roots = 0, plants = 0;
            for (int z = 0; z < world.Depth; z++)
            for (int x = 0; x < world.Width; x++)
            {
                int top = terrain.SurfaceY(x, z), dirt = terrain.DirtDepth(x, z);
                for (int y = 0; y <= top; y++)
                {
                    BlockType expected = y == 0 ? BlockType.Bedrock : y == top ? BlockType.Grass
                        : y >= top - dirt ? BlockType.Dirt : BlockType.Stone;
                    Require(world.GetBlock(x, y, z) == expected, "Base terrain or single bedrock layer changed.");
                }
                for (int y = top + 1; y < World.Height; y++)
                {
                    BlockType block = world.GetBlock(x, y, z);
                    Require(block is BlockType.Air or BlockType.WoodLog or BlockType.Leaves
                        or BlockType.TallGrass or BlockType.RedFlower or BlockType.YellowFlower, "An excluded material generated naturally.");
                    if (Blocks.Get(block).NeedsGrass) plants++;
                }
                if (world.GetBlock(x, top + 1, z) == BlockType.WoodLog) roots++;
                if (Math.Abs(x - world.Width / 2) <= 2 && Math.Abs(z - world.Depth / 2) <= 2)
                    Require(world.GetBlock(x, top + 1, z) == BlockType.Air, "Spawn clearing is obstructed.");
            }
            Require(roots > world.Width * world.Depth / 1500 && roots < world.Width * world.Depth / 100
                && plants > world.Width * world.Depth / 100, "Decoration density did not scale with area.");
            Require(world.Spawn.X == world.Width / 2f + 0.5f && world.Spawn.Z == world.Depth / 2f + 0.5f
                && !world.IntersectsSolid(new Player(world.Spawn).Bounds), "Selected world spawn is unsafe or off center.");
            Require(world.SetBlock(0, 0, 0, BlockType.Air) && world.TryPlace(new Point3(0, 0, 0), BlockType.Bedrock,
                new Player(world.Spawn).Bounds), "Bedrock is no longer breakable/placeable.");
        }
    }

    public static void Canopies()
    {
        var world = new World(0, WorldSize.Small, false);
        for (uint shape = 0; shape < 32; shape++)
        {
            int x = 6 + (int)(shape % 8) * 14, z = 6 + (int)(shape / 8) * 14;
            world.SetGeneratedBlock(x, 10, z, BlockType.Grass);
            Require(!WorldDecorator.TryTree(world, x, 10, z, 5, 3, shape), "Wide canopy request was accepted.");
            int height = 4 + (int)(shape % 3);
            Require(WorldDecorator.TryTree(world, x, 10, z, height, 2, shape), "Valid bounded tree rejected.");
            int leaves = 0;
            for (int dz = -4; dz <= 4; dz++)
            for (int dx = -4; dx <= 4; dx++)
            for (int y = 11; y <= 18; y++)
                if (world.GetBlock(x + dx, y, z + dz) == BlockType.Leaves)
                {
                    leaves++;
                    Require(Math.Abs(dx) <= 2 && Math.Abs(dz) <= 2, "Leaves exceeded a 5x5 footprint.");
                }
            Require(leaves > 20 && world.GetBlock(x, 10 + height, z) == BlockType.WoodLog, "Tree shape or height was lost.");
            Require(world.GetBlock(x + 1, 11 + height, z + 1) == ((shape & 16) == 0 ? BlockType.Air : BlockType.Leaves),
                "Canopy shape variation was lost.");
        }
    }

    public static void PersistenceAndMetadata()
    {
        foreach (WorldSize size in WorldExpansionChecks.RoutineSizes)
        {
            var world = new World(-312, size, false);
            foreach (Chunk chunk in world.Chunks)
            {
                var bytes = new byte[Chunk.BlockCount];
                // Fill every byte with a nontrivial pattern to catch chunk-order/index mistakes.
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((i + chunk.X * 3 + chunk.Z * 5) % 14);
                chunk.RestoreBlocks(bytes);
            }
            var save = new WorldSave(world, "Size " + size, new Vector3(world.Width - 2.5f, 70, world.Depth - 3.5f), 1.1f, -0.3f);
            using var stream = new MemoryStream();
            WorldSerializer.Write(stream, save);
            int headerSize = 52 + System.Text.Encoding.UTF8.GetByteCount(save.Name);
            // Non-seekable stream throws if metadata reading attempts to read a block byte.
            using var header = new HeaderOnlyStream(stream.GetBuffer(), headerSize);
            WorldMetadata metadata = WorldSerializer.ReadMetadata(header);
            Require(metadata.Size == size && metadata.Name == save.Name && header.BytesRead == headerSize, "Metadata read blocks or lost dimensions.");
            stream.Position = 0;
            WorldSave restored = WorldSerializer.Read(stream);
            Require(restored.World.Size == size && restored.PlayerPosition == save.PlayerPosition
                && restored.Yaw == save.Yaw && restored.Pitch == save.Pitch, "Size or saved pose changed.");
            foreach (Chunk chunk in world.Chunks)
                Require(chunk.Blocks.SequenceEqual(restored.World.GetChunk(chunk.X, chunk.Z).Blocks), "Block bytes changed across size round-trip.");
            // Full reading also supports non-seekable streams.
            using var nonSeekable = new HeaderOnlyStream(stream.GetBuffer(), (int)stream.Length);
            Require(WorldSerializer.Read(nonSeekable).World.Size == size, "Non-seekable save restoration failed.");
        }
    }

    public static void Management()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-management-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WorldSaveManager(root);
            var world = new World(73, WorldSize.Small);
            world.SetBlock(15, 62, 16, BlockType.Cobblestone);
            var original = new WorldSave(world, "Original", new Vector3(60.5f, 50, 65.5f), 1.2f, 0.3f);
            string id = store.Create(original), sibling = store.Create(original with { Name = "Other" });
            string path = Path.Combine(root, id, WorldSaveManager.FileName);
            byte[] previous = File.ReadAllBytes(path), other = File.ReadAllBytes(Path.Combine(root, sibling, WorldSaveManager.FileName));
            foreach (string invalid in new[] { "..", "../escape", root, id + "/..", Guid.NewGuid().ToString() })
            {
                Throws<ArgumentException>(() => store.Delete(invalid));
                Throws<ArgumentException>(() => store.Rename(invalid, "Safe"));
            }
            Throws<ArgumentException>(() => store.Rename(id, "../unsafe"));
            Require(previous.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Invalid rename modified the save.");
            store.SetActiveWorld(id);
            Throws<IOException>(() => store.Delete(id));
            Throws<IOException>(() => store.Rename(id, "Loaded"));
            store.SetActiveWorld(null);
            store.Rename(id, "  Renamed  ");
            WorldSave renamed = store.Load(id);
            Require(renamed.Name == "Renamed" && renamed.World.Size == WorldSize.Small && renamed.World.Seed == world.Seed
                && renamed.PlayerPosition == original.PlayerPosition && renamed.Yaw == original.Yaw && renamed.Pitch == original.Pitch,
                "Rename lost metadata or changed directory ID.");
            foreach (Chunk chunk in world.Chunks)
                Require(chunk.Blocks.SequenceEqual(renamed.World.GetChunk(chunk.X, chunk.Z).Blocks), "Rename regenerated or changed block data.");
            var restarted = new WorldSaveManager(root);
            Require(restarted.Load(id).Name == "Renamed" && restarted.ListWorlds().Count == 2, "Rename did not persist or moved a directory.");
            Require(restarted.ListWorlds()[0].Size == WorldSize.Small, "World list lost saved size metadata.");
            if (OperatingSystem.IsWindows())
            {
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Throws<IOException>(() => store.Rename(id, "Blocked"));
                Throws<IOException>(() => store.Delete(id));
            }
            store.Delete(id);
            Require(!Directory.Exists(Path.Combine(root, id)) && store.ListWorlds().Count == 1
                && other.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, sibling, WorldSaveManager.FileName))),
                "Deletion affected another world or failed to refresh listing.");
            Throws<IOException>(() => store.Delete(id));
            string broken = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(root, broken));
            File.WriteAllBytes(Path.Combine(root, broken, WorldSaveManager.FileName), new byte[] { 1, 2, 3 });
            Throws<InvalidDataException>(() => store.Rename(broken, "Corrupt"));
            store.Delete(broken);
            Require(store.ListWorlds().Count == 1, "Unreadable world could not be deleted safely.");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent))
                throw new InvalidOperationException("Test cleanup escaped its directory.");
            Directory.Delete(root, true);
        }
    }

    private sealed class HeaderOnlyStream(byte[] bytes, int limit) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0) return 0;
            if (BytesRead == limit)
            {
                if (limit != bytes.Length && limit < 1024) throw new InvalidOperationException("Metadata read beyond the header.");
                return 0;
            }
            int count = Math.Min(buffer.Length, limit - BytesRead);
            bytes.AsSpan(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
