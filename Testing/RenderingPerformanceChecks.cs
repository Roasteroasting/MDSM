using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class RenderingPerformanceChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Regions()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>()) { CheckRegion(size); GC.Collect(); }
        var clock = new SimulationClock();
        Require(clock.Advance(.5) == 60 && clock.Advance(0) == 0, "Render stalls/pause must not discard or advance gameplay time.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckRegion(WorldSize size)
    {
        var world = new World(173, size, false); var region = new ChunkRegion(world);
        var camera = new Camera { Position = new Vector3(world.Width / 2f, 115, world.Depth / 2f) };
        region.Update(camera.Position, 16f / 9);
        int initial = region.Indices.Count, version = region.Version;
        Require(initial <= region.MaximumCount && initial <= 1400, "Residency scaled with map dimensions.");
        Require(!region.Update(camera.Position + Vector3.One * .01f, 16f / 9) && region.Version == version, "Sub-chunk motion churned residency.");
        foreach (Vector3 position in new[] { camera.Position, new Vector3(1, 180, 1), new Vector3(world.Width - 1, 3, world.Depth - 1) })
        {
            camera.Position = position; region.Update(position, 16f / 9);
            Require(region.Indices.Count <= region.MaximumCount && region.Indices.TrueForAll(i => i >= 0 && i < world.ChunkCount), "Region escaped world/budget.");
            for (int turn = 0; turn < 8; turn++)
            {
                camera.RestoreOrientation(turn * MathHelper.PiOver4 > MathHelper.Pi ? turn * MathHelper.PiOver4 - MathHelper.TwoPi : turn * MathHelper.PiOver4, turn % 2 == 0 ? -.7f : .7f);
                var frustum = new BoundingFrustum(camera.View * camera.Projection(16f / 9));
                // Plane/AABB intersection is conservative for tall chunks. Test the actual frustum
                // volume instead of requiring residency for those out-of-volume false positives.
                Vector3[] corners = frustum.GetCorners();
                for (int a = 0; a < corners.Length; a++) for (int b = a; b < corners.Length; b++)
                for (int sample = 0; sample <= 8; sample++)
                {
                    Vector3 point = Vector3.Lerp(corners[a], corners[b], sample / 8f);
                    if (point.X < 0 || point.X >= world.Width || point.Z < 0 || point.Z >= world.Depth) continue;
                    int index = (int)point.Z / 16 * world.ChunksX + (int)point.X / 16;
                    Require(region.Contains(index), "Far/frustum corner missing from region.");
                }
            }
        }
        camera.Position = new Vector3(world.Width / 2f, 90, world.Depth / 2f); region.Update(camera.Position, 16f / 9);
        int center = world.ChunksX / 2 + world.ChunksX * (world.ChunksZ / 2);
        for (int step = 0; step < 100; step++)
        {
            region.Update(camera.Position + Vector3.UnitX * (step % 2 == 0 ? 16 : 0), 16f / 9);
            Require(region.Contains(center) && region.Removed.Count == 0, "Hysteresis failed at adjacent chunk boundary.");
        }
    }
    public static void CollisionCache()
    {
        var world = PigChecks.Flat();
        FieldInfo revision = typeof(PassiveMob).GetField("_cacheRevision", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (MobSpecies species in Enum.GetValues<MobSpecies>())
        {
            var state = new MobState(species, new Vector3(32.5f, 1, 32.5f), 0, MobDefinitions.Get(species).MaximumHealth);
            var cached = new PassiveMob(state, 321); var fresh = new PassiveMob(state, 321);
            for (int frame = 0; frame < 1600; frame++)
            {
                if (frame == 300) { cached.Damage(1, Vector3.UnitX); fresh.Damage(1, Vector3.UnitX); }
                if (frame == 500) world.SetBlock(32, 0, 32, BlockType.Air);
                if (frame == 700) world.SetBlock(32, 0, 32, BlockType.Stone);
                if (frame == 900) world.SetBlock(33, 1, 32, BlockType.WaterSource);
                revision.SetValue(fresh, -1); // Independent fresh voxel queries each tick, same AI/random sequence.
                cached.Update(world, (float)SimulationClock.StepSeconds); fresh.Update(world, (float)SimulationClock.StepSeconds);
                Require(cached.CaptureState() == fresh.CaptureState() && cached.Grounded == fresh.Grounded && cached.WalkPhase == fresh.WalkPhase,
                    "Collision cache changed movement, water, edits, or AI semantics.");
            }
        }
    }
    internal static void Warm(WorldRenderer renderer, Camera camera)
    {
        int frames = 0;
        while (!renderer.PrepareInitialMeshes(camera))
        {
            Require(renderer.RebuiltChunks <= WorldRenderer.MaximumBuildsPerFrame, "Upload count exceeded per-frame budget.");
            if (++frames > 10000) throw new InvalidOperationException("Residency never became ready.");
        }
    }
    internal static void Graphics(GraphicsDevice device, TextureAtlas atlas, World world, Camera camera)
    {
        long originalBytes = ChunkMesh.LiveBufferBytes; int originalBuffers = ChunkMesh.LiveBuffers;
        var renderer = new WorldRenderer(device, atlas, world);
        Warm(renderer, camera);
        Require(ChunkMesh.LiveBufferBytes - originalBytes == renderer.BufferBytes, "Diagnostics hid terrain buffer allocations.");
        Require(renderer.ResidentChunks < 1600 && renderer.BufferBytes < 400L * 1048576, "Full-map terrain GPU retention returned.");
        if (world.Size == WorldSize.Massive) Require(renderer.GpuChunks < world.ChunkCount / 8, "Massive residency scaled with 16,384 chunks.");
        int first = (int)camera.Position.Z / 16 * world.ChunksX + (int)camera.Position.X / 16;
        var meshes = (ChunkMesh[])typeof(WorldRenderer).GetField("_meshes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
        ChunkMesh oldMesh = meshes[first]; Require(oldMesh != null, "Nearby mesh absent.");
        int ox = world.Chunks[first].OriginX, oz = world.Chunks[first].OriginZ;
        var edit = new Point3(ox + 15, 125, oz + 15);
        world.SetBlock(edit.X, edit.Y, edit.Z, BlockType.WhiteWool);
        Warm(renderer, camera); Require(!meshes[first].NeedsBuild, "Seam edit failed to rebuild resident mesh.");
        foreach (int neighbor in new[] { first + 1, first + world.ChunksX })
            Require(meshes[neighbor] != null && !meshes[neighbor].NeedsBuild, "Face-sharing resident neighbor remained stale.");
        long builds = renderer.PreparedChunks;
        for (int turn = 0; turn < 4; turn++) { camera.RestoreOrientation(turn == 3 ? -MathHelper.PiOver2 : turn * MathHelper.PiOver2, -.2f); renderer.Draw(camera, null); renderer.DrawWater(camera); }
        Require(renderer.PreparedChunks == builds, "Turning rebuilt cached geometry.");
        foreach (Vector3 position in new[] { new Vector3(world.Width - 2, 180, world.Depth - 2), new Vector3(1, 2, 1), new Vector3(world.Width / 2, 120, world.Depth / 2) })
        {
            camera.Position = position; renderer.Prepare(camera);
            Require(renderer.RebuiltChunks <= 4 && renderer.ResidentChunks <= renderer.ResidentLimit, "Rapid movement escaped work/residency budget.");
            Warm(renderer, camera); renderer.Draw(camera, null); renderer.DrawWater(camera);
            Require(renderer.Water.CachedChunks <= renderer.ResidentChunks && renderer.BufferBytes < 450L * 1048576, "Distant water/terrain retained.");
            Require(ChunkMesh.LiveBufferBytes - originalBytes == renderer.BufferBytes, "Residency diagnostics omitted live GPU resources.");
        }
        Require(oldMesh.BufferBytes == 0, "Evicted terrain buffer was not disposed.");
        Require(meshes[first] == null, "Distant edit fixture did not leave the resident region.");
        // Edit while non-resident, then return: reconstruction must read current CPU blocks.
        world.SetBlock(edit.X, edit.Y, edit.Z, BlockType.Leaves);
        camera.Position = new Vector3(ox + 8, 126, oz + 8); Warm(renderer, camera);
        Require(world.GetBlock(edit.X, edit.Y, edit.Z) == BlockType.Leaves && !meshes[first].NeedsBuild, "Distant edit lost on re-entry.");
        renderer.Dispose();
        Require(ChunkMesh.LiveBufferBytes == originalBytes && ChunkMesh.LiveBuffers == originalBuffers
            && renderer.Water.CachedChunks == 0 && renderer.Water.BufferBytes == 0, "World switch/disposal leaked mesh resources.");
        using var switched = new WorldRenderer(device, atlas, world);
        Warm(switched, camera); Require(switched.GpuChunks > 0, "Renderer failed after world switching.");
        BufferLifecycle(device);
        Console.WriteLine($"PASS: {world.Size} actual WorldRenderer residency, budgets, frustum/elevation/edges, turns, movement, edits/re-entry, water eviction and GPU cleanup.");
    }
    private static void BufferLifecycle(GraphicsDevice device)
    {
        var world = new World(1, WorldSize.Small, false); Chunk chunk = world.GetChunk(0, 0);
        var data = new byte[Chunk.BlockCount]; Array.Fill(data, (byte)BlockType.Leaves, 0, 512); chunk.RestoreBlocks(data);
        using var mesh = new ChunkMesh(device, chunk); var builder = new ChunkMeshBuilder(); mesh.Update(world, builder);
        var oldBuffer = (VertexBuffer)typeof(ChunkMesh).GetField("_vertices", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(mesh);
        long denseBytes = mesh.BufferBytes;
        Array.Clear(data); data[0] = (byte)BlockType.Stone; chunk.RestoreBlocks(data); mesh.Update(world, builder);
        Require(oldBuffer.IsDisposed && mesh.BufferBytes < denseBytes / 2, "Reduced geometry retained an oversized old GPU buffer.");
        Array.Clear(data); chunk.RestoreBlocks(data); mesh.Update(world, builder);
        Require(!mesh.HasGeometry && mesh.BufferBytes == 0, "Empty chunk retained a GPU buffer.");
    }
}
