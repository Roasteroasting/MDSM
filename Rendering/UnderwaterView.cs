using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;
namespace MDSM.Rendering;
internal sealed class UnderwaterView:IDisposable
{
    internal static readonly Color FogColor=new(5,7,51);
    internal static bool Submerged(World world,Camera camera)=>BreathMeter.HeadInWater(world,Matrix.Invert(camera.View).Translation);
    internal static Vector2 Offset(Camera camera)=>new(camera.Yaw/MathHelper.PiOver2,camera.Pitch/MathHelper.Pi);
    private readonly GraphicsDevice _device;private readonly BasicEffect _effect;private readonly VertexPositionColorTexture[] _vertices=new VertexPositionColorTexture[6];
    internal UnderwaterView(GraphicsDevice device,Texture2D water)
    {_device=device;_effect=new BasicEffect(device){TextureEnabled=true,Texture=water,VertexColorEnabled=true,World=Matrix.Identity,View=Matrix.Identity,Projection=Matrix.Identity};}
    internal static void Fog(BasicEffect effect,Camera camera,Color normal)
    {effect.FogEnabled=true;effect.FogColor=(camera.Underwater?FogColor:normal).ToVector3();effect.FogStart=camera.Underwater?0:WorldRenderer.FogStart;effect.FogEnd=camera.Underwater?24:WorldRenderer.FogEnd;}
    internal static void Fog(AlphaTestEffect effect,Camera camera,Color normal)
    {effect.FogEnabled=true;effect.FogColor=(camera.Underwater?FogColor:normal).ToVector3();effect.FogStart=camera.Underwater?0:WorldRenderer.FogStart;effect.FogEnd=camera.Underwater?24:WorldRenderer.FogEnd;}
    internal void Draw(Camera camera)
    {
        if(!camera.Underwater)return;
        var blend=_device.BlendState;var depth=_device.DepthStencilState;var raster=_device.RasterizerState;var sampler=_device.SamplerStates[0];var texture=_device.Textures[0];
        try
        {
            var offset=Offset(camera);var color=new Color(105,145,230,28);
            _vertices[0]=new(new(-1,-1,0),color,offset+new Vector2(0,4));_vertices[1]=new(new(-1,1,0),color,offset);_vertices[2]=new(new(1,1,0),color,offset+new Vector2(4,0));
            _vertices[3]=_vertices[0];_vertices[4]=_vertices[2];_vertices[5]=new(new(1,-1,0),color,offset+new Vector2(4,4));
            _device.BlendState=BlendState.NonPremultiplied;_device.DepthStencilState=DepthStencilState.None;_device.RasterizerState=RasterizerState.CullNone;_device.SamplerStates[0]=SamplerState.PointWrap;
            foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.Textures[0]=null;_device.Textures[0]=_effect.Texture;_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,2);}
        }
        finally{_device.BlendState=blend;_device.DepthStencilState=depth;_device.RasterizerState=raster;_device.SamplerStates[0]=sampler;_device.Textures[0]=null;_device.Textures[0]=texture;}
    }
    public void Dispose()=>_effect.Dispose();
}
