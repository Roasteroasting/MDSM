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
    private readonly CuboidModelBuilder _clouds=new(16,16);
    private readonly Texture2D _snow;
    private const int CloudRadius=22,CloudWidth=CloudRadius*2+1;
    private readonly bool[] _cloudMask=new bool[CloudWidth*CloudWidth];
    private long _cloudX=long.MinValue,_cloudZ=long.MinValue;
    private int _cloudSeed;
    public SkyRenderer(GraphicsDevice device,Texture2D atlas)
    {
        _device=device;_effect=new BasicEffect(device){World=Matrix.Identity,VertexColorEnabled=true};
        // Isolate existing pixels once so large cloud faces can repeat the snow
        // tile with point wrapping, without sampling neighboring atlas artwork.
        var pixels=new Color[256];atlas.GetData(0,TextureAtlas.Source(Blocks.Get(BlockType.Snow).SideTile),pixels,0,256);
        _snow=new Texture2D(device,16,16);_snow.SetData(pixels);
        device.Textures[0]=null;device.Textures[0]=atlas;
    }
    public void Draw(Camera camera,WorldTime time,int seed=0,double cloudTime=-1,long originX=0,long originZ=0)
    {
        _device.DepthStencilState=DepthStencilState.None;_device.BlendState=BlendState.Opaque;_device.RasterizerState=RasterizerState.CullNone;
        _effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
        float cloudCover=time.RainCover;
        Color sky=time.Sky;
        Square(0,time.SunDirection,7,Color.Lerp(new Color(255,238,158),sky,cloudCover));
        Square(6,time.MoonDirection,5,Color.Lerp(new Color(203,217,235),sky,cloudCover));
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,4);}
        DrawClouds(camera,time,seed,cloudTime<0?time.Seconds:cloudTime,originX,originZ);
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
    private void DrawClouds(Camera camera,WorldTime time,int seed,double seconds,long originX,long originZ)
    {
        double drift=seconds*.5;long cx=(long)Math.Floor((originX+(double)camera.Position.X-drift)/CloudPattern.CellSize),cz=(long)Math.Floor((originZ+(double)camera.Position.Z)/CloudPattern.CellSize);
        Color tint=Color.Lerp(new Color(69,76,96),new Color(242,244,245),time.Daylight/14f);
        tint=Color.Lerp(tint,Color.Lerp(new Color(40,44,54),new Color(157,164,173),time.Daylight/14f),time.RainCover);
        if(cx!=_cloudX || cz!=_cloudZ || seed!=_cloudSeed)
        {
            _cloudX=cx;_cloudZ=cz;_cloudSeed=seed;_clouds.Clear();
            for(int z=0;z<CloudWidth;z++)for(int x=0;x<CloudWidth;x++)_cloudMask[z*CloudWidth+x]=CloudPattern.Occupied(cx-CloudRadius+x,cz-CloudRadius+z,seed);
            // Merge occupied cells into rectangles. Cost and mesh size depend only
            // on the fogged viewing region, never on explored terrain or chunks.
            for(int z=0;z<CloudWidth;z++)for(int x=0;x<CloudWidth;x++)
            {
                if(!_cloudMask[z*CloudWidth+x])continue;
                int width=1;while(x+width<CloudWidth&&_cloudMask[z*CloudWidth+x+width])width++;
                int height=1;
                for(;z+height<CloudWidth;height++)
                {bool full=true;for(int n=0;n<width;n++)full&=_cloudMask[(z+height)*CloudWidth+x+n];if(!full)break;}
                for(int dz=0;dz<height;dz++)for(int dx=0;dx<width;dx++)_cloudMask[(z+dz)*CloudWidth+x+dx]=false;
                Vector3 min=new(x*CloudPattern.CellSize,World.Height+12,z*CloudPattern.CellSize);
                int first=_clouds.Count;
                _clouds.Cuboid(min,min+new Vector3(width*CloudPattern.CellSize,3,height*CloudPattern.CellSize),Matrix.Identity,0,Color.White);
                for(int v=first;v<_clouds.Count;v+=6)
                {
                    var normal=Vector3.Cross(_clouds.Vertices[v+1].Position-_clouds.Vertices[v].Position,_clouds.Vertices[v+2].Position-_clouds.Vertices[v].Position);
                    for(int n=0;n<6;n++)
                    {
                        Vector3 p=_clouds.Vertices[v+n].Position;
                        _clouds.Vertices[v+n].TextureCoordinate=Math.Abs(normal.Y)>.01f?new(p.X/8,p.Z/8):new((Math.Abs(normal.X)>.01f?p.Z:p.X)/8,(p.Y-World.Height-12)/8);
                    }
                }
            }
        }
        _device.DepthStencilState=DepthStencilState.Default;
        _effect.World=Matrix.CreateTranslation((float)((cx-CloudRadius)*CloudPattern.CellSize-originX+drift),0,(cz-CloudRadius)*CloudPattern.CellSize-originZ);
        _effect.DiffuseColor=tint.ToVector3();
        _effect.FogEnabled=true;_effect.FogStart=90;_effect.FogEnd=155;_effect.FogColor=time.Sky.ToVector3();
        _effect.TextureEnabled=true;_effect.Texture=_snow;_device.SamplerStates[0]=SamplerState.PointWrap;
        _device.Textures[0]=null;_device.Textures[0]=_snow;
        if(_clouds.Count>0)foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_clouds.Vertices,0,_clouds.Count/3);}
        _effect.FogEnabled=false;_effect.TextureEnabled=false;_effect.World=Matrix.Identity;_effect.DiffuseColor=Vector3.One;
    }
    public void Dispose(){_effect.Dispose();_snow.Dispose();}
}
