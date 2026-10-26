using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

// Two cached unit cubes; each frame only translates their vertices into one reusable batch.
internal sealed class FallingBlockRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly AlphaTestEffect _effect;
    private readonly VertexPositionColorTexture[] _sand, _gravel;
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[256];
    private DynamicVertexBuffer _buffer;

    public FallingBlockRenderer(GraphicsDevice device, TextureAtlas atlas)
    {
        _device = device;
        var builder = new ChunkMeshBuilder();
        builder.AppendCube(BlockType.Sand, Vector3.Zero); _sand = builder.Vertices.AsSpan(0, builder.VertexCount).ToArray();
        builder.Clear(); builder.AppendCube(BlockType.Gravel, Vector3.Zero); _gravel = builder.Vertices.AsSpan(0, builder.VertexCount).ToArray();
        _effect = new AlphaTestEffect(device) { World = Matrix.Identity, Texture = atlas.Texture, VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128,
            FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = 80, FogEnd = 220 };
    }
    public void Draw(Camera camera, FallingBlockSimulation simulation, float fraction = 1)
    {
        if (simulation.Active.Count == 0) return;
        _effect.View = camera.View; _effect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        _frustum.Matrix = _effect.View * _effect.Projection;
        int count = 0;
        for (int i = 0; i < simulation.Active.Count; i++)
        {
            FallingBlock block = simulation.Active[i];
            Vector3 position = block.RenderPosition(fraction);
            if (!_frustum.Intersects(new BoundingBox(position, position + Vector3.One))) continue;
            VertexPositionColorTexture[] template = block.Block == BlockType.Sand ? _sand : _gravel;
            if (count + template.Length > _vertices.Length) Array.Resize(ref _vertices, _vertices.Length * 2);
            foreach (VertexPositionColorTexture vertex in template)
                _vertices[count++] = new VertexPositionColorTexture(vertex.Position + position, vertex.Color, vertex.TextureCoordinate);
        }
        if (count == 0) return;
        if (_buffer == null || _buffer.VertexCount < count)
        {
            _buffer?.Dispose(); _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), _vertices.Length, BufferUsage.WriteOnly);
        }
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState raster = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0]; Texture texture = _device.Textures[0];
        try
        {
            _device.BlendState = BlendState.Opaque; _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullCounterClockwise; _device.SamplerStates[0] = SamplerState.PointClamp;
            _buffer.SetData(_vertices, 0, count, SetDataOptions.Discard); _device.SetVertexBuffer(_buffer);
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            { pass.Apply(); _device.DrawPrimitives(PrimitiveType.TriangleList, 0, count / 3); }
        }
        finally
        {
            _device.BlendState = blend; _device.DepthStencilState = depth; _device.RasterizerState = raster; _device.SamplerStates[0] = sampler;
            _device.Textures[0] = null; _device.Textures[0] = texture;
        }
    }
    public void Dispose() { _buffer?.Dispose(); _effect.Dispose(); }
}
