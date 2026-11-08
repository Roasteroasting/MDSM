using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Worlds;
using MDSM.Inventories;

namespace MDSM.Rendering;

internal sealed class ProjectileRenderer:IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _vertices=new VertexPositionColor[Projectiles.Maximum*18];
    private readonly VertexPositionColorTexture[] _balls=new VertexPositionColorTexture[Projectiles.Maximum*12];
    private readonly AlphaTestEffect _ballEffect;
    private readonly TextureAtlas _atlas;
    public ProjectileRenderer(GraphicsDevice device,TextureAtlas atlas=null)
    {
        _atlas=atlas;_ballEffect=new(device){VertexColorEnabled=true,AlphaFunction=CompareFunction.GreaterEqual,ReferenceAlpha=128,World=Matrix.Identity};
        _device=device;_effect=new BasicEffect(device){VertexColorEnabled=true,World=Matrix.Identity,FogEnabled=true,
            FogColor=WorldRenderer.SkyColor.ToVector3(),FogStart=WorldRenderer.FogStart,FogEnd=WorldRenderer.FogEnd};
    }
    public void Draw(Camera camera,Projectiles projectiles,World world=null,float brightness=.5f)
    {
        int count=0,ballCount=0;
        float light=1;
        if(world!=null)_effect.FogColor=world.Time.Sky.ToVector3();
        for(int i=0;i<projectiles.Arrows.Count;i++)
        {
            var arrow=projectiles.Arrows[i];
            if(Vector3.DistanceSquared(camera.Position,arrow.Position)>WorldRenderer.FogEnd*WorldRenderer.FogEnd)continue;
            light=world==null?1:BlockLighting.Visibility(world.Lighting.Effective(arrow.Position),brightness);
            if(arrow.Kind==ProjectileKind.Snowball)
            {
                ItemSpriteGeometry.Fill(_balls,ballCount,arrow.Position,camera.Right,Vector3.Up,Items.Snowball);
                for(int v=ballCount;v<ballCount+12;v++)_balls[v].Color=new Color(new Vector3(light));ballCount+=12;continue;
            }
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
        if(count==0 && ballCount==0)return;
        BlendState blend=_device.BlendState;DepthStencilState depth=_device.DepthStencilState;RasterizerState raster=_device.RasterizerState;Texture texture=_device.Textures[0];
        try
        {
            _device.BlendState=BlendState.Opaque;_device.DepthStencilState=DepthStencilState.Default;_device.RasterizerState=RasterizerState.CullNone;
            _effect.View=camera.View;_effect.Projection=camera.Projection(_device.Viewport.AspectRatio);
            if(count>0)foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.LineList,_vertices,0,count/2);}
            if(ballCount>0 && _atlas!=null)
            {
                _device.SamplerStates[0]=SamplerState.PointClamp;_ballEffect.Texture=_atlas.ItemAtlas;_ballEffect.View=camera.View;_ballEffect.Projection=_effect.Projection;
                foreach(var pass in _ballEffect.CurrentTechnique.Passes){pass.Apply();_device.Textures[0]=null;_device.Textures[0]=_atlas.ItemAtlas;_device.DrawUserPrimitives(PrimitiveType.TriangleList,_balls,0,ballCount/3);}
            }
        }
        finally{_device.BlendState=blend;_device.DepthStencilState=depth;_device.RasterizerState=raster;_device.Textures[0]=null;_device.Textures[0]=texture;}
        void Line(Vector3 a,Vector3 b,Color color){color=new Color(color.ToVector3()*light);_vertices[count++]=new(a,color);_vertices[count++]=new(b,color);}
    }
    public void Dispose(){_effect.Dispose();_ballEffect.Dispose();}
}
