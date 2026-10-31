using System;

namespace MDSM.Worlds;

// A separate seeded pass before ores: no surface/water replacement or initial gravity work.
public static class UndergroundDeposits
{
    private static readonly Point3[] Neighbors={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
    public static int Generate(World world)
    {
        Span<Point3> cells=stackalloc Point3[30];int total=0;
        for(int cz=0;cz<world.ChunksZ;cz++)for(int cx=0;cx<world.ChunksX;cx++)
        {
            var random=new Random(unchecked(world.Seed^cx*628159^cz*192367^0x53414e44));
            if(random.Next(100)>=35)continue;
            Point3 origin=new(cx*16+random.Next(16),random.Next(8,54),cz*16+random.Next(16));
            int target=random.Next(10,31),count=0;
            for(int attempt=0;attempt<200 && count<target;attempt++)
            {
                Point3 p=count==0?origin:cells[random.Next(count)]+Neighbors[random.Next(6)];
                if(p.Y<5 || p.Y>56 || !world.Contains(p.X,p.Y,p.Z) || world.GetBlock(p.X,p.Y,p.Z)!=BlockType.Stone
                    || !world.IsSolid(p.X,p.Y-1,p.Z) || cells[..count].Contains(p))continue;
                // Keep a stone/earth roof even under water features excavated below natural land.
                bool roof=true;for(int y=p.Y+1;y<=p.Y+4;y++)if(!world.IsSolid(p.X,y,p.Z)){roof=false;break;}
                if(roof)cells[count++]=p;
            }
            if(count<10)continue;
            BlockType material=random.Next(2)==0?BlockType.Sand:BlockType.Gravel;
            foreach(Point3 p in cells[..count])world.SetGeneratedBlock(p.X,p.Y,p.Z,material);
            total+=count;
        }
        return total;
    }
}
