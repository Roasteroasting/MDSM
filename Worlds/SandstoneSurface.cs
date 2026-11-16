using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

// Deterministic equivalent of the classic 0..3 filler run, keyed by absolute
// column coordinates instead of a chunk-order-dependent random stream.
internal static class SandstoneSurface
{
    internal static int Thickness(int seed,long x,long z)=>(int)(InfiniteFeatures.Hash(seed^0x53414e53,x,0,z)&3);
    internal static bool Layer(int seed,long x,long z,int belowSand)=>belowSand>=0&&belowSand<Thickness(seed,x,z);
    internal static void UnderSand(World world,int x,int y,int z)
    {
        if(world.GetBlock(x,y,z)!=BlockType.Sand)return;
        while(y>0&&world.GetBlock(x,y,z)==BlockType.Sand)y--;
        int count=Thickness(world.Seed,x,z);
        for(int i=0;i<count&&y-i>0;i++)
        {
            if(world.GetBlock(x,y-i,z) is not (BlockType.Stone or BlockType.Dirt))break;
            world.SetGeneratedBlock(x,y-i,z,BlockType.Sandstone);
        }
    }
}
