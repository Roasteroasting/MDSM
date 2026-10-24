using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

public sealed class WorldRenderer : IDisposable
{
    public static readonly Color SkyColor = new(143, 190, 232);
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _terrainEffect;
    private readonly BasicEffect _selectionEffect;
    private VertexBuffer _vertices;
    private int _revision = -1;
    private int _vertexCount;
    private readonly VertexPositionColor[] _outline = new VertexPositionColor[24];

    // Corners follow the geometric normal; triangle order below converts to XNA's clockwise front faces.
    private static readonly Face[] Faces =
    {
        new(new Point3(1, 0, 0), Vector3.UnitX, 0.78f,
            new(1, 0, 1), new(1, 0, 0), new(1, 1, 0), new(1, 1, 1)),
        new(new Point3(-1, 0, 0), -Vector3.UnitX, 0.78f,
            new(0, 0, 0), new(0, 0, 1), new(0, 1, 1), new(0, 1, 0)),
        new(new Point3(0, 1, 0), Vector3.UnitY, 1f,
            new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0)),
        new(new Point3(0, -1, 0), -Vector3.UnitY, 0.5f,
            new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1)),
        new(new Point3(0, 0, 1), Vector3.UnitZ, 0.9f,
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)),
        new(new Point3(0, 0, -1), -Vector3.UnitZ, 0.9f,
            new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(1, 1, 0))
    };

    public WorldRenderer(GraphicsDevice device, TextureAtlas atlas)
    {
        _device = device;
        _terrainEffect = new BasicEffect(device)
        {
            World = Matrix.Identity,
            TextureEnabled = true,
            Texture = atlas.Texture,
            VertexColorEnabled = true,
            LightingEnabled = false,
            FogEnabled = true,
            FogColor = SkyColor.ToVector3(),
            FogStart = 32,
            FogEnd = 85
        };
        _selectionEffect = new BasicEffect(device)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true
        };
    }

    public void Draw(World world, Camera camera, BlockHit? selection)
    {
        if (_revision != world.Revision)
            Rebuild(world);

        // SpriteBatch changes these states, so restore them every frame.
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _terrainEffect.View = camera.View;
        _terrainEffect.Projection = camera.Projection(_device.Viewport.AspectRatio);
        if (_vertexCount > 0)
        {
            _device.SetVertexBuffer(_vertices);
            foreach (EffectPass pass in _terrainEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _vertexCount / 3);
            }
        }

        if (selection is BlockHit hit)
            DrawSelection(hit, camera);
    }

    private void Rebuild(World world)
    {
        VertexPositionColorTexture[] vertices = BuildMesh(world);
        _vertices?.Dispose();
        _vertices = null;
        _vertexCount = vertices.Length;
        if (_vertexCount > 0)
        {
            _vertices = new VertexBuffer(_device, typeof(VertexPositionColorTexture), _vertexCount, BufferUsage.WriteOnly);
            _vertices.SetData(vertices);
        }
        _revision = world.Revision;
    }

    // Mesh generation is independent of the GPU, which also lets self-tests check hidden faces.
    internal static VertexPositionColorTexture[] BuildMesh(World world)
    {
        var vertices = new List<VertexPositionColorTexture>();
        for (int x = 0; x < World.Width; x++)
        for (int y = 0; y < World.Height; y++)
        for (int z = 0; z < World.Depth; z++)
        {
            BlockType block = world.GetBlock(x, y, z);
            if (block == BlockType.Air)
                continue;

            Vector3 position = new(x, y, z);
            foreach (Face face in Faces)
            {
                Point3 neighbor = new Point3(x, y, z) + face.Offset;
                if (world.IsSolid(neighbor.X, neighbor.Y, neighbor.Z))
                    continue;

                int tile = Blocks.Tile(block, face.Normal);
                Color color = Color.White * face.Shade;
                var a = new VertexPositionColorTexture(position + face.A, color, TextureAtlas.Uv(tile, 0, 1));
                var b = new VertexPositionColorTexture(position + face.B, color, TextureAtlas.Uv(tile, 1, 1));
                var c = new VertexPositionColorTexture(position + face.C, color, TextureAtlas.Uv(tile, 1, 0));
                var d = new VertexPositionColorTexture(position + face.D, color, TextureAtlas.Uv(tile, 0, 0));
                vertices.Add(a); vertices.Add(c); vertices.Add(b);
                vertices.Add(a); vertices.Add(d); vertices.Add(c);
            }
        }

        return vertices.ToArray();
    }

    private void DrawSelection(BlockHit hit, Camera camera)
    {
        BoundingBox box = World.Bounds(hit.Cell);
        box.Min -= new Vector3(0.003f);
        box.Max += new Vector3(0.003f);
        Vector3[] corners = box.GetCorners();
        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        for (int i = 0; i < edges.Length; i++)
            _outline[i] = new VertexPositionColor(corners[edges[i]], new Color(25, 30, 35));

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
        _vertices?.Dispose();
        _terrainEffect.Dispose();
        _selectionEffect.Dispose();
    }

    private readonly record struct Face(Point3 Offset, Vector3 Normal, float Shade, Vector3 A, Vector3 B, Vector3 C, Vector3 D);
}
