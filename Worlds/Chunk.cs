using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed class Chunk
{
    public const int Size = 16;
    public const int BlockCount = Size * World.Height * Size;

    private readonly byte[] _blocks = new byte[BlockCount];
    private readonly byte[] _solidTops = new byte[Size * Size]; // Y+1; 128 fits in one byte, zero denotes air.
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
    internal int SurfaceY(int x, int z) => _solidTops[z * Size + x] - 1;
    internal void SetBlock(int x, int y, int z, BlockType block)
    {
        int index = (y * Size + z) * Size + x;
        if (Water.IsWater((BlockType)_blocks[index])) WaterCount--;
        _blocks[index] = (byte)block;
        if (Water.IsWater(block)) WaterCount++;
        int column = z * Size + x;
        if (MDSM.Worlds.Blocks.IsSolid(block)) _solidTops[column] = (byte)Math.Max(_solidTops[column], y + 1);
        else if (_solidTops[column] == y + 1)
        {
            int top = y - 1;
            while (top >= 0 && !MDSM.Worlds.Blocks.IsSolid(GetBlock(x, top, z))) top--;
            _solidTops[column] = (byte)(top + 1);
        }
    }
    internal void MarkWaterDirty() => WaterRevision++;
    internal void MarkDirty() => Revision++;

    internal void RestoreBlocks(System.ReadOnlySpan<byte> blocks, int storedHeight = World.Height)
    {
        if (storedHeight is not (64 or World.Height) || blocks.Length != Size * storedHeight * Size)
            throw new System.ArgumentException("Incorrect chunk data length.", nameof(blocks));
        blocks.CopyTo(_blocks);
        _blocks.AsSpan(blocks.Length).Clear();
        WaterCount = 0;
        Array.Clear(_solidTops);
        for (int y = 0; y < storedHeight; y++) for (int z = 0; z < Size; z++) for (int x = 0; x < Size; x++)
        {
            BlockType block = GetBlock(x,y,z);
            if (Water.IsWater(block)) WaterCount++;
            if (MDSM.Worlds.Blocks.IsSolid(block)) _solidTops[z*Size+x] = (byte)(y+1);
        }
        MarkWaterDirty();
        MarkDirty();
    }
}
