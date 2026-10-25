using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

public sealed class WorldRenderer : IDisposable
{
    public static readonly Color SkyColor = new(143, 190, 232);
    private readonly GraphicsDevice _device;
    private readonly AlphaTestEffect _terrainEffect;
    private readonly BasicEffect _selectionEffect;
    private readonly World _world;
    private readonly ChunkMesh[] _meshes = new ChunkMesh[World.ChunkCount];
    private readonly ChunkMesh[] _visible = new ChunkMesh[World.ChunkCount];
    private readonly ChunkMeshBuilder _builder = new();
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);
    private readonly VertexPositionColor[] _outline = new VertexPositionColor[24];
    private readonly Vector3[] _corners = new Vector3[8];
    private static readonly int[] OutlineEdges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
    public int VisibleChunks { get; private set; }
    public int RebuiltChunks { get; private set; }

    public WorldRenderer(GraphicsDevice device, TextureAtlas atlas, World world)
    {
        _device = device;
        _world = world;
        for (int i = 0; i < _meshes.Length; i++)
            _meshes[i] = new ChunkMesh(device, world.Chunks[i]);
        _terrainEffect = new AlphaTestEffect(device)
        {
            World = Matrix.Identity,
            Texture = atlas.Texture,
            VertexColorEnabled = true,
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 128,
            FogEnabled = true,
            FogColor = SkyColor.ToVector3(),
            FogStart = 80,
            FogEnd = 220
        };
        _selectionEffect = new BasicEffect(device)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true
        };
    }

    public void Draw(Camera camera, BlockHit? selection)
    {
        Matrix view = camera.View;
        Matrix projection = camera.Projection(_device.Viewport.AspectRatio);
        _frustum.Matrix = view * projection;
        VisibleChunks = 0;
        RebuiltChunks = 0;
        foreach (ChunkMesh mesh in _meshes)
        {
            // All meshes are initialized once; subsequent frames rebuild only edited chunks.
            if (mesh.Update(_world, _builder))
                RebuiltChunks++;
            if (mesh.HasGeometry && _frustum.Intersects(mesh.Bounds))
                _visible[VisibleChunks++] = mesh;
        }

        // SpriteBatch changes these states, so restore them every frame.
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _terrainEffect.View = view;
        _terrainEffect.Projection = projection;
        foreach (EffectPass pass in _terrainEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            for (int i = 0; i < VisibleChunks; i++)
                _visible[i].Draw();
        }

        if (selection is BlockHit hit)
            DrawSelection(hit, camera);
    }

    private void DrawSelection(BlockHit hit, Camera camera)
    {
        BoundingBox box = World.Bounds(hit.Cell);
        box.Min -= new Vector3(0.003f);
        box.Max += new Vector3(0.003f);
        box.GetCorners(_corners);
        for (int i = 0; i < OutlineEdges.Length; i++)
            _outline[i] = new VertexPositionColor(_corners[OutlineEdges[i]], new Color(25, 30, 35));

        _selectionEffect.View = camera.View;
        _selectionEffect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        foreach (EffectPass pass in _selectionEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.LineList, _outline, 0, 12);
        }
    }

    public void Dispose()
    {
        foreach (ChunkMesh mesh in _meshes)
            mesh.Dispose();
        _terrainEffect.Dispose();
        _selectionEffect.Dispose();
    }
}
