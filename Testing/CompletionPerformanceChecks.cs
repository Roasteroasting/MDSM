using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

// Controlled CPU/state stress alongside the real Game1/GPU benchmarks. Never a substitute for FPS.
internal static class CompletionPerformanceChecks
{
    private sealed record Result(WorldSize Size,byte[] Hash,int Mobs,int Crops,int Hostiles,long FileBytes);
    public static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".completion-profile-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            foreach(WorldSize size in Enum.GetValues<WorldSize>())
            {
                string path=Path.Combine(root,size+".dat");Result expected=Generate(size,path);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Read(expected,path);File.Delete(path);GC.Collect();
            }
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Path.GetDirectoryName(Path.GetFullPath(root))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe profile cleanup.");if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static byte[] Hash(World world){using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);foreach(var chunk in world.Chunks)hash.AppendData(chunk.Blocks);return hash.GetHashAndReset();}
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Result Generate(WorldSize size,string path)
    {
        var watch=Stopwatch.StartNew();var world=World.CreateNew(173,size);double generate=watch.Elapsed.TotalSeconds;var cave=world.GeneratedCaves;
        int initialLava=world.LavaFlow.PendingCount;if(initialLava!=0)throw new Exception("Unstable natural lava frontier.");
        MobState[] mobs=PassiveMobPopulation.Generate(world);int[] sheep=Enumerable.Range(0,mobs.Length).Where(i=>mobs[i].Species==MobSpecies.Sheep).Take(2).ToArray();
        // A small constructed underground diagnostic chamber, after measuring natural generation.
        for(int z=32;z<=52;z++)for(int x=32;x<=52;x++)for(int y=19;y<=24;y++)world.SetBlock(x,y,z,y==19?BlockType.Stone:BlockType.Air);
        for(int i=0;i<sheep.Length;i++)mobs[sheep[i]]=mobs[sheep[i]] with {Position=new(38+i,20,40)};
        var population=new PassiveMobPopulation(world,mobs);var inv=new Inventory();inv.SetSlot(0,new(Items.Wheat,64));
        for(int i=0;i<4;i++)inv.SetEquipment(i,new(Items.Armor(ArmorMaterial.Obsidian,(ArmorSlot)i),1));
        var hostile=new MobState[8];for(int i=0;i<8;i++){MobSpecies species=(MobSpecies)(4+i%3);hostile[i]=new(species,new(35+i*1.5f,20,35),0,MobDefinitions.Get(species).MaximumHealth);}
        var session=new GameSession("profile",new(world,"Active systems",new(48,20,48),0,0,inv,Pigs:population.CapturePigs(),Mobs:population.CaptureOthers(),Population:population.CapturePopulation(),Hostiles:hostile));
        foreach(var mob in session.Mobs.Entities)if(mob.Species==MobSpecies.Sheep && mob.Position.Y==20)session.Mobs.Feed(mob,session.Inventory);
        for(int z=40;z<=44;z++)for(int x=40;x<=44;x++){world.SetBlock(x,19,z,BlockType.FarmlandDry);world.SetBlock(x,20,z,BlockType.Wheat1);}
        world.SetBlock(42,19,42,BlockType.WaterSource);world.PlaceStructure(new(50,20,50),BlockType.Torch,0,new(0,-1,0),session.Player.Bounds);
        world.SetBlock(33,20,33,BlockType.LavaSource);world.SetBlock(34,20,33,BlockType.WaterSource);
        int revision=world.Revision;watch.Restart();long allocations=GC.GetAllocatedBytesForCurrentThread();
        for(int tick=0;tick<1200;tick++)
        {
            const float dt=1f/120;session.Player.RestoreHealth(20);session.Player.Update(world,Vector3.Zero,false,dt);
            world.Fluids.Update(dt);world.LavaFlow.Update(dt);world.Farming.Update(dt);world.Containers.Update(dt);world.Vegetation.Update(dt,session.Items);
            session.Mobs.Update(dt,session.Player.Position,session.Player.Bounds,session.Items);session.Hostiles.Update(dt,session.Player,session.Projectiles);
            session.Projectiles.Update(dt,session.Mobs,session.Items,session.ProjectileWear,session.Hostiles,session.Player);session.Items.Update(session.Player,session.Inventory,dt);
            if(tick%120==0){world.SetBlock(42,19,42,tick%240==0?BlockType.Air:BlockType.WaterSource);world.SetBlock(33,20,33,BlockType.Air);world.SetBlock(33,20,33,BlockType.LavaSource);}
            if(session.Hostiles.Entities.Count>8 || world.Fluids.LastProcessedCount>2048 || world.LavaFlow.LastProcessedCount>512 || world.Farming.LastProcessed>512)throw new Exception("Active-system budget exceeded.");
        }
        double milliseconds=watch.Elapsed.TotalMilliseconds;long bytes=GC.GetAllocatedBytesForCurrentThread()-allocations;watch.Restart();world.Farming.Update(2400);double growthMs=watch.Elapsed.TotalMilliseconds;byte[] digest=Hash(world);
        long managed=GC.GetTotalMemory(true);watch.Restart();using(var file=File.Create(path))WorldSerializer.Write(file,session.Capture());double saving=watch.Elapsed.TotalSeconds;long length=new FileInfo(path).Length;
        Console.WriteLine($"CONTENT {size}: generation={generate:F3}s caveMs={cave.Milliseconds:F1} systems={cave.Systems} carved={cave.Carved} pools={cave.LavaPools} surfaceOpenings={cave.SurfaceOpenings} exposedOres={cave.ExposedOres} initialLavaFrontier={initialLava} managedMiB={managed/1048576.0:F1} saveSeconds={saving:F3} saveMiB={length/1048576.0:F2}");
        Console.WriteLine($"ACTIVE {size}: simulatedSeconds=10 cpuMs={milliseconds:F2} allocations={bytes} blockEdits={world.Revision-revision} passive={session.Mobs.Entities.Count} hostiles={session.Hostiles.Entities.Count} crops={world.Farming.Count} growthBatchMs={growthMs:F3} torchProtection={world.Torches.Protected(new(50,20,50))} waterFrontier={world.Fluids.PendingCount} lavaFrontier={world.LavaFlow.PendingCount}");
        return new(size,digest,session.Mobs.Entities.Count,world.Farming.Count,session.Hostiles.Entities.Count,length);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Read(Result result,string path)
    {
        var watch=Stopwatch.StartNew();using var file=File.OpenRead(path);WorldSave save=WorldSerializer.Read(file);
        double loading=watch.Elapsed.TotalSeconds;
        if(!Hash(save.World).AsSpan().SequenceEqual(result.Hash)||save.Pigs.Length+save.Mobs.Length!=result.Mobs||save.World.Farming.Count!=result.Crops||save.Hostiles.Length!=result.Hostiles)throw new Exception("Profile save/load lost state.");
        Console.WriteLine($"RESTORE {result.Size}: loadSeconds={loading:F3} complete block SHA-256 and entity/crop counts match.");
    }
}
