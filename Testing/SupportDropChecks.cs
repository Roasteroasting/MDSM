using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class SupportDropChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Survival floor/wall Torch and Door support drops, positions and pickup",TorchesAndDoors),
        ("Support-loss flowers/saplings/crops and unchanged single/double grass loot probabilities",Plants),
        ("Chained Doors and attached Torches, direct halves and repeated neighbor updates",Chains),
        ("Creative cascade suppression, ambient updates and immediate mode restoration",Creative),
        ("Atomic cascade/container loot reservation and failed falling-block retries",Capacity),
        ("Generation/load safety, persisted collectible drops and unchanged direct harvest",PersistenceAndDirect)
    };
    private static void Check(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static int Count(DroppedItems items,ItemId item)=>items.Entities.Where(drop=>drop.Item==item).Sum(drop=>drop.Count);
    private static GameSession Session()
    {
        var world=new World(173,WorldSize.Small,false);
        for(int z=16;z<36;z++)for(int x=16;x<36;x++)world.SetGeneratedBlock(x,0,z,BlockType.Grass);
        return new("support",new(world,"Support drops",new(30,1,30),0,0));
    }
    private static readonly Point3 Root=new(20,1,20);
    private static bool Mine(GameSession session,Point3 cell,bool creative=false)
        => new MiningController(173).Update(session.World,session.Items,new(cell,default,1),true,10,session.Inventory,creative);
    private static void Put(World world,Point3 cell,BlockType block)=>Check(world.SetBlock(cell.X,cell.Y,cell.Z,block),"Fixture block edit failed: "+block);
    public static void TorchesAndDoors()
    {
        foreach(BlockType attachment in new[]{BlockType.Torch,BlockType.Door})
        {
            var session=Session();var world=session.World;Put(world,Root,BlockType.Dirt);
            Check(world.PlaceStructure(new(20,2,20),attachment,0,new(0,-1,0),session.Player.Bounds),"Attachment fixture failed.");
            Check(session.Mining.Update(world,session.Items,new(new(20,1,20),default,1),true,2,session.Inventory),"Could not mine support.");
            int count=session.Items.Entities.Where(item=>item.Item==Items.FromBlock(attachment)).Sum(item=>item.Count);
            Console.WriteLine($"  Mining Dirt under {attachment}: {count} attachment item(s), {session.Items.Entities.Count} total drops.");
            Check(count==1,"Mined support lost "+attachment+" drop.");
            Check(world.GetBlock(20,2,20)==BlockType.Air && world.GetBlock(20,3,20)==BlockType.Air && !world.Structures.ContainsKey(new(20,2,20)),"Removed attachment retained a segment/metadata.");
            Check(Count(session.Items,Items.FromBlock(BlockType.Dirt))==1 && session.Items.Entities.All(item=>item.Position==new Vector3(20.5f,item.Item==Items.FromBlock(attachment)?2.5f:1.5f,20.5f)),"Support/attachment quantities or positions changed.");
            var pickup=new Player(new(20.5f,1,20.5f));var inventory=new Inventory();
            for(int tick=0;tick<120;tick++)session.Items.Update(pickup,inventory,1f/120);
            Check(session.Items.Entities.Count==0 && Enumerable.Range(0,36).Sum(i=>inventory[i].Count)==2,"Support-loss drops were not collectible.");
        }
        var attached=Session();Put(attached.World,Root,BlockType.Dirt);
        foreach(Point3 offset in new[]{new Point3(0,1,0),new(-1,0,0),new(1,0,0),new(0,0,-1),new(0,0,1)})
        {
            Point3 cell=Root+offset;
            Check(attached.World.PlaceStructure(cell,BlockType.Torch,0,new(-offset.X,-offset.Y,-offset.Z),attached.Player.Bounds),"Torch attachment fixture failed.");
        }
        Check(Mine(attached,Root) && Count(attached.Items,Items.FromBlock(BlockType.Torch))==5 && attached.World.Structures.Count==0,"Multiple wall/floor Torches did not each drop once.");
        int before=attached.Items.Entities.Count;attached.World.SetBlock(20,1,20,BlockType.Air);attached.World.Gravity.Update(1);attached.World.Fluids.Tick();
        Check(attached.Items.Entities.Count==before,"Repeated neighbor updates duplicated Torch drops.");
    }
    public static void Plants()
    {
        foreach(BlockType plant in new[]{BlockType.RedFlower,BlockType.YellowFlower,BlockType.Sapling})
        foreach(bool mine in new[]{false,true})
        {
            var session=Session();Put(session.World,Root,BlockType.Grass);Put(session.World,new(20,2,20),plant);
            Check(mine?Mine(session,Root):session.World.SetBlock(20,1,20,plant==BlockType.Sapling?BlockType.Air:BlockType.Dirt),"Plant support did not change.");
            Check(Count(session.Items,Items.FromBlock(plant))==1 && session.World.GetBlock(20,2,20)==BlockType.Air,"Support loss failed plant loot: "+plant);
            session.World.Vegetation.Update(1900,session.Items);Check(Count(session.Items,Items.FromBlock(plant))==1,"Removed sapling retained a growth/drop timer.");
        }
        foreach(BlockType crop in Enumerable.Range((int)BlockType.Wheat1,7).Select(i=>(BlockType)i))
        foreach(bool mine in new[]{false,true})
        {
            var session=Session();Put(session.World,Root,BlockType.FarmlandWet);Put(session.World,new(20,2,20),crop);
            Check(mine?Mine(session,Root):session.World.SetBlock(20,1,20,BlockType.Air),"Farmland removal failed.");
            int seeds=Count(session.Items,Items.WheatSeeds),wheat=Count(session.Items,Items.Wheat);
            Check(crop==BlockType.Wheat7?seeds is >=1 and <=4 && wheat==1:seeds==1 && wheat==0,"Crop support loot missing/duplicated.");
            Check(session.World.Farming.Count==0,"Support removal retained crop state.");
            int before=session.Items.Entities.Sum(drop=>drop.Count);session.World.SetBlock(20,1,20,BlockType.Grass);session.World.SetBlock(20,1,20,BlockType.Dirt);
            Check(before==session.Items.Entities.Sum(drop=>drop.Count),"Subsequent soil updates repeated crop drops.");
        }
        foreach(bool doubleHeight in new[]{false,true})
        {
            var session=Session();int seeds=0,empty=0;
            for(int i=0;i<2000;i++)
            {
                Put(session.World,Root,BlockType.Grass);Put(session.World,new(20,2,20),doubleHeight?BlockType.DoubleTallGrassLower:BlockType.TallGrass);
                int before=Count(session.Items,Items.WheatSeeds);
                Check(session.World.SetBlock(20,1,20,BlockType.Dirt),"Grass support replacement failed.");
                int count=Count(session.Items,Items.WheatSeeds)-before;Check(count is 0 or 1,"Paired plant duplicated seeds.");seeds+=count;if(count==0)empty++;
                Check(session.World.GetBlock(20,2,20)==BlockType.Air && session.World.GetBlock(20,3,20)==BlockType.Air,"Support loss orphaned plant halves.");
            }
            Check(doubleHeight?seeds is >330 and <470:seeds is >140 and <260,"Support loss changed grass loot probability.");
            Check(empty>1500,"No-drop grass outcomes disappeared.");
        }
        // Glass/Pane do not require floor support; do not invent new removal rules.
        foreach(BlockType block in new[]{BlockType.Glass,BlockType.GlassPane,BlockType.Fence,BlockType.FenceGate})
        {
            var session=Session();Put(session.World,Root,BlockType.Dirt);Put(session.World,new(20,2,20),block);Mine(session,Root);
            Check(session.World.GetBlock(20,2,20)==block && Count(session.Items,Items.FromBlock(block))==0,"Changed unrelated block support/drop behavior.");
        }
    }
    private static void Tower(GameSession session)
    {
        Put(session.World,Root,BlockType.Dirt);
        for(int level=0;level<3;level++)
        {
            Point3 door=new(20,2+level*2,20);
            Check(session.World.PlaceStructure(door,BlockType.Door,(byte)level,new(0,-1,0),session.Player.Bounds),"Stacked Door fixture failed.");
            foreach(int side in new[]{-1,1})Check(session.World.PlaceStructure(new(20+side,door.Y+1,20),BlockType.Torch,0,new(-side,0,0),session.Player.Bounds),"Door-mounted Torch fixture failed.");
        }
    }
    public static void Chains()
    {
        foreach(int root in new[]{1,2,3})
        foreach(bool mine in new[]{false,true})
        {
            var session=Session();Tower(session);
            Check(mine?Mine(session,new(20,root,20)):session.World.SetBlock(20,root,20,BlockType.Air),"Chained removal failed.");
            // Raw SetBlock owns only cascading loot, while mining owns its root too.
            Check(Count(session.Items,Items.FromBlock(BlockType.Door))==(root==1 || mine?3:2)
                && Count(session.Items,Items.FromBlock(BlockType.Torch))==6,"Chained Door/Torch loot missing or duplicated.");
            for(int y=2;y<8;y++)Check(session.World.GetBlock(20,y,20)==BlockType.Air,"Chained removal orphaned a Door segment.");
            Check(session.World.GetBlock(20,1,20)==(root==1?BlockType.Air:BlockType.Dirt),"Door removal changed its surviving support.");
            Check(session.World.Structures.Count==0 && !session.World.Torches.Protected(new(20,4,20)),"Chained removal retained structure metadata.");
            int total=session.Items.Entities.Sum(item=>item.Count);
            for(int i=0;i<5;i++){session.World.SetBlock(20,root,20,BlockType.Air);session.World.Fluids.Tick();session.World.Gravity.Update(.1);}
            Check(total==session.Items.Entities.Sum(item=>item.Count),"Repeated cascade updates duplicated loot.");
        }
    }
    public static void Creative()
    {
        foreach(bool mine in new[]{false,true})
        {
            var session=Session();Tower(session);session.ApplySettings(new(){GameMode=GameMode.Creative});
            Check(mine?Mine(session,Root,true):session.World.SetBlock(20,1,20,BlockType.Air),"Creative support removal failed.");
            Check(session.Items.Entities.Count==0 && session.World.Structures.Count==0,"Creative support removal generated loot.");
            foreach(BlockType plant in new[]{BlockType.RedFlower,BlockType.Sapling,BlockType.DoubleTallGrassLower,BlockType.Wheat7})
            {
                Put(session.World,Root,Blocks.Crop(plant)?BlockType.FarmlandWet:BlockType.Grass);Put(session.World,new(20,2,20),plant);
                session.World.SetBlock(20,1,20,BlockType.Air);Check(session.Items.Entities.Count==0,"Creative ambient plant removal generated loot.");
            }
            session.ApplySettings(new(){GameMode=GameMode.Survival});Tower(session);Check(Mine(session,Root) && Count(session.Items,Items.FromBlock(BlockType.Door))==3,"Survival support loot did not resume after Creative.");
        }
    }
    public static void Capacity()
    {
        var session=Session();Put(session.World,Root,BlockType.Chest);var container=session.World.Containers.Get(Root);container.SetSlot(0,new(Items.IronIngot,7));
        foreach(Point3 offset in new[]{new Point3(0,1,0),new(-1,0,0),new(1,0,0),new(0,0,-1),new(0,0,1)})
            Check(session.World.PlaceStructure(Root+offset,BlockType.Torch,0,new(-offset.X,-offset.Y,-offset.Z),session.Player.Bounds),"Capacity Torch fixture failed.");
        // Pad only the backing list's count to exercise its production capacity
        // boundary without allocating a million irrelevant item objects. No update
        // or serialization traverses the padding, which is cleared in finally.
        var list=(List<DroppedItem>)typeof(DroppedItems).GetField("_items",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session.Items);
        try
        {
            CollectionsMarshal.SetCount(list,DroppedItems.MaximumEntities-6);int revision=session.World.Revision;
            Check(!Mine(session,Root) && session.World.Revision==revision && session.World.GetBlock(20,1,20)==BlockType.Chest
                && ReferenceEquals(container,session.World.Containers.Get(Root)) && container[0].Count==7 && session.World.Structures.Count==5,"Failed reservation partially destroyed container/attachments.");
            CollectionsMarshal.SetCount(list,DroppedItems.MaximumEntities-7);
            Check(Mine(session,Root) && list.Count==DroppedItems.MaximumEntities,"Exact-fit cascade reservation failed.");
            var emitted=list.TakeLast(7).ToArray();Check(emitted.Count(drop=>drop.Item==Items.FromBlock(BlockType.Torch))==5 && emitted.Single(drop=>drop.Item==Items.IronIngot).Count==7,"Cascade reservation lost/duplicated loot.");
        }
        finally { list.Clear(); }
        var falling=Session();Put(falling.World,new(20,3,20),BlockType.Sand);
        Check(falling.World.PlaceStructure(new(21,3,20),BlockType.Torch,0,new(-1,0,0),falling.Player.Bounds),"Falling support fixture failed.");
        list=(List<DroppedItem>)typeof(DroppedItems).GetField("_items",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(falling.Items);
        try
        {
            CollectionsMarshal.SetCount(list,DroppedItems.MaximumEntities);falling.World.Gravity.Tick();
            Check(falling.World.Gravity.Active.Count==0 && falling.World.GetBlock(20,3,20)==BlockType.Sand && falling.World.Gravity.PendingCount>0,"Failed support-drop reservation duplicated a falling block.");
        }
        finally { list.Clear(); }
        falling.World.Gravity.Tick();Check(falling.World.Gravity.Active.Count==1 && Count(falling.Items,Items.FromBlock(BlockType.Torch))==1,"Falling support did not retry exactly once.");
        for(int i=0;i<120;i++)falling.World.Gravity.Update(1d/120);
        Check(falling.World.Gravity.Active.Count==0 && falling.World.GetBlock(20,1,20)==BlockType.Sand,"Falling support did not settle normally.");
    }
    public static void PersistenceAndDirect()
    {
        var world=new World(173,WorldSize.Small,false);var unused=new DroppedItems(world);
        Put(world,Root,BlockType.Grass);Put(world,new(20,2,20),BlockType.RedFlower);world.SetBlock(20,1,20,BlockType.Dirt);
        Check(unused.Entities.Count==0 && world.GetBlock(20,2,20)==BlockType.Air,"Pre-session updates spawned unintended drops.");
        var session=Session();Tower(session);
        using(var stream=new MemoryStream())
        {
            WorldSerializer.Write(stream,session.Capture());stream.Position=0;var restored=new GameSession("loaded",WorldSerializer.Read(stream));
            Check(restored.Items.Entities.Count==0 && restored.World.Structures.Count==9,"Loading generated attachment loot or lost structures.");
            Check(Mine(restored,Root),"Loaded support could not be mined.");
            using var again=new MemoryStream();WorldSerializer.Write(again,restored.Capture());again.Position=0;var loaded=new GameSession("again",WorldSerializer.Read(again));
            Check(Count(loaded.Items,Items.FromBlock(BlockType.Door))==3 && Count(loaded.Items,Items.FromBlock(BlockType.Torch))==6 && loaded.World.Structures.Count==0,"Support loot did not persist exactly once.");
            loaded.World.SetBlock(20,1,20,BlockType.Air);Check(loaded.Items.Entities.Sum(drop=>drop.Count)==10,"Reload repeated support loot.");
        }
        foreach(BlockType block in new[]{BlockType.Torch,BlockType.Door,BlockType.RedFlower,BlockType.YellowFlower,BlockType.Sapling,BlockType.Wheat1,BlockType.Wheat7})
        {
            var direct=Session();Put(direct.World,Root,Blocks.Crop(block)?BlockType.FarmlandWet:BlockType.Grass);
            if(block is BlockType.Door or BlockType.Torch)direct.World.PlaceStructure(new(20,2,20),block,0,new(0,-1,0),direct.Player.Bounds);
            else Put(direct.World,new(20,2,20),block);
            Check(Mine(direct,new(20,2,20)),"Direct mining broke: "+block);
            if(Blocks.Crop(block))Check(Count(direct.Items,Items.WheatSeeds) is >=1 and <=4 && Count(direct.Items,Items.Wheat)==(block==BlockType.Wheat7?1:0),"Direct crop loot changed.");
            else Check(Count(direct.Items,Items.FromBlock(block))==1 && direct.Items.Entities.Count==1,"Direct harvest duplicated loot: "+block);
        }
        foreach(BlockType block in new[]{BlockType.Glass,BlockType.GlassPane})
        {var direct=Session();Put(direct.World,Root,block);Check(Mine(direct,Root) && direct.Items.Entities.Count==0,"No-drop Glass rules changed.");}
    }
    public static int Run()
    {
        int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failures}/{All.Length} support-drop checks passed.");return failures==0?0:1;
    }
}
