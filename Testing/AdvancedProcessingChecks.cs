using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;
using MDSM.Inventories;
using MDSM.Gameplay;
using MDSM.Persistence;
using MDSM.Rendering;

namespace MDSM.Testing;

internal static class AdvancedProcessingChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Electric batches, atomic transactions, zero power, blocked outputs and work restore",Processing),
        ("All glass kiln/dye products, cooker foods, recipes and ceramic pot return",Recipes),
        ("Real electric machines share output/switch power, brownouts and source interruptions",Networks),
        ("Glass slabs/stairs culling, placement, tint, vase flowers and binary persistence",Glass),
        ("Infinite electric machine work, products and vase contents survive chunk retirement",Streaming)};
    internal static int Run(){try{foreach(var t in All){t.Check();Console.WriteLine("PASS: "+t.Name);}return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static void C(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Near(double a,double b,string message)=>C(Math.Abs(a-b)<.0001,$"{message}: {a} != {b}");
    private static ItemId B(BlockType b)=>Items.FromBlock(b);
    private static BlockContainer New(BlockType b)=>new(new(20,1,20),b,null);
    private static void Tick(World w,int count=1){for(int i=0;i<count;i++)w.Electricity.Update(.25);}
    private static void Put(World w,Point3 p,BlockType b,int facing=0)
    {
        C(w.SetBlock(p.X,p.Y,p.Z,b),"Placement failed: "+b);
        if(facing!=0){w.Structures[p]=new(p,(byte)facing,false,new(0,-1,0));w.Electricity.Invalidate();}
    }
    private static void Processing()
    {
        foreach(var recipe in Smelting.Recipes.Where(r=>Smelting.Result(r.Input,BlockType.Furnace)==r.Output))
        {
            var f=New(BlockType.ElectricFurnace);f.SetSlot(0,new(recipe.Input,1));
            f.ReceivePower(2000,2.5);C(f[0].IsEmpty&&f[4].Item==recipe.Output,"Electric furnace missed existing recipe");
        }
        var batch=New(BlockType.ElectricFurnace);
        for(int i=0;i<4;i++)batch.SetSlot(i,new(B(BlockType.Sand),64));
        batch.ReceivePower(2000,640);
        C(Enumerable.Range(0,4).All(i=>batch[i].IsEmpty)&&Enumerable.Range(4,4).Sum(i=>batch[i].Count)==256,"Large batch lost materials/output capacity");
        var machine=New(BlockType.ElectricFurnace);machine.SetSlot(0,new(B(BlockType.IronOre),10));
        machine.ReceivePower(1000,1);Near(machine.Progress,.5,"Half-power processing speed");
        var saved=machine.Capture();machine.ReceivePower(0,100);Near(machine.Progress,.5,"Zero power lost work");C(machine[0].Count==10,"Zero power consumed inputs");
        var restored=New(machine.Block);restored.Restore(StreamingJson.Decode<BlockEntityState>(StreamingJson.Encode(saved)));
        for(int i=4;i<8;i++)restored.SetSlot(i,new(Items.IronIngot,64));
        Near(restored.RequestedWatts,0,"Blocked machine wasted power");restored.ReceivePower(2000,5);Near(restored.Progress,.5,"Blocked output lost work");
        restored.SetSlot(4,default);restored.ReceivePower(2000,2);
        C(restored[0].Count==9&&restored[4].Count==1&&restored.Progress==0,"Resumed transaction was not atomic");
        var interaction=new InventoryInteraction(new Inventory());interaction.Inventory.SetSlot(0,new(Items.Stick,3));interaction.Click(0,false);
        restored.Click(0,interaction,false);C(restored[0].Count==9&&interaction.Cursor.Count==3,"Machine accepted invalid input");
        bool rejected=false;try{var bad=saved.Slots.ToArray();bad[4]=new(Items.Stick,1);New(machine.Block).Restore(saved with{Slots=bad});}catch(InvalidDataException){rejected=true;}
        C(rejected,"Corrupt electric output loaded");
    }
    private static void Recipes()
    {
        for(int color=0;color<17;color++)for(int choice=0;choice<5;choice++)
        {
            var kiln=New(BlockType.ElectricGlassKiln);C(kiln.SelectRecipe(choice),"Could not select glass product");
            ItemId input=B(color==0?BlockType.Glass:DyePalette.Glass((DyeColor)(color-1)));
            var recipe=AdvancedProcessing.Recipe(kiln.Block,choice,input);kiln.SetSlot(0,new(input,recipe.Amount));
            kiln.ReceivePower(2500,recipe.Seconds/2);C(!kiln.SelectRecipe((choice+1)%5),"Changing product discarded active work");
            kiln.ReceivePower(0,20);kiln.ReceivePower(2500,recipe.Seconds/2);
            C(kiln[0].IsEmpty&&kiln[1]==new ItemStack(recipe.Output,recipe.Quantity),"Kiln product/color/quantity failed");
        }
        foreach(GlassProduct product in new[]{GlassProduct.Slab,GlassProduct.Stairs,GlassProduct.Tile,GlassProduct.Vase})foreach(DyeColor color in Enum.GetValues<DyeColor>())
        {
            var grid=new CraftingGrid(3);for(int i=0;i<9;i++)grid.SetSlot(i,new(i==4?Items.Dye(color):B(GlassProcessing.Block(product)),1));
            C(grid.Preview==new ItemStack(B(GlassProcessing.Block(product,(int)color+1)),8),"Processed glass dye recipe failed");
        }
        ItemId iron=Items.IronIngot,wire=Items.CopperWire,copper=Items.CopperIngot;
        var machineRecipes=new[]{
            (BlockType.ElectricFurnace,new[]{iron,Items.CeramicPot,iron,wire,B(BlockType.Furnace),wire,iron,copper,iron}),
            (BlockType.ElectricGlassKiln,new[]{iron,B(BlockType.Glass),iron,wire,B(BlockType.BrickKiln),wire,Items.Brick,copper,Items.Brick}),
            (BlockType.ElectricCooker,new[]{iron,Items.CeramicPot,iron,wire,B(BlockType.Smoker),wire,iron,copper,iron})};
        foreach(var (block,pattern) in machineRecipes)
        {
            var grid=new CraftingGrid(3,CraftingStation.Metalworking);for(int i=0;i<9;i++)grid.SetSlot(i,new(pattern[i],1));
            C(grid.Preview==new ItemStack(B(block),1)&&grid.TryCraft(new InventoryInteraction(new Inventory())),"Electric machine crafting transaction failed");
        }
        for(int choice=0;choice<5;choice++)
        {
            var cooker=New(BlockType.ElectricCooker);cooker.SelectRecipe(choice);var recipe=AdvancedProcessing.Recipe(cooker.Block,choice,default);
            cooker.SetSlot(0,new(recipe.Input,1));if(recipe.Second!=default)cooker.SetSlot(1,new(recipe.Second,1));if(recipe.Vessel!=default)cooker.SetSlot(2,new(recipe.Vessel,1));
            cooker.ReceivePower(400,recipe.Seconds);C(cooker[3]==new ItemStack(recipe.Output,1),"Cooker recipe failed");
            C(Enumerable.Range(0,3).All(i=>cooker[i].IsEmpty),"Cooker duplicated ingredients");
        }
        var inventory=new Inventory();inventory.SetSlot(0,new(Items.VegetableStew,1));var player=new Player(new(20,1,20),5,inventory);
        C(ItemUse.TryConsume(player,inventory,true,false)&&player.Health==13&&inventory[0]==new ItemStack(Items.CeramicPot,1),"Stew did not heal and return its pot");
        C(Smelting.Result(Items.UnfiredPot,BlockType.Furnace)==default&&Smelting.Result(Items.UnfiredPot,BlockType.BrickKiln)==Items.CeramicPot,"Primitive kiln niche was removed");
    }
    private static void Networks()
    {
        var w=PigChecks.Flat();w.Time.Restore(300);var source=new Point3(20,1,20);var sw=new Point3(23,1,20);
        Put(w,source,BlockType.CoalGenerator);w.Containers.Get(source).SetSlot(0,new(Items.Coal,2));
        Put(w,new(21,1,20),BlockType.OutputPanel,1);Put(w,new(22,1,20),BlockType.CopperWire);Put(w,sw,BlockType.PowerSwitch,1);Put(w,new(24,1,20),BlockType.CopperWire);
        var cells=new[]{new Point3(25,1,20),new(24,1,21),new(24,1,19)};
        Put(w,cells[0],BlockType.ElectricFurnace);Put(w,cells[1],BlockType.ElectricGlassKiln);Put(w,cells[2],BlockType.ElectricCooker);
        var machines=cells.Select(w.Containers.Get).ToArray();machines[0].SetSlot(0,new(B(BlockType.Sand),64));machines[1].SetSlot(0,new(B(BlockType.Glass),64));machines[1].SelectRecipe(1);machines[2].SelectRecipe(2);machines[2].SetSlot(0,new(Items.Carrot,64));
        Tick(w,4);foreach(var machine in machines){Near(machine.AllocatedWatts,machine.NominalWatts,"Full-network allocation");Near(machine.Progress,1,"Live network processing");}
        w.Toggle(sw);Tick(w,8);foreach(var machine in machines){Near(machine.AllocatedWatts,0,"Off switch supplied a real machine");Near(machine.Progress,1,"Switch interruption lost progress");}
        w.Containers.Detach(source);w.SetBlock(source.X,source.Y,source.Z,BlockType.SolarPanel);Put(w,new(20,1,21),BlockType.AccessPanel);w.Toggle(sw);
        Tick(w);double ratio=750d/4900;
        foreach(var machine in machines){Near(machine.AllocatedWatts,machine.NominalWatts*ratio,"Real-machine proportional shortage");Near(machine.Progress,1+.25*ratio,"Brownout work fraction");}
        Put(w,new(20,2,20),BlockType.Stone);Tick(w);foreach(var machine in machines)Near(machine.AllocatedWatts,0,"Shaded generation powered work");
        w.SetBlock(20,2,20,BlockType.Air);Put(w,new(19,1,20),BlockType.CoalGenerator);w.Containers.Get(new(19,1,20)).SetSlot(0,new(Items.Charcoal,1));Tick(w,8);
        C(machines[0][4].Count==1&&machines[2][3].Count==1,"Power restoration did not complete real products");
    }
    private static void Glass()
    {
        var w=PigChecks.Flat();var mesh=new ChunkMeshBuilder();var cell=new Point3(20,5,20);var player=new Player(new(40,1,40));
        C(w.PlaceBuilding(cell,BlockType.GlassSlab,0,false,player.Bounds,new(0,-1,0)),"Glass slab placement failed");
        mesh.BuildBlock(w,cell);C(mesh.VertexCount==36,"Isolated slab mesh is not six merged faces");
        foreach(var block in new[]{BlockType.GlassSlab,BlockType.GlassStairs,GlassProcessing.Block(GlassProduct.Stairs,15)})
        {
            var isolated=new Point3(40,6,40);Put(w,isolated,block);mesh.BuildBlock(w,isolated);int count=mesh.VertexCount;
            mesh.Clear();mesh.AppendItem(block,Vector3.Zero);C(mesh.VertexCount==count,"Glass held/inventory model retained buried faces");
        }
        Put(w,cell+new Point3(1,0,0),BlockType.GlassSlab);mesh.BuildBlock(w,cell);C(mesh.VertexCount==30,"Matching glass slab seam was not culled");
        C(w.PlaceBuilding(cell,BlockType.GlassSlab,0,true,player.Bounds,new(0,-1,0))&&w.GetBlock(cell.X,cell.Y,cell.Z)==BlockType.DoubleGlassSlab,"Glass slab joining failed");
        Span<ItemId> drops=stackalloc ItemId[5];C(MiningRules.Drops(BlockType.DoubleGlassSlab,default,new Random(1),drops)==2&&drops[0]==B(BlockType.GlassSlab),"Double glass slab drops failed");
        for(int facing=0;facing<4;facing++)foreach(bool upper in new[]{false,true})
        {
            var p=new Point3(25+facing*2,5,25);w.SetBlock(p.X,p.Y,p.Z,BlockType.Air);
            C(w.PlaceBuilding(p,GlassProcessing.Block(GlassProduct.Stairs,15),(byte)facing,upper,player.Bounds,new(0,-1,0)),"Colored stair placement failed");
            mesh.BuildBlock(w,p);C(mesh.VertexCount>36&&mesh.VertexCount<144,"Stair faces were missing or unnecessarily subdivided");
        }
        for(int color=0;color<17;color++)
        {
            var p=new Point3(20,1,30);w.Containers.Detach(p);w.SetBlock(p.X,p.Y,p.Z,GlassProcessing.Block(GlassProduct.Vase,color));
            var inventory=new Inventory();inventory.SetSlot(0,new(B(BlockType.Cornflower),2));
            C(w.UseVase(p,inventory,false)&&inventory[0].Count==1&&w.Containers.Get(p)[0].Count==1,"Vase insertion lost flower accounting");
            mesh.BuildBlock(w,p);C(mesh.VertexCount>180,"Vase flower geometry is absent");
            inventory.SetSlot(0,default);C(w.UseVase(p,inventory,false)&&inventory[0]==new ItemStack(B(BlockType.Cornflower),1)&&w.Containers.Get(p)[0].IsEmpty,"Vase flower removal failed");
            w.UseVase(p,inventory,false);
        }
        var machineCell=new Point3(22,1,30);Put(w,machineCell,BlockType.ElectricGlassKiln);var kiln=w.Containers.Get(machineCell);kiln.SelectRecipe(4);kiln.SetSlot(0,new(B(BlockType.Glass),2));kiln.ReceivePower(1250,2);
        using var data=new MemoryStream();WorldSerializer.Write(data,new(w,"Advanced glass",new(40,1,40),0,0));data.Position=0;var restored=WorldSerializer.Read(data).World;
        var loaded=restored.Containers.Get(machineCell);Near(loaded.Progress,1,"Binary electric work progress");C(loaded.SelectedRecipe==4,"Binary kiln selection lost");
        C(restored.Containers.Get(new(20,1,30))[0].Item==B(BlockType.Cornflower),"Binary vase flower lost");
        loaded.ReceivePower(2500,4);C(loaded[1].Item==B(BlockType.GlassVase),"Binary work did not finish once");
    }
    private static void Streaming()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,".advanced-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        World w=null;var center=new ChunkAddress(-1,-1);var source=new Point3(-2,120,-3);var output=new Point3(-1,120,-3);var sw=new Point3(1,120,-3);
        var furnace=new Point3(2,120,-3);var kilnCell=new Point3(2,120,-2);var cookerCell=new Point3(2,120,-4);var vaseCell=new Point3(4,120,-3);
        try
        {
            w=World.OpenStreamed(173,directory,loaded:3,render:1,simulation:1);Pump(center,()=>w.IsLoaded(source)&&w.IsLoaded(vaseCell));
            for(long x=-2;x<=4;x++)for(long z=-4;z<=-2;z++)for(int y=120;y<128;y++)w.SetBlock(x,y,z,BlockType.Air);
            Put(w,source,BlockType.CoalGenerator);w.Containers.Get(source).SetSlot(0,new(Items.Coal,2));Put(w,output,BlockType.OutputPanel,1);Put(w,new(0,120,-3),BlockType.CopperWire);Put(w,sw,BlockType.PowerSwitch,1);
            Put(w,furnace,BlockType.ElectricFurnace);Put(w,kilnCell,BlockType.ElectricGlassKiln);Put(w,cookerCell,BlockType.ElectricCooker);Put(w,vaseCell,GlassProcessing.Block(GlassProduct.Vase,4));
            var f=w.Containers.Get(furnace);f.SetSlot(0,new(B(BlockType.Sand),64));var k=w.Containers.Get(kilnCell);k.SelectRecipe(1);k.SetSlot(0,new(B(DyePalette.Glass(DyeColor.Blue)),6));
            var c=w.Containers.Get(cookerCell);c.SelectRecipe(4);c.SetSlot(0,new(Items.Carrot,3));c.SetSlot(1,new(Items.Potato,3));c.SetSlot(2,new(Items.CeramicPot,3));w.Containers.Get(vaseCell).SetSlot(0,new(B(BlockType.RedFlower),1));Tick(w,4);
            Near(f.Progress,1,"Streamed electric furnace did not register");Near(k.Progress,1,"Streamed kiln did not register");Near(c.Progress,1,"Streamed cooker did not register");
            Pump(new(10,10),()=>!w.IsLoaded(source)&&!w.IsLoaded(furnace));Near(f.AllocatedWatts,0,"Retired machine kept allocated power");
            w.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();w=World.OpenStreamed(173,directory,loaded:3,render:1,simulation:1);Pump(center,()=>w.IsLoaded(source)&&w.IsLoaded(vaseCell));
            f=w.Containers.Get(furnace);k=w.Containers.Get(kilnCell);c=w.Containers.Get(cookerCell);
            Near(f.Progress,1,"Loaded furnace work changed");Near(k.Progress,1,"Loaded kiln work changed");Near(c.Progress,1,"Loaded cooker work changed");C(k.SelectedRecipe==1&&c.SelectedRecipe==4,"Loaded selections changed");
            C(w.Containers.Get(vaseCell)[0].Item==B(BlockType.RedFlower),"Streamed vase lost its flower");
            Tick(w,10);C(f[4].Count==1&&f[0].Count==63,"Reload duplicated or lost a furnace result");
            w.Toggle(sw);double progress=f.Progress;Tick(w,8);Near(f.Progress,progress,"Reloaded switch interruption lost work");
            var finished=f.Capture();var pendingKiln=k.Capture();var pendingCooker=c.Capture();
            Pump(new(10,10),()=>!w.IsLoaded(source)&&!w.IsLoaded(furnace));
            w.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();w=World.OpenStreamed(173,directory,loaded:3,render:1,simulation:1);Pump(center,()=>w.IsLoaded(source)&&w.IsLoaded(vaseCell));
            f=w.Containers.Get(furnace);k=w.Containers.Get(kilnCell);c=w.Containers.Get(cookerCell);Tick(w,8);
            foreach(var pair in new[]{(f,finished),(k,pendingKiln),(c,pendingCooker)})
            {
                Near(pair.Item1.Progress,pair.Item2.Progress,"Saved off switch lost partial work");
                C(pair.Item1.Capture().Slots.SequenceEqual(pair.Item2.Slots),"Completed products or input reserves changed on reload");
                Near(pair.Item1.AllocatedWatts,0,"Saved off switch resumed production");
            }
            C(w.Containers.Get(vaseCell)[0].Item==B(BlockType.RedFlower),"Second vase reload lost flower");
            var snapshot=w.MeshSnapshot(w.GetChunk(0L,-1L));var mesh=new ChunkMeshBuilder();mesh.Build(snapshot,snapshot.GetChunk(0L,0L));C(mesh.VertexCount>0,"Async glass/vase/machine snapshot failed");
        }
        finally{w?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();Directory.Delete(directory,true);}
        void Pump(ChunkAddress at,Func<bool> ready)
        {
            var timer=Stopwatch.StartNew();do{w.UpdateStreaming(at);if(w.ChunkLifecycle.Error is Exception e)throw e;if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException("Advanced machine streaming fixture stalled");Thread.Sleep(1);}while(!ready());
        }
    }
}
