using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

// CPU-only geometry builder. One reusable scratch buffer serves all chunk uploads.
internal sealed class ChunkMeshBuilder
{
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[4096];
    public VertexPositionColorTexture[] Vertices => _vertices;
    public int VertexCount { get; private set; }

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
            if (block == BlockType.Air)
                continue;

            int worldX = chunk.OriginX + x, worldZ = chunk.OriginZ + z;
            Vector3 position = new(worldX, y, worldZ);
            foreach (Face face in Faces)
            {
                // World queries include adjacent chunks and return air beyond finite edges.
                if (world.IsSolid(worldX + face.Offset.X, y + face.Offset.Y, worldZ + face.Offset.Z))
                    continue;

                if (VertexCount + 6 > _vertices.Length)
                    Array.Resize(ref _vertices, _vertices.Length * 2);
                int tile = Blocks.Tile(block, face.Normal);
                Color color = Color.White * face.Shade;
                var a = new VertexPositionColorTexture(position + face.A, color, TextureAtlas.Uv(tile, 0, 1));
                var b = new VertexPositionColorTexture(position + face.B, color, TextureAtlas.Uv(tile, 1, 1));
                var c = new VertexPositionColorTexture(position + face.C, color, TextureAtlas.Uv(tile, 1, 0));
                var d = new VertexPositionColorTexture(position + face.D, color, TextureAtlas.Uv(tile, 0, 0));
                _vertices[VertexCount++] = a; _vertices[VertexCount++] = c; _vertices[VertexCount++] = b;
                _vertices[VertexCount++] = a; _vertices[VertexCount++] = d; _vertices[VertexCount++] = c;
            }
        }
    }

    private readonly record struct Face(Point3 Offset, Vector3 Normal, float Shade, Vector3 A, Vector3 B, Vector3 C, Vector3 D);
}
