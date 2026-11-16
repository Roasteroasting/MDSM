using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;
using MDSM.Inventories;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal enum ViewModelKind { Hidden, Arm, Cube, Plant, Sprite }
internal readonly record struct ViewModelSelection(ViewModelKind Kind, ItemId Item)
{
    public static ViewModelSelection Select(GameState state, Inventory inventory)
    {
        if (state != GameState.Playing) return new(ViewModelKind.Hidden, default);
        ItemStack stack = inventory.Selected;
        if(stack.IsEmpty)return new(ViewModelKind.Arm,default);
        if (!stack.IsEmpty && Items.IsSprite(stack.Item) && (!Items.TryGetBlock(stack.Item,out var spriteBlock) || spriteBlock==BlockType.Bed)) return new(ViewModelKind.Sprite, stack.Item);
        if (stack.IsEmpty || !Items.TryGetBlock(stack.Item, out BlockType block)) return new(ViewModelKind.Hidden, default);
        return new(Blocks.Get(block).Shape is BlockShape.Cross or BlockShape.Torch ? ViewModelKind.Plant : ViewModelKind.Cube, stack.Item);
    }
}

internal static class ViewModelLayout
{
    // Items share the camera projection; HUD elements are composed afterward.
    // The empty arm retains its separately framed presentation.
    public static Rectangle Bounds(Viewport viewport,bool arm=false)
    {
        if(!arm)return viewport.Bounds;
        if(arm&&viewport.Width>=640)
        {
            int armWidth=(int)Math.Min(viewport.Width*.36f,viewport.Height*.66f),armHeight=(int)(viewport.Height*.42f);
            return new(viewport.X+viewport.Width-armWidth,viewport.Y+viewport.Height-armHeight,armWidth,armHeight);
        }
        int width = Math.Max(1, (int)Math.Min(viewport.Width * 0.23f, viewport.Height * 0.32f));
        int left = viewport.Width - width - Math.Max(4, viewport.Width / 30);
        bool besideHotbar = left > InventoryLayout.HotbarSlot(viewport, 8).Right + 8;
        int bottom = besideHotbar ? viewport.Height : InventoryLayout.HotbarSlot(viewport, 0).Top - 28;
        int height = Math.Max(1, (int)Math.Min(viewport.Height * 0.42f, bottom - viewport.Height * 0.4f));
        // The empty arm reaches the right edge, retaining its vertical anchor.
        return new Rectangle(viewport.X + (arm?viewport.Width-width:left),
            viewport.Y + bottom - height, width, height);
    }
}

internal sealed class ViewModelGeometry
{
    private readonly ChunkMeshBuilder _builder = new();
    private readonly VertexPositionColorTexture[] _sprite = new VertexPositionColorTexture[ItemSpriteGeometry.VertexCount];
    private bool _isSprite;
    private readonly CuboidModelBuilder _arm=new(4,12);
    private bool _isArm;
    public VertexPositionColorTexture[] Vertices => _isArm?_arm.Vertices:_isSprite ? _sprite : _builder.Vertices;
    public int VertexCount => _isArm?_arm.Count:_isSprite ? _sprite.Length : _builder.VertexCount;
    public void Build(ViewModelSelection selection)
    {
        _builder.Clear();
        _isArm=selection.Kind==ViewModelKind.Arm;
        if(_isArm)
        {
            _arm.Clear();
            // A single 4x12x4-pixel rectangular arm, with a short sleeve at its
            // shoulder end. Shared dimensions avoid seams or cube-shaped hands.
            _arm.Cuboid(new(-.16f,-.65f,-.16f),new(.16f,-.32f,.16f),Matrix.Identity,0,new Color(55,138,153));
            _arm.Cuboid(new(-.16f,-.32f,-.16f),new(.16f,.45f,.16f),Matrix.Identity,0,new Color(184,139,108));
            return;
        }
        _isSprite = selection.Kind == ViewModelKind.Sprite;
        if (selection.Kind == ViewModelKind.Hidden) return;
        if (_isSprite)
        {
            ItemSpriteGeometry.Fill(_sprite, 0, Vector3.Zero, Vector3.Right, Vector3.Up,selection.Item);
        }
        else
        {
            if (!Items.TryGetBlock(selection.Item, out BlockType block)) return;
            if(block==BlockType.Torch)
            {
                _builder.AppendInventoryItem(block);
                for(int i=0;i<_builder.VertexCount;i++)_builder.Vertices[i].Position=(_builder.Vertices[i].Position-Vector3.One*.5f)*.25f;
            }
            else _builder.AppendItem(block, Vector3.Zero);
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
    private readonly Texture2D _armTexture;
    private DynamicVertexBuffer _buffer;
    private readonly ViewModelGeometry _geometry = new();
    private readonly HeldSpriteMeshes _sprites;
    private int _drawCount;
    private Matrix? _itemFacing;
    private ViewModelSelection _previous = new(ViewModelKind.Hidden, default);

    public FirstPersonRenderer(GraphicsDevice device, TextureAtlas atlas)
    {
        _device = device; _atlas = atlas;
        _sprites=new HeldSpriteMeshes(device,atlas.ItemAtlas);
        _armTexture=new Texture2D(device,4,12);
        var skin=new Color[48];for(int i=0;i<skin.Length;i++){uint hash=unchecked((uint)i*747796405u+2891336453u);hash=(hash^(hash>>16))*2246822519u;byte tone=(byte)(245+hash%11);skin[i]=new Color(tone,tone,tone);}_armTexture.SetData(skin);
        _effect = new AlphaTestEffect(device)
        {
            VertexColorEnabled = true, View = Matrix.Identity,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128
        };
        _buffer = new DynamicVertexBuffer(device, typeof(VertexPositionColorTexture), 48, BufferUsage.WriteOnly);
    }

    public void Draw(GameState state, Inventory inventory,float light=1,Matrix? swing=null,Matrix? walkingBob=null,Matrix? equip=null)
    {
        ViewModelSelection selection = ViewModelSelection.Select(state, inventory);
        if (selection.Kind == ViewModelKind.Hidden) return;
        if (selection != _previous)
        {
            _geometry.Build(selection);
            var vertices=selection.Kind==ViewModelKind.Sprite?_sprites.Get(selection.Item):_geometry.Vertices;
            _drawCount=selection.Kind==ViewModelKind.Sprite?vertices.Length:_geometry.VertexCount;
            _itemFacing=selection.Kind==ViewModelKind.Sprite&&!Items.TryGetBlock(selection.Item,out _)
                ?HeldItemPose.SpriteFacing(_sprites.FacingVertices(selection.Item),HeldItemPose.World(selection,Matrix.Identity,Matrix.Identity)):null;
            if (_buffer.VertexCount < _drawCount)
            {
                int capacity = _buffer.VertexCount;
                while (capacity < _drawCount) capacity *= 2;
                var replacement = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
                _buffer.Dispose();
                _buffer = replacement;
            }
            _buffer.SetData(vertices, 0, _drawCount, SetDataOptions.Discard);
            _previous = selection;
        }
        Viewport original = _device.Viewport;
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState rasterizer = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0];
        try
        {
            Rectangle bounds = ViewModelLayout.Bounds(original,selection.Kind==ViewModelKind.Arm);
            _device.SamplerStates[0] = SamplerState.PointClamp;
            _device.BlendState = BlendState.Opaque;
            _device.DepthStencilState = DepthStencilState.Default;
            // Reflected held models reverse triangle winding; the arm is unchanged.
            _device.RasterizerState = selection.Kind==ViewModelKind.Sprite?RasterizerState.CullNone
                :selection.Kind==ViewModelKind.Arm?RasterizerState.CullCounterClockwise:RasterizerState.CullClockwise;
            // The world is finished. Clear depth while retaining its color, then draw in camera space.
            _device.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            _device.Viewport = new Viewport(bounds);
            _effect.Texture = selection.Kind==ViewModelKind.Arm?_armTexture:selection.Kind==ViewModelKind.Sprite?_atlas.ItemTexture(selection.Item):_atlas.Texture;
            // renderHand establishes a fresh camera-space bob before the item
            // renderer adds its own transforms. Row-vector order is model * bob,
            // not bob * model; never reuse the world camera View here.
            _effect.View = walkingBob??Matrix.Identity;
            _effect.DiffuseColor=new Vector3(light);
            _effect.Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(selection.Kind==ViewModelKind.Arm?50:70), bounds.Width / (float)bounds.Height, 0.05f, 10);
            _effect.World = selection.Kind!=ViewModelKind.Arm
                ? HeldItemPose.World(selection,swing??Matrix.Identity,equip??Matrix.Identity,_itemFacing)
                : Matrix.CreateRotationY(-0.6f) * Matrix.CreateRotationX(-.25f) * Matrix.CreateRotationZ(.45f)
                * Matrix.CreateScale(selection.Kind==ViewModelKind.Arm?(original.Width>=640?3.2f:1.65f):1)
                * (swing??Matrix.Identity) * (equip??Matrix.Identity) * Matrix.CreateTranslation(selection.Kind==ViewModelKind.Arm?.25f:0,selection.Kind==ViewModelKind.Arm?(original.Width>=640?-.95f:-.35f):0, -2.2f);
            _device.SetVertexBuffer(_buffer);
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.Textures[0]=_effect.Texture;
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _drawCount / 3);
            }
        }
        finally
        {
            _device.Viewport = original;
            _device.BlendState = blend; _device.DepthStencilState = depth;
            _device.RasterizerState = rasterizer; _device.SamplerStates[0] = sampler;
        }
    }
    public void Dispose() { _buffer.Dispose(); _effect.Dispose(); _armTexture.Dispose(); }
}
