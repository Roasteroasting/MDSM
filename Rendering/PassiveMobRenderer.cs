using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;

namespace MDSM.Rendering;

internal sealed class PassiveMobRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D[] _textures = new Texture2D[MobDefinitions.SpeciesCount];
    private readonly AlphaTestEffect _effect;
    private readonly CuboidModelBuilder _builder = new(64, 16);
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private DynamicVertexBuffer _buffer;
    public PassiveMobRenderer(GraphicsDevice device, ContentManager content)
    {
        _device = device;
        for (int i = 0; i < _textures.Length; i++)
        {
            string name = MobDefinitions.Get((MobSpecies)i).Texture;
            _textures[i] = content.Load<Texture2D>(name);
            if (_textures[i].Width != 64 || _textures[i].Height != 16) throw new ContentLoadException(name + ".png must be 64x16.");
        }
        _effect = new AlphaTestEffect(device) { World = Matrix.Identity, VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128,
            FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = 80, FogEnd = 220 };
    }
    public void Draw(Camera camera, PassiveMobPopulation population)
    {
        _frustum.Matrix = camera.View * camera.Projection(_device.Viewport.AspectRatio);
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState raster = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0]; Texture texture = _device.Textures[0];
        try
        {
            _device.BlendState = BlendState.Opaque; _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullCounterClockwise; _device.SamplerStates[0] = SamplerState.PointClamp;
            _effect.View = camera.View; _effect.Projection = camera.Projection(_device.Viewport.AspectRatio);
            for (int species = 0; species < _textures.Length; species++)
            {
                _builder.Clear();
                for (int i = 0; i < population.Entities.Count; i++)
                {
                    PassiveMob mob = population.Entities[i];
                    if ((int)mob.Species == species && !mob.IsDead && _frustum.Intersects(mob.Bounds)) PassiveMobModels.Append(_builder, mob);
                }
                if (_builder.Count == 0) continue;
                if (_buffer == null || _buffer.VertexCount < _builder.Count)
                {
                    int capacity = 4096; while (capacity < _builder.Count) capacity *= 2;
                    _buffer?.Dispose(); _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
                }
                _buffer.SetData(_builder.Vertices, 0, _builder.Count, SetDataOptions.Discard);
                _device.SetVertexBuffer(_buffer); _effect.Texture = _textures[species];
                foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
                { pass.Apply(); _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _builder.Count / 3); }
            }
        }
        finally
        {
            _device.BlendState = blend; _device.DepthStencilState = depth; _device.RasterizerState = raster; _device.SamplerStates[0] = sampler;
            _device.Textures[0] = null; _device.Textures[0] = texture;
        }
    }
    public void Dispose() { _buffer?.Dispose(); _effect.Dispose(); }
}
