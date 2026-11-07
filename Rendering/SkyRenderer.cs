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
    public SkyRenderer(GraphicsDevice device)
    { _device=device;_effect=new BasicEffect(device){World=Matrix.Identity,VertexColorEnabled=true}; }
    public void Draw(Camera camera,WorldTime time)
    {
        _device.DepthStencilState=DepthStencilState.None;_device.BlendState=BlendState.Opaque;_device.RasterizerState=RasterizerState.CullNone;
        _effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
        Square(0,time.SunDirection,7,new Color(255,238,158));Square(6,time.MoonDirection,5,new Color(203,217,235));
        foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,4);}
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
    public void Dispose()=>_effect.Dispose();
}
