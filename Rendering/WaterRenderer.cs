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
    private readonly FaceOrder _order = new();
    private WaterFace[] _faces = new WaterFace[1024];
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[6144];
    private DynamicVertexBuffer _buffer;
    private Vector3 _lastPosition;
    private Matrix _lastViewProjection;
    private int _count;
    private bool _initialized;
    private readonly Texture2D _texture;

    public WaterRenderer(GraphicsDevice device, TextureAtlas atlas, World world)
    {
        _device = device; _world = world; _texture = atlas.Water;
        _chunks = new List<WaterFace>[world.ChunkCount]; _revisions = new int[world.ChunkCount];
        Array.Fill(_revisions, -1);
        _effect = new BasicEffect(device) { World = Matrix.Identity, TextureEnabled = true, VertexColorEnabled = true,
            Texture = _texture, FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = 80, FogEnd = 220 };
    }

    public void Draw(Camera camera)
    {
        Matrix projection = camera.Projection(_device.Viewport.AspectRatio), viewProjection = camera.View * projection;
        bool changed = !_initialized || camera.Position != _lastPosition || viewProjection != _lastViewProjection;
        for (int i = 0; i < _chunks.Length; i++)
        {
            Chunk chunk = _world.Chunks[i];
            if (_revisions[i] == chunk.WaterRevision) continue;
            _revisions[i] = chunk.WaterRevision;
            if (chunk.WaterCount != 0 || _chunks[i] != null)
            {
                _chunks[i] ??= new List<WaterFace>(); WaterMeshBuilder.Build(_world, chunk, _chunks[i]); changed = true;
            }
        }
        if (changed)
        {
            _frustum.Matrix = viewProjection; _count = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                if (_chunks[i] == null || !_frustum.Intersects(_world.Chunks[i].Bounds)) continue;
                foreach (WaterFace face in _chunks[i])
                {
                    if (_count == _faces.Length) Array.Resize(ref _faces, _faces.Length * 2);
                    _faces[_count++] = face;
                }
            }
            // Sort all visible faces, not just chunks, to handle overlapping lakes/waterfalls.
            _order.Position = camera.Position; Array.Sort(_faces, 0, _count, _order);
            if (_vertices.Length < _count * 6) Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _count * 6));
            for (int i = 0; i < _count; i++) _faces[i].CopyTo(_vertices, i * 6);
            if (_count > 0)
            {
                if (_buffer == null || _buffer.VertexCount < _count * 6)
                {
                    _buffer?.Dispose(); _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), _vertices.Length, BufferUsage.WriteOnly);
                }
                _buffer.SetData(_vertices, 0, _count * 6, SetDataOptions.Discard);
            }
            _initialized = true; _lastPosition = camera.Position; _lastViewProjection = viewProjection;
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
    private sealed class FaceOrder : IComparer<WaterFace>
    {
        public Vector3 Position;
        public int Compare(WaterFace a, WaterFace b) => Vector3.DistanceSquared(b.Center, Position).CompareTo(Vector3.DistanceSquared(a.Center, Position));
    }
    public void Dispose() { _buffer?.Dispose(); _effect.Dispose(); }
}
