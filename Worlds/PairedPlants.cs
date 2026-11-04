namespace MDSM.Worlds;

public sealed partial class World
{
    // SIMD byte searches skip empty chunks and avoid storing a second per-plant position map.
    internal void ValidatePairedPlants()
    {
        foreach (Chunk chunk in Chunks)
        {
            if (chunk.DoubleGrassCount == 0) continue;
            var blocks = chunk.Blocks;
            int offset = 0;
            while (offset < blocks.Length)
            {
                int found = System.MemoryExtensions.IndexOfAny(blocks[offset..], (byte)BlockType.DoubleTallGrassLower, (byte)BlockType.DoubleTallGrassUpper);
                if (found < 0) break;
                int index = offset + found; offset = index + 1;
                int x = chunk.OriginX + index % 16, y = index / 256, z = chunk.OriginZ + index / 16 % 16;
                bool lower = blocks[index] == (byte)BlockType.DoubleTallGrassLower;
                if (lower ? GetBlock(x,y-1,z)!=BlockType.Grass || GetBlock(x,y+1,z)!=BlockType.DoubleTallGrassUpper
                    : GetBlock(x,y-1,z)!=BlockType.DoubleTallGrassLower)
                    throw new System.IO.InvalidDataException("Save contains an orphaned double-height grass plant.");
            }
        }
    }
    private bool _plantEdit;
    private void PairedPlantChanged(Point3 cell, BlockType previous, BlockType next)
    {
        if (_plantEdit) return;
        _plantEdit = true;
        try
        {
            if (Blocks.DoubleGrass(previous))
            {
                Point3 other = cell + new Point3(0, previous == BlockType.DoubleTallGrassLower ? 1 : -1, 0);
                BlockType expected = previous == BlockType.DoubleTallGrassLower ? BlockType.DoubleTallGrassUpper : BlockType.DoubleTallGrassLower;
                if (GetBlock(other.X,other.Y,other.Z) == expected) SetBlock(other.X,other.Y,other.Z,BlockType.Air);
            }
            if (next == BlockType.DoubleTallGrassLower) SetBlock(cell.X,cell.Y+1,cell.Z,BlockType.DoubleTallGrassUpper);
        }
        finally { _plantEdit = false; }
    }
}
