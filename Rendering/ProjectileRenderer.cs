using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;

namespace MDSM.Rendering;

internal sealed class ProjectileRenderer:IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _vertices=new VertexPositionColor[Projectiles.Maximum*18];
    public ProjectileRenderer(GraphicsDevice device)
    {
        _device=device;_effect=new BasicEffect(device){VertexColorEnabled=true,World=Matrix.Identity,FogEnabled=true,
            FogColor=WorldRenderer.SkyColor.ToVector3(),FogStart=WorldRenderer.FogStart,FogEnd=WorldRenderer.FogEnd};
    }
    public void Draw(Camera camera,Projectiles projectiles)
    {
        int count=0;
        for(int i=0;i<projectiles.Arrows.Count;i++)
        {
            var arrow=projectiles.Arrows[i];
            if(Vector3.DistanceSquared(camera.Position,arrow.Position)>WorldRenderer.FogEnd*WorldRenderer.FogEnd)continue;
            Vector3 forward=arrow.Velocity.LengthSquared()>.00001f?Vector3.Normalize(arrow.Velocity):Vector3.Down,side=Vector3.Cross(forward,Vector3.Up);
            if(side.LengthSquared()<.001f)side=Vector3.Right;else side.Normalize();Vector3 up=Vector3.Cross(side,forward);
            Vector3 tail=arrow.Position-forward*.6f;
            Line(tail,arrow.Position,Color.SaddleBrown);
            Line(arrow.Position-forward*.13f+side*.06f,arrow.Position,Color.Silver);
            Line(arrow.Position-forward*.13f-side*.06f,arrow.Position,Color.Silver);
            Line(tail+side*.09f,tail+forward*.18f,Color.White);
            Line(tail-side*.09f,tail+forward*.18f,Color.White);
            Line(tail+up*.09f,tail+forward*.18f,Color.White);
            Line(tail-up*.09f,tail+forward*.18f,Color.White);
        }
        if(count==0)return;
        BlendState blend=_device.BlendState;DepthStencilState depth=_device.DepthStencilState;RasterizerState raster=_device.RasterizerState;Texture texture=_device.Textures[0];
        try
        {
            _device.BlendState=BlendState.Opaque;_device.DepthStencilState=DepthStencilState.Default;_device.RasterizerState=RasterizerState.CullNone;
            _effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
            foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.LineList,_vertices,0,count/2);}
        }
        finally{_device.BlendState=blend;_device.DepthStencilState=depth;_device.RasterizerState=raster;_device.Textures[0]=null;_device.Textures[0]=texture;}
        void Line(Vector3 a,Vector3 b,Color color){_vertices[count++]=new(a,color);_vertices[count++]=new(b,color);}
    }
    public void Dispose()=>_effect.Dispose();
}
