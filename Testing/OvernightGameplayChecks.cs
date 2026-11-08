using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class OvernightGameplayChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Building recipes, both slab halves, joining and exact slab drops",Slabs),
        ("All stair facings/halves, corners, collision and texel-scale geometry",Stairs),
        ("Editable non-solid floor/wall signs, support loss and text persistence",Signs),
        ("Adult chicken egg odds, gameplay timers, babies and capacity retries",Eggs),
        ("Carrot/potato planting, stages, harvest, food and Furnace/Smoker baking",Food),
        ("Cave-only dungeons, entrances, room dimensions, chest loot and determinism",Dungeons),
        ("Regional rain, desert exclusion, snowy climate, snow collision and shovel loot",Weather),
        ("Snowball press use, swept terrain/mob collision and zero-damage knockback",Snowballs),
        ("Shapeless flint/steel, successful-use wear, bounded fire and extinguishing",Fire),
        ("Nine-second burn, one damage per second, wet extinguishing and Creative safety",Burning),
        ("Version 14 signs, orientation, crops, eggs, weather, fire and projectile roundtrip",Persistence),
        ("Deterministic biome/mountain fields, safe heights, cliffs and protected spawn",Terrain)
    };
    private static void C(bool ok,string message)=>OvernightChecks.Check(ok,message);
    private static ItemId B(BlockType b)=>Items.FromBlock(b);
    public static int Run()
    {int failed=0;foreach(var t in All)try{t.Check();Console.WriteLine("PASS: "+t.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+t.Name+": "+e);}return failed==0?0:1;}
    public static int GenerationProbe(WorldSize size)
    {
        try
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();var w=World.CreateNew(173,size,Console.WriteLine);
            Console.WriteLine($"{size}: {w.GeneratedWaterFeatures}, {w.GeneratedDungeons.Count} dungeons, {timer.Elapsed.TotalSeconds:F2}s generation.");
            C(w.GeneratedWaterFeatures.Ponds==LakeGenerator.MaximumLakes(size) && w.GeneratedWaterFeatures.Rivers==LakeGenerator.RiverBudget(size) && w.GeneratedWaterFeatures.Lakes==MajorLakeGenerator.TargetCount(size,173),"Generation missed unchanged water population targets.");
            WorldExpansionChecks.CheckWaterShapes(w);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    public static void Slabs()
    {
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];Span<ItemId> loot=stackalloc ItemId[5];
        foreach(var material in new[]{(BlockType.WoodenPlanks,BlockType.WoodenSlab),(BlockType.Cobblestone,BlockType.CobblestoneSlab)})
        {
            var grid=new CraftingGrid(3);for(int i=0;i<3;i++)grid.SetSlot(i+3,new(B(material.Item1),1));C(grid.Preview.Item==B(material.Item2)&&grid.Preview.Count==6,"Slab recipe wrong.");
            var w=PigChecks.Flat();var inventory=new Inventory();inventory.SetSlot(0,new(B(material.Item2),3));var player=new Player(new(30,1,30));Point3 p=new(20,1,20);
            C(w.PlaceBuilding(p,material.Item2,0,false,player.Bounds,new(0,-1,0)),"Lower slab placement failed.");
            w.BlockBoxes(p,boxes,true);C(boxes[0].Min.Y==1&&boxes[0].Max.Y==1.5f,"Lower slab bounds wrong.");
            C(BlockPlacement.TryPlace(w,inventory,new BlockHit(p,new(0,1,0),1,new(20.5f,1.5f,20.5f)),player.Bounds),"Slab joining failed.");
            C(inventory[0].Count==2,"Joining did not consume exactly one slab.");
            var pick=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),1);
            C(MiningRules.Drops(w.GetBlock(20,1,20),pick,new Random(1),loot)==2 && loot[0]==B(material.Item2)&&loot[1]==loot[0],"Double slab lost an item.");
            Point3 upper=new(24,1,20);C(w.PlaceBuilding(upper,material.Item2,0,true,player.Bounds,new(0,-1,0)),"Upper slab failed.");w.BlockBoxes(upper,boxes,true);C(boxes[0].Min.Y==1.5f && boxes[0].Max.Y==2,"Upper slab bounds wrong.");
            C(!w.IntersectsSolid(new(new(24.1f,1,20.1f),new(24.9f,1.4f,20.9f))),"Upper slab fills lower air.");
            Point3 lower=new(28,1,20);C(w.PlaceBuilding(lower,material.Item2,0,false,player.Bounds,new(0,-1,0)),"Partial support fixture failed.");
            C(!w.PlaceStructure(lower+new Point3(0,1,0),BlockType.Torch,0,new(0,-1,0),player.Bounds),"Torch floats above a bottom slab.");
            var session=new GameSession("slabs",new(w,"Slabs",player.Position,0,0));w.SetBlock(28,5,20,BlockType.Sand);
            for(int i=0;i<240;i++)w.Gravity.Update(1d/120);
            C(w.Gravity.Active.Count==0 && w.GetBlock(28,2,20)==BlockType.Air && session.Items.Entities.Sum(d=>d.Item==B(BlockType.Sand)?d.Count:0)==1,"Sand on a bottom slab was lost, embedded or left floating.");
        }
    }
    public static void Stairs()
    {
        var w=PigChecks.Flat();var player=new Player(new(40,1,40));Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        foreach(bool upper in new[]{false,true})for(byte f=0;f<4;f++)
        {
            Point3 p=new(16+f*4,upper?5:1,16);C(w.PlaceBuilding(p,BlockType.WoodenStairs,f,upper,player.Bounds,new(0,-1,0)),"Stair placement failed.");
            int n=w.BlockBoxes(p,boxes,true);C(n==3,"Straight stair volume wrong.");float volume=0;
            for(int i=0;i<n;i++){Vector3 d=boxes[i].Max-boxes[i].Min;volume+=d.X*d.Y*d.Z;}C(Math.Abs(volume-.75)<.001,"Stair volume is not 3/4 block.");
        }
        Point3 a=new(20,1,25);w.PlaceBuilding(a,BlockType.CobblestoneStairs,0,false,player.Bounds,new(0,-1,0));
        w.PlaceBuilding(a+new Point3(0,0,-1),BlockType.CobblestoneStairs,1,false,player.Bounds,new(0,-1,0));
        C(w.BlockBoxes(a,boxes,true)==2,"Outer corner failed.");
        foreach(bool upper in new[]{false,true})for(byte facing=0;facing<4;facing++)
        {
            Point3 center=new(60+facing*5,upper?5:1,60),front=center+World.FacingDirection(facing),back=center+World.FacingDirection(facing+2);
            C(w.PlaceBuilding(center,BlockType.WoodenStairs,facing,upper,player.Bounds,new(0,-1,0)),"Corner center failed.");
            C(w.PlaceBuilding(front,BlockType.WoodenStairs,(byte)((facing+1)%4),upper,player.Bounds,new(0,-1,0)),"Corner front failed.");
            C(w.BlockBoxes(center,boxes,true)==2,"Outer corner changed with rotation/upper placement.");
            w.SetBlock(front.X,front.Y,front.Z,BlockType.Air);
            C(w.PlaceBuilding(back,BlockType.CobblestoneStairs,(byte)((facing+1)%4),upper,player.Bounds,new(0,-1,0)),"Corner back failed.");
            C(w.BlockBoxes(center,boxes,true)==4,"Inner corner changed with rotation/upper placement.");
        }
        var mesh=new ChunkMeshBuilder();mesh.Build(w,w.GetChunk(1,1));C(mesh.VertexCount>0,"Building geometry missing.");
        for(int i=0;i<mesh.VertexCount;i++)C(float.IsFinite(mesh.Vertices[i].Position.LengthSquared()) && mesh.Vertices[i].TextureCoordinate.X>0 && mesh.Vertices[i].TextureCoordinate.X<1,"Invalid building vertex/UV.");
    }
    public static void Signs()
    {
        var w=PigChecks.Flat();var player=new Player(new(40,1,40));Point3 p=new(15,1,15);C(w.PlaceBuilding(p,BlockType.Sign,0,false,player.Bounds,new(0,-1,0)),"Standing sign failed.");
        C(w.EditSign(p,"Hello\nMDSM") && !w.EditSign(p,new string('x',25)),"Sign text validation failed.");
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];C(w.BlockBoxes(p,boxes,true)==0 && w.BlockBoxes(p,boxes)==2,"Sign board/post collision/selection wrong.");
        for(byte facing=0;facing<4;facing++)
        {Point3 cell=new(20+facing*4,2,20),support=World.FacingDirection(facing+2);w.SetBlock(cell.X+support.X,cell.Y,cell.Z+support.Z,BlockType.Stone);C(w.PlaceBuilding(cell,BlockType.Sign,facing,false,player.Bounds,support),"Wall sign failed.");}
        var session=new GameSession("sign",new(w,"Signs",new(40,1,40),0,0));w.SetBlock(15,0,15,BlockType.Air);C(w.GetBlock(15,1,15)==BlockType.Air && session.Items.Entities.Count==1 && session.Items.Entities[0].Item==B(BlockType.Sign),"Sign support loss lost/duplicated item.");
    }
    public static void Eggs()
    {
        var w=PigChecks.Flat();var drops=new DroppedItems(w);int eggs=0;
        for(int seed=0;seed<1000;seed++)
        {var mob=new PassiveMob(new(MobSpecies.Chicken,new(20,1,20),0,6),seed);mob.LayEgg(299,drops);C(mob.EggSeconds==1,"Egg timer wrong.");int before=drops.Entities.Count;mob.LayEgg(1,drops);if(drops.Entities.Count>before)eggs++;C(mob.EggSeconds is >=270 and <=330,"Egg interval not randomized near five minutes.");}
        C(eggs is >430 and <570,"Egg probability not near 50%.");
        var baby=new PassiveMob(new(MobSpecies.Chicken,new(20,1,20),0,6,BabySeconds:600),1);baby.LayEgg(300,drops);C(baby.EggSeconds==300,"Baby laid an egg.");
        var ready=new PassiveMob(new(MobSpecies.Chicken,new(20,1,20),0,6,EggSeconds:0),2);ready.LayEgg(1,null);C(ready.EggSeconds==0,"Failed laying discarded pending egg.");ready.LayEgg(5,drops);C(ready.EggSeconds>0,"Pending egg did not retry.");
        Console.WriteLine($"  Eggs: {eggs}/1000 adult attempts.");
    }
    public static void Food()
    {
        Span<ItemId> loot=stackalloc ItemId[5];
        var w=PigChecks.Flat();var inventory=new Inventory();Point3 p=new(20,1,20);w.SetBlock(p.X,p.Y,p.Z,BlockType.FarmlandWet);
        foreach(var produce in new[]{(Items.Carrot,BlockType.Carrot1,BlockType.Carrot7),(Items.Potato,BlockType.Potato1,BlockType.Potato7)})
        {
            inventory.SetSlot(0,new(produce.Item1,4));C(w.Farming.Plant(p,inventory)&&inventory[0].Count==3,"Produce planting failed.");
            C(w.GetBlock(20,2,20)==produce.Item2,"Wrong crop species.");
            w.SetBlock(20,2,20,produce.Item3);int count=MiningRules.Drops(produce.Item3,default,new Random(2),loot);C(count>=2 && loot[0]==produce.Item1,"Crop harvest wrong.");w.SetBlock(20,2,20,BlockType.Air);
        }
        foreach(var machine in new[]{BlockType.Furnace,BlockType.Smoker})
        {var f=new BlockContainer(new(0,1,0),machine,null);f.SetSlot(0,new(Items.Potato,2));f.SetSlot(1,new(Items.Coal,1));f.Update(10/Smelting.Speed(machine));C(f[2].Item==Items.BakedPotato && f[2].Count==1 && f[0].Count==1,"Potato smelting failed.");}
        var player=new Player(new(20,1,20),18);inventory.SetSlot(0,new(Items.BakedPotato,2));C(ItemUse.TryConsume(player,inventory,true,false)&&player.Health==20&&inventory[0].Count==1,"Baked potato did not heal/cap.");C(!ItemUse.TryConsume(player,inventory,true,false)&&inventory[0].Count==1,"Full health consumed food.");
    }
    public static void Dungeons()
    {
        var w=new World(1,WorldSize.Small,false);
        for(int y=0;y<=35;y++)for(int z=10;z<=30;z++)for(int x=10;x<=30;x++)w.SetGeneratedBlock(x,y,z,BlockType.Stone);
        C(!DungeonGenerator.TryRoom(w,new(20,10,20),new Random(1)),"Dungeon generated inside solid stone.");
        for(int y=10;y<=14;y++)for(int z=18;z<=22;z++)for(int x=16;x<=22;x++)w.SetGeneratedBlock(x,y,z,BlockType.Air);
        C(DungeonGenerator.TryRoom(w,new(20,10,20),new Random(1)),"Suitable cave room rejected.");
        C(w.Containers.Entries.Count is >=1 and <=3,"Dungeon chest count wrong.");
        foreach(var chest in w.Containers.Entries.Values)C(!chest.Empty,"Dungeon chest is empty.");
        C(w.GetBlock(17,10,20)==BlockType.Air && w.GetBlock(17,11,20)==BlockType.Air,"Dungeon sealed cave entrance.");
        C(DungeonGenerator.Budget(WorldSize.Massive)==128 && DungeonGenerator.Budget(WorldSize.Small)==2,"Dungeon area scaling wrong.");
    }
    public static void Weather()
    {
        var w=PigChecks.Flat();w.Weather.Restore(new(300,180,30,30,128,1));C(w.Weather.RainAt(30,30)&&w.Weather.ExposedRain(new(30,3,30)),"Regional rain/exposure failed.");
        w.SetBlock(30,5,30,BlockType.Stone);C(!w.Weather.ExposedRain(new(30,3,30)),"Rain passes through roofs.");
        w.Climate.VariedGeneration=true;int desert=0,snow=0;
        for(int z=0;z<1024;z+=4)for(int x=0;x<1024;x+=4)
        {if(w.Climate.At(x,z)==Biome.Desert){desert++;C(!w.Weather.Precipitation(x,z),"Rain falls in desert.");}if(w.Climate.At(x,z)==Biome.SnowyForest)snow++;}
        C(desert>0&&snow>0,"Biome variation missing.");
        w.SetBlock(20,1,20,BlockType.Snow);Span<BoundingBox> boxes=stackalloc BoundingBox[9];C(w.BlockBoxes(new(20,1,20),boxes,true)==0 && w.BlockBoxes(new(20,1,20),boxes)==1 && Math.Abs(boxes[0].Max.Y-1.1875f)<.001,"Snow collision/selection wrong.");
        Span<ItemId> loot=stackalloc ItemId[5];C(MiningRules.Drops(BlockType.Snow,default,new Random(1),loot)==0,"Hand snow mining gave a ball.");C(MiningRules.Drops(BlockType.Snow,new(Items.Tool(ToolTier.Wood,ToolKind.Shovel),1),new Random(1),loot)==1&&loot[0]==Items.Snowball,"Shovel snow drop missing.");
    }
    public static void Snowballs()
    {
        var w=PigChecks.Flat();var inv=new Inventory();inv.SetSlot(0,new(Items.Snowball,3));var projectiles=new Projectiles(w);var mobs=new PassiveMobPopulation(w,new[]{new MobState(MobSpecies.Pig,new(20.5f,1,17),0,10)});var drops=new DroppedItems(w);
        C(projectiles.ThrowSnowball(inv,new(20.5f,1.6f,20),Vector3.Forward)&&inv[0].Count==2,"Throw did not consume once.");
        for(int i=0;i<60;i++)projectiles.Update(1f/120,mobs,drops,_=>throw new Exception("Snowball wore a bow."));
        C(projectiles.Arrows.Count==0 && mobs.Entities[0].Health==10,"Snowball damaged mob or failed collision.");
        var mob=mobs.Entities[0];Vector3 before=mob.Position;mob.Update(w,1f/120,Vector3.Zero);C(mob.Position.Y>before.Y,"Snowball did not knock back.");
    }
    public static void Fire()
    {
        var grid=new CraftingGrid(3);grid.SetSlot(8,new(Items.Flint,2));grid.SetSlot(0,new(Items.IronIngot,2));C(grid.Preview.Item==Items.FlintAndSteel,"Shapeless recipe missing.");var cursor=new InventoryInteraction(new Inventory());C(grid.TryCraft(cursor)&&cursor.Cursor.Durability==64&&grid[0].Count==1&&grid[8].Count==1,"Flint recipe accounting failed.");
        var w=PigChecks.Flat();w.SetBlock(20,1,20,BlockType.WoodenPlanks);C(w.Fire.Ignite(new(20,2,20))&&w.Fire.Count==1,"Ignition failed.");
        w.SetBlock(21,2,20,BlockType.WaterSource);C(w.Fire.Count==0 && w.GetBlock(20,2,20)==BlockType.Air,"Water did not extinguish fire.");w.SetBlock(21,2,20,BlockType.Air);
        C(w.Fire.Ignite(new(20,2,20)),"Reignition failed.");w.Weather.Restore(new(300,180,20,20,100,1));w.Fire.Update(1);C(w.Fire.Count==0,"Rain did not extinguish exposed fire.");
        C(!FireSimulation.Flammable(BlockType.Chest)&&FireSimulation.Flammable(BlockType.WoodLog)&&FireSimulation.Flammable(BlockType.WhiteWool),"Flammability rules wrong.");
        w.Weather.Restore(new(300,0,20,20,100,1));w.SetBlock(30,1,30,BlockType.WoodLog);w.SetBlock(31,1,30,BlockType.LavaSource);w.Fire.Update(.1);C(w.Fire.Count>0,"Lava contact did not ignite flammable block.");C(w.Fire.LastProcessed<=FireSimulation.Budget,"Fire budget exceeded.");
        for(int x=40;x<50;x++)for(int z=40;z<50;z++)C(w.Fire.Ignite(new(x,1,z)),"Fire budget fixture failed.");
        var paused=w.Fire.Capture();w.Fire.Update(0);C(paused.SequenceEqual(w.Fire.Capture()),"Paused gameplay advanced fire deadlines.");
        w.Fire.Update(1);C(w.Fire.LastProcessed==FireSimulation.Budget,"Dense fire skipped the scheduled-update budget.");
    }
    public static void Burning()
    {
        var w=PigChecks.Flat();w.SetBlock(20,1,20,BlockType.Fire);var state=new BurningStatus();var bounds=new Player(new(20.5f,1,20.5f)).Bounds;
        C(state.Update(w,bounds,1)==1 && state.Remaining==8,"Burn contact did not deal 1 HP/s.");
        var clear=new Player(new(30,1,30)).Bounds;C(state.Update(w,clear,8)==8 && state.Remaining==0 && state.Update(w,clear,3)==0,"Burn duration not nine seconds.");
        state.Restore(new(8,.5));w.SetBlock(30,1,30,BlockType.WaterSource);C(state.Update(w,clear,1)==0&&state.Remaining==0,"Water failed to extinguish entity.");
        w.SetBlock(30,1,30,BlockType.LavaSource);C(state.Update(w,clear,1)==0,"Lava contact applied a second damage source over the existing Lava hazard.");
        var session=new GameSession("burn",new(w,"Burn",new(20.5f,1,20.5f),0,0));session.ApplySettings(new(){GameMode=GameMode.Creative});session.Hazards.Update(session,1);C(session.Player.Health==20&&session.Player.Burning.Remaining==0,"Creative burned.");
    }
    public static void Persistence()
    {
        var w=PigChecks.Flat();w.Climate.VariedGeneration=true;var session=new GameSession("new",new(w,"Features",new(40,1,40),0,0,Mobs:new[]{new MobState(MobSpecies.Chicken,new(30,1,30),0,6,EggSeconds:47)}));
        C(w.PlaceBuilding(new(15,1,15),BlockType.Sign,1,false,session.Player.Bounds,new(0,-1,0))&&w.EditSign(new(15,1,15),"Persistent\nSign"),"Sign fixture failed.");
        C(w.PlaceBuilding(new(16,1,15),BlockType.WoodenStairs,3,true,session.Player.Bounds,new(0,-1,0)),"Stair save fixture failed.");
        w.SetBlock(25,1,25,BlockType.WoodenPlanks);C(w.Fire.Ignite(new(25,2,25)),"Fire save fixture failed.");session.Player.Burning.Restore(new(7.5,.25));session.Inventory.SetSlot(0,new(Items.Snowball,3));session.Projectiles.ThrowSnowball(session.Inventory,session.Camera.Position,Vector3.Forward);
        w.Weather.Restore(new(200,123,40,40,80,7));using var stream=new MemoryStream();WorldSerializer.Write(stream,session.Capture());stream.Position=0;var save=WorldSerializer.Read(stream);var loaded=new GameSession("loaded",save);
        C(save.World.Signs[new(15,1,15)]=="Persistent\nSign" && save.World.Structure(new(16,1,15)).Facing==7,"Sign/orientation changed.");
        C(save.Mobs[0].EggSeconds==47&&save.World.Fire.Count==1&&save.World.Weather.Remaining==123&&save.World.Climate.VariedGeneration,"Environment/timers lost.");
        C(loaded.Player.Burning.Capture()==new BurningState(7.5,.25)&&loaded.Projectiles.Arrows.Count==1&&loaded.Projectiles.Arrows[0].Kind==ProjectileKind.Snowball,"Burn/projectile state lost.");
        var oldWorld=PigChecks.Flat();oldWorld.SavedGameMode=GameMode.Creative;oldWorld.SavedDifficulty=Difficulty.Hard;var oldInventory=new Inventory();oldInventory.SetSlot(4,new(Items.Leather,23));
        using var legacy=new MemoryStream();WorldSerializer.Write(legacy,new(oldWorld,"V13",new(40,1,40),0,0,oldInventory));
        byte[] v13=OvernightChecks.WithoutFeatures(legacy.ToArray());BitConverter.GetBytes(13).CopyTo(v13,4);v13=OvernightChecks.WithoutFeatures(v13);
        var prior=WorldSerializer.Read(new MemoryStream(v13));C(prior.Inventory[4]==oldInventory[4] && prior.World.SavedGameMode==GameMode.Creative && prior.World.SavedDifficulty==Difficulty.Hard && !prior.World.Climate.VariedGeneration && prior.World.Fire.Count==0,"V13 compatibility reset state or regenerated biomes.");
    }
    public static void Terrain()
    {
        foreach(int seed in new[]{173,137,-907})
        {
            var terrain=new TerrainGenerator(seed,true,1024);var same=new TerrainGenerator(seed,true,1024);int high=0,cliffs=0;var biomes=new Climate(seed){VariedGeneration=true};var seen=new bool[3];
            for(int z=0;z<1024;z+=3)for(int x=0;x<1024;x+=3)
            {int y=terrain.SurfaceY(x,z);C(y>=63&&y<=121&&y==same.SurfaceY(x,z),"Terrain bounds/determinism failed.");if(y>110)high++;if(Math.Abs(y-terrain.SurfaceY(x+1,z))>=2)cliffs++;seen[(int)biomes.At(x,z)]=true;}
            C(high>50&&cliffs>10&&seen.All(x=>x),"Mountains, cliff shoulders or biomes missing.");C(terrain.MountainStrength(512,512)==0,"Mountain damaged protected spawn.");Console.WriteLine($"  Terrain {seed}: {high} highland samples, {cliffs} cliff samples; all biomes present.");
        }
    }
}
