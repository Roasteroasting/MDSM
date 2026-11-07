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
    public int LavaCount { get; private set; }
    public int StructureCount { get; private set; }
    public int CropCount { get; private set; }
    public int DoubleGrassCount { get; private set; }
    private static bool Special(BlockType block)=>block is BlockType.FenceGate or BlockType.Door or BlockType.DoorUpper or BlockType.Torch or BlockType.Bed or BlockType.BedHead;
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
        if (MDSM.Worlds.Blocks.DoubleGrass((BlockType)_blocks[index])) DoubleGrassCount--;
        if (MDSM.Worlds.Blocks.DoubleGrass(block)) DoubleGrassCount++;
        if (Water.IsWater((BlockType)_blocks[index])) WaterCount--;
        if (Lava.IsLava((BlockType)_blocks[index])) LavaCount--;
        if(Special((BlockType)_blocks[index]))StructureCount--;
        if(MDSM.Worlds.Blocks.Crop((BlockType)_blocks[index]))CropCount--;
        _blocks[index] = (byte)block;
        if (Water.IsWater(block)) WaterCount++;
        if (Lava.IsLava(block)) LavaCount++;
        if(Special(block))StructureCount++;
        if(MDSM.Worlds.Blocks.Crop(block))CropCount++;
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
        LavaCount=0;
        StructureCount=0;
        CropCount=0;
        DoubleGrassCount=0;
        Array.Clear(_solidTops);
        Span<int> counts=stackalloc int[256];counts.Clear();
        foreach(byte block in blocks)counts[block]++;
        DoubleGrassCount=counts[(int)BlockType.DoubleTallGrassLower]+counts[(int)BlockType.DoubleTallGrassUpper];
        for(int i=(int)BlockType.WaterSource;i<=(int)BlockType.WaterFalling;i++)WaterCount+=counts[i];
        for(int i=(int)BlockType.LavaSource;i<=(int)BlockType.LavaFalling;i++)LavaCount+=counts[i];
        for(int i=(int)BlockType.Wheat1;i<=(int)BlockType.Wheat7;i++)CropCount+=counts[i];
        StructureCount=counts[(int)BlockType.FenceGate]+counts[(int)BlockType.Door]+counts[(int)BlockType.DoorUpper]+counts[(int)BlockType.Torch]+counts[(int)BlockType.Bed]+counts[(int)BlockType.BedHead];
        for (int z = 0; z < Size; z++) for (int x = 0; x < Size; x++)
        {
            int y=storedHeight-1;while(y>=0 && !MDSM.Worlds.Blocks.IsSolid(GetBlock(x,y,z)))y--;
            _solidTops[z*Size+x]=(byte)(y+1);
        }
        MarkWaterDirty();
        MarkDirty();
    }
}
