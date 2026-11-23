using System;
using System.Collections.Generic;
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
    private readonly BasicEffect _shadowEffect;
    private readonly Texture2D _shadow;
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private readonly ChunkMeshBuilder _builder = new();
    private readonly Dictionary<BlockType,(VertexPositionColorTexture[] Vertices,float Lift)> _models=new();
    private readonly TextureAtlas _atlas;
    private VertexPositionColorTexture[] _blocks=new VertexPositionColorTexture[256],_sprites=new VertexPositionColorTexture[256],_shadows=new VertexPositionColorTexture[256];
    private DynamicVertexBuffer _buffer;
    internal long BufferBytes => (long)(_buffer?.VertexCount ?? 0) * VertexPositionColorTexture.VertexDeclaration.VertexStride + 16*16*4;
    internal int BlockVertices {get;private set;}
    internal int SpriteVertices {get;private set;}
    internal int ShadowVertices {get;private set;}

    public DroppedItemRenderer(GraphicsDevice device,TextureAtlas atlas)
    {
        _device=device;_atlas=atlas;
        _effect=new AlphaTestEffect(device){World=Matrix.Identity,Texture=atlas.Texture,VertexColorEnabled=true,AlphaFunction=CompareFunction.GreaterEqual,ReferenceAlpha=128};
        _shadow=new Texture2D(device,16,16);
        var pixels=new Color[256];
        for(int y=0;y<16;y++)for(int x=0;x<16;x++)
        {float r=new Vector2((x-7.5f)/7.5f,(y-7.5f)/7.5f).Length();pixels[y*16+x]=new Color(0,0,0,(int)(32*MathF.Pow(Math.Max(0,1-r),.6f)));}
        _shadow.SetData(pixels);
        _shadowEffect=new BasicEffect(device){World=Matrix.Identity,VertexColorEnabled=true,TextureEnabled=true,Texture=_shadow};
    }
    private (VertexPositionColorTexture[] Vertices,float Lift) Model(BlockType block)
    {
        if(_models.TryGetValue(block,out var cached))return cached;
        _builder.AppendInventoryItem(block);
        var model=new VertexPositionColorTexture[_builder.VertexCount];
        Vector3 min=new(float.MaxValue),max=new(float.MinValue);
        for(int i=0;i<model.Length;i++){min=Vector3.Min(min,_builder.Vertices[i].Position);max=Vector3.Max(max,_builder.Vertices[i].Position);}
        Vector3 center=(min+max)*.5f;
        for(int i=0;i<model.Length;i++){model[i]=_builder.Vertices[i];model[i].Position=(model[i].Position-center)*.25f;}
        // Tall items (notably two-block doors) need their visual foot lifted too.
        var result=(model,Math.Max(0,(max.Y-min.Y)*.125f-DroppedItem.Radius));
        _models.Add(block,result);return result;
    }
    private static void Reserve(ref VertexPositionColorTexture[] vertices,int count)
    {if(vertices.Length<count)Array.Resize(ref vertices,Math.Max(count,vertices.Length*2));}
    public void Draw(Camera camera,DroppedItems items,World world=null,float brightness=.5f,float interpolation=1)
    {
        BlockVertices=SpriteVertices=ShadowVertices=0;
        if(items==null || items.Entities.Count==0)return;
        var blend=_device.BlendState;var depth=_device.DepthStencilState;var raster=_device.RasterizerState;var sampler=_device.SamplerStates[0];var texture=_device.Textures[0];
        try
        {
            _frustum.Matrix=camera.View*camera.Projection(_device.Viewport.AspectRatio);
            foreach(var item in items.Entities)
            {
                Vector3 center=item.VisualPosition(interpolation);
                if(!_frustum.Intersects(new BoundingSphere(center,.6f)))continue;
                float rotation=item.RenderPhase(interpolation)*.5f;
                Matrix turn=Matrix.CreateRotationY(rotation);
                float light=world==null?1:BlockLighting.Visibility(world.Lighting.ForRendering(item.Position),brightness);
                if(!Items.IsSprite(item.Item) && Items.TryGetBlock(item.Item,out var block))
                {
                    var model=Model(block);Reserve(ref _blocks,BlockVertices+model.Vertices.Length);center.Y+=model.Lift;
                    foreach(var v in model.Vertices)_blocks[BlockVertices++]=new(Vector3.Transform(v.Position,turn)+center,new Color(v.Color.ToVector3()*light),v.TextureCoordinate);
                }
                else if(Items.IsSprite(item.Item))
                {
                    Reserve(ref _sprites,SpriteVertices+ItemSpriteGeometry.VertexCount);
                    ItemSpriteGeometry.Fill(_sprites,SpriteVertices,center,Vector3.TransformNormal(Vector3.Right,turn),Vector3.Up,item.Item);
                    for(int v=SpriteVertices;v<SpriteVertices+ItemSpriteGeometry.VertexCount;v++)_sprites[v].Color=new Color(new Vector3(light));
                    SpriteVertices+=ItemSpriteGeometry.VertexCount;
                }
                if(world!=null)AppendShadow(world,item.RenderPosition(interpolation));
            }
            _effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
            if(world!=null)UnderwaterView.Fog(_effect,camera,world.Time.Sky);
            _device.BlendState=BlendState.Opaque;_device.DepthStencilState=DepthStencilState.Default;_device.RasterizerState=RasterizerState.CullNone;_device.SamplerStates[0]=SamplerState.PointClamp;
            Batch(_blocks,BlockVertices,_atlas.Texture,_effect);Batch(_sprites,SpriteVertices,_atlas.ItemAtlas,_effect);
            _shadowEffect.View=camera.View;_shadowEffect.Projection=_effect.Projection;
            if(world!=null)UnderwaterView.Fog(_shadowEffect,camera,world.Time.Sky);
            _device.BlendState=BlendState.NonPremultiplied;_device.DepthStencilState=DepthStencilState.DepthRead;
            Batch(_shadows,ShadowVertices,_shadow,_shadowEffect);
        }
        finally
        {_device.BlendState=blend;_device.DepthStencilState=depth;_device.RasterizerState=raster;_device.SamplerStates[0]=sampler;_device.Textures[0]=null;_device.Textures[0]=texture;}
    }
    // Project onto real top faces, clipping at edges/steps. The physical pose,
    // rather than the bobbed render pose, controls ground distance and opacity.
    private void AppendShadow(World world,Vector3 position)
    {
        const float radius=.22f;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(int z=(int)MathF.Floor(position.Z-radius);z<=(int)MathF.Floor(position.Z+radius);z++)
        for(int x=(int)MathF.Floor(position.X-radius);x<=(int)MathF.Floor(position.X+radius);x++)
        for(int y=Math.Min(World.Height-1,(int)MathF.Floor(position.Y));y>=Math.Max(0,(int)MathF.Floor(position.Y)-4);y--)
        {
            var cell=world.GlobalCell(x,y,z);
            if(!world.IsLoaded(cell)||!Blocks.IsSolid(world.GetBlock(x,y,z)))continue;
            int count=world.BlockBoxes(cell,boxes);
            for(int i=0;i<count;i++)
            {
                var b=boxes[i];float height=b.Max.Y,distance=position.Y-DroppedItem.Radius-height;
                if(distance<-.01f || distance>3)continue;
                float left=Math.Max(position.X-radius,b.Min.X),right=Math.Min(position.X+radius,b.Max.X),near=Math.Max(position.Z-radius,b.Min.Z),far=Math.Min(position.Z+radius,b.Max.Z);
                if(left>=right||near>=far)continue;
                Reserve(ref _shadows,ShadowVertices+6);Color tint=new(255,255,255,(int)(255*(1-distance/3)));
                VertexPositionColorTexture V(float px,float pz)=>new(new(px,height+.003f,pz),tint,new((px-position.X+radius)/(2*radius),(pz-position.Z+radius)/(2*radius)));
                var a=V(left,near);var c=V(right,far);_shadows[ShadowVertices++]=a;_shadows[ShadowVertices++]=V(right,near);_shadows[ShadowVertices++]=c;
                _shadows[ShadowVertices++]=a;_shadows[ShadowVertices++]=c;_shadows[ShadowVertices++]=V(left,far);
            }
            // A full top covers this column; partial shapes may expose a lower surface.
            if(Blocks.Get(world.GetBlock(x,y,z)).Shape==BlockShape.Cube)break;
        }
    }
    private void Batch(VertexPositionColorTexture[] vertices,int count,Texture2D texture,Effect effect)
    {
        if(count==0)return;
        if(_buffer==null || _buffer.VertexCount<count)
        {
            int capacity=256;while(capacity<count)capacity*=2;
            _buffer?.Dispose();_buffer=new DynamicVertexBuffer(_device,typeof(VertexPositionColorTexture),capacity,BufferUsage.WriteOnly);
        }
        _device.SetVertexBuffer(_buffer);_buffer.SetData(vertices,0,count,SetDataOptions.Discard);
        if(effect is AlphaTestEffect alpha)alpha.Texture=texture;
        else ((BasicEffect)effect).Texture=texture;
        foreach(var pass in effect.CurrentTechnique.Passes)
        {pass.Apply();_device.Textures[0]=null;_device.Textures[0]=texture;_device.DrawPrimitives(PrimitiveType.TriangleList,0,count/3);}
    }
    public void Dispose(){_buffer?.Dispose();_effect.Dispose();_shadowEffect.Dispose();_shadow.Dispose();}
}
