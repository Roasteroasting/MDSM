using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

public sealed class WorldRenderer : IDisposable
{
    public static readonly Color SkyColor = new(143, 190, 232);
    public const float FogStart = 80, FogEnd = 144;
    private readonly GraphicsDevice _device;
    private readonly AlphaTestEffect _terrainEffect;
    private readonly BasicEffect _selectionEffect;
    private readonly World _world;
    private readonly DroppedItemRenderer _items;
    private readonly FallingBlockRenderer _falling;
    private readonly WaterRenderer _water;
    private readonly ChunkRegion _region;
    private readonly ChunkMesh[] _meshes;
    private readonly ChunkMesh[] _visible;
    private readonly ChunkMeshBuilder _builder = new();
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private readonly VertexPositionColor[] _outline = new VertexPositionColor[24];
    private readonly Vector3[] _corners = new Vector3[8];
    private static readonly int[] OutlineEdges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
    public int VisibleChunks { get; private set; }
    public int RebuiltChunks { get; private set; }
    public int PreparedChunks { get; private set; }
    public const int MaximumBuildsPerFrame = 4;
    public const double BuildBudgetMilliseconds = 3;
    public int ResidentChunks => _region.Indices.Count;
    public int ResidentLimit => Math.Min(_world.ChunkCount, _region.MaximumCount);
    public int MeshedChunks { get; private set; }
    public int GpuChunks { get; private set; }
    public int PendingChunks { get; private set; }
    public long BufferBytes { get; private set; }
    internal long VertexCount { get; private set; }
    internal long DrawnVertices { get { long total = 0; for (int i = 0; i < VisibleChunks; i++) total += _visible[i].VertexCount; return total; } }
    internal WaterRenderer Water => _water;
    internal long DynamicBufferBytes => _items.BufferBytes + _falling.BufferBytes + _water.BufferBytes;
    public bool PrepareInitialMeshes(int budget = MaximumBuildsPerFrame)
    {
        var camera = new Camera { Position = _world.Spawn + Vector3.Up * Player.StandingEyeHeight };
        return PrepareInitialMeshes(camera, budget);
    }
    internal bool PrepareInitialMeshes(Camera camera, int budget = MaximumBuildsPerFrame)
    {
        Prepare(camera, budget);
        return PendingChunks == 0;
    }

    // All creation/uploads/evictions happen here on the graphics thread. No CPU mesh cache.
    internal void Prepare(Camera camera, int budget = MaximumBuildsPerFrame)
    {
        _frustum.Matrix = camera.View * camera.Projection(_device.Viewport.AspectRatio);
        if (_region.Update(camera.Position, _device.Viewport.AspectRatio))
            foreach (int index in _region.Removed) { _meshes[index]?.Dispose(); _meshes[index] = null; _water.Evict(index); }
        long start = Stopwatch.GetTimestamp(); RebuiltChunks = 0;
        while (RebuiltChunks < budget)
        {
            int next = -1; float priority = float.MaxValue;
            foreach (int index in _region.Indices)
            {
                ChunkMesh mesh = _meshes[index];
                Chunk chunk = _world.Chunks[index];
                if (mesh != null && !mesh.NeedsBuild && !_water.NeedsBuild(index)) continue;
                float dx = chunk.OriginX + 8 - camera.Position.X, dz = chunk.OriginZ + 8 - camera.Position.Z;
                float score = dx * dx + dz * dz;
                if (mesh != null && mesh.NeedsBuild) score -= 1000000; // Edits first, keeping the old mesh until replaced.
                else if (!_frustum.Intersects(chunk.Bounds) && score > 32 * 32) score += 100000;
                if (score < priority) { next = index; priority = score; }
            }
            if (next < 0) break;
            _meshes[next] ??= new ChunkMesh(_device, _world.Chunks[next]);
            _meshes[next].Update(_world, _builder);
            _water.PrepareChunk(next);
            RebuiltChunks++; PreparedChunks++;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= BuildBudgetMilliseconds) break;
        }
        MeshedChunks = GpuChunks = PendingChunks = 0; BufferBytes = VertexCount = 0;
        foreach (int index in _region.Indices)
        {
            ChunkMesh mesh = _meshes[index];
            if (mesh == null || mesh.NeedsBuild || _water.NeedsBuild(index)) PendingChunks++;
            if (mesh == null) continue;
            MeshedChunks++; if (mesh.BufferBytes > 0) GpuChunks++;
            BufferBytes += mesh.BufferBytes; VertexCount += mesh.VertexCount;
        }
    }

    public WorldRenderer(GraphicsDevice device, TextureAtlas atlas, World world)
    {
        _device = device;
        _world = world;
        _items = new DroppedItemRenderer(device, atlas);
        _falling = new FallingBlockRenderer(device, atlas);
        _region = new ChunkRegion(world);
        _water = new WaterRenderer(device, atlas, world, _region);
        _meshes = new ChunkMesh[world.ChunkCount];
        _visible = new ChunkMesh[world.ChunkCount];
        _terrainEffect = new AlphaTestEffect(device)
        {
            World = Matrix.Identity,
            Texture = atlas.Texture,
            VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 128,
            FogEnabled = true,
            FogColor = SkyColor.ToVector3(),
            FogStart = WorldRenderer.FogStart,
            FogEnd = WorldRenderer.FogEnd
        };
        _selectionEffect = new BasicEffect(device)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true
        };
    }

    public void Draw(Camera camera, BlockHit? selection, DroppedItems items = null, float miningProgress = 0, float fallingInterpolation = 1)
    {
        Matrix view = camera.View;
        Matrix projection = camera.Projection(_device.Viewport.AspectRatio);
        _frustum.Matrix = view * projection;
        VisibleChunks = 0;
        Prepare(camera);
        foreach (int index in _region.Indices)
        {
            ChunkMesh mesh = _meshes[index];
            if (mesh != null && mesh.HasGeometry && _frustum.Intersects(mesh.Bounds))
                _visible[VisibleChunks++] = mesh;
        }

        // SpriteBatch changes these states, so restore them every frame.
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _terrainEffect.View = view;
        _terrainEffect.Projection = projection;
        foreach (EffectPass pass in _terrainEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            for (int i = 0; i < VisibleChunks; i++)
                _visible[i].Draw();
        }

        _items.Draw(camera, items);
        _falling.Draw(camera, _world.Gravity, fallingInterpolation);
        if (selection is BlockHit hit)
            DrawSelection(hit, camera, miningProgress);
    }

    public void DrawWater(Camera camera) => _water.Draw(camera);

    private void DrawSelection(BlockHit hit, Camera camera, float miningProgress)
    {
        BoundingBox box = World.Bounds(hit.Cell);
        box.Min -= new Vector3(0.003f);
        box.Max += new Vector3(0.003f);
        box.GetCorners(_corners);
        for (int i = 0; i < OutlineEdges.Length; i++)
            _outline[i] = new VertexPositionColor(_corners[OutlineEdges[i]], miningProgress > 0 ? Color.Orange : new Color(25, 30, 35));

        _selectionEffect.View = camera.View;
        _selectionEffect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        foreach (EffectPass pass in _selectionEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.LineList, _outline, 0, 12);
        }
    }

    public void Dispose()
    {
        foreach (ChunkMesh mesh in _meshes)
            mesh?.Dispose();
        Array.Clear(_meshes); BufferBytes = VertexCount = 0; GpuChunks = MeshedChunks = 0;
        _terrainEffect.Dispose();
        _selectionEffect.Dispose();
        _items.Dispose();
        _falling.Dispose();
        _water.Dispose();
    }
}
