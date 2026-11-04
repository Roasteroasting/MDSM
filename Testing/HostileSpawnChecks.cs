using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class HostileSpawnChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Verify()
    {
        Check(HostilePopulation.Maximum==16 && HostilePopulation.MaximumSpawnAttempts==128
            && HostilePopulation.MinimumSpawnSeconds==2 && HostilePopulation.MaximumSpawnSeconds==4
            && HostilePopulation.MinimumSpawnDistance==24 && HostilePopulation.SpawnRadius==64
            && HostilePopulation.ActiveRadius==64 && HostilePopulation.DespawnRadius==128,"Agreed spawn settings changed.");
        var world=new World(173,WorldSize.Small,false);
        // A three-block-high underground chamber with solid rock above it, not a surface-height fixture.
        for(int z=35;z<222;z++)for(int x=35;x<222;x++)
        {
            world.SetGeneratedBlock(x,19,z,BlockType.Stone);
            for(int y=23;y<=50;y++)world.SetGeneratedBlock(x,y,z,BlockType.Stone);
        }
        var player=new Player(new(128.5f,20,128.5f)){Settings=new(){GameMode=GameMode.Creative}};
        var population=new HostilePopulation(world);var arrows=new Projectiles(world);
        population.Update(4.99f,player,arrows);Check(population.Entities.Count==0 && population.LastSpawnAttempts==0,"Initial delay was bypassed.");
        population.Update(.02f,player,arrows);
        Check(population.Entities.Count==1 && population.LastSpawnSearch.Spawned==1,"Floor-aware search failed in a suitable chamber.");
        Check(population.Entities[0].Position.Y==20,"Search used the surface/ceiling instead of the nearby cave floor.");
        for(int cycle=0;cycle<30 && population.Entities.Count<HostilePopulation.Maximum;cycle++)
        {
            double interval=population.SpawnSecondsRemaining;
            Check(interval>=2 && interval<=4,"Spawn interval escaped 2-4 seconds.");
            int before=population.Entities.Count;
            population.Update((float)(interval+.001),player,arrows);
            Check(population.Entities.Count>=before && population.Entities.Count<=Math.Min(16,before+1),"More than one hostile spawned in a cycle.");
            var stats=population.LastSpawnSearch;
            Check(stats.CandidateColumns<=128 && stats.FloorChecks<=128*47,"Search work was unbounded.");
            Check(stats.CandidateColumns==stats.Spawned+stats.OutsideWorld+stats.Distance+stats.NoFloor+stats.Blocked+stats.Fluid+stats.Torch+stats.Occupied,"Diagnostic accounting is inconsistent.");
        }
        population.Update(4.01f,player,arrows);
        Check(population.Entities.Count==16 && population.LastSpawnAttempts==0,"Population did not reach or respect the shared cap.");
        foreach(var mob in population.Entities)
        {
            float distance=Vector3.Distance(mob.Position,player.Position);
            Check(distance>=24 && distance<=64 && !world.IntersectsSolid(mob.Bounds),"Spawn escaped distance/clearance restrictions.");
        }
        Check(population.ActiveCount==16,"New nearby hostiles are not active.");
        var drops=new DroppedItems(world);Check(population.Hit(population.Entities[0],100,player.Position,drops),"Could not remove a hostile for replenishment.");
        population.Update(4.01f,player,arrows);Check(population.Entities.Count==16,"Spawning failed to refill a freed slot.");

        // Radius, collision volume, fluid and finite-boundary safeguards stay in effect.
        Check(!population.Suitable(MobSpecies.Zombie,new(140.5f,20,128.5f),player.Position)
            && !population.Suitable(MobSpecies.Zombie,new(200.5f,20,128.5f),player.Position)
            && !population.Suitable(MobSpecies.Zombie,new(1.5f,20,128.5f),player.Position),"Distance/edge protections failed.");
        world.SetBlock(160,21,128,BlockType.Stone);
        Check(!population.Suitable(MobSpecies.Zombie,new(160.5f,20,128.5f),player.Position)
            && population.Suitable(MobSpecies.Spider,new(160.5f,20,128.5f),player.Position),"Species-specific headroom was ignored.");
        world.SetBlock(160,21,128,BlockType.Air);world.SetBlock(160,20,128,BlockType.WaterSource);
        Check(!population.Suitable(MobSpecies.Spider,new(160.5f,20,128.5f),player.Position),"Water spawn was accepted.");
        world.SetBlock(160,20,128,BlockType.LavaSource);
        Check(!population.Suitable(MobSpecies.Spider,new(160.5f,20,128.5f),player.Position),"Lava spawn was accepted.");world.SetBlock(160,20,128,BlockType.Air);

        // Fully Torch-protected chamber must fail without scanning the entire world or retrying every frame.
        for(int z=64;z<=192;z+=16)for(int x=64;x<=192;x+=16)world.SetBlock(x,20,z,BlockType.Torch);
        var protectedPopulation=new HostilePopulation(world);protectedPopulation.Update(5.01f,player,arrows);
        Check(protectedPopulation.Entities.Count==0 && protectedPopulation.LastSpawnAttempts==128
            && protectedPopulation.LastSpawnSearch.Torch>100
            && protectedPopulation.LastSpawnSearch.Torch+protectedPopulation.LastSpawnSearch.Distance==128,"Torch rejection/search budget incorrect.");
        HostileSpawnSearch lastSearch=protectedPopulation.LastSpawnSearch;protectedPopulation.Update(.001f,player,arrows);
        Check(protectedPopulation.LastSpawnAttempts==0 && protectedPopulation.LastSpawnSearch==lastSearch,"Failed spawning retried every tick or discarded diagnostics.");
        Console.WriteLine($"  Protected search: {lastSearch.CandidateColumns} columns, {lastSearch.FloorChecks} floor checks, {lastSearch.Torch} Torch rejections.");

        // Variable-length version-10 entity records support both the former cap and the new cap.
        foreach(int count in new[]{8,16})
        {
            MobState[] states=population.Capture().Take(count).ToArray();
            using var stream=new MemoryStream();WorldSerializer.Write(stream,new(world,"Spawn cap",player.Position,0,0,Hostiles:states));stream.Position=0;
            WorldSave restored=WorldSerializer.Read(stream);
            Check(restored.Hostiles.AsSpan().SequenceEqual(states),"Old/new hostile populations changed in a save roundtrip.");
        }
        bool rejected=false;
        try{using var stream=new MemoryStream();WorldSerializer.Write(stream,new(world,"Invalid cap",player.Position,0,0,Hostiles:Enumerable.Repeat(population.Capture()[0],17).ToArray()));}
        catch(InvalidDataException){rejected=true;}Check(rejected,"Oversized saved population was accepted.");
        foreach(int seed in new[]{173,-907})
        {
            var natural=World.CreateNew(seed,WorldSize.Small);var random=new Random(seed);Player explorer=null;
            for(int attempt=0;attempt<100000 && explorer==null;attempt++)
            {
                Vector3 feet=new(random.Next(16,natural.Width-16)+.5f,random.Next(6,45),random.Next(16,natural.Depth-16)+.5f);
                var candidate=new Player(feet){Settings=new(){GameMode=GameMode.Creative}};
                if(natural.IsSolid((int)feet.X,(int)feet.Y-1,(int)feet.Z) && !natural.IntersectsSolid(candidate.Bounds)
                    && !Water.Intersects(natural,candidate.Bounds) && !Lava.Intersects(natural,candidate.Bounds))explorer=candidate;
            }
            Check(explorer!=null,"No valid generated cave floor found for spawn verification.");
            var enemies=new HostilePopulation(natural);var projectiles=new Projectiles(natural);int checks=0;double seconds=5.01;
            for(int cycle=0;cycle<10;cycle++)
            {
                enemies.Update((float)seconds,explorer,projectiles);checks+=enemies.LastSpawnSearch.FloorChecks;
                seconds=enemies.SpawnSecondsRemaining+.001;
            }
            Check(enemies.Entities.Count>=3 && enemies.LastSpawnSearch.FloorChecks<=128*47,"Generated cave spawning remained excessively sparse or unbounded.");
            Console.WriteLine($"  Natural Small seed {seed}, cave at {explorer.Position}: {enemies.Entities.Count} hostiles / 10 cycles, {checks} floor checks total.");
        }
    }
    public static int Run()
    {
        try{Verify();Console.WriteLine("PASS: Hostile spawn tuning, floor search, intervals, caps, diagnostics, safeguards and save compatibility.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
