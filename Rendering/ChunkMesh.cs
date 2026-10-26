using System;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Worlds;

namespace MDSM.Rendering;

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
    internal Chunk Chunk => _chunk;
    internal bool NeedsBuild => _revision != _chunk.Revision;
    internal int VertexCount => _vertexCount;
    internal long BufferBytes => (long)(_vertices?.VertexCount ?? 0) * VertexPositionColorTexture.VertexDeclaration.VertexStride;
    internal static long LiveBufferBytes { get; private set; }
    internal static int LiveBuffers { get; private set; }

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
            if (_vertices == null || _vertices.VertexCount < _vertexCount || _vertices.VertexCount > Math.Max(4096, _vertexCount * 2))
            {
                // Leave room for edits instead of reallocating after every added face.
                // A modest edit margin avoids the up-to-100% waste of power-of-two capacities.
                int capacity = (_vertexCount + 1023) / 1024 * 1024;
                ReleaseBuffer();
                _vertices = new VertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
                LiveBufferBytes += BufferBytes; LiveBuffers++;
            }
            _vertices.SetData(builder.Vertices, 0, _vertexCount);
        }
        else ReleaseBuffer();
        _revision = _chunk.Revision;
        return true;
    }

    public void Draw()
    {
        _device.SetVertexBuffer(_vertices);
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _vertexCount / 3);
    }

    private void ReleaseBuffer()
    {
        if (_vertices == null) return;
        LiveBufferBytes -= BufferBytes; LiveBuffers--; _vertices.Dispose(); _vertices = null;
    }
    public void Dispose() { ReleaseBuffer(); _vertexCount = 0; _revision = -1; }
}
