using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

// Separate transparent pass after opaque terrain/entities, before view models and UI.
internal sealed class WaterRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly World _world;
    private readonly BasicEffect _effect;
    private readonly List<WaterFace>[] _chunks;
    private readonly int[] _revisions;
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private List<int> _visible = new(), _nextVisible = new();
    private WaterSortKey[] _order = new WaterSortKey[1024];
    private WaterFace[] _faces = new WaterFace[1024];
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[6144];
    private DynamicVertexBuffer _buffer;
    private Vector3 _lastPosition;
    private Matrix _lastViewProjection;
    private int _count;
    private bool _initialized;
    private readonly Texture2D _texture;
    private readonly ChunkRegion _region;
    private readonly bool _ownsRegion;
    private int _regionVersion = -1;
    private bool _geometryChanged;
    internal int CachedChunks { get; private set; }
    internal int CachedFaces { get; private set; }
    internal long BufferBytes => (long)(_buffer?.VertexCount ?? 0) * VertexPositionColorTexture.VertexDeclaration.VertexStride;
    internal int DrawnVertices => _count * 6;
    internal bool NeedsBuild(int index) => _revisions[index] != _world.Chunks[index].WaterRevision;
    internal void Evict(int index)
    {
        if (_chunks[index] != null) { CachedChunks--; CachedFaces -= _chunks[index].Count; }
        _chunks[index] = null; _revisions[index] = -1; _geometryChanged = true;
    }
    internal void PrepareChunk(int index)
    {
        if (!NeedsBuild(index)) return;
        Chunk chunk = _world.Chunks[index]; _revisions[index] = chunk.WaterRevision;
        if (chunk.WaterCount == 0) { Evict(index); _revisions[index] = chunk.WaterRevision; return; }
        if (_chunks[index] == null) { _chunks[index] = new List<WaterFace>(); CachedChunks++; }
        CachedFaces -= _chunks[index].Count;
        WaterMeshBuilder.Build(_world, chunk, _chunks[index]);
        CachedFaces += _chunks[index].Count; _geometryChanged = true;
    }

    public WaterRenderer(GraphicsDevice device, TextureAtlas atlas, World world, ChunkRegion region = null)
    {
        _device = device; _world = world; _texture = atlas.Water;
        _ownsRegion = region == null; _region = region ?? new ChunkRegion(world);
        _chunks = new List<WaterFace>[world.ChunkCount]; _revisions = new int[world.ChunkCount];
        Array.Fill(_revisions, -1);
        _effect = new BasicEffect(device) { World = Matrix.Identity, TextureEnabled = true, VertexColorEnabled = true,
            Texture = _texture, FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = WorldRenderer.FogStart, FogEnd = WorldRenderer.FogEnd };
    }

    public void Draw(Camera camera)
    {
        Matrix projection = camera.Projection(_device.Viewport.AspectRatio), viewProjection = camera.View * projection;
        if (_ownsRegion)
        {
            if (_region.Update(camera.Position, _device.Viewport.AspectRatio)) foreach (int index in _region.Removed) Evict(index);
            // Standalone water checks use the same bounded region. Gameplay shares terrain's upload budget.
            foreach (int index in _region.Indices) PrepareChunk(index);
        }
        bool changed = !_initialized || _geometryChanged || _regionVersion != _region.Version
            || camera.Position != _lastPosition || viewProjection != _lastViewProjection;
        if (changed)
        {
            bool faceSetChanged = !_initialized || _geometryChanged || _regionVersion != _region.Version;
            bool upload = faceSetChanged || camera.Position != _lastPosition;
            _frustum.Matrix = viewProjection; _nextVisible.Clear();
            foreach (int i in _region.Indices)
            {
                if (_chunks[i] == null || _chunks[i].Count == 0 || !_frustum.Intersects(_world.Chunks[i].Bounds)) continue;
                int index = _nextVisible.Count;
                if (index >= _visible.Count || _visible[index] != i) faceSetChanged = upload = true;
                _nextVisible.Add(i);
            }
            if (_nextVisible.Count != _visible.Count) faceSetChanged = upload = true;
            // Rotation does not change distance ordering. Reuse the GPU batch while the
            // visible chunk set, camera position and geometry are unchanged.
            if (upload)
            {
                if (faceSetChanged)
                {
                    _count = 0;
                    foreach (int index in _nextVisible) foreach (WaterFace face in _chunks[index])
                    {
                        if (_count == _faces.Length) Array.Resize(ref _faces, _faces.Length * 2);
                        _faces[_count++] = face;
                    }
                }
                // Sort compact keys, computing each distance once rather than comparing and
                // repeatedly copying 52-byte faces/averaging four corners during every comparison.
                if (_order.Length < _count) Array.Resize(ref _order, _faces.Length);
                for (int i = 0; i < _count; i++)
                {
                    int index = faceSetChanged ? i : _order[i].Index;
                    _order[i] = new WaterSortKey(index, Vector3.DistanceSquared(_faces[index].Center, camera.Position));
                }
                if (faceSetChanged) Array.Sort(_order, 0, _count);
                else UpdateOrder(_order, _count);
                if (_vertices.Length < _count * 6) Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _count * 6));
                for (int i = 0; i < _count; i++) _faces[_order[i].Index].CopyTo(_vertices, i * 6);
                if (_count > 0)
                {
                    if (_buffer == null || _buffer.VertexCount < _count * 6)
                    {
                        _buffer?.Dispose(); _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), _vertices.Length, BufferUsage.WriteOnly);
                    }
                    _buffer.SetData(_vertices, 0, _count * 6, SetDataOptions.Discard);
                }
            }
            (_visible, _nextVisible) = (_nextVisible, _visible);
            _initialized = true; _geometryChanged = false; _regionVersion = _region.Version;
            _lastPosition = camera.Position; _lastViewProjection = viewProjection;
        }
        if (_count == 0) return;
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState raster = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0]; Texture texture = _device.Textures[0];
        try
        {
            _device.BlendState = BlendState.NonPremultiplied;
            _device.DepthStencilState = DepthStencilState.DepthRead;
            _device.RasterizerState = RasterizerState.CullNone;
            _device.SamplerStates[0] = SamplerState.PointClamp;
            _device.Textures[0] = _texture;
            _effect.View = camera.View; _effect.Projection = projection;
            _device.SetVertexBuffer(_buffer);
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            { pass.Apply(); _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _count * 2); }
        }
        finally
        {
            _device.BlendState = blend; _device.DepthStencilState = depth; _device.RasterizerState = raster; _device.SamplerStates[0] = sampler;
            _device.Textures[0] = null; _device.Textures[0] = texture; // Invalidate DesktopGL's cached binding, including after readback.
        }
    }
    internal readonly record struct WaterSortKey(int Index, float Distance) : IComparable<WaterSortKey>
    {
        public int CompareTo(WaterSortKey other)
        {
            int order = other.Distance.CompareTo(Distance);
            return order != 0 ? order : Index.CompareTo(other.Index);
        }
    }
    // Walking changes an already sorted order slightly. Repair it with bounded work;
    // fall back to the normal sort after large motion, never quadratic insertion sorting.
    internal static void UpdateOrder(WaterSortKey[] order, int count)
    {
        int moves = 0, limit = count * 4;
        for (int i = 1; i < count; i++)
        {
            WaterSortKey value = order[i]; int j = i - 1;
            while (j >= 0 && value.CompareTo(order[j]) < 0)
            {
                order[j + 1] = order[j]; j--;
                if (++moves > limit)
                {
                    order[j + 1] = value; Array.Sort(order, 0, count); return;
                }
            }
            order[j + 1] = value;
        }
    }
    public void Dispose()
    {
        _buffer?.Dispose(); _buffer = null; _effect.Dispose(); Array.Clear(_chunks);
        _faces = Array.Empty<WaterFace>(); _vertices = Array.Empty<VertexPositionColorTexture>();
        _order = Array.Empty<WaterSortKey>(); _visible.Clear(); _nextVisible.Clear();
        _count = CachedChunks = CachedFaces = 0;
    }
}
