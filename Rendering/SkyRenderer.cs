using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class SkyRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _vertices=new VertexPositionColor[12];
    private readonly CuboidModelBuilder _clouds=new(TextureAtlas.AtlasSize,TextureAtlas.AtlasSize);
    private readonly Texture2D _atlas;
    public SkyRenderer(GraphicsDevice device,Texture2D atlas)
    { _device=device;_atlas=atlas;_effect=new BasicEffect(device){World=Matrix.Identity,VertexColorEnabled=true}; }
    public void Draw(Camera camera,WorldTime time,int seed=0,double cloudTime=-1)
    {
        _device.DepthStencilState=DepthStencilState.None;_device.BlendState=BlendState.Opaque;_device.RasterizerState=RasterizerState.CullNone;
        _effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
        Square(0,time.SunDirection,7,new Color(255,238,158));Square(6,time.MoonDirection,5,new Color(203,217,235));
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,4);}
        DrawClouds(camera,time,seed,cloudTime<0?time.Seconds:cloudTime);
        void Square(int offset,Vector3 direction,float radius,Color color)
        {
            direction.Normalize();Vector3 center=camera.Position+direction*120;
            Vector3 right=Vector3.Cross(direction,Vector3.Forward);if(right.LengthSquared()<.001f)right=Vector3.Right;right.Normalize();
            Vector3 up=Vector3.Normalize(Vector3.Cross(right,direction))*radius;right*=radius;
            Vector3 a=center-right-up,b=center+right-up,c=center+right+up,d=center-right+up;
            _vertices[offset]=new(a,color);_vertices[offset+1]=new(b,color);_vertices[offset+2]=new(c,color);
            _vertices[offset+3]=new(a,color);_vertices[offset+4]=new(c,color);_vertices[offset+5]=new(d,color);
        }
    }
    private void DrawClouds(Camera camera,WorldTime time,int seed,double seconds)
    {
        float drift=(float)(seconds*.5);int cx=(int)MathF.Floor((camera.Position.X-drift)/32),cz=(int)MathF.Floor(camera.Position.Z/32);
        _clouds.Clear();Color tint=Color.Lerp(new Color(69,76,96),new Color(242,244,245),time.Daylight/14f);
        for(int z=cz-5;z<=cz+5;z++)for(int x=cx-5;x<=cx+5;x++)
        {
            uint hash=unchecked((uint)(x*73856093^z*19349663^seed));hash^=hash>>13;hash*=1274126177;hash^=hash>>16;
            if(hash%7>2)continue;
            Vector3 min=new(x*32+drift,World.Height+12,z*32),max=min+new Vector3(22+hash%8,3,20+(hash>>8)%10);
            _clouds.Cuboid(min,max,Matrix.Identity,Blocks.Get(BlockType.Snow).SideTile,tint);
        }
        _device.DepthStencilState=DepthStencilState.Default;_effect.World=Matrix.Identity;
        _effect.FogEnabled=true;_effect.FogStart=90;_effect.FogEnd=155;_effect.FogColor=time.Sky.ToVector3();
        _effect.TextureEnabled=true;_effect.Texture=_atlas;_device.SamplerStates[0]=SamplerState.PointClamp;
        _device.Textures[0]=null;_device.Textures[0]=_atlas;
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_clouds.Vertices,0,_clouds.Count/3);}
        _effect.FogEnabled=false;_effect.TextureEnabled=false;
    }
    public void Dispose()=>_effect.Dispose();
}
