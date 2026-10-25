using System;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

// GPU resources belong to the renderer, keeping storage and generation graphics-independent.
internal sealed class ChunkMesh : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Chunk _chunk;
    private VertexBuffer _vertices;
    private int _revision = -1;
    private int _vertexCount;
    public Microsoft.Xna.Framework.BoundingBox Bounds => _chunk.Bounds;
    public bool HasGeometry => _vertexCount > 0;

    public ChunkMesh(GraphicsDevice device, Chunk chunk)
    {
        _device = device;
        _chunk = chunk;
    }

    public bool Update(World world, ChunkMeshBuilder builder)
    {
        if (_revision == _chunk.Revision)
            return false;

        builder.Build(world, _chunk);
        _vertexCount = builder.VertexCount;
        if (_vertexCount > 0)
        {
            if (_vertices == null || _vertices.VertexCount < _vertexCount)
            {
                // Leave room for edits instead of reallocating after every added face.
                int capacity = 256;
                while (capacity < _vertexCount)
                    capacity *= 2;
                _vertices?.Dispose();
                _vertices = new VertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
            }
            _vertices.SetData(builder.Vertices, 0, _vertexCount);
        }
        _revision = _chunk.Revision;
        return true;
    }

    public void Draw()
    {
        _device.SetVertexBuffer(_vertices);
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _vertexCount / 3);
    }

    public void Dispose() => _vertices?.Dispose();
}
