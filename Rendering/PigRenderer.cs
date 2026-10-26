using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;

namespace MDSM.Rendering;

internal sealed class PigRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D _texture; // ContentManager owns it.
    private readonly AlphaTestEffect _effect;
    private readonly CuboidModelBuilder _builder = new(64, 16);
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private DynamicVertexBuffer _buffer;

    public PigRenderer(GraphicsDevice device, ContentManager content)
    {
        _device = device; _texture = content.Load<Texture2D>("pig");
        if (_texture.Width != 64 || _texture.Height != 16) throw new ContentLoadException("pig.png must be 64x16.");
        _effect = new AlphaTestEffect(device)
        {
            World = Matrix.Identity, Texture = _texture, VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual, ReferenceAlpha = 128,
            FogEnabled = true, FogColor = WorldRenderer.SkyColor.ToVector3(), FogStart = 80, FogEnd = 220
        };
    }

    internal static void Append(CuboidModelBuilder builder, Pig pig)
    {
        Matrix world = Matrix.CreateRotationY(pig.Yaw) * Matrix.CreateTranslation(pig.Position);
        Color tint = pig.HurtSeconds > 0 ? new Color(255, 85, 85) : Color.White;
        builder.Cuboid(new(-0.32f,0.35f,-0.4f), new(0.32f,0.85f,0.4f), world, 0, tint);
        builder.Cuboid(new(-0.27f,0.45f,-0.65f), new(0.27f,0.95f,-0.25f), world, 0, tint, 1);
        builder.Cuboid(new(-0.18f,0.50f,-0.78f), new(0.18f,0.70f,-0.65f), world, 0, tint, 2);
        float swing = pig.Walking ? MathF.Sin(pig.WalkPhase) * 0.65f : 0;
        for (int leg = 0; leg < 4; leg++)
        {
            float x = (leg & 1) == 0 ? -0.22f : 0.22f, z = leg < 2 ? -0.25f : 0.25f;
            float angle = (leg == 0 || leg == 3) ? swing : -swing;
            Matrix transform = Matrix.CreateRotationX(angle) * Matrix.CreateTranslation(x, 0.35f, z) * world;
            builder.Cuboid(new(-0.11f,-0.35f,-0.11f), new(0.11f,0,0.11f), transform, 3, tint);
        }
    }

    public void Draw(Camera camera, PigPopulation pigs)
    {
        _builder.Clear();
        _frustum.Matrix = camera.View * camera.Projection(_device.Viewport.AspectRatio);
        for (int i = 0; i < pigs.Entities.Count; i++)
        {
            Pig pig = pigs.Entities[i];
            if (!pig.IsDead && _frustum.Intersects(pig.Bounds)) Append(_builder, pig);
        }
        if (_builder.Count == 0) return;
        if (_buffer == null || _buffer.VertexCount < _builder.Count)
        {
            int capacity = 4096; while (capacity < _builder.Count) capacity *= 2;
            _buffer?.Dispose();
            _buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
        }
        BlendState blend = _device.BlendState; DepthStencilState depth = _device.DepthStencilState;
        RasterizerState raster = _device.RasterizerState; SamplerState sampler = _device.SamplerStates[0];
        Texture texture = _device.Textures[0];
        try
        {
            _device.BlendState = BlendState.Opaque; _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullCounterClockwise; _device.SamplerStates[0] = SamplerState.PointClamp;
            _buffer.SetData(_builder.Vertices, 0, _builder.Count, SetDataOptions.Discard);
            _device.SetVertexBuffer(_buffer);
            _effect.View = camera.View; _effect.Projection = camera.Projection(_device.Viewport.AspectRatio);
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            { pass.Apply(); _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _builder.Count / 3); }
        }
        finally
        {
            _device.BlendState = blend; _device.DepthStencilState = depth; _device.RasterizerState = raster;
            _device.SamplerStates[0] = sampler; _device.Textures[0] = null; _device.Textures[0] = texture;
        }
    }
    public void Dispose() { _buffer?.Dispose(); _effect.Dispose(); }
}
