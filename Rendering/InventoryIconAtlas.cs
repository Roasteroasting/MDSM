using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Rendering;

// One startup bake on the graphics thread; all UI consumers use cached sprites.
internal sealed class InventoryIconAtlas : IDisposable
{
    internal const int IconSize=32;
    internal RenderTarget2D Texture {get;}
    private readonly bool[] _models=new bool[256];
    private readonly Rectangle[] _spriteBounds=new Rectangle[512];
    internal bool IsModel(ItemId item)=>item.Value<256&&_models[item.Value];
    internal static Rectangle ModelSource(ItemId item)=>new(item.Value%16*IconSize,item.Value/16*IconSize,IconSize,IconSize);
    internal Rectangle SpriteSource(ItemId item,Rectangle tile)
        =>item.Value<_spriteBounds.Length&&_spriteBounds[item.Value].Width>0?_spriteBounds[item.Value]:tile;
    internal InventoryIconAtlas(TextureAtlas atlas)
    {
        var device=atlas.Texture.GraphicsDevice;
        // Read each existing atlas once, only at content load. Preserve artwork;
        // alpha bounds center sprites at their original texel scale.
        var terrain=new Color[256*256];var items=new Color[256*256];atlas.Texture.GetData(terrain);atlas.ItemAtlas.GetData(items);
        foreach(var item in Items.Obtainable)
        {
            if(!Items.IsSprite(item)&&Items.TryGetBlock(item,out var block)&&Blocks.Get(block).Shape is not (BlockShape.Cross or BlockShape.Crop or BlockShape.Fire))
            {_models[item.Value]=true;continue;}
            Rectangle tile=atlas.ItemSource(item);var pixels=Items.IsSprite(item)?items:terrain;
            int left=16,top=16,right=-1,bottom=-1;
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)if(pixels[(tile.Y+y)*256+tile.X+x].A>0)
            {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            if(right>=left)_spriteBounds[item.Value]=new(tile.X+left,tile.Y+top,right-left+1,bottom-top+1);
        }
        var targets=device.GetRenderTargets();var viewport=device.Viewport;var scissor=device.ScissorRectangle;
        var blend=device.BlendState;var depth=device.DepthStencilState;var raster=device.RasterizerState;
        var sampler=device.SamplerStates[0];var texture=device.Textures[0];var indices=device.Indices;
        Texture=new RenderTarget2D(device,512,512,false,SurfaceFormat.Color,DepthFormat.Depth24,0,RenderTargetUsage.PreserveContents);
        try
        {
            device.SetRenderTarget(Texture);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Transparent,1,0);
            device.BlendState=BlendState.Opaque;device.DepthStencilState=DepthStencilState.Default;
            device.RasterizerState=RasterizerState.CullNone;device.SamplerStates[0]=SamplerState.PointClamp;
            using var effect=new AlphaTestEffect(device){Texture=atlas.Texture,VertexColorEnabled=true,AlphaFunction=CompareFunction.GreaterEqual,ReferenceAlpha=128};
            effect.View=Matrix.Identity;
            var builder=new ChunkMeshBuilder();
            // Front and right faces, with a classic elevated isometric view.
            Matrix orientation=Matrix.CreateLookAt(new(3,2.45f,-3),Vector3.Zero,Vector3.Up);
            foreach(var item in Items.Obtainable)
            {
                if(!IsModel(item))continue;
                builder.AppendInventoryItem((BlockType)item.Value);
                Vector3 min=new(float.MaxValue),max=new(float.MinValue);
                for(int i=0;i<builder.VertexCount;i++)
                {Vector3 p=Vector3.Transform(builder.Vertices[i].Position,orientation);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
                if(builder.VertexCount==0)throw new InvalidOperationException("Empty block inventory model: "+item);
                float span=Math.Max(max.X-min.X,max.Y-min.Y)*1.12f;
                effect.World=orientation*Matrix.CreateTranslation(-(min+max)*.5f);
                effect.Projection=Matrix.CreateOrthographic(span,span,-10,10);
                var region=ModelSource(item);device.Viewport=new(region.X,region.Y,region.Width,region.Height);
                effect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList,builder.Vertices,0,builder.VertexCount/3);
            }
        }
        catch{Texture.Dispose();throw;}
        finally
        {
            device.SetRenderTargets(targets);device.Viewport=viewport;device.ScissorRectangle=scissor;
            device.BlendState=blend;device.DepthStencilState=depth;device.RasterizerState=raster;device.SamplerStates[0]=sampler;
            device.SetVertexBuffer(null);device.Indices=indices;
            // Force the DesktopGL binding, even when its managed cache appears
            // unchanged after render-target creation/readback.
            device.Textures[0]=null;device.Textures[0]=texture;
        }
    }
    public void Dispose()=>Texture.Dispose();
}
