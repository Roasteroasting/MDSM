using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;
namespace MDSM.Rendering;
internal struct CrackVertex:IVertexType
{
    public Vector3 Position;public Vector2 BaseUv,DamageUv;
    private static readonly VertexDeclaration Declaration=new(new VertexElement(0,VertexElementFormat.Vector3,VertexElementUsage.Position,0),new VertexElement(12,VertexElementFormat.Vector2,VertexElementUsage.TextureCoordinate,0),new VertexElement(20,VertexElementFormat.Vector2,VertexElementUsage.TextureCoordinate,1));
    VertexDeclaration IVertexType.VertexDeclaration=>Declaration;
    internal CrackVertex(Vector3 p,Vector2 uv,Vector2 damage){Position=p;BaseUv=uv;DamageUv=damage;}
}
internal sealed class MiningCracks:IDisposable
{
    internal const float Offset=.001f;
    internal static int Stage(float progress)=>float.IsFinite(progress)&&progress>=0&&progress<1?Math.Min(9,(int)(progress*10)):-1;
    private readonly GraphicsDevice _device;private readonly Effect _effect;private readonly TextureAtlas _atlas;private readonly ChunkMeshBuilder _shape=new();
    private readonly BlendState _multiply=new(){ColorSourceBlend=Blend.DestinationColor,ColorDestinationBlend=Blend.Zero,AlphaSourceBlend=Blend.Zero,AlphaDestinationBlend=Blend.One};
    private CrackVertex[] _vertices=new CrackVertex[4096];private Point3? _cell;private int _revision=-1;private long _originX,_originZ;
    internal int VertexCount=>_shape.VertexCount;
    internal MiningCracks(GraphicsDevice device,TextureAtlas atlas)
    {_device=device;_atlas=atlas;_effect=atlas.CrackEffect.Clone();}
    internal void Draw(World world,Camera camera,Point3 cell,float progress)
    {
        int stage=Stage(progress);if(stage<0||!world.Contains(cell.X,cell.Y,cell.Z)||!world.TryGetChunkAt(cell.X,cell.Z,out var chunk))return;
        if(_cell!=cell||_revision!=chunk.Revision||_originX!=world.OriginX||_originZ!=world.OriginZ)
        {
        _shape.BuildBlock(world,cell);_cell=cell;_revision=chunk.Revision;_originX=world.OriginX;_originZ=world.OriginZ;
        if(_vertices.Length<_shape.VertexCount)Array.Resize(ref _vertices,_shape.Vertices.Length);
        for(int i=0;i<_shape.VertexCount;i+=6)
        {
            var raw=_shape.Vertices;Vector3 normal=Vector3.Cross(raw[i+2].Position-raw[i].Position,raw[i+1].Position-raw[i].Position);
            if(normal.LengthSquared()>0)normal.Normalize();
            for(int j=0;j<6;j++)
            {
                var v=raw[i+j];float u=(v.TextureCoordinate.X*256%16-.5f)/15,vv=(v.TextureCoordinate.Y*256%16-.5f)/15;
                _vertices[i+j]=new(v.Position+normal*Offset,v.TextureCoordinate,new(u,vv));
            }
        }
        }
        var blend=_device.BlendState;var depth=_device.DepthStencilState;var raster=_device.RasterizerState;var sampler=_device.SamplerStates[0];var sampler1=_device.SamplerStates[1];var texture=_device.Textures[0];var texture1=_device.Textures[1];
        try
        {
            _device.BlendState=_multiply;_device.DepthStencilState=DepthStencilState.DepthRead;_device.RasterizerState=RasterizerState.CullCounterClockwise;_device.SamplerStates[0]=SamplerState.PointClamp;
            _effect.Parameters["ViewProjection"].SetValue(camera.View*camera.Projection(_device.Viewport.AspectRatio));
            _effect.Parameters["Stage"].SetValue((float)stage);_effect.Parameters["Texture"].SetValue(_atlas.Texture);_effect.Parameters["Cracks"].SetValue(_atlas.DestroyStages);
            foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.Textures[0]=null;_device.Textures[0]=_atlas.Texture;_device.Textures[1]=null;_device.Textures[1]=_atlas.DestroyStages;if(VertexCount>0)_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,VertexCount/3);}
        }
        finally{_device.BlendState=blend;_device.DepthStencilState=depth;_device.RasterizerState=raster;_device.SamplerStates[0]=sampler;_device.SamplerStates[1]=sampler1;_device.Textures[0]=null;_device.Textures[0]=texture;_device.Textures[1]=null;_device.Textures[1]=texture1;}
    }
    public void Dispose(){_effect.Dispose();_multiply.Dispose();}
}
