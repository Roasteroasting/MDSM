using System;
using System.Collections.Generic;

namespace MDSM.Worlds;

internal static class ShoreCropPatch
{
    internal static bool TryPlant(Point3 center,Random random,Func<long,long,Biome> biome,
        Func<long,long,int> surface,Func<long,int,long,BlockType> read,Action<long,int,long,BlockType> write,
        Func<Point3,bool> nearPond,List<CropState> crops)
    {
        Span<Point3> plants=stackalloc Point3[25];
        int count=0;
        for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
        {
            if(dx*dx+dz*dz>5)continue;
            long x=center.X+dx,z=center.Z+dz;
            int y=surface(x,z);
            if(y is <1 or >=World.Height-1 || Math.Abs(y-center.Y)>1 || biome(x,z)!=Biome.Temperate || read(x,y+1,z)!=BlockType.Air)continue;
            if(read(x,y,z) is not (BlockType.Grass or BlockType.Sand))continue;
            var soil=new Point3(x,y,z);
            if(nearPond(soil))plants[count++]=soil;
        }
        if(count<2)return false;

        // Blend only the exposed sandy bank immediately around the patch.
        for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
        {
            long x=center.X+dx,z=center.Z+dz;int y=surface(x,z);
            if(Math.Abs(y-center.Y)<=1 && biome(x,z)==Biome.Temperate &&
                read(x,y,z)==BlockType.Sand && read(x,y+1,z)==BlockType.Air)
                write(x,y,z,BlockType.Grass);
        }
        int planted=Math.Min(count,random.Next(2,6));
        var crop=random.Next(2)==0?BlockType.Carrot1:BlockType.Potato1;
        for(int i=0;i<planted;i++)
        {
            int j=random.Next(i,count);
            Point3 soil=plants[j];plants[j]=plants[i];
            write(soil.X,soil.Y,soil.Z,BlockType.FarmlandWet);
            write(soil.X,soil.Y+1,soil.Z,(BlockType)((int)crop+random.Next(3)));
            double interval=100+random.NextDouble()*300;
            crops.Add(new(new(soil.X,soil.Y+1,soil.Z),interval,interval));
        }
        return true;
    }
}
