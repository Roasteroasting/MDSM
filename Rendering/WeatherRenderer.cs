using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class WeatherRenderer:IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _vertices=new VertexPositionColor[17*17*6];
    public WeatherRenderer(GraphicsDevice device){_device=device;_effect=new(device){VertexColorEnabled=true};}
    public void Draw(Camera camera,World world)
    {
        if(!world.Weather.Active)return;int count=0,cx=(int)camera.Position.X,cz=(int)camera.Position.Z;
        float time=(float)(world.Weather.MotionSeconds%100000);
        for(int z=cz-8;z<=cz+8;z++)for(int x=cx-8;x<=cx+8;x++)
        {
            if(!world.Contains(x,0,z)||!world.Weather.Precipitation(x,z))continue;
            bool snow=world.Climate.At(x,z)==Biome.SnowyForest;
            uint hash=unchecked((uint)(x*73856093^z*19349663));
            float y=camera.Position.Y+7-((time*(snow?1.5f:18)+(hash%997)/71f)%14);
            if(y<=world.SurfaceY(x,z)+1)continue;
            Vector3 center=new(x+.5f+(snow?MathF.Sin(time+hash%13)*.3f:0),y,z+.5f);
            Vector3 r=camera.Right*(snow?.06f:.012f),h=Vector3.Up*(snow?.06f:.5f);
            Color color=snow?new Color(225,233,245,190):new Color(135,171,210,130);
            var a=new VertexPositionColor(center-r-h,color);var b=new VertexPositionColor(center+r-h,color);var c=new VertexPositionColor(center+r+h,color);var d=new VertexPositionColor(center-r+h,color);
            _vertices[count++]=a;_vertices[count++]=b;_vertices[count++]=c;_vertices[count++]=a;_vertices[count++]=c;_vertices[count++]=d;
        }
        if(count==0)return;
        _device.BlendState=BlendState.NonPremultiplied;_device.DepthStencilState=DepthStencilState.DepthRead;_device.RasterizerState=RasterizerState.CullNone;
        _effect.World=Matrix.Identity;_effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,count/3);}
    }
    public void Dispose()=>_effect.Dispose();
}
