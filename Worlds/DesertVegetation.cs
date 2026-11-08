using System;

namespace MDSM.Worlds;

public sealed partial class World
{
    internal bool CanCactus(Point3 cell,Point3? changed=null,BlockType next=BlockType.Air)
    {
        BlockType At(Point3 p)=>p==changed?next:GetBlock(p.X,p.Y,p.Z);
        if(At(cell+new Point3(0,-1,0)) is not (BlockType.Sand or BlockType.Cactus))return false;
        for(int i=0;i<4;i++){BlockType b=At(cell+FacingDirection(i));if(Blocks.IsSolid(b)||Lava.Fluid(b))return false;}
        return Contains(cell.X,cell.Y,cell.Z);
    }
}

internal static class DesertVegetation
{
    internal static void Generate(World world)
    {
        if(!world.Climate.VariedGeneration)return;
        for(int z=2;z<world.Depth-2;z++)for(int x=2;x<world.Width-2;x++)
        {
            if(world.Climate.At(x,z)!=Biome.Desert || Math.Abs(x-world.Width/2)<12 && Math.Abs(z-world.Depth/2)<12)continue;
            uint h=Hash(world.Seed,x,z);int ground=world.SurfaceY(x,z);
            if(world.GetBlock(x,ground,z)!=BlockType.Sand || world.GetBlock(x,ground+1,z)!=BlockType.Air)continue;
            if(h%200<2)
            {
                int height=2+(int)((h>>10)%3);bool clear=ground+height<World.Height;
                for(int y=ground+1;clear && y<=ground+height;y++)
                {
                    clear=world.GetBlock(x,y,z)==BlockType.Air;
                    for(int i=0;i<4 && clear;i++){Point3 d=World.FacingDirection(i);clear=world.GetBlock(x+d.X,y,z+d.Z)==BlockType.Air;}
                }
                if(clear)for(int y=1;y<=height;y++)world.SetGeneratedBlock(x,ground+y,z,BlockType.Cactus);
            }
            else if(h%100<4)world.SetGeneratedBlock(x,ground+1,z,BlockType.DeadBush);
        }
    }
    internal static uint Hash(int seed,int x,int z)
    {uint h=unchecked((uint)(seed^x*73856093^z*19349663^0x44455345));h=(h^(h>>16))*0x7feb352d;return h^(h>>15);}
}
