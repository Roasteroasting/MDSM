using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

internal sealed class DroppedItemRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly AlphaTestEffect _effect;
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private readonly ChunkMeshBuilder _builder = new();
    private DynamicVertexBuffer _buffer;

    public DroppedItemRenderer(GraphicsDevice device, TextureAtlas atlas)
    {
        _device = device;
        _effect = new AlphaTestEffect(device)
        {
            World = Matrix.Identity, Texture = atlas.Texture, VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128,
            FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = 80, FogEnd = 220
        };
    }
    public void Draw(Camera camera, DroppedItems items)
    {
        if (items == null || items.Entities.Count == 0) return;
        _builder.Clear();
        _frustum.Matrix = camera.View * camera.Projection(_device.Viewport.AspectRatio);
        for (int i = 0; i < items.Entities.Count; i++)
        {
            DroppedItem item = items.Entities[i];
            if (_frustum.Intersects(new BoundingSphere(item.Position, DroppedItem.Radius * 2))
                && Items.TryGetBlock(item.Item, out BlockType block))
                _builder.AppendItem(block, item.Position);
        }
        if (_builder.VertexCount == 0) return;
        if (_buffer == null || _buffer.VertexCount < _builder.VertexCount)
        {
            int capacity = 256;
            while (capacity < _builder.VertexCount) capacity *= 2;
            _buffer?.Dispose();
            _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
        }
        _buffer.SetData(_builder.Vertices, 0, _builder.VertexCount, SetDataOptions.Discard);
        _device.SetVertexBuffer(_buffer);
        _effect.View = camera.View;
        _effect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _builder.VertexCount / 3);
        }
    }
    public void Dispose() { _buffer?.Dispose(); _effect.Dispose(); }
}
