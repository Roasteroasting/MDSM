using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace MDSM;

internal static class DecorationChecks
{
    public static void Definitions()
    {
        Require((byte)BlockType.Air == 0 && (byte)BlockType.Grass == 1 && (byte)BlockType.Dirt == 2 && (byte)BlockType.Stone == 3,
            "Legacy block IDs changed.");
        for (int id = 0; id <= 13; id++)
        {
            BlockType block = (BlockType)id;
            BlockDefinition definition = Blocks.Get(block);
            Require(Blocks.IsDefined(block) && definition.Renderable == (id != 0) && definition.Placeable == (id != 0), "Block properties are incomplete.");
            Require(definition.Solid == (id > 0 && id <= 10), "Collision properties are incorrect.");
            Require(definition.Opaque == (id > 0 && id <= 10 && block != BlockType.Leaves), "Occlusion properties are incorrect.");
        }
        Require(!Blocks.IsDefined((BlockType)255) && !Blocks.IsRenderable((BlockType)255), "Unknown block ID is usable.");
        Require(Blocks.Tile(BlockType.WoodLog, Vector3.Up) == 5 && Blocks.Tile(BlockType.WoodLog, Vector3.Down) == 5
            && Blocks.Tile(BlockType.WoodLog, Vector3.Right) == 4, "Log end and bark textures are incorrect.");
        var palette = new BlockPalette();
        Require(palette.Selected == BlockType.Grass && palette.PageCount == 2, "Initial hotbar changed.");
        var seen = new HashSet<BlockType>();
        for (int page = 0; page < palette.PageCount; page++)
        {
            for (int slot = 0; slot < BlockPalette.SlotsPerPage; slot++)
            {
                BlockType block = palette.AtSlot(slot);
                if (block == BlockType.Air) continue;
                palette.SelectSlot(slot);
                Require(palette.Selected == block, "Hotbar selection failed.");
                seen.Add(block);
            }
            palette.NextPage();
        }
        Require(seen.Count == 13 && palette.Selected == BlockType.Grass, "Not all blocks are accessible or page wrap failed.");
    }

    public static void Determinism()
    {
        var world = new World(173);
        var same = new World(173);
        var other = new World(174);
        bool changed = false;
        for (int i = 0; i < World.ChunkCount; i++)
        {
            Require(world.Chunks[i].Blocks.SequenceEqual(same.Chunks[i].Blocks), "Decorations changed for the same seed.");
            changed |= !world.Chunks[i].Blocks.SequenceEqual(other.Chunks[i].Blocks);
            Require(world.Chunks[i].Revision == 0, "Generation dirtied chunk meshes before they existed.");
        }
        Require(changed, "Different seeds produced the same decorated world.");
        var terrain = new TerrainGenerator(173);
        int roots = 0, leaves = 0, plants = 0;
        var trunkHeights = new HashSet<int>();
        for (int z = 0; z < World.Depth; z++)
        for (int x = 0; x < World.Width; x++)
        {
            int ground = terrain.SurfaceY(x, z), dirt = terrain.DirtDepth(x, z);
            for (int y = 0; y <= ground; y++)
            {
                BlockType expected = y == ground ? BlockType.Grass : y >= ground - dirt ? BlockType.Dirt : BlockType.Stone;
                Require(world.GetBlock(x, y, z) == expected, "Decoration modified base terrain.");
            }
            for (int y = ground + 1; y < World.Height; y++)
            {
                BlockType block = world.GetBlock(x, y, z);
                if (block == BlockType.Leaves) leaves++;
                if (Blocks.Get(block).NeedsGrass)
                {
                    plants++;
                    Require(y == ground + 1 && world.GetBlock(x, y - 1, z) == BlockType.Grass, "Plant lacks grass support.");
                }
                Require(block is BlockType.Air or BlockType.WoodLog or BlockType.Leaves
                    or BlockType.TallGrass or BlockType.RedFlower or BlockType.YellowFlower, "Unexpected generated feature.");
            }
            if (world.GetBlock(x, ground + 1, z) == BlockType.WoodLog)
            {
                roots++;
                int height = 0;
                while (world.GetBlock(x, ground + height + 1, z) == BlockType.WoodLog) height++;
                Require(height >= 4 && height <= 6 && ground + height + 1 < World.Height, "Tree height is invalid.");
                trunkHeights.Add(height);
                Require(x >= 3 && x < World.Width - 3 && z >= 3 && z < World.Depth - 3, "Trunk is too near an edge.");
                int min = ground, max = ground;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int neighbor = terrain.SurfaceY(x + dx, z + dz);
                    min = Math.Min(min, neighbor); max = Math.Max(max, neighbor);
                }
                Require(max - min <= 2, "Tree generated on a steep slope.");
            }
        }
        Require(roots >= 40 && roots <= 450 && leaves > 1000 && plants > 500 && plants < 6000, "Decoration density is outside the intended sparse range.");
        Require(trunkHeights.Count >= 2, "Trunk heights lack variation.");
        for (int z = 126; z <= 130; z++)
        for (int x = 126; x <= 130; x++)
            Require(world.GetBlock(x, terrain.SurfaceY(x, z) + 1, z) == BlockType.Air, "Spawn clearance is obstructed.");
        Require(!world.IntersectsSolid(new Player(world.Spawn).Bounds), "Decorations obstructed spawning.");
        Console.WriteLine($"  Seed 173: {roots} trees, {leaves} leaf blocks, {plants} plants.");
    }

    public static void TreeBoundaries()
    {
        var world = new World(0, false);
        world.SetGeneratedBlock(15, 10, 15, BlockType.Grass);
        Require(WorldDecorator.TryTree(world, 15, 10, 15, 5, 2, 0), "Valid seam tree was rejected.");
        Require(world.GetBlock(15, 15, 15) == BlockType.WoodLog
            && world.GetBlock(16, 13, 15) == BlockType.Leaves && world.GetBlock(15, 13, 16) == BlockType.Leaves
            && world.GetBlock(16, 13, 16) == BlockType.Leaves, "Canopy did not cross chunk boundaries.");
        world.SetGeneratedBlock(0, 10, 0, BlockType.Grass);
        world.SetGeneratedBlock(255, 10, 255, BlockType.Grass);
        world.SetGeneratedBlock(40, 62, 40, BlockType.Grass);
        Require(!WorldDecorator.TryTree(world, 0, 10, 0, 4, 2, 0)
            && !WorldDecorator.TryTree(world, 255, 10, 255, 4, 3, 0)
            && !WorldDecorator.TryTree(world, 40, 62, 40, 4, 2, 0), "Out-of-bounds tree was accepted.");
        Require(world.GetBlock(0, 11, 0) == BlockType.Air && world.GetBlock(40, 63, 40) == BlockType.Air, "Rejected tree wrote partial blocks.");
        world.SetGeneratedBlock(50, 10, 50, BlockType.Grass);
        world.SetGeneratedBlock(50, 12, 50, BlockType.Stone);
        Require(!WorldDecorator.TryTree(world, 50, 10, 50, 5, 2, 0) && world.GetBlock(50, 11, 50) == BlockType.Air,
            "Obstructed tree left a partial trunk.");
    }

    public static void Meshes()
    {
        var world = new World(0, false);
        var mesh = new ChunkMeshBuilder();
        world.SetBlock(15, 10, 8, BlockType.Leaves);
        world.SetBlock(16, 10, 8, BlockType.Leaves);
        mesh.Build(world, world.GetChunk(0, 0));
        Require(mesh.VertexCount == 36, "Leaf-to-leaf seam faces were incorrectly hidden.");
        for (int i = 0; i < mesh.VertexCount; i++)
            Require(mesh.Vertices[i].Color.A == 255, "Face shading changed alpha and could discard dark leaf faces.");
        world.SetBlock(15, 10, 8, BlockType.Stone);
        mesh.Build(world, world.GetChunk(0, 0));
        Require(mesh.VertexCount == 36, "Leaves hid an opaque neighbor's face.");
        mesh.Build(world, world.GetChunk(1, 0));
        Require(mesh.VertexCount == 30, "Opaque neighbor did not hide the covered leaf face.");
        world.SetBlock(16, 10, 8, BlockType.Stone);
        mesh.Build(world, world.GetChunk(0, 0));
        Require(mesh.VertexCount == 30, "Opaque seam culling regressed.");
        world.SetBlock(15, 10, 8, BlockType.TallGrass);
        mesh.Build(world, world.GetChunk(0, 0));
        Require(mesh.VertexCount == 24, "Plant must emit two double-sided quads, not a cube.");
        for (int i = 0; i < mesh.VertexCount; i += 12)
        {
            Vector3 first = Vector3.Cross(mesh.Vertices[i + 1].Position - mesh.Vertices[i].Position,
                mesh.Vertices[i + 2].Position - mesh.Vertices[i].Position);
            Vector3 back = Vector3.Cross(mesh.Vertices[i + 7].Position - mesh.Vertices[i + 6].Position,
                mesh.Vertices[i + 8].Position - mesh.Vertices[i + 6].Position);
            Require(Vector3.Dot(first, back) < 0, "Plant back face is missing or incorrectly wound.");
        }
        Require(!world.IsSolid(15, 10, 8), "Decorative vegetation is collidable.");
        world.SetBlock(15, 10, 8, BlockType.Leaves);
        Require(world.IsSolid(15, 10, 8), "Leaves lost collision.");
    }

    public static void Interaction()
    {
        var world = new World(0, false);
        var cell = new Point3(16, 10, 16);
        var player = new Player(new Vector3(128.5f, 30, 128.5f));
        world.SetBlock(16, 9, 16, BlockType.Grass);
        foreach (BlockType block in Blocks.Selection)
        {
            Require(world.TryPlace(cell, block, player.Bounds), $"Cannot place {block}.");
            Require(world.SetBlock(16, 10, 16, BlockType.Air), $"Cannot remove {block}.");
        }
        player = new Player(new Vector3(16.5f, 10, 16.5f));
        Require(world.TryPlace(cell, BlockType.TallGrass, player.Bounds), "Non-solid plant was rejected inside player's cell.");
        Require(!world.IntersectsSolid(player.Bounds), "Plant blocks player movement.");
        BlockHit? hit = BlockRaycaster.Cast(world, new Vector3(14.5f, 10.5f, 16.5f), Vector3.Right);
        Require(hit.HasValue && hit.Value.Cell == cell, "Non-solid plant cannot be targeted.");
        world.SetBlock(16, 9, 16, BlockType.Air);
        Require(world.GetBlock(16, 10, 16) == BlockType.Air, "Unsupported plant was not removed.");
        Require(!world.TryPlace(cell, BlockType.RedFlower, player.Bounds), "Plant accepted a missing grass support.");
        world.SetBlock(16, 9, 16, BlockType.Dirt);
        Require(!world.TryPlace(cell, BlockType.YellowFlower, player.Bounds), "Plant accepted non-grass support.");
    }

    public static void Persistence()
    {
        // Independent v1 writer restricted to Milestone 3's original IDs.
        var legacy = new World(173, false);
        new TerrainGenerator(173).Generate(legacy);
        legacy.SetBlock(15, 63, 15, BlockType.Stone);
        legacy.SetBlock(128, legacy.SurfaceY(128, 128), 128, BlockType.Air);
        using var oldFile = new MemoryStream();
        using (var writer = new BinaryWriter(oldFile, Encoding.UTF8, true))
        {
            writer.Write(0x4d53444du); writer.Write(1);
            writer.Write(256); writer.Write(64); writer.Write(256); writer.Write(173);
            byte[] name = Encoding.UTF8.GetBytes("Milestone 3");
            writer.Write(name.Length); writer.Write(name);
            writer.Write(128.5f); writer.Write(60f); writer.Write(128.5f);
            writer.Write(-0.4f); writer.Write(0.2f); writer.Write(4194304);
            foreach (Chunk chunk in legacy.Chunks) writer.Write(chunk.Blocks);
        }
        oldFile.Write(SHA256.HashData(oldFile.GetBuffer().AsSpan(0, (int)oldFile.Length)));
        oldFile.Position = 0;
        WorldSave restored = WorldSerializer.Read(oldFile);
        Require(WorldSerializer.FormatVersion == 1 && restored.Name == "Milestone 3" && restored.World.Seed == 173,
            "Legacy save version or metadata changed.");
        for (int i = 0; i < World.ChunkCount; i++)
            Require(restored.World.Chunks[i].Blocks.SequenceEqual(legacy.Chunks[i].Blocks), "Loading legacy data regenerated decorations.");
        Require(restored.PlayerPosition == new Vector3(128.5f, 60, 128.5f) && restored.Yaw == -0.4f && restored.Pitch == 0.2f,
            "Legacy pose changed.");
        foreach (BlockType block in Blocks.Selection)
        {
            int x = (int)block * 3;
            restored.World.SetBlock(x, 59, 16, BlockType.Grass);
            restored.World.SetBlock(x, 60, 16, block);
        }
        using var newFile = new MemoryStream();
        WorldSerializer.Write(newFile, restored);
        newFile.Position = 0;
        WorldSave expanded = WorldSerializer.Read(newFile);
        for (int i = 0; i < World.ChunkCount; i++)
            Require(expanded.World.Chunks[i].Blocks.SequenceEqual(restored.World.Chunks[i].Blocks), "New block IDs were not saved/restored exactly.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
