using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class PassiveMobRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D[] _textures = new Texture2D[7];
    private readonly Texture2D _bowAtlas;
    private readonly LeashRenderer _leashes;
    private readonly AlphaTestEffect _effect;
    private readonly CuboidModelBuilder _builder = new(64, 16);
    private readonly CuboidModelBuilder _bows = new(TextureAtlas.AtlasSize, TextureAtlas.AtlasSize);
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private DynamicVertexBuffer _buffer;
    internal long BufferBytes => (long)(_buffer?.VertexCount ?? 0) * VertexPositionColorTexture.VertexDeclaration.VertexStride;
    public PassiveMobRenderer(GraphicsDevice device, ContentManager content)
    {
        _device = device;
        _leashes=new(device);
        for (int i = 0; i < _textures.Length; i++)
        {
            string name = MobDefinitions.Get((MobSpecies)i).Texture;
            Texture2D source = content.Load<Texture2D>(name);
            if (source.Width != 64 || source.Height != 16) throw new ContentLoadException(name + ".png must be 64x16.");
            _textures[i] = i >= (int)MobSpecies.Zombie ? HostilePalettes.Create(device, source, (MobSpecies)i) : source;
        }
        _bowAtlas=content.Load<Texture2D>("Items/items");
        _effect = new AlphaTestEffect(device) { World = Matrix.Identity, VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128,
            FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = WorldRenderer.FogStart, FogEnd = WorldRenderer.FogEnd };
    }
    public void Draw(Camera camera, PassiveMobPopulation population,HostilePopulation hostiles=null,World world=null,float brightness=.5f,Vector3? leashPlayer=null)
    {
        _frustum.Matrix = camera.View * camera.Projection(_device.Viewport.AspectRatio);
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState raster = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0]; Texture texture = _device.Textures[0];
        try
        {
            _bows.Clear();
            _device.BlendState = BlendState.Opaque; _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullCounterClockwise; _device.SamplerStates[0] = SamplerState.PointClamp;
            _effect.View = camera.View; _effect.Projection = camera.Projection(_device.Viewport.AspectRatio);
            if(world!=null)UnderwaterView.Fog(_effect,camera,world.Time.Sky);
            for (int species = 0; species < _textures.Length; species++)
            {
                _builder.Clear();
                for (int i = 0; i < population.Entities.Count; i++)
                {
                    PassiveMob mob = population.Entities[i];
                    if ((int)mob.Species == species && !mob.IsDead && _frustum.Intersects(mob.RenderBounds)) Append(mob);
                }
                if(hostiles!=null)for(int i=0;i<hostiles.Entities.Count;i++)
                {var mob=hostiles.Entities[i];if((int)mob.Species==species && !mob.IsDead && _frustum.Intersects(mob.RenderBounds))Append(mob);}
                DrawBatch(_builder,_textures[species]);
            }
            DrawBatch(_bows,_bowAtlas);
            if(world!=null)_leashes.Draw(camera,population,world,leashPlayer);
        }
        finally
        {
            _device.BlendState = blend; _device.DepthStencilState = depth; _device.RasterizerState = raster; _device.SamplerStates[0] = sampler;
            _device.Textures[0] = null; _device.Textures[0] = texture;
        }
        void Append(PassiveMob mob)
        {
            int start=_builder.Count;PassiveMobModels.Append(_builder,mob);
            float light=world==null?1:BlockLighting.Visibility(world.Lighting.Effective(mob.Position+Vector3.Up*(mob.Definition.Height*.5f)),brightness);
            Shade(_builder,start,light);
            if(mob.Species==MobSpecies.Skeleton)
            {
                int bowStart=_bows.Count;
                PassiveMobModels.AppendBow(_bows,mob);
                Shade(_bows,bowStart,light);
            }
        }
    }
    private static void Shade(CuboidModelBuilder mesh,int start,float light)
    {
        if(light==1)return;
        for(int v=start;v<mesh.Count;v++)mesh.Vertices[v].Color=new Color(mesh.Vertices[v].Color.ToVector3()*light);
    }
    private void DrawBatch(CuboidModelBuilder mesh,Texture2D texture)
    {
        if(mesh.Count==0)return;
        if(_buffer==null || _buffer.VertexCount<mesh.Count)
        {
            int capacity=4096;while(capacity<mesh.Count)capacity*=2;
            _buffer?.Dispose();_buffer=new DynamicVertexBuffer(_device,typeof(VertexPositionColorTexture),capacity,BufferUsage.WriteOnly);
        }
        _buffer.SetData(mesh.Vertices,0,mesh.Count,SetDataOptions.Discard);
        _device.SetVertexBuffer(_buffer);_effect.Texture=texture;
        foreach(EffectPass pass in _effect.CurrentTechnique.Passes)
        {pass.Apply();_device.DrawPrimitives(PrimitiveType.TriangleList,0,mesh.Count/3);}
    }
    public void Dispose()
    {
        _buffer?.Dispose();_effect.Dispose();_leashes.Dispose();
        for(int i=(int)MobSpecies.Zombie;i<_textures.Length;i++)_textures[i].Dispose();
    }
}
