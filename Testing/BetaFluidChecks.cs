using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class BetaFluidChecks
{
    private static void Check(bool value,string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    internal static readonly (string Name,Action Check)[] All =
    {
        ("Scheduled fluid clocks, drop routing and waterfall metadata", Routes),
        ("Water current direction and player drift", Currents),
        ("Fluid frontiers and falling levels survive Infinite streaming and reload", Streaming),
        ("Water bursts respect frame budgets and defer unfinished work", FrameBudget)
    };

    internal static int Run()
    {
        var checks=new (string Name,Action Check)[]
        {
            ("Water IDs and placement",WaterChecks.StatesAndPlacement),
            ("Waterfall, stream reach and obstacles",WaterChecks.Propagation),
            ("Infinite pools and water retraction",WaterChecks.RecessionAndRegeneration),
            ("Update budget and fixed-time scheduling",WaterChecks.BoundariesAndScheduling),
            ("Water level geometry and shared corners",WaterChecks.Geometry),
            ("Swimming and surface bobbing",()=>{WaterChecks.Swimming();WaterChecks.SurfaceBobbing();}),
            ("Water block and frontier persistence",WaterChecks.Persistence),
            ("Lava scheduling, contact, and bucket interactions",()=>{CompletionChecks.Fluids();CompletionChecks.Buckets();})
        };
        int failures=0;
        void Verify((string Name,Action Check) test)
        {
            try { test.Check(); Console.WriteLine("PASS: "+test.Name); }
            catch (Exception e) { failures++; Console.Error.WriteLine("FAIL: "+test.Name+" "+e); }
        }
        foreach (var test in checks) Verify(test);
        foreach (var test in All) Verify(test);
        return failures==0 ? 0 : 1;
    }

    private static void Routes()
    {
        var world=WaterChecks.Floor();
        for (int x=14; x<=26; x++)
        for (int z=14; z<=26; z++)
            if (x!=25 || z!=20) world.SetGeneratedBlock(x,4,z,BlockType.Stone);
        world.Fluids.Update(.17);
        world.SetBlock(20,5,20,BlockType.WaterSource);
        world.Fluids.Update(.24);
        Check(world.GetBlock(21,5,20)==BlockType.Air,"A late edit borrowed time from the previous fluid interval.");
        world.Fluids.Update(.01);
        Check(world.GetBlock(21,5,20)==Water.Flow(1) && world.GetBlock(19,5,20)==BlockType.Air &&
              world.GetBlock(20,5,19)==BlockType.Air && world.GetBlock(20,5,21)==BlockType.Air,
              "The four-step lookahead did not prefer the route to the drop.");
        for(int i=0;i<5;i++) world.Fluids.Tick();
        Check(world.GetBlock(25,5,20)==Water.Flow(5) && Water.Level(world.GetBlock(25,4,20))==13,
              "The stream did not carry its incoming level into the waterfall.");
        WaterChecks.Settle(world);
        Check(Water.Level(world.GetBlock(25,1,20))==13 && world.GetBlock(26,1,20)==Water.Flow(1),
              "The falling column lost its level or failed to spread at its foot.");

        var pool=WaterChecks.Floor();
        pool.SetBlock(30,1,30,BlockType.WaterSource);
        pool.SetBlock(29,2,30,BlockType.WaterSource);
        pool.SetBlock(31,2,30,BlockType.WaterSource);
        WaterChecks.Settle(pool);
        Check(pool.GetBlock(30,2,30)==Water.Flow(1),"Beta water incorrectly regenerated above another source.");

        var lava=WaterChecks.Floor();
        lava.SetBlock(20,1,20,BlockType.LavaSource);
        for(int i=0;i<12;i++) lava.LavaFlow.Tick();
        Check(lava.GetBlock(21,1,20)==BlockType.LavaFlow2 && lava.GetBlock(23,1,20)==BlockType.LavaFlow6
            && lava.GetBlock(24,1,20)==BlockType.Air,"Lava did not thin by two levels per block.");
        var inventory = new Inventory();
        inventory.SetSlot(0,new(Items.FromBlock(BlockType.Stone),2));
        Check(BlockPlacement.TryPlace(lava,inventory,new Point3(20,1,20),default)
            && inventory.Selected.Count==1 && BlockPlacement.TryPlace(lava,inventory,new Point3(21,1,20),default,creative:true)
            && inventory.Selected.Count==1,"Solid placement did not displace lava or respect mode-specific consumption.");
        lava.SetBlock(30,2,30,BlockType.LavaSource);
        lava.SetBlock(30,1,30,BlockType.WaterSource);
        Check(lava.GetBlock(30,2,30)==BlockType.LavaSource,"Water below hardened a lava source.");
        lava.LavaFlow.Tick();
        Check(lava.GetBlock(30,1,30)==BlockType.LavaFalling,"Downward lava failed to displace water.");
    }

    private static void Currents()
    {
        var world=WaterChecks.Floor();
        for(int x=19;x<=27;x++)
        {
            world.SetGeneratedBlock(x,1,19,BlockType.Stone);
            world.SetGeneratedBlock(x,1,21,BlockType.Stone);
            world.SetGeneratedBlock(x,1,20,x<=20 ? BlockType.WaterSource : Water.Flow(x-20));
        }
        Check(Water.FlowVector(world,new(23,1,20),Water.Flow(3)).X>.99f,"Current did not point downstream.");
        var player=new Player(new(22.5f,1,20.5f));
        for(int i=0;i<60;i++) player.Update(world,Vector3.Zero,false,1f/120);
        Check(player.Position.X>22.9f && player.Position.Z==20.5f,"Player did not drift with the current.");
        world.SetGeneratedBlock(30,4,30,BlockType.WaterFalling3);
        world.SetGeneratedBlock(30,4,31,BlockType.Stone);
        Check(Water.FlowVector(world,new(30,4,30),BlockType.WaterFalling3).Y<-.99f,"A wall-adjacent waterfall did not pull down.");
        world.SetGeneratedBlock(40,1,40,BlockType.WaterSource);
        Check(Water.FlowVector(world,new(40,1,40),BlockType.WaterSource)==Vector3.Zero,"Still water without a gradient pushed entities.");
    }

    private static void Streaming()
    {
        string parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string path=Path.Combine(parent,".fluid-stream-"+Guid.NewGuid().ToString("N"));
        var home=new ChunkAddress(-1,0);
        var away=new ChunkAddress(6,0);
        var source=new Point3(-3,64,8);
        var drop=new Point3(1,63,8);
        World world=null;
        try
        {
            using (var regions=new ChunkRegionStore(path))
            foreach (var center in new[] { home,away })
            for (long z=-3;z<=3;z++)
            for (long x=center.X-3;x<=center.X+3;x++)
            {
                var address=new ChunkAddress(x,z);
                var blocks=new byte[Chunk.BlockCount];
                for (int pz=0;pz<16;pz++)
                for (int px=0;px<16;px++)
                {
                    if (!(x==0 && px==1 && z==0 && pz==8)) blocks[Coordinates.Index(px,63,pz)]=(byte)BlockType.Stone;
                    blocks[Coordinates.Index(px,60,pz)]=(byte)BlockType.Stone;
                    if (address==away && px is >=3 and <=12 && pz is >=3 and <=12)
                        blocks[Coordinates.Index(px,64,pz)]=(byte)(px is 3 or 12 || pz is 3 or 12 ? BlockType.Stone : BlockType.WaterSource);
                }
                regions.WriteAsync(address,new ChunkSnapshot(address,BetaDensityGenerator.Version,blocks).Encode()).GetAwaiter().GetResult();
            }
            world=World.OpenStreamed(173,path,3,0,1);
            Pump(home,()=>world.IsLoaded(source));
            world.Lighting.GetPage(home.X,home.Z);
            world.SetBlock(source.X,source.Y,source.Z,BlockType.WaterSource);
            int builtPages=world.Lighting.BuiltPages;
            for(int i=0;i<32;i++) world.Lighting.ForRendering(new(-2.5f,64,8.5f));
            Check(world.Lighting.BuiltPages==builtPages,"Drawing near a changed water cell synchronously rebuilt lighting.");
            Pump(home,()=>world.IsLoaded(drop));
            for(int i=0;i<5;i++) world.Fluids.Tick();
            Check(Water.Level(world.GetBlock(drop.X,drop.Y,drop.Z))==12,"Flow failed across the signed chunk seam.");
            Pump(away,()=>world.IsLoaded(new(away.BlockX,63,8)) && !world.IsLoaded(source) && !world.IsLoaded(drop));
            world.Fluids.Tick();
            Check(world.Fluids.PendingCount==0 && world.Fluids.LastProcessedCount==0,
                "A stationary pool or an unloaded frontier continued to consume simulation work.");
            world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            world=World.OpenStreamed(173,path,3,0,1);
            Pump(home,()=>world.ChunkCount==49);
            Check(Water.Level(world.GetBlock(drop.X,drop.Y,drop.Z))==12,"Persisted falling level changed on reload.");
            for(int i=0;i<5;i++) world.Fluids.Tick();
            Check(Water.Level(world.GetBlock(1L,61,8L))==12,"Reloaded waterfall did not finish descending.");
            world.SetBlock(source.X,source.Y,source.Z,BlockType.Air);
            WaterChecks.Settle(world);
            Check(world.GetBlock(1L,61,8L)==BlockType.Air,"Reloaded water failed to retract when its source was removed.");
        }
        finally
        {
            world?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(path) && Path.GetDirectoryName(path)==parent) Directory.Delete(path,true);
        }

        void Pump(ChunkAddress center,Func<bool> ready)
        {
            var timer=Stopwatch.StartNew();
            do
            {
                world.UpdateStreaming(center);
                if (world.ChunkLifecycle.Error is Exception error) throw error;
                if (timer.Elapsed.TotalSeconds>20) throw new TimeoutException("Fluid streaming fixture did not load.");
                Thread.Sleep(1);
            } while(!ready());
        }
    }

    private static void FrameBudget()
    {
        var world=WaterChecks.Floor();
        for(int x=4;x<64;x+=3)
        for(int z=4;z<64;z+=3)
            world.SetBlock(x,5,z,BlockType.WaterSource);
        int work=0, remaining=FluidSimulation.MaximumUpdatesPerFrame;
        for(int i=0;i<10;i++)
        {
            world.Fluids.Update(i==0 ? .25 : 1d/120,Math.Min(32,remaining));
            work+=world.Fluids.LastProcessedCount;
            remaining-=world.Fluids.LastProcessedCount;
        }
        Check(work==FluidSimulation.MaximumUpdatesPerFrame && world.Fluids.PendingCount>0,
            "A water burst exceeded the frame budget or lost deferred updates.");
        world.Fluids.Update(1d/120,32);
        Check(world.Fluids.LastProcessedCount==32,"Next frame did not resume the water backlog.");
    }
}
