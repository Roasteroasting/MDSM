using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Gameplay;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class ElectricalChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Electrical proportional allocation and overlapping source bottlenecks",Allocation),
        ("Electrical solar environment, gates, five-machine switches, loops and edits",Circuits),
        ("Electrical recipes, fuel transactions, geometry and saved machine validation",Integration),
        ("Infinite electrical seam, retirement, signed coordinates and saved fuel/switch state",Streaming)};
    internal static int Run(){try{foreach(var t in All){t.Check();Console.WriteLine("PASS: "+t.Name);}return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static void C(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Near(double actual,double expected,string message)=>C(Math.Abs(actual-expected)<.001,$"{message}: {actual} != {expected}");
    private sealed class Load(Point3 cell,double demand):IElectricalConsumer
    {public Point3 Cell=>cell;public double RequestedWatts{get;set;}=demand;public double Received;public void ReceivePower(double watts,double elapsed)=>Received=watts;}
    private static void Allocation()
    {
        var a=ElectricalAllocation.Solve(new[]{6000d},new[]{new[]{0,1}},new[]{4000d,8000});
        Near(a.Delivered[0],2000,"Small load ration");Near(a.Delivered[1],4000,"Large load ration");Near(a.Used.Sum(),6000,"Source conservation");
        a=ElectricalAllocation.Solve(new[]{6000d,6000},new[]{new[]{0,1},new[]{0,1}},new[]{1000d,2000});
        Near(a.Delivered[0],1000,"Full request");Near(a.Delivered[1],2000,"Full request");Near(a.Used.Sum(),3000,"No duplicated demand");
        a=ElectricalAllocation.Solve(new[]{100d,900},new[]{new[]{0},new[]{1,2}},new[]{200d,300,600});
        Near(a.Delivered[0],100,"Isolated bottleneck");Near(a.Delivered[1],300,"Spare unrelated supply");Near(a.Delivered[2],600,"Spare unrelated supply");
        a=ElectricalAllocation.Solve(new[]{100d,900},new[]{new[]{0,1},new[]{1,2}},new[]{200d,200,800});
        Near(a.Delivered[0],100,"Restricted source");Near(a.Delivered[1],180,"Progressive fair allocation");Near(a.Delivered[2],720,"Progressive fair allocation");
        a=ElectricalAllocation.Solve(Array.Empty<double>(),Array.Empty<int[]>(),new[]{1d});Near(a.Delivered[0],0,"No generators");
    }
    private static void Put(World w,Point3 p,BlockType b,byte facing=0)
    {
        C(w.SetBlock(p.X,p.Y,p.Z,b),"Could not place "+b);
        if(facing!=0)w.Structures[p]=new(p,facing,false,new(0,-1,0));
        w.Electricity.Invalidate();
    }
    private static void Tick(World w)=>w.Electricity.Update(.25);
    private static void Circuits()
    {
        var w=PigChecks.Flat();w.Time.Restore(300);
        Point3 solar=new(20,5,20),access=new(20,5,21),output=new(21,5,20),wire=new(22,5,20),sw=new(23,5,20);
        Put(w,solar,BlockType.SolarPanel);Put(w,output,BlockType.OutputPanel,1);Put(w,wire,BlockType.CopperWire);Put(w,sw,BlockType.PowerSwitch,1);
        var loads=new Load[6];
        for(int i=0;i<6;i++)
        {Put(w,new(24+i,5,20),BlockType.CopperWire);loads[i]=new(new(24+i,5,21),100);w.Electricity.Register(loads[i]);}
        Tick(w);C(loads.All(l=>l.Received==0),"Solar bypassed its access panel");
        Put(w,access,BlockType.AccessPanel);Tick(w);
        Near(w.Electricity.Statistics(access).Production,750,"Noon solar");
        C(loads.Count(l=>l.Received>0)==5,"Switch did not cap its attached machines at five");
        C(w.Electricity.Statistics(sw).Attached==6,"Switch did not count excess attachments");
        C(w.Toggle(sw),"Switch could not toggle");Tick(w);C(loads.All(l=>l.Received==0),"Off switch leaked power");
        C(w.Toggle(sw),"Switch could not restore");Tick(w);
        w.Time.Restore(900);Tick(w);C(loads.All(l=>l.Received==0),"Night solar produced power");
        w.Time.Restore(300);Put(w,new(20,6,20),BlockType.Glass);Tick(w);Near(w.Electricity.Statistics(access).Production,0,"Roof must block sky");
        w.SetBlock(20,6,20,BlockType.Air);Tick(w);Near(w.Electricity.Statistics(access).Production,750,"Roof removal exposure");
        w.Weather.Restore(new(300,180,20,20,128,1));Tick(w);Near(w.Electricity.Statistics(access).Production,225,"Overcast solar");
        w.Weather.Restore(new(300,0,20,20,128,1));
        // An alternative output legitimately supplies the sixth consumer, but never
        // duplicates the original solar's 750 watts through the parallel route.
        Put(w,new(21,5,21),BlockType.OutputPanel,1);Put(w,new(22,5,21),BlockType.CopperWire);
        Put(w,new(23,5,21),BlockType.CopperWire);Tick(w);
        C(loads.All(l=>l.Received>0),"Parallel output did not distribute to all consumers");
        foreach(var load in loads)load.RequestedWatts=1000;Tick(w);
        Near(loads.Sum(l=>l.Received),750,"Parallel panels duplicated generation");
        foreach(var load in loads)Near(load.Received,125,"Parallel path fairness");
        w.SetBlock(wire.X,wire.Y,wire.Z,BlockType.Air);w.SetBlock(22,5,21,BlockType.Air);Tick(w);
        C(loads.All(l=>l.Received==0),"Network split left stale power");
        int builds=w.Electricity.TopologyBuilds;for(int i=0;i<100;i++)w.Electricity.Update(1d/120);
        C(w.Electricity.TopologyBuilds==builds,"Quiet network rebuilt every tick");
        w.Electricity.Unregister(loads[0]);Near(loads[0].Received,0,"Unregister power reset");
    }
    private static void Integration()
    {
        C(Items.TryGetBlock(Items.CopperWire,out var b)&&b==BlockType.CopperWire&&Items.FromBlock(b)==Items.CopperWire,"Wire item identity changed");
        C(Items.Obtainable.ToArray().Count(i=>i==Items.CopperWire)==1&&!Items.Obtainable.ToArray().Contains(new((byte)b)),"Duplicate Creative wire");
        var coal=new BlockContainer(new(1,2,3),BlockType.CoalGenerator,null);
        var cursor=new InventoryInteraction(new Inventory());cursor.ClickStack(default,false);
        coal.SetSlot(0,new(Items.Charcoal,2));Near(coal.Generate(.25),6000,"Charcoal production");
        Near(coal.Burn,79.75,"Fuel burn");C(coal[0].Count==1,"Fuel intake");
        var state=StreamingJson.Decode<BlockEntityState>(StreamingJson.Encode(coal.Capture()));
        var restored=new BlockContainer(coal.Cell,coal.Block,null);restored.Restore(state);
        Near(restored.Generate(100),6000,"Continuous saved reserves");Near(restored.Burn,59.75,"Residual fuel time");
        Near(restored.Generate(119.5),3000,"Part-tick exhaustion must conserve energy");
        bool rejected=false;try{restored.Restore(state with{Slots=new[]{new ItemStack(Items.Stick,1)}});}catch(InvalidDataException){rejected=true;}
        C(rejected,"Invalid generator fuel loaded");
        ItemId I=Items.IronIngot,W=Items.CopperWire,G=Items.FromBlock(BlockType.Glass),Cpr=Items.CopperIngot;
        var patterns=new[]{
            (BlockType.SolarPanel,new[]{G,G,G,Items.Dye(DyeColor.Black),Cpr,Items.Dye(DyeColor.Black),I,W,I}),
            (BlockType.CoalGenerator,new[]{I,I,I,W,Items.FromBlock(BlockType.Furnace),W,I,Cpr,I}),
            (BlockType.AccessPanel,new[]{I,G,I,W,Cpr,W}),
            (BlockType.OutputPanel,new[]{I,W,I,W,Cpr,W}),
            (BlockType.PowerSwitch,new[]{I,Items.Stick,I,W,Cpr,W})};
        foreach(var (block,pattern) in patterns)
        {
            var grid=new CraftingGrid(3,CraftingStation.Metalworking);
            for(int i=0;i<pattern.Length;i++)grid.SetSlot(i,new(pattern[i],1));
            C(grid.Preview==new ItemStack(Items.FromBlock(block),1),"Missing recipe "+block);
            C(grid.TryCraft(new InventoryInteraction(new Inventory()))&&grid.Preview.IsEmpty,"Recipe transaction "+block);
        }
        var w=PigChecks.Flat();Point3 cell=new(20,5,20);Put(w,cell,BlockType.CopperWire);
        foreach(var d in ElectricalNetwork.Directions)Put(w,cell+d,BlockType.CopperWire);
        C(ElectricalGeometry.Connections(w,cell)==63,"Wire did not connect in six directions");
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];C(w.BlockBoxes(cell,boxes)==7&&w.BlockBoxes(cell,boxes,true)==0,"Wire target/collision geometry");
        var mesh=new ChunkMeshBuilder();mesh.BuildBlock(w,cell);C(mesh.VertexCount>36,"Connected wire mesh missing arms");
        foreach(var (block,_) in patterns){mesh.AppendInventoryItem(block);C(mesh.VertexCount>0,"Missing inventory model "+block);}
        mesh.AppendInventoryItem(BlockType.CopperWire);C(mesh.VertexCount>0,"Missing wire held model");
        C(Blocks.Get(BlockType.AccessPanel).Opaque&&Blocks.Get(BlockType.OutputPanel).Opaque,"Panels are not full blocks");
        var inventory=new Inventory();inventory.SetSlot(0,new(Items.CopperWire,2));
        C(BlockPlacement.TryPlace(w,inventory,new Point3(30,5,30),new Player(new(40,1,40)).Bounds)
            &&inventory.Selected.Count==1&&w.GetBlock(30,5,30)==BlockType.CopperWire,"Survival wire placement did not consume the existing item");
        C(BlockPlacement.TryPlace(w,inventory,new Point3(31,5,30),new Player(new(40,1,40)).Bounds,true)
            &&inventory.Selected.Count==1,"Creative wire placement consumed its stack");
        C(MiningRules.Drop(BlockType.CopperWire,default,new Random(1))==Items.CopperWire,"Wire mining returned another item identity");
    }
    private static void Streaming()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,".electrical-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        World w=null;var center=new ChunkAddress(-1,-1);Point3 generator=new(-2,120,-2),wire=new(-1,120,-2),output=new(0,120,-2),sw=new(2,120,-2);
        try
        {
            w=World.OpenStreamed(173,directory,loaded:3,render:1,simulation:1);
            Pump(center,()=>w.IsLoaded(generator)&&w.IsLoaded(new(4,120,-2)));
            for(long x=-2;x<=4;x++)for(int y=120;y<128;y++)w.SetBlock(x,y,-2L,BlockType.Air);
            Put(w,generator,BlockType.CoalGenerator);Put(w,wire,BlockType.CopperWire);Put(w,output,BlockType.OutputPanel,1);
            Put(w,new(1,120,-2),BlockType.CopperWire);Put(w,sw,BlockType.PowerSwitch,1);Put(w,new(3,120,-2),BlockType.CopperWire);
            var load=new Load(new(4,120,-2),9000);w.Electricity.Register(load);
            w.Containers.Get(generator).SetSlot(0,new(Items.Coal,2));Tick(w);Near(load.Received,6000,"Seam distribution");
            C(w.Lighting.Emission(generator,BlockType.CoalGenerator)==13,"Burning generator did not emit furnace light");
            C((ElectricalGeometry.Connections(w,wire)&2)!=0,"Seam mesh did not connect to output rear");
            C(w.Toggle(sw),"Streamed switch toggle");Tick(w);Near(load.Received,0,"Streamed switch isolation");
            double burn=w.Containers.Get(generator).Burn;
            Pump(new(10,10),()=>!w.IsLoaded(generator)&&!w.IsLoaded(sw));
            C(w.Electricity.ResidentNodeCount==0,"Retirement leaked electrical nodes");Near(load.Received,0,"Retirement leaked power");
            w.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            w=World.OpenStreamed(173,directory,loaded:3,render:1,simulation:1);
            Pump(center,()=>w.IsLoaded(generator)&&w.IsLoaded(new(4,120,-2)));
            C(w.Structure(sw).Open&&w.Structure(output).Facing==1,"Chunk lost switch or panel orientation");
            Near(w.Containers.Get(generator).Burn,burn,"Unloaded generator burned fuel");
            C(w.Containers.Get(generator)[0].Count==1,"Saved fuel stack changed");
            load=new(new(4,120,-2),3000);w.Electricity.Register(load);Tick(w);Near(load.Received,0,"Reloaded off switch leaked");
            w.Toggle(sw);Tick(w);Near(load.Received,3000,"Reload did not rebuild distribution");
            w.SetBlock(wire.X,wire.Y,wire.Z,BlockType.Air);Tick(w);Near(load.Received,0,"Seam split retained power");
            Put(w,wire,BlockType.CopperWire);Tick(w);Near(load.Received,3000,"Seam merge did not recover power");
            double beforeSleep=w.Containers.Get(generator).Burn;
            w.UpdateStreaming(new(1,-1));Tick(w);Near(load.Received,0,"Dormant supply powered a simulated consumer");
            Near(w.Containers.Get(generator).Burn,beforeSleep,"Dormant source consumed fuel");
            w.UpdateStreaming(center);Tick(w);Near(load.Received,3000,"Simulation-window reentry lost the circuit");
            var snapshot=w.MeshSnapshot(w.GetChunk(-1L,-1L));var mesh=new ChunkMeshBuilder();mesh.Build(snapshot,snapshot.GetChunk(0L,0L));C(mesh.VertexCount>0,"Async mesh snapshot lost electricity geometry");
        }
        finally{w?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();Directory.Delete(directory,true);}
        void Pump(ChunkAddress at,Func<bool> ready)
        {
            var timer=Stopwatch.StartNew();do{w.UpdateStreaming(at);if(w.ChunkLifecycle.Error is Exception e)throw e;if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException("Electrical streaming fixture timed out.");Thread.Sleep(1);}while(!ready());
        }
    }
}
