using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class BurningRenderer:IDisposable
{
    private readonly GraphicsDevice _device;private readonly Texture2D _atlas;private readonly AlphaTestEffect _effect;
    private readonly VertexPositionColorTexture[] _vertices=new VertexPositionColorTexture[128*24];private int _count;
    public BurningRenderer(GraphicsDevice device,Texture2D atlas)
    {_device=device;_atlas=atlas;_effect=new(device){VertexColorEnabled=true,AlphaFunction=CompareFunction.GreaterEqual,ReferenceAlpha=128,World=Matrix.Identity};}
    public void Draw(Camera camera,World world)
    {
        _count=0;
        if(world.LiveMobs!=null)foreach(var mob in world.LiveMobs.Entities)Append(mob);
        if(world.LiveHostiles!=null)foreach(var mob in world.LiveHostiles.Entities)Append(mob);
        if(_count==0)return;
        _device.BlendState=BlendState.Opaque;_device.DepthStencilState=DepthStencilState.Default;_device.RasterizerState=RasterizerState.CullNone;_device.SamplerStates[0]=SamplerState.PointClamp;
        _effect.Texture=_atlas;_effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.Textures[0]=null;_device.Textures[0]=_atlas;_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,_count/3);}
        void Append(PassiveMob mob)
        {
            if(_count+24>_vertices.Length||mob.Burning.Remaining<=0||Vector3.DistanceSquared(camera.Position,mob.Position)>64*64)return;
            var box=mob.Bounds;Vector3 center=(box.Min+box.Max)*.5f;float width=(box.Max.X-box.Min.X)*.65f,height=(box.Max.Y-box.Min.Y)*.6f;
            Quad(center+new Vector3(-width,-height,-width),center+new Vector3(width,-height,width),center+new Vector3(width,height,width),center+new Vector3(-width,height,-width));
            Quad(center+new Vector3(width,-height,-width),center+new Vector3(-width,-height,width),center+new Vector3(-width,height,width),center+new Vector3(width,height,-width));
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var va=new VertexPositionColorTexture(a,Color.White,TextureAtlas.Uv(62,0,1));var vb=new VertexPositionColorTexture(b,Color.White,TextureAtlas.Uv(62,1,1));
            var vc=new VertexPositionColorTexture(c,Color.White,TextureAtlas.Uv(62,1,0));var vd=new VertexPositionColorTexture(d,Color.White,TextureAtlas.Uv(62,0,0));
            _vertices[_count++]=va;_vertices[_count++]=vb;_vertices[_count++]=vc;_vertices[_count++]=va;_vertices[_count++]=vc;_vertices[_count++]=vd;
        }
    }
    public void Dispose()=>_effect.Dispose();
}
