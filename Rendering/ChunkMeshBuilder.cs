using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Entities;
using MDSM.Worlds;

namespace MDSM.Rendering;

// CPU-only geometry builder. One reusable scratch buffer serves all chunk uploads.
internal sealed class ChunkMeshBuilder
{
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[4096];
    public VertexPositionColorTexture[] Vertices => _vertices;
    public int VertexCount { get; private set; }
    public void Clear() => VertexCount = 0;

    public void AppendCube(BlockType block, Vector3 origin)
    {
        foreach (Face face in Faces)
            AddQuad(origin + face.A, origin + face.B, origin + face.C, origin + face.D,
                Blocks.Tile(block, face.Normal), new Color(new Vector3(face.Shade)), false);
    }

    public void AppendItem(BlockType block, Vector3 center)
    {
        float size = DroppedItem.Radius * 2;
        Vector3 origin = center - new Vector3(DroppedItem.Radius);
        BlockDefinition definition = Blocks.Get(block);
        if (definition.Shape == BlockShape.Cross)
        {
            AddQuad(origin, origin + new Vector3(size, 0, size), origin + new Vector3(size, size, size),
                origin + new Vector3(0, size, 0), definition.SideTile, Color.White, true);
            AddQuad(origin + new Vector3(size, 0, 0), origin + new Vector3(0, 0, size),
                origin + new Vector3(0, size, size), origin + new Vector3(size, size, 0), definition.SideTile, Color.White, true);
        }
        else
            foreach (Face face in Faces)
                AddQuad(origin + face.A * size, origin + face.B * size, origin + face.C * size, origin + face.D * size,
                    definition.Tile(face.Normal), new Color(new Vector3(face.Shade)), false);
    }

    // Corners follow the geometric normal; emitted triangles use XNA's clockwise front faces.
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

    public void Build(World world, Chunk chunk)
    {
        VertexCount = 0;
        for (int y = 0; y < World.Height; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            BlockType block = chunk.GetBlock(x, y, z);
            BlockDefinition definition = Blocks.Get(block);
            if (!definition.Renderable || definition.Shape == BlockShape.Fluid)
                continue;

            int worldX = chunk.OriginX + x, worldZ = chunk.OriginZ + z;
            Vector3 position = new(worldX, y, worldZ);
            if (definition.Shape == BlockShape.Cross)
            {
                AddQuad(position + new Vector3(0.1f, 0, 0.1f), position + new Vector3(0.9f, 0, 0.9f),
                    position + new Vector3(0.9f, 1, 0.9f), position + new Vector3(0.1f, 1, 0.1f), definition.SideTile, Color.White, true);
                AddQuad(position + new Vector3(0.9f, 0, 0.1f), position + new Vector3(0.1f, 0, 0.9f),
                    position + new Vector3(0.1f, 1, 0.9f), position + new Vector3(0.9f, 1, 0.1f), definition.SideTile, Color.White, true);
                continue;
            }
            foreach (Face face in Faces)
            {
                // World queries include adjacent chunks and return air beyond finite edges.
                if (Blocks.IsOpaque(world.GetBlock(worldX + face.Offset.X, y + face.Offset.Y, worldZ + face.Offset.Z)))
                    continue;

                int tile = Blocks.Tile(block, face.Normal);
                // Shading changes RGB only: reducing vertex alpha would discard dark leaf faces.
                Color color = new(new Vector3(face.Shade));
                AddQuad(position + face.A, position + face.B, position + face.C, position + face.D, tile, color, false);
            }
        }
    }

    private void AddQuad(Vector3 aPosition, Vector3 bPosition, Vector3 cPosition, Vector3 dPosition, int tile, Color color, bool twoSided)
    {
        int needed = twoSided ? 12 : 6;
        if (VertexCount + needed > _vertices.Length)
            Array.Resize(ref _vertices, _vertices.Length * 2);
        var a = new VertexPositionColorTexture(aPosition, color, TextureAtlas.Uv(tile, 0, 1));
        var b = new VertexPositionColorTexture(bPosition, color, TextureAtlas.Uv(tile, 1, 1));
        var c = new VertexPositionColorTexture(cPosition, color, TextureAtlas.Uv(tile, 1, 0));
        var d = new VertexPositionColorTexture(dPosition, color, TextureAtlas.Uv(tile, 0, 0));
        _vertices[VertexCount++] = a; _vertices[VertexCount++] = c; _vertices[VertexCount++] = b;
        _vertices[VertexCount++] = a; _vertices[VertexCount++] = d; _vertices[VertexCount++] = c;
        if (twoSided)
        {
            _vertices[VertexCount++] = a; _vertices[VertexCount++] = b; _vertices[VertexCount++] = c;
            _vertices[VertexCount++] = a; _vertices[VertexCount++] = c; _vertices[VertexCount++] = d;
        }
    }

    private readonly record struct Face(Point3 Offset, Vector3 Normal, float Shade, Vector3 A, Vector3 B, Vector3 C, Vector3 D);
}
