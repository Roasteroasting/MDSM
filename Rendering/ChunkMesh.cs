using System;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Worlds;

namespace MDSM.Rendering;

// GPU resources belong to the renderer, keeping storage and generation graphics-independent.
internal sealed class ChunkMesh : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Chunk _chunk;
    private DynamicVertexBuffer _vertices,_text;
    private int _revision = -1;
    private int _vertexCount,_textCount;
    public Microsoft.Xna.Framework.BoundingBox Bounds => _chunk.Bounds;
    public bool HasGeometry => _vertexCount > 0 || _textCount>0;
    internal bool HasText=>_textCount>0;
    internal Chunk Chunk => _chunk;
    internal bool NeedsBuild => _revision != _chunk.Revision;
    internal int VertexCount => _vertexCount+_textCount;
    internal long BufferBytes => ((long)(_vertices?.VertexCount ?? 0)+(_text?.VertexCount??0)) * VertexPositionColorTexture.VertexDeclaration.VertexStride;
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
        UploadPrepared(builder,_chunk.Revision);return true;
    }
    internal void UploadPrepared(ChunkMeshBuilder builder,int revision)
    {
        _vertexCount = builder.VertexCount;
        _textCount=builder.TextVertexCount;
        Upload(ref _vertices,builder.Vertices,_vertexCount,1024);
        Upload(ref _text,builder.TextVertices,_textCount,96);
        _revision = revision;
    }

    private void Upload(ref DynamicVertexBuffer buffer,VertexPositionColorTexture[] data,int count,int step)
    {
        if (count > 0)
        {
            if (buffer == null || buffer.VertexCount < count || buffer.VertexCount > Math.Max(step*4, count * 2))
            {
                // Leave room for edits instead of reallocating after every added face.
                // A modest edit margin avoids the up-to-100% waste of power-of-two capacities.
                int capacity = (count + step-1) / step * step;
                ReleaseBuffer(ref buffer);
                buffer = new DynamicVertexBuffer(_device, typeof(VertexPositionColorTexture), capacity, BufferUsage.WriteOnly);
                LiveBufferBytes += (long)capacity*VertexPositionColorTexture.VertexDeclaration.VertexStride; LiveBuffers++;
            }
            buffer.SetData(data, 0, count,SetDataOptions.Discard);
        }
        else ReleaseBuffer(ref buffer);
    }

    public void Draw()
    {
        if(_vertexCount==0)return;
        _device.SetVertexBuffer(_vertices);
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _vertexCount / 3);
    }

    internal void DrawText()
    {
        if(_textCount==0)return;
        _device.SetVertexBuffer(_text);_device.DrawPrimitives(PrimitiveType.TriangleList,0,_textCount/3);
    }
    private static void ReleaseBuffer(ref DynamicVertexBuffer buffer)
    {
        if(buffer==null)return;
        LiveBufferBytes-=(long)buffer.VertexCount*VertexPositionColorTexture.VertexDeclaration.VertexStride;LiveBuffers--;buffer.Dispose();buffer=null;
    }
    public void Dispose() { ReleaseBuffer(ref _vertices);ReleaseBuffer(ref _text); _vertexCount = _textCount = 0; _revision = -1; }
}
