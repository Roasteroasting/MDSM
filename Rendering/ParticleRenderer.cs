using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class ParticleRenderer:IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D _texture;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColorTexture[] _vertices=new VertexPositionColorTexture[BlockParticles.Capacity*6];
    public ParticleRenderer(GraphicsDevice device,Texture2D texture)
    {_device=device;_texture=texture;_effect=new(device){TextureEnabled=true,VertexColorEnabled=true};}
    public void Draw(Camera camera,World world,float brightness)
    {
        int count=0;Vector3 right=camera.Right,up=Vector3.Normalize(Vector3.Cross(right,camera.Forward));
        foreach(var p in world.Particles.Active)
        {
            if(p.Life<=0||Vector3.DistanceSquared(p.Position,camera.Position)>48*48)continue;
            Color color=new(new Vector3(BlockLighting.Visibility(world.Lighting.Effective(p.Position),brightness)));
            Rectangle tile=TextureAtlas.Source(p.Tile);float u=(tile.X+p.Patch%8*2+.5f)/256f,v=(tile.Y+p.Patch/8*2+.5f)/256f;
            Vector3 r=right*p.Size,h=up*p.Size;
            var a=new VertexPositionColorTexture(p.Position-r-h,color,new(u,v+1/256f));
            var b=new VertexPositionColorTexture(p.Position+r-h,color,new(u+1/256f,v+1/256f));
            var c=new VertexPositionColorTexture(p.Position+r+h,color,new(u+1/256f,v));
            var d=new VertexPositionColorTexture(p.Position-r+h,color,new(u,v));
            _vertices[count++]=a;_vertices[count++]=b;_vertices[count++]=c;_vertices[count++]=a;_vertices[count++]=c;_vertices[count++]=d;
        }
        if(count==0)return;
        _device.BlendState=BlendState.AlphaBlend;_device.DepthStencilState=DepthStencilState.DepthRead;
        _device.RasterizerState=RasterizerState.CullNone;_device.SamplerStates[0]=SamplerState.PointClamp;
        _effect.World=Matrix.Identity;_effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);_effect.Texture=_texture;
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.Textures[0]=_texture;_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,count/3);}
    }
    public void Dispose()=>_effect.Dispose();
}
