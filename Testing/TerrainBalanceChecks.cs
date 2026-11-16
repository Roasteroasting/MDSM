using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using MDSM.Persistence;
using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;
namespace MDSM.Testing;
internal static class TerrainBalanceChecks
{
    internal static readonly (string Name,Action Check)[] All = {
        ("Infinite terrain density and carved-cavity diagnostics", Diagnose),
        ("Regional relief, tunnel continuity, water protection and version-pinned worlds", Integrity) };
    internal static int Run(){try{foreach(var t in All){t.Check();Console.WriteLine("PASS: "+t.Name);}return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static void Diagnose()
    {
        foreach(int seed in new[]{173,-907,137})
        {
            var g=new BetaDensityGenerator(seed);var watch=Stopwatch.StartNew();long[] empty=new long[4],carved=new long[4],lava=new long[4];int columns=0,min=128,max=0;long heights=0;
            foreach(var a in new[]{new ChunkAddress(0,0),new(8,-9),new(-24,18),new(50,35),new(-70,-45),new(100,-100)})
            {
                byte[] raw=g.Generate(a);
                byte[] before=new InfiniteFeatures(g,seed,a,default).GenerateTerrain(false),full=new InfiniteFeatures(g,seed,a,default).GenerateTerrain();
                Check(raw.SequenceEqual(new BetaDensityGenerator(seed).Generate(a)),"Base terrain lost determinism.");
                for(int z=0;z<16;z++)for(int x=0;x<16;x++)
                {
                    int top=127;while(top>0&&!Blocks.IsSolid((BlockType)raw[Coordinates.Index(x,top,z)]))top--;
                    columns++;min=Math.Min(min,top);max=Math.Max(max,top);heights+=top;
                    for(int y=1;y<=60;y++)
                    {
                        int i=Coordinates.Index(x,y,z),band=(y-1)/15;bool solid=Blocks.IsSolid((BlockType)raw[i]);
                        if(!solid)empty[band]++;
                        if(Blocks.IsSolid((BlockType)before[i])&&full[i] is (byte)BlockType.Air or (byte)BlockType.LavaSource)carved[band]++;
                        if(full[i]==(byte)BlockType.LavaSource)lava[band]++;
                        if(Water.IsWater((BlockType)before[i]))Check(before[i]==full[i],"Carver replaced generated water.");
                        if(full[i]==(byte)BlockType.LavaSource)Check(y<10,"Cave lava escaped its deep layer.");
                    }
                }
            }
            Console.WriteLine($"seed={seed} surface={min}..{max} mean={heights/(double)columns:F1}; {watch.Elapsed.TotalSeconds:F2}s");
            for(int b=0;b<4;b++)Console.WriteLine($"  Y={b*15+1}..{b*15+15}: baseVoid={100.0*empty[b]/(columns*15):F2}% carved={100.0*carved[b]/(columns*15):F2}% lava={100.0*lava[b]/(columns*15):F2}%");
            Check(empty[0]==0&&empty[1]==0&&empty[2]<columns*15*.01,"Ordinary underground base terrain is not predominantly solid.");
            Check(carved.Sum()>columns*15*.01&&carved.Sum()<columns*60*.2,"Tunnel volume is absent or excessive.");
            Check(watch.Elapsed.TotalSeconds<30,"Representative generation exceeded its bounded work allowance.");
        }
        // Compare the same complete production workload, including ores,
        // containers, vegetation and animals, without reusing either cache.
        foreach(int version in new[]{1,2,BetaDensityGenerator.Version}.Distinct())
        {
            var watch=Stopwatch.StartNew();long start=GC.GetAllocatedBytesForCurrentThread();var g=new BetaDensityGenerator(173,version);
            foreach(var a in new[]{new ChunkAddress(0,0),new(1,0),new(0,1),new(1,1),new(-24,18),new(50,35)})g.GenerateComplete(a);
            Console.WriteLine($"Complete production v{version}: 6 chunks {watch.Elapsed.TotalMilliseconds:F0}ms, allocated {(GC.GetAllocatedBytesForCurrentThread()-start)/1048576.0:F1} MiB (cumulative, not peak/resident memory).");
        }
    }
    private static void Integrity()
    {
        int gentle=0,mountains=0,overhangs=0;var g=new BetaDensityGenerator(173);
        for(int rz=-5;rz<=5;rz++)for(int rx=-5;rx<=5;rx++)
        {
            byte[] blocks=g.Generate(new(rx*25,rz*25));int min=128,max=0;
            for(int z=0;z<16;z+=3)for(int x=0;x<16;x+=3)
            {
                int y=127;while(y>0&&!Blocks.IsSolid((BlockType)blocks[Coordinates.Index(x,y,z)]))y--;
                min=Math.Min(min,y);max=Math.Max(max,y);bool air=false;
                for(int py=1;py<y;py++){if(blocks[Coordinates.Index(x,py,z)]==(byte)BlockType.Air)air=true;else if(air) {overhangs++;break;}}
            }
            if(max-min<=8&&max<85)gentle++;if(max>=90)mountains++;
        }
        Console.WriteLine($"121 regional chunks: gentle={gentle}, high={mountains}, overhang columns={overhangs}");
        Check(gentle>30&&mountains>0&&overhangs>0,"Regional balance flattened terrain or lost irregular 3D formations.");
        int crossings=0,cells=0;
        for(int cz=-2;cz<=2;cz++)for(int cx=-2;cx<=2;cx++)
        {
            var a=new ChunkAddress(cx,cz);var mask=new InfiniteCaves(173,a,(_,_,_)=>BlockType.Stone,default).Mask;
            var next=new InfiniteCaves(173,new(cx+1,cz),(_,_,_)=>BlockType.Stone,default).Mask;
            Check(mask.SequenceEqual(new InfiniteCaves(173,a,(_,_,_)=>BlockType.Stone,default).Mask),"Cave masks depend on request order.");
            cells+=mask.Count(b=>b!=0);
            for(int y=1;y<120;y++)for(int z=0;z<16;z++)if(mask[Coordinates.Index(15,y,z)]!=0&&next[Coordinates.Index(0,y,z)]!=0)crossings++;
            var wet=new InfiniteCaves(173,a,(_,_,_)=>BlockType.WaterSource,default);
            Check(wet.CarvedCells==0,"Caves breached water.");
        }
        Check(cells>0&&crossings>0,"Tunnels did not connect across chunk boundaries.");
        Console.WriteLine($"Cave masks: {cells} cells, {crossings} X-boundary connections in 25 chunks.");
        string parent=Path.GetFullPath(AppContext.BaseDirectory),path=Path.Combine(parent,".terrain-version-"+Guid.NewGuid().ToString("N"));
        World world=null,loaded=null;
        try
        {
            foreach(int version in new[]{1,2})
            {
            string directory=Path.Combine(path,"v"+version);
            world=World.OpenStreamed(173,Path.Combine(directory,"regions"),2,0,0,generatorVersion:version);
            var clock=Stopwatch.StartNew();
            while(!world.IsLoaded(new(0,0,0))){world.UpdateStreaming(new(0,0));if(world.ChunkLifecycle.Error!=null)throw world.ChunkLifecycle.Error;Check(clock.Elapsed.TotalSeconds<30,"Legacy generation stalled.");Thread.Sleep(1);}
            byte[] old=new BetaDensityGenerator(173,version).GenerateComplete(new(0,0)).Blocks;
            for(int y=0;y<128;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)Check((byte)world.GetBlock(x,y,z)==old[Coordinates.Index(x,y,z)],"Legacy generator changed.");
            InfiniteWorldPersistence.Save(directory,new(world,"Legacy generation",new Vector3(0,100,0),0,0));
            world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();world=null;
            var save=InfiniteWorldPersistence.Load(directory);loaded=save.World;
            Check(loaded.ChunkLifecycle.GeneratorVersion==version,"Legacy world was silently upgraded, risking seams.");
            clock.Restart();
            while(!loaded.IsLoaded(new(0,0,0))){loaded.UpdateStreaming(new(0,0));if(loaded.ChunkLifecycle.Error!=null)throw loaded.ChunkLifecycle.Error;Check(clock.Elapsed.TotalSeconds<30,"Legacy reload stalled.");Thread.Sleep(1);}
            for(int y=0;y<128;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)Check((byte)loaded.GetBlock(x,y,z)==old[Coordinates.Index(x,y,z)],"Legacy saved chunk changed on reload.");
            var snapshot=new ChunkSnapshot(new(-1,0),version,old);
            Check(ChunkSnapshot.Decode(snapshot.Address,snapshot.Encode()).Blocks.SequenceEqual(old),"Legacy chunk record changed.");
            InfiniteWorldPersistence.Save(directory,save);
            loaded.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();loaded=null;
            }
        }
        finally
        {
            if(world!=null)world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();if(loaded!=null)loaded.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if(Path.GetDirectoryName(Path.GetFullPath(path))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe cleanup.");
            if(Directory.Exists(path))Directory.Delete(path,true);
        }
    }
}
