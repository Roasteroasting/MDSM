using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;
using MDSM.Inventories;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal enum ViewModelKind { Hidden, Cube, Plant, Sprite }
internal readonly record struct ViewModelSelection(ViewModelKind Kind, ItemId Item)
{
    public static ViewModelSelection Select(GameState state, Inventory inventory)
    {
        if (state != GameState.Playing) return new(ViewModelKind.Hidden, default);
        ItemStack stack = inventory.Selected;
        if (!stack.IsEmpty && Items.IsSprite(stack.Item)) return new(ViewModelKind.Sprite, stack.Item);
        if (stack.IsEmpty || !Items.TryGetBlock(stack.Item, out BlockType block)) return new(ViewModelKind.Hidden, default);
        return new(Blocks.Get(block).Shape == BlockShape.Cross ? ViewModelKind.Plant : ViewModelKind.Cube, stack.Item);
    }
}

internal static class ViewModelLayout
{
    // Leave the centered crosshair, hearts, and entire hotbar clear even in small windows.
    public static Rectangle Bounds(Viewport viewport)
    {
        int width = Math.Max(1, (int)Math.Min(viewport.Width * 0.23f, viewport.Height * 0.32f));
        int left = viewport.Width - width - Math.Max(4, viewport.Width / 30);
        bool besideHotbar = left > InventoryLayout.HotbarSlot(viewport, 8).Right + 8;
        int bottom = besideHotbar ? viewport.Height : InventoryLayout.HotbarSlot(viewport, 0).Top - 28;
        int height = Math.Max(1, (int)Math.Min(viewport.Height * 0.42f, bottom - viewport.Height * 0.4f));
        return new Rectangle(viewport.X + left,
            viewport.Y + bottom - height, width, height);
    }
}

internal sealed class ViewModelGeometry
{
    private readonly ChunkMeshBuilder _builder = new();
    private readonly VertexPositionColorTexture[] _sprite = new VertexPositionColorTexture[ItemSpriteGeometry.VertexCount];
    private bool _isSprite;
    public VertexPositionColorTexture[] Vertices => _isSprite ? _sprite : _builder.Vertices;
    public int VertexCount => _isSprite ? _sprite.Length : _builder.VertexCount;
    public void Build(ViewModelSelection selection)
    {
        _builder.Clear();
        _isSprite = selection.Kind == ViewModelKind.Sprite;
        if (selection.Kind == ViewModelKind.Hidden) return;
        if (_isSprite)
        {
            ItemSpriteGeometry.Fill(_sprite, 0, Vector3.Zero, Vector3.Right, Vector3.Up);
        }
        else
        {
            if (!Items.TryGetBlock(selection.Item, out BlockType block)) return;
            _builder.AppendItem(block, Vector3.Zero);
        }
        for (int i = 0; i < VertexCount; i++)
            Vertices[i].Position *= new Vector3(3.5f);
    }
}

// Camera-space geometry, drawn after the world and before the HUD. No world entities or mesh invalidations.
internal sealed class FirstPersonRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly TextureAtlas _atlas; // ContentManager owns all textures.
    private readonly AlphaTestEffect _effect;
    private readonly DynamicVertexBuffer _buffer;
    private readonly ViewModelGeometry _geometry = new();
    private ViewModelSelection _previous = new(ViewModelKind.Hidden, default);

    public FirstPersonRenderer(GraphicsDevice device, TextureAtlas atlas)
    {
        _device = device; _atlas = atlas;
        _effect = new AlphaTestEffect(device)
        {
            VertexColorEnabled = true, View = Matrix.Identity,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128
        };
        _buffer = new DynamicVertexBuffer(device, typeof(VertexPositionColorTexture), 48, BufferUsage.WriteOnly);
    }

    public void Draw(GameState state, Inventory inventory)
    {
        ViewModelSelection selection = ViewModelSelection.Select(state, inventory);
        if (selection.Kind == ViewModelKind.Hidden) return;
        if (selection != _previous)
        {
            _geometry.Build(selection);
            _buffer.SetData(_geometry.Vertices, 0, _geometry.VertexCount, SetDataOptions.Discard);
            _previous = selection;
        }
        Viewport original = _device.Viewport;
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState rasterizer = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0];
        try
        {
            Rectangle bounds = ViewModelLayout.Bounds(original);
            _device.SamplerStates[0] = SamplerState.PointClamp;
            _device.BlendState = BlendState.Opaque;
            _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullCounterClockwise;
            // The world is finished. Clear depth while retaining its color, then draw in camera space.
            _device.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            _device.Viewport = new Viewport(bounds);
            _effect.Texture = _atlas.ItemTexture(selection.Item);
            _effect.Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(50), bounds.Width / (float)bounds.Height, 0.05f, 10);
            _effect.World = Matrix.CreateRotationY(-0.6f) * Matrix.CreateRotationX(0.25f) * Matrix.CreateRotationZ(-0.1f) * Matrix.CreateTranslation(0, 0, -2.2f);
            _device.SetVertexBuffer(_buffer);
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _geometry.VertexCount / 3);
            }
        }
        finally
        {
            _device.Viewport = original;
            _device.BlendState = blend; _device.DepthStencilState = depth;
            _device.RasterizerState = rasterizer; _device.SamplerStates[0] = sampler;
        }
    }
    public void Dispose() { _buffer.Dispose(); _effect.Dispose(); }
}
