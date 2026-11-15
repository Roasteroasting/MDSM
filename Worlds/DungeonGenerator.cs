using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MDSM.Inventories;

namespace MDSM.Worlds;

// Small cave-connected rooms. Candidate positions come from the cave walk, never
// a full-volume search, and rejected candidates leave all terrain untouched.
public static class DungeonGenerator
{
    // About half of the 18%-per-chunk cave walks; actual targets use the
    // generated cave count instead of an unrelated horizontal-area room cap.
    public static int Budget(WorldSize size)=>size.Width()/16*(size.Width()/16)*9/100;
    public static int Target(int caveSystems)=>(caveSystems+1)/2;
    internal const int AttemptsPerSystem=48,MinimumSpacing=18;
    internal static IReadOnlyList<Point3> Generate(World world)
    {
        var random=new Random(unchecked(world.Seed^0x44554e47));var rooms=new List<Point3>();
        if(world.CaveOreSites.Count==0){world.CaveSystemSites.Clear();return rooms;}
        int target=Target(world.CaveSystemSites.Count);
        var systems=new int[world.CaveSystemSites.Count];for(int i=0;i<systems.Length;i++)systems[i]=i;
        for(int i=systems.Length-1;i>0;i--){int j=random.Next(i+1);(systems[i],systems[j])=(systems[j],systems[i]);}
        var nearby=new Dictionary<Point3,List<Point3>>();
        foreach(int system in systems)
        {
            if(rooms.Count>=target)break;
            var range=world.CaveSystemSites[system];if(range.Count==0)continue;
            int start=random.Next(range.Count);
            // Try distinct sites before retrying at a slightly different floor
            // height. No full-world scan, and at most one room per cave walk.
            for(int attempt=0;attempt<Math.Min(AttemptsPerSystem,range.Count*3);attempt++)
            {
                Point3 site=world.CaveOreSites[range.Start+(start+attempt)%range.Count];
                int shift=attempt/range.Count;
                Point3 floor=new(site.X,site.Y-2+(shift==1?1:shift==2?-1:0),site.Z);
                Point3 bucket=new(floor.X/MinimumSpacing,floor.Y/MinimumSpacing,floor.Z/MinimumSpacing);
                bool close=false;
                for(int z=-1;z<=1 && !close;z++)for(int y=-1;y<=1 && !close;y++)for(int x=-1;x<=1 && !close;x++)
                    if(nearby.TryGetValue(bucket+new Point3(x,y,z),out var neighbors))foreach(Point3 other in neighbors)
                        if(Vector3.DistanceSquared(new(other.X,other.Y,other.Z),new(floor.X,floor.Y,floor.Z))<MinimumSpacing*MinimumSpacing){close=true;break;}
                if(close || !TryRoom(world,floor,random))continue;
                rooms.Add(floor);if(!nearby.TryGetValue(bucket,out var group))nearby.Add(bucket,group=new());group.Add(floor);
                break;
            }
        }
        world.CaveSystemSites.Clear();
        return rooms;
    }
    internal static bool TryRoom(World world,Point3 center,Random random)
    {
        if(center.Y<3 || center.Y+6>=world.SurfaceY(center.X,center.Z) || center.X<8 || center.Z<8 || center.X>=world.Width-8 || center.Z>=world.Depth-8
            || Math.Abs(center.X-world.Width/2)<20 && Math.Abs(center.Z-world.Depth/2)<20)return false;
        int air=0,footing=0;Point3 entrance=default;bool accessible=false;
        for(int z=-3;z<=3;z++)for(int x=-3;x<=3;x++)
        {
            if(world.IsSolid(center.X+x,center.Y-2,center.Z+z))footing++;
            for(int y=-1;y<=5;y++)
            {
                BlockType b=world.GetBlock(center.X+x,center.Y+y,center.Z+z);
                if(Lava.Fluid(b)||BlockEntities.HasState(b)||b==BlockType.Bedrock || Blocks.Get(b).Shape is BlockShape.Slab or BlockShape.Stairs)return false;
                if(Math.Abs(x)<=2 && Math.Abs(z)<=2 && y>=0 && y<5 && b==BlockType.Air)air++;
            }
        }
        if(air<30 || footing<20)return false;
        for(int facing=0;facing<4;facing++)
        {
            Point3 d=World.FacingDirection(facing),outer=center+new Point3(d.X*4,0,d.Z*4);
            if(world.GetBlock(outer.X,outer.Y,outer.Z)==BlockType.Air && world.GetBlock(outer.X,outer.Y+1,outer.Z)==BlockType.Air)
            {entrance=new(d.X*3,0,d.Z*3);accessible=true;break;}
        }
        if(!accessible)return false;
        for(int y=-1;y<=5;y++)for(int z=-3;z<=3;z++)for(int x=-3;x<=3;x++)
        {
            bool wall=y==-1||y==5||Math.Abs(x)==3||Math.Abs(z)==3;
            bool doorway=x==entrance.X && z==entrance.Z && y is 0 or 1;
            world.SetGeneratedBlock(center.X+x,center.Y+y,center.Z+z,wall && !doorway
                ?random.NextDouble()<(y==-1?.55:.3)?BlockType.MossyCobblestone:BlockType.Cobblestone:BlockType.Air);
        }
        int chests=random.Next(1,4);
        Point3[] corners={new(-2,0,-2),new(2,0,2),new(2,0,-2),new(-2,0,2)};
        int offset=random.Next(4);
        for(int i=0;i<chests;i++)
        {
            Point3 cell=center+corners[(offset+i)%4];world.SetGeneratedBlock(cell.X,cell.Y,cell.Z,BlockType.Chest);
            var chest=world.Containers.Get(cell);int rolls=random.Next(4,9);
            for(int roll=0;roll<rolls;roll++)
            {
                ItemId item=random.Next(10) switch{0=>Items.Armor(ArmorMaterial.Iron,(ArmorSlot)random.Next(4)),1=>Items.IronIngot,2=>Items.String,3=>Items.Bread,4=>Items.Potato,5=>Items.Carrot,6=>Items.Arrow,7=>Items.Coal,8=>Items.Apple,_=>Items.Stick};
                chest.SetSlot(roll,new(item,Items.StackLimit(item)==1?1:random.Next(1,6)));
            }
        }
        return true;
    }
}
