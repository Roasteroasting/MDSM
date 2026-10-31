using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MDSM.Worlds;

public readonly record struct OreGenerationStats(int Coal,int Iron,int Diamond,int Veins,double Milliseconds);
public static class OreGenerator
{
    private static readonly Point3[] Directions={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
    public static OreGenerationStats Generate(World world)
    {
        var watch=Stopwatch.StartNew();int coal=0,iron=0,diamond=0,veins=0;
        Span<Point3> cells=stackalloc Point3[8];
        for(int cz=0;cz<world.ChunksZ;cz++)for(int cx=0;cx<world.ChunksX;cx++)
        {
            var random=new Random(unchecked(world.Seed^cx*73428767^cz*912367^0x4f524553));
            for(int material=0;material<3;material++)for(int attempt=0;attempt<(material==0?18:material==1?10:1);attempt++)
            {
                BlockType block=material==0?BlockType.CoalOre:material==1?BlockType.IronOre:BlockType.DiamondOre;
                int ceiling=material==2?20:60,target=random.Next(2,9),count=0;
                Point3 cell=new(cx*16+random.Next(16),random.Next(4,ceiling+1),cz*16+random.Next(16));
                for(int step=0;step<32 && count<target;step++)
                {
                    if(world.Contains(cell.X,cell.Y,cell.Z) && cell.Y>=4 && cell.Y<=ceiling && world.GetBlock(cell.X,cell.Y,cell.Z) is BlockType.Stone or BlockType.Sand or BlockType.Gravel
                        && !cells[..count].Contains(cell))cells[count++]=cell;
                    cell=(count>0?cells[random.Next(count)]:cell)+Directions[random.Next(6)];
                }
                if(count<2)continue;
                bool touches=false;
                foreach(Point3 point in cells[..count])foreach(Point3 offset in Directions)
                    if(world.GetBlock(point.X+offset.X,point.Y+offset.Y,point.Z+offset.Z)==block)touches=true;
                if(touches)continue;
                foreach(Point3 point in cells[..count])world.SetGeneratedBlock(point.X,point.Y,point.Z,block);
                if(material==0)coal+=count;else if(material==1)iron+=count;else diamond+=count;
                veins++;
            }
        }
        return new(coal,iron,diamond,veins,watch.Elapsed.TotalMilliseconds);
    }
}
