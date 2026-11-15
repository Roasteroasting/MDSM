using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class DungeonFrequencyChecks
{
    public static int Probe(WorldSize size)
    {
        foreach(int seed in new[]{173,137,-907})Measure(size,seed);
        return 0;
    }
    private static World Measure(WorldSize size,int seed)
    {
        var timer=Stopwatch.StartNew();var world=World.CreateNew(seed,size);
        Console.WriteLine($"DUNGEONS {size} seed={seed}: {world.GeneratedDungeons.Count} rooms / {world.GeneratedCaves.Systems} cave walks ({world.GeneratedDungeons.Count/(double)Math.Max(1,world.GeneratedCaves.Systems):P1}), generation {timer.Elapsed.TotalSeconds:F2}s.");
        return world;
    }
    public static void Generation()
    {
        foreach(var size in WorldSizes.Finite)
        foreach(int seed in size<=WorldSize.Normal?new[]{173,137,-907}:new[]{173})
        {
            World world=Measure(size,seed);
            int count=world.GeneratedDungeons.Count,systems=world.GeneratedCaves.Systems;
            Check(count>=systems*.4 && count<=DungeonGenerator.Target(systems),"Dungeon population is not approximately one per two cave walks.");
            Check(world.CaveSystemSites.Count==0 && world.CaveOreSites.Count==0,"Generation retained temporary cave-site data.");
            foreach(Point3 room in world.GeneratedDungeons)
            {
                Check(room.X>=8 && room.Z>=8 && room.X<world.Width-8 && room.Z<world.Depth-8 && room.Y>=3,"Dungeon outside safe boundaries.");
                Check(Math.Abs(room.X-world.Width/2)>=20 || Math.Abs(room.Z-world.Depth/2)>=20,"Dungeon intersected protected spawn.");
                bool accessible=false;for(int i=0;i<4;i++)
                {
                    Point3 d=World.FacingDirection(i),door=room+new Point3(d.X*3,0,d.Z*3),outer=door+d;
                    if(world.GetBlock(door.X,door.Y,door.Z)==BlockType.Air && world.GetBlock(door.X,door.Y+1,door.Z)==BlockType.Air
                        && world.GetBlock(outer.X,outer.Y,outer.Z)==BlockType.Air && world.GetBlock(outer.X,outer.Y+1,outer.Z)==BlockType.Air)accessible=true;
                }
                Check(accessible,"Dungeon has no two-block cave entrance.");
                int chests=0;for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++)
                {
                    Point3 cell=room+new Point3(x,0,z);BlockType floor=world.GetBlock(cell.X,cell.Y-1,cell.Z);
                    Check(floor is BlockType.Cobblestone or BlockType.MossyCobblestone,"Dungeon lacks intact floor.");
                    if(world.GetBlock(cell.X,cell.Y,cell.Z)==BlockType.Chest){chests++;Check(!world.Containers.Get(cell).Empty,"Empty dungeon chest.");}
                }
                Check(chests is >=1 and <=3,"Dungeon lost its original chest budget.");
            }
            for(int i=0;i<count;i++)for(int j=0;j<i;j++)
            {
                Point3 a=world.GeneratedDungeons[i],b=world.GeneratedDungeons[j];
                Check(Vector3.DistanceSquared(new(a.X,a.Y,a.Z),new(b.X,b.Y,b.Z))>=18*18,"Rooms clustered/overlapped.");
            }
            if(size==WorldSize.Normal && seed==173)
            {
                var again=World.CreateNew(seed,size);
                Check(world.GeneratedDungeons.SequenceEqual(again.GeneratedDungeons),"Room positions are not deterministic.");
                foreach(var entry in world.Containers.Entries)
                {
                    var other=again.Containers.Get(entry.Key);
                    // Equipment intentionally gets a fresh identity in a new world;
                    // deterministic loot concerns type, quantity and durability.
                    for(int i=0;i<entry.Value.SlotCount;i++)
                    {var a=entry.Value[i];var b=other[i];Check(a.Item==b.Item && a.Count==b.Count && a.Durability==b.Durability,"Dungeon loot is not deterministic.");}
                }
                using var data=new MemoryStream();WorldSerializer.Write(data,new(world,"Dungeons",world.Spawn,0,0));data.Position=0;
                var loaded=WorldSerializer.Read(data).World;
                Check(loaded.GeneratedDungeons.Count==0 && loaded.Containers.Entries.Count==world.Containers.Entries.Count,"Loading regenerated dungeons or lost chests.");
                foreach(Point3 room in world.GeneratedDungeons)for(int y=-1;y<=5;y++)for(int z=-3;z<=3;z++)for(int x=-3;x<=3;x++)
                    Check(world.GetBlock(room.X+x,room.Y+y,room.Z+z)==loaded.GetBlock(room.X+x,room.Y+y,room.Z+z),"Dungeon terrain changed during save/load.");
                foreach(var entry in world.Containers.Entries)
                {var other=loaded.Containers.Get(entry.Key);for(int i=0;i<entry.Value.SlotCount;i++)Check(entry.Value[i]==other[i],"Dungeon chest contents changed during save/load.");}
            }
        }
    }
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
