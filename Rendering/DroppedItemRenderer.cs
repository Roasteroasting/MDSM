using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class DroppedItemRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly AlphaTestEffect _effect;
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private readonly ChunkMeshBuilder _builder = new();
    private readonly TextureAtlas _atlas;
    private VertexPositionColorTexture[] _sprites = new VertexPositionColorTexture[256];
    private DynamicVertexBuffer _buffer;
    internal long BufferBytes => (long)(_buffer?.VertexCount ?? 0) * VertexPositionColorTexture.VertexDeclaration.VertexStride;

    public DroppedItemRenderer(GraphicsDevice device, TextureAtlas atlas)
    {
        _device = device;
        _atlas = atlas;
        _effect = new AlphaTestEffect(device)
        {
            World = Matrix.Identity, Texture = atlas.Texture, VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128,
            FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = WorldRenderer.FogStart, FogEnd = WorldRenderer.FogEnd
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
            if (!_frustum.Intersects(new BoundingSphere(item.Position, DroppedItem.Radius * 2))) continue;
            if (Items.TryGetBlock(item.Item, out BlockType block)) _builder.AppendItem(block, item.Position);
        }
        _effect.View = camera.View;
        _effect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        DrawBatch(_builder.Vertices, _builder.VertexCount, _atlas.Texture);
        for(int group=0;group<4;group++)
        {
            ItemId type=group<3?Items.Sprites[group]:default;
            int spriteCount = 0;
            for (int i = 0; i < items.Entities.Count; i++)
            {
                DroppedItem item = items.Entities[i];
                if ((group<3?item.Item!=type:item.Item.Value<259 || !Items.IsSprite(item.Item)) || !_frustum.Intersects(new BoundingSphere(item.Position, DroppedItem.Radius * 2))) continue;
                if (spriteCount + ItemSpriteGeometry.VertexCount > _sprites.Length) Array.Resize(ref _sprites, _sprites.Length * 2);
                ItemSpriteGeometry.Fill(_sprites, spriteCount, item.Position, camera.Right, Vector3.Up,item.Item);
                spriteCount += ItemSpriteGeometry.VertexCount;
            }
            DrawBatch(_sprites, spriteCount, group<3?_atlas.ItemTexture(type):_atlas.Survival);
        }

        void DrawBatch(VertexPositionColorTexture[] vertices, int count, Texture2D texture)
        {
            if (count == 0) return;
            if (_buffer == null || _buffer.VertexCount < count)
            {
                int capacity = 256; while (capacity < count) capacity *= 2;
                _buffer?.Dispose();
                _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
            }
            _device.SetVertexBuffer(_buffer);
            _buffer.SetData(vertices, 0, count, SetDataOptions.Discard);
            _effect.Texture = texture;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, count / 3);
            }
        }
    }
    public void Dispose() { _buffer?.Dispose(); _effect.Dispose(); }
}
