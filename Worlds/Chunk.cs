using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed class Chunk
{
    public const int Size = 16;
    public const int BlockCount = Size * World.Height * Size;

    private readonly byte[] _blocks = new byte[BlockCount];
    public int X { get; }
    public int Z { get; }
    public int OriginX => X * Size;
    public int OriginZ => Z * Size;
    public int Revision { get; private set; }
    public int WaterRevision { get; private set; }
    public int WaterCount { get; private set; }
    public BoundingBox Bounds { get; }
    internal System.ReadOnlySpan<byte> Blocks => _blocks;

    internal Chunk(int x, int z)
    {
        X = x;
        Z = z;
        Bounds = new BoundingBox(new Vector3(OriginX, 0, OriginZ),
            new Vector3(OriginX + Size, World.Height, OriginZ + Size));
    }

    // Callers validate coordinates. X is contiguous, then Z, then Y.
    internal BlockType GetBlock(int x, int y, int z) => (BlockType)_blocks[(y * Size + z) * Size + x];
    internal void SetBlock(int x, int y, int z, BlockType block)
    {
        int index = (y * Size + z) * Size + x;
        if (Water.IsWater((BlockType)_blocks[index])) WaterCount--;
        _blocks[index] = (byte)block;
        if (Water.IsWater(block)) WaterCount++;
    }
    internal void MarkWaterDirty() => WaterRevision++;
    internal void MarkDirty() => Revision++;

    internal void RestoreBlocks(System.ReadOnlySpan<byte> blocks)
    {
        if (blocks.Length != BlockCount)
            throw new System.ArgumentException("Incorrect chunk data length.", nameof(blocks));
        blocks.CopyTo(_blocks);
        WaterCount = 0;
        foreach (byte block in blocks) if (Water.IsWater((BlockType)block)) WaterCount++;
        MarkWaterDirty();
        MarkDirty();
    }
}
