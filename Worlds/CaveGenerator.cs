using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public readonly record struct CaveStats(int Systems,int Carved,int LavaPools,int ExposedOres,double Milliseconds,int SurfaceOpenings=0);
public static class CaveGenerator
{
    public static CaveStats Generate(World world,IReadOnlyList<Rectangle> waterRegions=null)
    {
        var timer=Stopwatch.StartNew();var sites=new List<Point3>();int systems=0,carved=0,pools=0,exposed=0,openings=0;
        for(int cz=0;cz<world.ChunksZ;cz++)for(int cx=0;cx<world.ChunksX;cx++)
        {
            var random=new Random(unchecked(world.Seed^cx*1788631^cz*638827^0x43415645));
            if(random.NextDouble()>.18)continue;
            Vector3 start=new(cx*16+random.Next(16),random.Next(9,47),cz*16+random.Next(16));systems++;
            Walk(start,random.NextSingle()*MathHelper.TwoPi,random.Next(55,140),0,random);
        }
        // Final ore exposure pass runs after deposits/ordinary veins, from sparse cave sites.
        world.CaveOreSites=sites;
        return new(systems,carved,pools,exposed,timer.Elapsed.TotalMilliseconds,openings);
        void Walk(Vector3 position,float yaw,int length,int branch,Random random)
        {
            float pitch=(random.NextSingle()-.5f)*.4f;bool entrance=branch==0 && random.Next(10)==0;
            if(entrance){position.Y=Math.Max(6,world.SurfaceY((int)position.X,(int)position.Z)-random.Next(6,13));pitch=-.1f;}
            for(int step=0;step<length;step++)
            {
                yaw+=(random.NextSingle()-.5f)*.4f;pitch=Math.Clamp(pitch*.9f+(random.NextSingle()-.5f)*.12f,-.4f,.4f);
                if(entrance && step>length*2/3)pitch=.65f;
                position+=new Vector3(MathF.Cos(yaw),pitch,MathF.Sin(yaw));
                if(position.X<5||position.Z<5||position.X>world.Width-6||position.Z>world.Depth-6||position.Y<5||position.Y>World.Height-7)break;
                float radius=1.65f+random.NextSingle()*.65f;if(step%39==0)radius+=.6f;
                int x=(int)position.X,y=(int)position.Y,z=(int)position.Z;
                if(Math.Abs(x-world.Width/2)<18 && Math.Abs(z-world.Depth/2)<18)continue;
                if(waterRegions!=null && y+radius>=world.SurfaceY(x,z)-8)
                {
                    bool shoreline=false;foreach(var region in waterRegions)
                        if(x>=region.Left-36 && x<region.Right+36 && z>=region.Top-36 && z<region.Bottom+36){shoreline=true;break;}
                    if(shoreline)continue;
                }
                for(int dy=-(int)Math.Ceiling(radius);dy<=radius;dy++)for(int dz=-(int)Math.Ceiling(radius);dz<=radius;dz++)for(int dx=-(int)Math.Ceiling(radius);dx<=radius;dx++)
                {
                    if(dx*dx+dz*dz+dy*dy*1.15f>radius*radius)continue;
                    int px=x+dx,py=y+dy,pz=z+dz;BlockType b=world.GetBlock(px,py,pz);
                    if(py<2 || b is not (BlockType.Stone or BlockType.Dirt or BlockType.Grass) || !world.Contains(px,py,pz) || Blocks.Falls(world.GetBlock(px,py+1,pz)))continue;
                    int surface=world.SurfaceY(px,pz);
                    if(b!=BlockType.Grass && py>=surface-6 && world.GetBlock(px,surface,pz)==BlockType.Grass)continue;
                    bool wet=false;for(int i=-1;i<=1;i++)if(Lava.Fluid(world.GetBlock(px,py+i,pz))||Lava.Fluid(world.GetBlock(px+1,py+i,pz))||Lava.Fluid(world.GetBlock(px-1,py+i,pz))||Lava.Fluid(world.GetBlock(px,py+i,pz+1))||Lava.Fluid(world.GetBlock(px,py+i,pz-1)))wet=true;
                    if(wet)continue;if(b==BlockType.Grass)openings++;world.SetGeneratedBlock(px,py,pz,BlockType.Air);carved++;
                }
                if(step%8==0 && sites.Count<world.Width*world.Depth/32)sites.Add(new(x,y,z));
                if(branch==0 && step==length/2)Walk(position,yaw+1.2f,length/2,1,random);
                if(step%30==0 && y is >=4 and <=18 && random.Next(3)==0)
                {
                    int floor=y;while(floor>3 && world.GetBlock(x,floor-1,z)==BlockType.Air)floor--;
                    floor--;if(floor<3 || floor>18)continue;bool poolSealed=true;
                    for(int pz=z-2;pz<=z+2;pz++)for(int px=x-2;px<=x+2;px++)
                    {
                        if(!world.IsSolid(px,floor-1,pz))poolSealed=false;
                        if((Math.Abs(px-x)==2 || Math.Abs(pz-z)==2) && !world.IsSolid(px,floor,pz))poolSealed=false;
                    }
                    if(!poolSealed)continue;
                    for(int pz=z-1;pz<=z+1;pz++)for(int px=x-1;px<=x+1;px++)if(world.GetBlock(px,floor,pz) is BlockType.Stone or BlockType.Air)world.SetGeneratedBlock(px,floor,pz,BlockType.LavaSource);
                    pools++;
                }
            }
        }
    }
    internal static int ExposedOres(World world)
    {
        var random=new Random(unchecked(world.Seed^0x4558504f));int added=0,coal=0,iron=0,diamond=0,veins=0;
        Point3[] directions={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
        Span<Point3> cells=stackalloc Point3[8];
        foreach(Point3 site in world.CaveOreSites)
        {
            if(random.NextDouble()>.3)continue;
            BlockType ore=site.Y<=20 && random.Next(16)==0?BlockType.DiamondOre:random.Next(3)==0?BlockType.IronOre:BlockType.CoalOre;
            if(site.Y<4 || site.Y>(ore==BlockType.DiamondOre?20:60))continue;
            Point3 start=site;bool wall=false;
            for(int attempt=0;attempt<12;attempt++)
            {
                start=site+new Point3(random.Next(-3,4),random.Next(-2,3),random.Next(-3,4));
                if(world.GetBlock(start.X,start.Y,start.Z) is not (BlockType.Stone or BlockType.Sand or BlockType.Gravel))continue;
                foreach(var direction in directions)if(world.GetBlock(start.X+direction.X,start.Y+direction.Y,start.Z+direction.Z)==BlockType.Air)wall=true;if(wall)break;
            }
            if(!wall)continue;int target=random.Next(2,9),count=0;
            for(int step=0;step<40 && count<target;step++)
            {
                if(start.Y is >=4 and <=60 && (ore!=BlockType.DiamondOre||start.Y<=20) && world.GetBlock(start.X,start.Y,start.Z) is BlockType.Stone or BlockType.Sand or BlockType.Gravel && !cells[..count].Contains(start))cells[count++]=start;
                start=(count>0?cells[random.Next(count)]:start)+directions[random.Next(6)];
            }
            if(count<2)continue;bool touches=false;foreach(var cell in cells[..count])foreach(var direction in directions)if(world.GetBlock(cell.X+direction.X,cell.Y+direction.Y,cell.Z+direction.Z)==ore)touches=true;
            if(touches)continue;foreach(var cell in cells[..count])world.SetGeneratedBlock(cell.X,cell.Y,cell.Z,ore);added+=count;veins++;
            if(ore==BlockType.CoalOre)coal+=count;else if(ore==BlockType.IronOre)iron+=count;else diamond+=count;
        }
        world.CaveOreSites.Clear();world.NoteCaveOres(coal,iron,diamond,veins);return added;
    }
}
