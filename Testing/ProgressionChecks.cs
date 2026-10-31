using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class ProgressionChecks
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static ItemId Block(BlockType block)=>Items.FromBlock(block);
    private static ItemStack Stack(ItemId item,int count=1)=>new(item,count);
    public static int Run()
    {
        int failures=0;
        foreach(var test in new (string,Action)[]{("Equipment",Equipment),("Recipes",Recipes),("Mining",Mining),("Containers/furnaces",Containers),
            ("Melee",Melee),("Projectiles",Arrows),("Persistence",Persistence),("Ores",Ores),("Survival progression",Walkthrough)})
            try{test.Item2();Console.WriteLine("PASS: Survival "+test.Item1);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: Survival "+test.Item1+": "+e);}
        return failures==0?0:1;
    }
    public static void Equipment()
    {
        int[] uses={59,131,250,1561};var inventory=new Inventory();var mouse=new InventoryInteraction(inventory);
        for(int tier=1;tier<=4;tier++)for(int kind=1;kind<=4;kind++)
        {
            ItemId id=Items.Tool((ToolTier)tier,(ToolKind)kind);var item=Stack(id);
            Require(item.Count==1 && item.Limit==1 && item.Durability==uses[tier-1] && item.InstanceId!=Guid.Empty,"Equipment initialization invalid.");
            ItemStack damaged=item.Wear();inventory.SetSlot(0,damaged);mouse.Click(0,true);mouse.Click(1,false);
            Require(inventory[1]==damaged && inventory[0].IsEmpty,"Cursor transfer reset equipment metadata.");
            inventory.SetSlot(0,Stack(id));mouse.Click(0,false);mouse.Click(1,true);
            Require(mouse.Cursor.Durability==uses[tier-1] && inventory[1]==damaged,"Equipment right-click merged instances.");
            mouse.Click(1,false);Require(mouse.Cursor==damaged && inventory[1].InstanceId!=item.InstanceId,"Equipment did not swap.");
            Require(mouse.TryReturnCursor(),"Equipment cursor return failed.");
            inventory.SetSlot(0,new ItemStack(id,1,1));inventory.WearSelected();Require(inventory[0].IsEmpty,"Last durability use did not destroy equipment.");
            try{_=new ItemStack(id,2);throw new Exception("Equipment stacked.");}catch(ArgumentException){}
            inventory=new Inventory();mouse=new InventoryInteraction(inventory);
        }
        var bow=Stack(Items.Bow);Require(bow.Durability==384 && !bow.Compatible(Stack(Items.Bow)),"Bow limit/durability wrong.");
        var world=PigChecks.Flat();var drops=new DroppedItems(world);var tool=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Axe),1,17);
        Require(drops.TrySpawn(tool,new Vector3(40.5f,1.2f,40.5f),0),"Tool drop failed.");
        drops.Update(new Player(new Vector3(40.5f,1,40.5f)),inventory,.01f);
        Require(drops.Entities.Count==0 && inventory[0]==tool,"Pickup changed tool instance/durability.");
    }
    public static void Recipes()
    {
        Require(CraftingRecipes.All.Length==31,"Recipe registration count wrong.");
        ItemId P=Block(BlockType.WoodenPlanks),C=Block(BlockType.Cobblestone),S=Items.Stick,T=Items.String;
        void Check(int w,int h,ItemId result,int quantity,params ItemId[] cells)
        {
            for(int size=2;size<=3;size++)
            {
                if(w>size || h>size)continue;
                for(int oy=0;oy<=size-h;oy++)for(int ox=0;ox<=size-w;ox++)
                {
                    var grid=new CraftingGrid(size);
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(cells[y*w+x].Value!=0)grid.SetSlot((y+oy)*size+x+ox,Stack(cells[y*w+x],2));
                    Require(grid.Preview.Item==result && grid.Preview.Count==quantity,"Recipe/offset failed: "+Items.Name(result));
                    int before=Enumerable.Range(0,grid.SlotCount).Sum(i=>grid[i].Count);var inventory=new Inventory();var interaction=new InventoryInteraction(inventory);
                    Require(grid.TryCraft(interaction) && interaction.Cursor.Item==result && interaction.Cursor.Count==quantity,"Taking recipe failed.");
                    Require(Enumerable.Range(0,grid.SlotCount).Sum(i=>grid[i].Count)==before-cells.Count(c=>c.Value!=0),"Recipe consumed wrong amounts.");
                    Guid first=interaction.Cursor.InstanceId;
                    if(Items.Get(result).Durability>0)
                    {Require(interaction.Cursor.Durability==Items.Get(result).Durability && !grid.TryCraft(interaction),"Equipment stacked or was damaged on craft.");interaction.Click(0,false);}
                    Require(grid.TryCraft(interaction) && grid.Preview.IsEmpty,"Repeated recipe/exhaustion failed.");
                    if(first!=Guid.Empty)Require(first!=interaction.Cursor.InstanceId,"Crafting reused an equipment instance identity.");
                    grid.SetSlot(0,Stack(Block(BlockType.Dirt)));Require(grid.Preview.IsEmpty,"Wrong material matched a recipe.");
                }
            }
        }
        Check(1,1,P,4,Block(BlockType.WoodLog));Check(1,2,S,4,P,P);
        Check(2,2,Block(BlockType.CraftingTable),1,P,P,P,P);
        Check(3,3,Block(BlockType.Chest),1,P,P,P,P,default,P,P,P,P);Check(3,3,Block(BlockType.Furnace),1,C,C,C,C,default,C,C,C,C);
        Check(1,3,Items.Arrow,4,Items.Flint,S,Items.Feather);Check(1,1,T,2,Block(BlockType.WhiteWool));
        Check(3,3,Items.Bow,1,default,S,T,S,default,T,default,S,T);Check(3,3,Items.Bow,1,T,S,default,T,default,S,T,S,default);
        for(int tier=1;tier<=4;tier++)
        {
            ItemId M=tier==1?P:tier==2?C:tier==3?Items.IronIngot:Items.Diamond;
            Check(3,3,Items.Tool((ToolTier)tier,ToolKind.Pickaxe),1,M,M,M,default,S,default,default,S,default);
            Check(2,3,Items.Tool((ToolTier)tier,ToolKind.Axe),1,M,M,M,S,default,S);
            Check(2,3,Items.Tool((ToolTier)tier,ToolKind.Axe),1,M,M,S,M,S,default);
            Check(1,3,Items.Tool((ToolTier)tier,ToolKind.Sword),1,M,M,S);Check(1,3,Items.Tool((ToolTier)tier,ToolKind.Shovel),1,M,S,S);
        }
        var large=new CraftingGrid(2);large.SetSlot(0,Stack(P,8));Require(large.Preview.IsEmpty,"3x3 recipe was flattened into 2x2.");
    }
    public static void Mining()
    {
        var world=PigChecks.Flat();var items=new DroppedItems(world);var inventory=new Inventory();var mining=new MiningController(173);
        var cell=new Point3(15,4,16);var hit=new BlockHit(cell,new Point3(0,1,0),2);
        foreach(BlockType block in new[]{BlockType.Stone,BlockType.Cobblestone,BlockType.CoalOre,BlockType.IronOre,BlockType.DiamondOre,BlockType.Furnace})
        {world.SetBlock(cell.X,cell.Y,cell.Z,block);Require(!mining.Update(world,items,hit,true,100,inventory) && mining.Progress==0,"Fist broke a mineral.");world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Air);}
        for(int tier=1;tier<=4;tier++)
        {
            var pick=Stack(Items.Tool((ToolTier)tier,ToolKind.Pickaxe));inventory.SetSlot(0,pick);
            Require(Items.Get(pick.Item).Efficiency==tier*2,"Tool efficiency incorrect.");
            float last=MiningRules.Seconds(BlockType.Stone,pick);
            if(tier>1)Require(last<MiningRules.Seconds(BlockType.Stone,Stack(Items.Tool((ToolTier)(tier-1),ToolKind.Pickaxe))),"Better pickaxe is not faster.");
            foreach(var block in new[]{BlockType.Stone,BlockType.Cobblestone,BlockType.CoalOre,BlockType.IronOre,BlockType.DiamondOre})
            {
                world.SetBlock(cell.X,cell.Y,cell.Z,block);int before=items.Entities.Count,durability=inventory.Selected.Durability;
                Require(mining.Update(world,items,hit,true,100,inventory),"Pickaxe could not break mineral.");
                bool harvest=MiningRules.Harvests(block,pick);Require(items.Entities.Count==before+(harvest?1:0),"Under-tier ore yielded a drop.");
                Require(inventory.Selected.Durability==durability-1,"Successful block use did not wear exactly once.");
                if(harvest)Require(items.Entities[^1].Item==MiningRules.Drop(block,pick,new Random(0)),"Wrong mineral drop.");
            }
        }
        foreach(var pair in new[]{(ToolKind.Axe,BlockType.WoodLog),(ToolKind.Shovel,BlockType.Dirt),(ToolKind.Pickaxe,BlockType.Stone)})
            Require(MiningRules.Seconds(pair.Item2,Stack(Items.Tool(ToolTier.Diamond,pair.Item1)))<MiningRules.Seconds(pair.Item2,Stack(Items.Tool(ToolTier.Wood,pair.Item1))),"Tool category efficiency failed.");
        inventory.SetSlot(0,Stack(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe)));world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Stone);
        int original=inventory.Selected.Durability;mining.Update(world,items,hit,true,.1f,inventory);mining.Update(world,items,hit,false,1,inventory);
        Require(inventory.Selected.Durability==original,"Interrupted mining wore tool.");
        mining.Update(world,items,hit,true,.1f,inventory);inventory.SetSlot(1,Stack(Items.Tool(ToolTier.Stone,ToolKind.Pickaxe)));inventory.SelectedSlot=1;
        mining.Update(world,items,hit,true,0,inventory);Require(mining.Progress==0,"Switching tools retained mining progress.");
        var random=new Random(173);int flint=0;for(int i=0;i<10000;i++)if(MiningRules.Drop(BlockType.Gravel,default,random)==Items.Flint)flint++;
        Require(flint>900 && flint<1100,"Flint drop frequency not approximately 10%.");Console.WriteLine($"  Flint: {flint}/10000.");
        Require(MiningRules.Seconds(BlockType.Bedrock,inventory.Selected)<0,"Bedrock became breakable.");
        inventory.SelectedSlot=0;inventory.SetSlot(0,new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe),1,1));world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Stone);
        Require(mining.Update(world,items,hit,true,100,inventory) && inventory[0].IsEmpty && items.Entities[^1].Item==Block(BlockType.Cobblestone),"Last mining use failed to break the tool exactly once.");
    }
    public static void Melee()
    {
        for(int tier=1;tier<=4;tier++)
        {
            var session=Session();var mobs=new PassiveMobPopulation(session.World,new[]{new MobState(MobSpecies.Cow,new Vector3(40.5f,1,36),0,10)});
            ItemId sword=Items.Tool((ToolTier)tier,ToolKind.Sword);session.Inventory.SetSlot(0,new ItemStack(sword,1,1));
            Vector3 origin=new(40.5f,1.5f,40.5f);
            session.Combat.Update(session.World,mobs,session.Items,session.Mining,origin,Vector3.Right,null,true,1,session.Inventory);
            Require(session.Inventory[0].Durability==1 && mobs.Entities[0].Health==10,"A missed attack wore equipment.");
            session.Combat.Update(session.World,mobs,session.Items,session.Mining,origin,Vector3.Forward,null,true,.01f,session.Inventory);
            Require(mobs.Entities[0].Health==10-(3+tier) && session.Inventory[0].IsEmpty,"Sword damage or last-use wear is incorrect.");
            int health=mobs.Entities[0].Health;
            for(int i=0;i<10;i++)session.Combat.Update(session.World,mobs,session.Items,session.Mining,origin,Vector3.Forward,null,true,.01f,session.Inventory);
            Require(mobs.Entities[0].Health==health,"Holding LMB bypassed cooldown after tool break.");
        }
        foreach(int fps in new[]{30,60,144,360})
        {
            var session=Session();var mobs=new PassiveMobPopulation(session.World,new[]{new MobState(MobSpecies.Cow,new Vector3(40.5f,1,36),0,10)});
            session.Inventory.SetSlot(0,Stack(Items.Tool(ToolTier.Diamond,ToolKind.Sword)));var clock=new MDSM.Core.SimulationClock();
            for(int frame=0;frame<fps*2;frame++)for(int step=clock.Advance(1.0/fps);step>0;step--)
                session.Combat.Update(session.World,mobs,session.Items,session.Mining,new Vector3(40.5f,1.5f,40.5f),Vector3.Forward,null,true,(float)MDSM.Core.SimulationClock.StepSeconds,session.Inventory);
            Require(mobs.Entities.Count==0 && session.Inventory[0].Durability==1559 && session.Items.Entities.Count==1,"Render rate changed successful melee uses or death drops.");
        }
    }
    private static GameSession Session(World world=null)=>new("survival",new WorldSave(world??PigChecks.Flat(),"Survival",new Vector3(40.5f,1,40.5f),0,0,new Inventory()));
    public static void Containers()
    {
        var session=Session();var world=session.World;var a=new Point3(15,1,16);var b=new Point3(16,1,16);
        world.SetBlock(a.X,a.Y,a.Z,BlockType.Chest);world.SetBlock(b.X,b.Y,b.Z,BlockType.Chest);
        var chest=world.Containers.Get(a);var other=world.Containers.Get(b);var tool=new ItemStack(Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe),1,31);
        session.Inventory.SetSlot(0,tool);session.InventoryInteraction.Click(0,false);chest.Click(0,session.InventoryInteraction,false);
        Require(chest[0]==tool && other.Empty,"Chest transfer lost metadata or shared storage.");
        chest.SetSlot(1,Stack(Items.Coal,50));Require(!world.SetBlock(a.X,a.Y,a.Z,BlockType.Air),"Direct block update silently deleted stored items.");
        Require(session.TryOpenContainer(new BlockHit(a,default,3)) && session.TryReturnInterfaceItems() && chest[0]==tool,"Closing container removed contents.");
        session.Mining.Update(world,session.Items,new BlockHit(a,default,3),true,100,session.Inventory);
        Require(world.GetBlock(a.X,a.Y,a.Z)==BlockType.Air && session.Items.Entities.Sum(i=>i.Count)==52 && session.Items.Entities.Any(i=>i.Stack==tool),"Chest break lost contents or block.");
        var furnaceCell=new Point3(255,1,255);world.SetBlock(furnaceCell.X,furnaceCell.Y,furnaceCell.Z,BlockType.Furnace);var furnace=world.Containers.Get(furnaceCell);
        foreach(var recipe in Smelting.Recipes.Where(r=>Items.StackLimit(r.Input)>1))foreach(ItemId fuel in new[]{Items.Coal,Items.Charcoal})
        {
            var f=new BlockContainer(default,BlockType.Furnace,null);f.SetSlot(0,Stack(recipe.Input,64));f.SetSlot(1,Stack(fuel));
            f.Update(79.999);Require(f[2].Count==7 && f[0].Count==57,"Input/output consumed early.");f.Update(.001);
            Require(f[2]==Stack(recipe.Output,8) && f[0].Count==56 && f[1].IsEmpty && f.Burn<1e-7,"Coal/charcoal did not power eight operations.");
        }
        foreach(ItemId fuel in new[]{Block(BlockType.WoodLog),Block(BlockType.WoodenPlanks)})
        {var f=new BlockContainer(default,BlockType.Furnace,null);f.SetSlot(0,Stack(Block(BlockType.WoodLog),3));f.SetSlot(1,Stack(fuel,2));f.Update(30);Require(f[2]==Stack(Items.Charcoal,3),"Wood fuel cannot bootstrap charcoal efficiently.");}
        furnace.SetSlot(0,Stack(Block(BlockType.IronOre),2));furnace.SetSlot(1,Stack(Items.Coal));world.Containers.Update(4.25);
        Require(furnace[0].Count==2 && furnace[2].IsEmpty && Math.Abs(furnace.Progress-4.25)<1e-8,"Partial smelting changed input.");
        var saved=Decode(Encode(session.Capture()));var restored=saved.World.Containers.Get(furnaceCell);
        Require(Math.Abs(restored.Burn-75.75)<1e-8 && restored.Progress==4.25,"Furnace restart reset fuel/progress.");
        saved.World.Containers.Update(5.75);Require(restored[2]==Stack(Items.IronIngot) && restored[0].Count==1 && restored.Burn==70,"Restored furnace duplicated fuel/output.");
        restored.SetSlot(2,Stack(Block(BlockType.Glass),64));saved.World.Containers.Update(10);Require(restored[0].Count==1 && restored.Progress==0,"Blocked output consumed input.");
        var transfer=new InventoryInteraction(new Inventory());restored.Click(2,transfer,true);saved.World.Containers.Update(10);
        Require(transfer.Cursor.Count==32 && restored[2].Count==32 && restored[0].Count==1,"Partial output retrieval corrupted blocked smelting.");
        transfer.Click(0,false);restored.Click(2,transfer,false);saved.World.Containers.Update(10);
        Require(restored[0].IsEmpty && restored[2]==Stack(Items.IronIngot) && transfer.Cursor.Count==32,"Unblocking output did not resume exactly one operation.");
        var breaking=Session();var point=new Point3(15,1,16);breaking.World.SetBlock(point.X,point.Y,point.Z,BlockType.Furnace);
        var partial=breaking.World.Containers.Get(point);partial.SetSlot(0,Stack(Block(BlockType.IronOre),2));partial.SetSlot(1,Stack(Items.Coal,2));breaking.World.Containers.Update(5);
        breaking.Inventory.SetSlot(0,Stack(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe)));
        Require(breaking.Mining.Update(breaking.World,breaking.Items,new BlockHit(point,default,3),true,100,breaking.Inventory),"Could not mine a furnace.");
        Require(breaking.Items.Entities.Sum(i=>i.Count)==4 && breaking.Items.Entities.All(i=>i.Item!=Items.IronIngot)
            && breaking.World.Containers.Entries.Count==0,"Broken furnace granted unfinished output or lost input/fuel/block.");
        int entries=world.Containers.Entries.Count;world.Containers.Update(.01);Require(world.Containers.LastProcessed<=entries,"Furnace update exceeded registry.");
    }
    public static void Arrows()
    {
        var session=Session();var bow=new ItemStack(Items.Bow,1,2);session.Inventory.SetSlot(0,bow);
        Require(!session.Projectiles.Shoot(session.Inventory,new Vector3(40.5f,1.5f,40.5f),Vector3.Forward,1,bow.InstanceId),"No-ammo shot created an arrow.");
        session.Inventory.SetSlot(1,Stack(Items.Arrow,10));var origin=new Vector3(40.5f,1.5f,40.5f);
        session.Bow.Update(session.Inventory,session.Projectiles,origin,Vector3.Forward,true,1);
        Require(session.Bow.Update(session.Inventory,session.Projectiles,origin,Vector3.Forward,false,0) && session.Inventory[1].Count==9 && session.Inventory[0]==bow,"Release/ammo/draw durability wrong.");
        Require(!session.Bow.Update(session.Inventory,session.Projectiles,origin,Vector3.Forward,false,1),"Release fired twice.");
        session.Projectiles.Update(12,session.Mobs,session.Items,session.WearEquipment);Require(session.Projectiles.Arrows.Count==0 && session.Inventory[0]==bow,"Miss wore bow or arrows accumulated.");
        var mobState=new MobState(MobSpecies.Pig,new Vector3(40.5f,1,35.5f),0,10);
        var save=new WorldSave(session.World,"Arrows",new Vector3(40.5f,1,40.5f),0,0,session.Inventory,Pigs:new[]{new PigState(mobState.Position,0,10)});
        session=new GameSession("arrows",save);Require(session.Projectiles.Shoot(session.Inventory,origin,Vector3.Forward,1,bow.InstanceId),"Valid shot failed.");
        var cell=new Point3(42,1,40);session.World.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Chest);
        session.InventoryInteraction.Click(0,false);session.World.Containers.Get(cell).Click(0,session.InventoryInteraction,false);
        session=new GameSession("reload",Decode(Encode(session.Capture())));
        session.Projectiles.Update(.3f,session.Mobs,session.Items,session.WearEquipment);
        Require(session.Mobs.Entities[0].Health==5 && session.World.Containers.Get(cell)[0].Durability==1 && session.Player.Health==20,"Projectile hit did not wear the original stored bow once.");
        session.Projectiles.Update(.5f,session.Mobs,session.Items,session.WearEquipment);Require(session.Mobs.Entities[0].Health==5,"Arrow repeated damage after impact.");
        var wall=Session();wall.Inventory.SetSlot(0,Stack(Items.Bow));wall.Inventory.SetSlot(1,Stack(Items.Arrow,2));wall.World.SetBlock(40,1,38,BlockType.Glass);
        wall.Projectiles.Shoot(wall.Inventory,origin,Vector3.Forward,1,wall.Inventory[0].InstanceId);wall.Projectiles.Update(.5f,wall.Mobs,wall.Items,wall.WearEquipment);
        Require(wall.Projectiles.Arrows.Count==0 && wall.Inventory[0].Durability==384,"Block collision failed or missed shot wore bow.");
        var high=Session();high.Inventory.SetSlot(0,Stack(Items.Bow));high.Inventory.SetSlot(1,Stack(Items.Arrow));
        Require(high.Projectiles.Shoot(high.Inventory,new Vector3(40.5f,World.Height+10,40.5f),Vector3.Up,1,high.Inventory[0].InstanceId),"High-building shot rejected.");
        high.Projectiles.Update(.5f,high.Mobs,high.Items,high.WearEquipment);
        Require(high.Projectiles.Arrows.Count==1 && high.Projectiles.Arrows[0].Position.Y>World.Height+16,"Valid arrow arc above the build ceiling disappeared.");
    }
    private static byte[] Encode(WorldSave save){using var stream=new MemoryStream();WorldSerializer.Write(stream,save);return stream.ToArray();}
    private static WorldSave Decode(byte[] bytes)=>WorldSerializer.Read(new MemoryStream(bytes));
    public static void Persistence()
    {
        foreach(var size in WorldExpansionChecks.RoutineSizes)
        {
            var session=Session(PigChecks.Flat(size));var tool=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Sword),1,42);
            session.Inventory.SetSlot(3,tool);session.Inventory.SelectedSlot=3;var cell=new Point3(size.Width()-1,2,size.Width()-1);
            session.World.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Chest);session.World.Containers.Get(cell).SetSlot(26,Stack(Items.Tool(ToolTier.Wood,ToolKind.Shovel)));
            session.Items.TrySpawn(new ItemStack(Items.Bow,1,29),new Vector3(41,1.5f,40),1);
            WorldSave restored=Decode(Encode(session.Capture()));Require(restored.Inventory[3]==tool && restored.Inventory.SelectedSlot==3 && restored.World.Containers.Get(cell)[26].Durability==59
                && restored.DroppedItems.Single().Durability==29,"Equipment/container/drop metadata changed in save roundtrip.");
        }
        var simple=Session().Capture();byte[] modern=Encode(simple);var legacy=new byte[SaveLayout.Survival(modern)+32];
        Array.Copy(modern,legacy,legacy.Length-32);BitConverter.GetBytes(7).CopyTo(legacy,4);SHA256.HashData(legacy.AsSpan(0,legacy.Length-32)).CopyTo(legacy.AsSpan(legacy.Length-32));
        WorldSave old=Decode(legacy);Require(old.Inventory[0].IsEmpty && old.World.Containers.Entries.Count==0 && old.Arrows.Length==0,"Version 7 migration reset/created player/container state.");
        var duplicate=Session();var shared=Stack(Items.Bow);duplicate.Inventory.SetSlot(0,shared);duplicate.Items.TrySpawn(shared,new Vector3(41,2,40),1);
        try{Encode(duplicate.Capture());throw new Exception("Duplicate equipment GUID saved.");}catch(InvalidDataException){}
        var damaged=Session();damaged.Inventory.SetSlot(0,new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Axe),1,17));
        byte[] valid=Encode(damaged.Capture());int extension=SaveLayout.Survival(valid);
        foreach(int durability in new[]{-1,0,60})
        {
            byte[] invalid=(byte[])valid.Clone();BitConverter.GetBytes(durability).CopyTo(invalid,extension+9);
            SHA256.HashData(invalid.AsSpan(0,invalid.Length-32)).CopyTo(invalid.AsSpan(invalid.Length-32));
            try{Decode(invalid);throw new Exception("Invalid durability was silently reset.");}catch(InvalidDataException){}
        }
    }
    public static void Ores()
    {
        foreach(int seed in new[]{173,-907})
        {
            var world=World.CreateNew(seed,WorldSize.Small);var counts=new int[3];var remaining=new HashSet<Point3>();
            foreach(var chunk in world.Chunks)for(int y=0;y<128;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
            {
                BlockType block=chunk.GetBlock(x,y,z);int type=block==BlockType.CoalOre?0:block==BlockType.IronOre?1:block==BlockType.DiamondOre?2:-1;if(type<0)continue;
                Require(y>=4 && y<=(type==2?20:60),"Ore escaped vertical limits.");counts[type]++;remaining.Add(new Point3(chunk.OriginX+x,y,chunk.OriginZ+z));
            }
            int veins=0,crossings=0;var queue=new Queue<Point3>();Point3[] offsets={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
            while(remaining.Count>0)
            {
                Point3 start=remaining.First();remaining.Remove(start);queue.Enqueue(start);BlockType type=world.GetBlock(start.X,start.Y,start.Z);int size=0;bool crosses=false;
                while(queue.TryDequeue(out var point))
                {size++;if(point.X/16!=start.X/16 || point.Z/16!=start.Z/16)crosses=true;foreach(var offset in offsets){var next=point+offset;if(world.GetBlock(next.X,next.Y,next.Z)==type && remaining.Remove(next))queue.Enqueue(next);}}
                Require(size>=2 && size<=8,"Ore vein merged, fragmented or exceeds 2-8 cells.");veins++;if(crosses)crossings++;
            }
            Require(counts[0]>counts[1] && counts[1]>counts[2]*3 && counts[2]>100 && crossings>10,"Ore distribution or seams failed.");
            Require(veins==world.GeneratedOres.Veins && counts[0]==world.GeneratedOres.Coal && counts[1]==world.GeneratedOres.Iron && counts[2]==world.GeneratedOres.Diamond,"Reported ore counts differ from blocks.");
            var second=new World(seed,WorldSize.Small,false);new TerrainGenerator(seed).Generate(second);OreGenerator.Generate(second);
            foreach(var chunk in world.Chunks)for(int y=4;y<=60;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
            {var block=chunk.GetBlock(x,y,z);if(block is BlockType.CoalOre or BlockType.IronOre or BlockType.DiamondOre)
                Require(second.GetBlock(chunk.OriginX+x,y,chunk.OriginZ+z)==block,"Ore generation is not deterministic in unaffected underground Stone.");}
            Console.WriteLine($"  ORES Small seed {seed}: coal={counts[0]}, iron={counts[1]}, diamond={counts[2]}, veins={veins}, crossing={crossings}, generation={world.GeneratedOres.Milliseconds:F0}ms.");
        }
    }
    // Uses real mined drops and shared cursor transactions, never grants ingredients.
    // Repositions the test player between fixtures; this is not a walking/playtesting claim.
    public static void Walkthrough()
    {
        World world=World.CreateNew(173,WorldSize.Small);var terrain=new TerrainGenerator(173);Point3 root=default;
        for(int z=8;z<world.Depth-8 && root==default;z++)for(int x=8;x<world.Width-8;x++)
        {int y=terrain.SurfaceY(x,z)+1;if(world.GetBlock(x,y,z)==BlockType.WoodLog){root=new(x,y,z);break;}}
        Require(root!=default,"No generated tree available.");
        var session=new GameSession("walkthrough",new WorldSave(world,"Walkthrough",new Vector3(root.X+3.5f,terrain.SurfaceY(root.X+3,root.Z)+1,root.Z+.5f),0,0,Inventory.CreateNewWorld()));
        Require(Enumerable.Range(0,36).All(i=>session.Inventory[i].IsEmpty),"Walkthrough did not start empty.");
        void Move(Vector3 feet)=>session=new GameSession("walkthrough",session.Capture() with{PlayerPosition=feet});
        void Collect(){for(int tick=0;tick<80;tick++)session.Items.Update(session.Player,session.Inventory,.05f);}
        int Count(ItemId item)=>Enumerable.Range(0,36).Where(i=>session.Inventory[i].Item==item).Sum(i=>session.Inventory[i].Count);
        void Select(ItemId item)
        {
            int slot=Enumerable.Range(0,36).First(i=>!session.Inventory[i].IsEmpty && session.Inventory[i].Item==item);
            if(slot>=9){session.InventoryInteraction.Click(slot,false);session.InventoryInteraction.Click(0,false);session.InventoryInteraction.Click(slot,false);slot=0;}
            session.Inventory.SelectedSlot=slot;
        }
        void Mine(Point3 target)
        {
            for(int attempt=0;attempt<32 && world.GetBlock(target.X,target.Y,target.Z)!=BlockType.Air;attempt++)
            {
                Vector3 direction=new Vector3(target.X+.5f,target.Y+.5f,target.Z+.5f)-session.Camera.Position;
                BlockHit? hit=BlockRaycaster.Cast(world,session.Camera.Position,direction);
                Require(hit.HasValue && session.Mining.Update(world,session.Items,hit,true,100,session.Inventory),"Progression could not mine target/occlusion with held tool.");
            }
            Require(world.GetBlock(target.X,target.Y,target.Z)==BlockType.Air,"Progression target remained.");
        }
        for(int y=root.Y;y<root.Y+4;y++)Mine(root with{Y=y});
        Move(new Vector3(root.X+.5f,root.Y,root.Z+.5f));Collect();Require(Count(Block(BlockType.WoodLog))>=4,"Mined logs could not be collected.");
        Point3 table=default,furnace=default;
        Point3 Plot()
        {
            for(int dz=-4;dz<=4;dz++)for(int dx=-4;dx<=4;dx++)
            {int x=root.X+dx,z=root.Z+dz,y=terrain.SurfaceY(x,z)+1;var point=new Point3(x,y,z);
                if(world.GetBlock(x,y,z)==BlockType.Air && !World.Overlaps(World.Bounds(point),session.Player.Bounds)
                    && Vector3.Distance(session.Camera.Position,new Vector3(x+.5f,y+.5f,z+.5f))<5.5f)return point;}
            throw new Exception("No nearby plot.");
        }
        void Craft(int size,ItemId result,params ItemId[] pattern)
        {
            if(size==3)Require(session.TryOpenCraftingTable(new BlockHit(table,default,3)),"Placed table did not open.");else Require(session.TryOpenCrafting(2),"Personal crafting unavailable.");
            for(int i=0;i<pattern.Length;i++)if(pattern[i].Value!=0)
            {
                int slot=Enumerable.Range(0,36).First(s=>!session.Inventory[s].IsEmpty && session.Inventory[s].Item==pattern[i]);
                session.InventoryInteraction.Click(slot,false);session.Crafting.Click(i,session.InventoryInteraction,true);Require(session.InventoryInteraction.TryReturnCursor(),"Input remainder did not return.");
            }
            Require(session.Crafting.Preview.Item==result && session.Crafting.TryCraft(session.InventoryInteraction) && session.TryReturnInterfaceItems(),"Progression recipe/output failed.");
        }
        ItemId P=Block(BlockType.WoodenPlanks),S=Items.Stick,C=Block(BlockType.Cobblestone);
        for(int i=0;i<4;i++)Craft(2,P,Block(BlockType.WoodLog),default,default,default);
        Craft(2,S,P,default,P,default);Craft(2,Block(BlockType.CraftingTable),P,P,P,P);
        table=Plot();Select(Block(BlockType.CraftingTable));Require(BlockPlacement.TryPlace(world,session.Inventory,table,session.Player.Bounds),"Crafted table did not place.");
        Craft(3,Items.Tool(ToolTier.Wood,ToolKind.Pickaxe),P,P,P,default,S,default,default,S,default);
        Select(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe));
        for(int y=root.Y-1;Count(C)<11;y--){Mine(root with{Y=y});Move(new Vector3(root.X+.5f,y,root.Z+.5f));Collect();}
        Move(new Vector3(root.X+.5f,root.Y,root.Z+.5f));
        Craft(3,Items.Tool(ToolTier.Stone,ToolKind.Pickaxe),C,C,C,default,S,default,default,S,default);
        Craft(3,Block(BlockType.Furnace),C,C,C,C,default,C,C,C,C);furnace=Plot();Select(Block(BlockType.Furnace));
        Require(BlockPlacement.TryPlace(world,session.Inventory,furnace,session.Player.Bounds),"Crafted furnace did not place.");
        Point3[] Vein(BlockType type,int needed)
        {
            for(int y=type==BlockType.DiamondOre?20:60;y>=4;y--)for(int z=2;z<world.Depth-2;z++)for(int x=2;x<world.Width-2;x++)
            {
                if(world.GetBlock(x,y,z)!=type || world.GetBlock(x,terrain.SurfaceY(x,z)+1,z)!=BlockType.Air || world.GetBlock(x,terrain.SurfaceY(x,z),z)!=BlockType.Grass)continue;
                var found=new List<Point3>{new(x,y,z)};
                for(int i=0;i<found.Count;i++)foreach(var offset in new[]{new Point3(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)})
                {var next=found[i]+offset;if(world.GetBlock(next.X,next.Y,next.Z)==type && !found.Contains(next))found.Add(next);}
                if(found.Count>=needed)return found.Take(needed).ToArray();
            }
            throw new Exception("No accessible ore vein.");
        }
        void Gather(BlockType type,int needed,ToolTier tier)
        {
            Select(Items.Tool(tier,ToolKind.Pickaxe));var targets=Vein(type,needed);Point3 start=targets[0];int top=terrain.SurfaceY(start.X,start.Z);
            Move(new Vector3(start.X+.5f,top+1,start.Z+.5f));
            for(int y=top;y>start.Y;y--){Mine(start with{Y=y});Move(new Vector3(start.X+.5f,y,start.Z+.5f));Collect();}
            foreach(Point3 ore in targets)
            {
                Mine(ore);if(world.IsSolid(ore.X,ore.Y+1,ore.Z))Mine(ore with{Y=ore.Y+1});
                Move(new Vector3(ore.X+.5f,ore.Y,ore.Z+.5f));Collect();
            }
            Move(new Vector3(root.X+.5f,root.Y,root.Z+.5f));
        }
        Gather(BlockType.CoalOre,1,ToolTier.Stone);Gather(BlockType.IronOre,3,ToolTier.Stone);
        var oven=world.Containers.Get(furnace);
        void InsertFuelOrInput(ItemId item,int slot,int count)
        {
            int source=Enumerable.Range(0,36).First(i=>session.Inventory[i].Item==item && session.Inventory[i].Count>=count);
            session.InventoryInteraction.Click(source,false);for(int i=0;i<count;i++)oven.Click(slot,session.InventoryInteraction,true);Require(session.InventoryInteraction.TryReturnCursor(),"Smelting transfer failed.");
        }
        InsertFuelOrInput(Block(BlockType.IronOre),0,3);InsertFuelOrInput(Items.Coal,1,1);world.Containers.Update(30);
        oven.Click(2,session.InventoryInteraction,false);Require(session.InventoryInteraction.TryReturnCursor() && Count(Items.IronIngot)>=3,"Mined iron could not be smelted/retrieved.");
        Craft(2,S,P,default,P,default);Craft(3,Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),Items.IronIngot,Items.IronIngot,Items.IronIngot,default,S,default,default,S,default);
        Gather(BlockType.DiamondOre,3,ToolTier.Iron);
        Craft(3,Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe),Items.Diamond,Items.Diamond,Items.Diamond,default,S,default,default,S,default);
        Require(Count(Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe))==1,"Empty-inventory progression never reached diamond equipment.");
        Console.WriteLine("  Empty start -> mined logs -> planks/sticks/table -> wooden/stone picks -> mined coal/iron -> furnace/ingots -> iron pick -> mined diamonds -> diamond pick.");
    }
}
