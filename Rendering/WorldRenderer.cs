using System;
using System.Diagnostics;
using System.Collections.Generic;
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
    private readonly Effect _terrainEffect;
    private readonly Texture2D _texture;
    private readonly Texture2D _textTexture;
    private readonly SkyRenderer _sky;
    private readonly ParticleRenderer _particles;
    private readonly WeatherRenderer _weather;
    private readonly BurningRenderer _burning;
    public float Brightness { get; set; }=.5f;
    private readonly BasicEffect _selectionEffect;
    private readonly World _world;
    private readonly DroppedItemRenderer _items;
    private readonly FallingBlockRenderer _falling;
    private readonly WaterRenderer _water;
    private readonly ChunkRegion _region;
    private readonly ChunkMesh[] _meshes;
    private readonly ChunkMesh[] _visible;
    private readonly Queue<ChunkMesh> _retiring=new(2048);
    private long _retiringBytes;
    public int RetiringChunks=>_retiring.Count;
    private readonly ChunkMeshBuilder _builder = new();
    private int _building=-1,_buildRevision;
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
    internal double PrepareMilliseconds {get;private set;}
    internal double TerrainBuildMilliseconds {get;private set;}
    internal double WaterBuildMilliseconds {get;private set;}
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
        Prepare(camera, budget,initial:true);
        return PendingChunks == 0 && _retiring.Count==0;
    }

    // All creation/uploads/evictions happen here on the graphics thread. No CPU mesh cache.
    internal void Prepare(Camera camera, int budget = MaximumBuildsPerFrame,bool initial=false)
    {
        long start = Stopwatch.GetTimestamp();
        _frustum.Matrix = camera.View * camera.Projection(_device.Viewport.AspectRatio);
        if (_region.Update(camera.Position, _device.Viewport.AspectRatio))
            foreach (int index in _region.Removed)
            {if(_meshes[index] is ChunkMesh old){_retiring.Enqueue(old);_retiringBytes+=old.BufferBytes;}_meshes[index] = null; _water.Evict(index);}
        for(int i=0;i<MaximumBuildsPerFrame && _retiring.TryDequeue(out var old);i++){_retiringBytes-=old.BufferBytes;old.Dispose();}
        RebuiltChunks = 0;TerrainBuildMilliseconds=WaterBuildMilliseconds=0;
        _world.Lighting.FlushChanges();
        if(_building>=0 && (!_region.Contains(_building) || _world.Chunks[_building].Revision!=_buildRevision))_building=-1;
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
            if(_building>=0 && next!=_building && _meshes[next]?.NeedsBuild==true)_building=-1;
            if(_building>=0)next=_building;
            Chunk nextChunk=_world.Chunks[next];
            if(_building<0 && _meshes[next]!=null && !_meshes[next].NeedsBuild)
            {
                long waterStart=Stopwatch.GetTimestamp();_water.PrepareChunk(next);WaterBuildMilliseconds+=Stopwatch.GetElapsedTime(waterStart).TotalMilliseconds;
                RebuiltChunks++;PreparedChunks++;
                if(Stopwatch.GetElapsedTime(start).TotalMilliseconds>=(initial?BuildBudgetMilliseconds:1))break;
                continue;
            }
            if(_building<0)
            {
                double lightBudget=initial?3:.35;
                if(!_world.Lighting.PreparePage(nextChunk.X,nextChunk.Z,lightBudget))break;
                _building=next;_buildRevision=nextChunk.Revision;_builder.BeginBuild(_world,nextChunk);
            }
            long buildStart=Stopwatch.GetTimestamp();
            float editX=nextChunk.OriginX+8-camera.Position.X,editZ=nextChunk.OriginZ+8-camera.Position.Z;
            double slice=_meshes[next]?.NeedsBuild==true && editX*editX+editZ*editZ<22*22?2:.55;
            long deadline=initial?long.MaxValue:buildStart+(long)(slice*Stopwatch.Frequency/1000);
            bool complete=_builder.AdvanceBuild(deadline);TerrainBuildMilliseconds+=Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
            if(!complete)break;
            _meshes[next] ??= new ChunkMesh(_device, _world.Chunks[next]);
            buildStart=Stopwatch.GetTimestamp();_meshes[next].UploadPrepared(_builder,_buildRevision);TerrainBuildMilliseconds+=Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;_building=-1;
            buildStart=Stopwatch.GetTimestamp();
            _water.PrepareChunk(next);
            WaterBuildMilliseconds+=Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
            RebuiltChunks++; PreparedChunks++;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= (initial?BuildBudgetMilliseconds:1)) break;
        }
        MeshedChunks = GpuChunks = PendingChunks = 0;BufferBytes=_retiringBytes;VertexCount = 0;
        foreach (int index in _region.Indices)
        {
            ChunkMesh mesh = _meshes[index];
            if (mesh == null || mesh.NeedsBuild || _water.NeedsBuild(index)) PendingChunks++;
            if (mesh == null) continue;
            MeshedChunks++; if (mesh.BufferBytes > 0) GpuChunks++;
            BufferBytes += mesh.BufferBytes; VertexCount += mesh.VertexCount;
        }
        PrepareMilliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public WorldRenderer(GraphicsDevice device, TextureAtlas atlas, World world)
    {
        _device = device;
        _world = world;
        _texture=atlas.Texture;_textTexture=atlas.Text;_sky=new SkyRenderer(device,atlas.Texture);
        _particles=new ParticleRenderer(device,atlas.Texture);
        _weather=new WeatherRenderer(device);
        _burning=new BurningRenderer(device,atlas.Texture);
        _items = new DroppedItemRenderer(device, atlas);
        _falling = new FallingBlockRenderer(device, atlas);
        _region = new ChunkRegion(world);
        _water = new WaterRenderer(device, atlas, world, _region);
        _meshes = new ChunkMesh[world.ChunkCount];
        _visible = new ChunkMesh[world.ChunkCount];
        _terrainEffect = atlas.LightingEffect.Clone();
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
        _sky.Draw(camera,_world.Time,_world.Seed,_world.Weather.MotionSeconds);
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
        FlatLighting.Apply(_terrainEffect,_device,_texture,_world,camera,Brightness,.5f);
        foreach (EffectPass pass in _terrainEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            for (int i = 0; i < VisibleChunks; i++)
                _visible[i].Draw();
        }

        bool signs=false;for(int i=0;i<VisibleChunks;i++)signs|=_visible[i].HasText;
        if(signs)
        {
            FlatLighting.Apply(_terrainEffect,_device,_textTexture,_world,camera,Brightness,.5f);
            foreach(var pass in _terrainEffect.CurrentTechnique.Passes)
            {pass.Apply();for(int i=0;i<VisibleChunks;i++)_visible[i].DrawText();}
            // Keep the established terrain binding contract for later entity,
            // water and screenshot passes, including HUD-hidden frames.
            _device.Textures[0]=null;_device.Textures[0]=_texture;
        }

        _items.Draw(camera, items,_world,Brightness);
        _falling.Draw(camera, _world.Gravity, fallingInterpolation,_world,Brightness);
        if (selection is BlockHit hit)
            DrawSelection(hit, camera, miningProgress);
    }

    public void DrawWater(Camera camera) { _burning.Draw(camera,_world);_water.Brightness=Brightness;_water.Draw(camera);_particles.Draw(camera,_world,Brightness);_weather.Draw(camera,_world); }

    private void DrawSelection(BlockHit hit, Camera camera, float miningProgress)
    {
        BoundingBox box = World.Bounds(hit.Cell);
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];int count=_world.BlockBoxes(hit.Cell,boxes);
        _selectionEffect.View = camera.View;
        _selectionEffect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        if(_world.GetBlock(hit.Cell.X,hit.Cell.Y,hit.Cell.Z)==BlockType.GlassPane)
        {
            for(int i=0;i<count;i++)DrawSelectionBox(boxes[i],miningProgress);
            return;
        }
        if(count>0){box=boxes[0];for(int i=1;i<count;i++)box=BoundingBox.CreateMerged(box,boxes[i]);}
        DrawSelectionBox(box,miningProgress);
    }
    private void DrawSelectionBox(BoundingBox box,float miningProgress)
    {
        box.Min -= new Vector3(0.003f);
        box.Max += new Vector3(0.003f);
        box.GetCorners(_corners);
        for (int i = 0; i < OutlineEdges.Length; i++)
            _outline[i] = new VertexPositionColor(_corners[OutlineEdges[i]], miningProgress > 0 ? Color.Orange : new Color(25, 30, 35));

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
        while(_retiring.TryDequeue(out var old))old.Dispose();_retiringBytes=0;
        Array.Clear(_meshes); BufferBytes = VertexCount = 0; GpuChunks = MeshedChunks = 0;
        _terrainEffect.Dispose();
        _selectionEffect.Dispose();
        _items.Dispose();
        _falling.Dispose();
        _water.Dispose();
        _sky.Dispose();
        _particles.Dispose();
        _weather.Dispose();
        _burning.Dispose();
    }
}
