using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class GameplayRefinementChecks
{
    internal static readonly (string Name, Action Check)[] All =
    {
        ("Difficulty before armor, fractional hits, fixed food damage and Creative immunity", Damage),
        ("Persistent validated mode/difficulty preferences and compact Settings layout", Settings),
        ("Head-only breath, 15-second capacity, difficulty drowning and immediate surfacing", Breathing),
        ("Creative registry completeness, capacity, fresh equipment and mode-safe ownership", Catalog),
        ("Instant Creative mining, bedrock, no loot/wear and unlimited placement", CreativeBlocks),
        ("Creative flight, collision, mode transitions and frame-rate independence", Flight),
        ("Farmland step-up, headroom, full walls, fences and diagonal approaches", Steps),
        ("Seven # crop meshes, stage bounds, alpha and vegetation targeting", Plants),
        ("Paired grass integrity, single loot rolls, deterministic abundance and persistence", Grass),
        ("Peaceful clears every hostile and Creative disengages without changing passives", Hostiles)
    };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static ItemStack S(ItemId item,int count=1)=>new(item,count);
    public static int Run()
    {
        int failed=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failed}/{All.Length} gameplay refinement checks passed.");return failed==0?0:1;
    }
    public static void Damage()
    {
        var world=PigChecks.Flat();
        foreach(Difficulty difficulty in Enum.GetValues<Difficulty>())
        {
            var inventory=new Inventory();inventory.SetEquipment(0,S(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Helmet)));
            var player=new Player(new(20,1,20),20,inventory){Settings=new(){Difficulty=difficulty}};
            int durability=inventory.Equipped(0).Durability;
            player.TakeDamage(1,false);
            Check(Math.Abs(player.HealthValue-(20-player.Settings.DamageMultiplier*.94))<1e-8,"Damage multiplier or armor order incorrect.");
            Check(inventory.Equipped(0).Durability==durability-1,"Armor wore more than once.");
            double before=player.HealthValue;player.TakeDamage(1,false,true);Check(Math.Abs(player.HealthValue-before+1)<1e-8,"Fixed Rotten Flesh damage was scaled.");
            player.Settings.GameMode=GameMode.Creative;before=player.HealthValue;durability=inventory.Equipped(0).Durability;
            Check(!player.TakeDamage(10000,false,true) && !player.TakeDamage(10000,false) && player.HealthValue==before && inventory.Equipped(0).Durability==durability,"Creative took damage or wore armor.");
            foreach(int distance in new[]{3,4,5,13,23})
            {
                var falling=new Player(new(25,1+distance,25)){Settings=new(){Difficulty=difficulty}};
                for(int tick=0;tick<480 && !falling.Grounded && !falling.IsDead;tick++)falling.Update(world,Vector3.Zero,false,1f/120);
                Check(Math.Abs(falling.HealthValue-Math.Max(0,20-Math.Max(0,distance-3)*falling.Settings.DamageMultiplier))<1e-6,"Fall damage was not scaled exactly once.");
            }
            world.SetBlock(30,1,30,BlockType.LavaSource);
            var lava=new Player(new(30.5f,1,30.5f)){Settings=new(){Difficulty=difficulty}};
            for(int tick=0;tick<120;tick++)lava.Update(world,Vector3.Zero,false,1f/120);
            Check(Math.Abs(lava.HealthValue-(20-2*lava.Settings.DamageMultiplier))<1e-6,"Lava damage was not scaled exactly once.");
            world.SetBlock(30,1,30,BlockType.Air);
        }
    }
    public static void Settings()
    {
        var defaults=new GameSettings();Check(defaults.Difficulty==Difficulty.Normal && defaults.GameMode==GameMode.Survival,"New preferences have wrong defaults.");
        string root=Path.Combine(AppContext.BaseDirectory,".gameplay-settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var store=new SettingsStore(Path.Combine(root,"settings.json"));defaults.Difficulty=Difficulty.Hard;defaults.GameMode=GameMode.Creative;defaults.FpsCap=144;defaults.ViewBobbing=false;
            store.Save(defaults);var loaded=store.Load();Check(loaded.Difficulty==Difficulty.Hard && loaded.GameMode==GameMode.Creative && loaded.FpsCap==144 && !loaded.ViewBobbing,"Settings did not roundtrip.");
            File.WriteAllText(store.FilePath,"{\"Difficulty\":99,\"GameMode\":-1}");loaded=store.Load();Check(loaded.Difficulty==Difficulty.Normal && loaded.GameMode==GameMode.Survival,"Invalid enum values accepted.");
            File.WriteAllText(store.FilePath,"{\"FpsCap\":240}");loaded=store.Load();Check(loaded.Difficulty==Difficulty.Normal && loaded.GameMode==GameMode.Survival && loaded.FpsCap==240,"Old settings were not compatible.");
        }
        finally{Directory.Delete(root,true);}
        foreach(var dimensions in new[]{new Point(320,240),new Point(640,360),new Point(800,600),new Point(1920,1080)})
        {
            var viewport=new Viewport(0,0,dimensions.X,dimensions.Y);
            Check(SettingsLayout.Back(viewport).Bottom<=dimensions.Y && SettingsLayout.Choice(viewport,0).Bottom<SettingsLayout.Information(viewport,0).Top,"Settings options overlap or leave viewport.");
        }
    }
    public static void Breathing()
    {
        var world=PigChecks.Flat();var player=new Player(new(20.5f,1,20.5f));
        world.SetBlock(20,1,20,BlockType.WaterSource);player.Breath.Update(player,world,30);
        Check(!player.Breath.Submerged && player.Health==20,"Feet water counted as head submersion.");
        world.SetBlock(20,2,20,BlockType.WaterSource);
        foreach(Difficulty difficulty in Enum.GetValues<Difficulty>())
        {
            player=new Player(new(20.5f,1,20.5f)){Settings=new(){Difficulty=difficulty}};
            for(int i=0;i<1800;i++)player.Breath.Update(player,world,1d/120);
            Check(player.Health==20 && player.Breath.Bubbles==0,"Breath capacity/damage threshold wrong.");
            player.Breath.Update(player,world,1);Check(Math.Abs(player.HealthValue-(20-2*player.Settings.DamageMultiplier))<1e-7,"Drowning scaling wrong.");
            world.SetBlock(20,2,20,BlockType.Air);player.Breath.Update(player,world,.001);double health=player.HealthValue;
            Check(!player.Breath.Submerged && player.Breath.Bubbles==10,"Surfacing did not reset/hide air.");
            player.Breath.Update(player,world,30);Check(player.HealthValue==health,"Drowning continued above water.");world.SetBlock(20,2,20,BlockType.WaterSource);
        }
        player.Settings.GameMode=GameMode.Creative;player.Breath.Update(player,world,60);Check(!player.Breath.Submerged && player.Breath.Bubbles==10,"Creative depleted breath.");
        Check(!BreathMeter.HeadInWater(world,new(20.5f,2.99f,20.5f)),"Head above fluid surface counted underwater.");
        foreach(int fps in new[]{30,60,144,360})
        {player=new(new(20.5f,1,20.5f));for(int i=0;i<17*fps;i++)player.Breath.Update(player,world,1d/fps);Check(Math.Abs(player.HealthValue-16)<1e-7,"Drowning depends on frame rate.");}
    }
    public static void Catalog()
    {
        ItemId[] all=Items.Obtainable.ToArray();Check(all.Length==all.Distinct().Count(),"Catalog duplicated items.");
        for(int id=1;id<328;id++)Check(all.Contains(new((ushort)id))==Items.IsDefined(new((ushort)id)),"Catalog omitted a registered obtainable item.");
        foreach(ItemId item in all)
        {
            var inventory=new Inventory();Check(CreativeCatalog.Grant(inventory,item,true)==Items.StackLimit(item) && inventory[0].Count==Items.StackLimit(item),"Wrong Creative stack size.");
            Check(inventory[0].Durability==Items.Get(item).Durability,"Equipment was not granted at full durability.");
            if(inventory[0].Durability>0){CreativeCatalog.Grant(inventory,item,false);Check(inventory[0].InstanceId!=inventory[1].InstanceId,"Creative cloned equipment identity.");}
        }
        var full=new Inventory();for(int i=0;i<36;i++)full.SetSlot(i,S(Items.Diamond,64));Check(CreativeCatalog.Grant(full,Items.Apple,true)==0 && full[0].Count==64,"Full catalog grant overwrote inventory.");
        var session=new GameSession("test",new(PigChecks.Flat(),"Modes",new(20,1,20),0,0));var settings=new GameSettings{GameMode=GameMode.Creative};session.ApplySettings(settings);
        CreativeCatalog.Grant(session.Inventory,Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe),true);session.Inventory.SetEquipment(0,S(Items.Armor(ArmorMaterial.Diamond,ArmorSlot.Helmet)));
        ItemStack tool=session.Inventory[0],armor=session.Inventory.Equipped(0);settings.GameMode=GameMode.Survival;session.ApplySettings(settings);
        Check(session.Inventory[0]==tool && session.Inventory.Equipped(0)==armor,"Switching mode changed Creative-obtained items.");
        var interaction=session.InventoryInteraction;interaction.Click(0,false);ItemStack cursor=interaction.Cursor;
        settings.GameMode=GameMode.Creative;session.ApplySettings(settings);settings.GameMode=GameMode.Survival;session.ApplySettings(settings);
        Check(interaction.Cursor==cursor && session.Inventory.Equipped(0)==armor,"Mode changes modified cursor/equipment ownership.");
        Check(interaction.TryReturnCursor(),"Could not restore cursor after mode switch.");
        using var stream=new MemoryStream();WorldSerializer.Write(stream,session.Capture());stream.Position=0;var loaded=WorldSerializer.Read(stream);
        Check(loaded.Inventory[0]==tool && loaded.Inventory.Equipped(0)==armor,"Creative-obtained equipment did not persist normally.");
        foreach(var dimensions in new[]{new Point(320,240),new Point(640,360),new Point(1280,720)})
        {
            var viewport=new Viewport(0,0,dimensions.X,dimensions.Y);Rectangle panel=InventoryLayout.Panel(viewport,true),browser=InventoryLayout.Browser(viewport);
            Check(panel.Left>=0 && browser.Right<=dimensions.X && panel.Right<browser.Left && browser.Bottom<=dimensions.Y,"Creative panel layout escaped viewport.");
        }
    }
    public static void CreativeBlocks()
    {
        var world=PigChecks.Flat();var items=new DroppedItems(world);world.LiveDrops=items;var inventory=new Inventory();inventory.SetSlot(0,S(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe)));
        ItemStack tool=inventory.Selected;var mining=new MiningController();Point3 cell=new(20,1,20);
        foreach(BlockType block in new[]{BlockType.Bedrock,BlockType.Stone,BlockType.Leaves,BlockType.Wheat1,BlockType.TallGrass,BlockType.Chest})
        {
            world.SetBlock(cell.X,cell.Y,cell.Z,block);Check(mining.Update(world,items,new(cell,default,1),true,0,inventory,true),"Creative failed instant break: "+block);
            Check(items.Entities.Count==0 && inventory.Selected==tool,"Creative mined loot or wore tool.");
        }
        inventory.SetSlot(0,S(Items.FromBlock(BlockType.Stone),2));var player=new Player(new(25,1,25));
        Check(BlockPlacement.TryPlace(world,inventory,cell,player.Bounds,true) && inventory.Selected.Count==2,"Creative placement consumed items.");
        Check(!BlockPlacement.TryPlace(world,inventory,cell,player.Bounds,true),"Placement ignored occupied cell.");
        world.SetBlock(20,1,20,BlockType.Air);Check(BlockPlacement.TryPlace(world,inventory,cell,player.Bounds) && inventory.Selected.Count==1,"Survival consumption failed after Creative.");
        world.SetBlock(20,1,20,BlockType.Bedrock);Check(!mining.Update(world,items,new(cell,default,1),true,10,inventory),"Survival can mine bedrock.");
        world.SetBlock(20,1,20,BlockType.Stone);inventory.SetSlot(0,tool);
        Check(!mining.Update(world,items,new(cell,default,1),true,.01f,inventory),"Creative instant-mining leaked into Survival.");
        Check(mining.Update(world,items,new(cell,default,1),true,10,inventory) && inventory.Selected.Durability==tool.Durability-1 && items.Entities.Count==1,"Survival loot/wear not restored.");
    }
    public static void Flight()
    {
        var world=PigChecks.Flat();float last=0;
        foreach(int fps in new[]{30,60,144,360})
        {
            var player=new Player(new(20,5,20)){Settings=new(){GameMode=GameMode.Creative}};
            for(int i=0;i<fps;i++)player.Update(world,Vector3.UnitX,false,1f/fps,swimUp:true);
            Check(Math.Abs(player.Position.X-24.3f)<.002f && Math.Abs(player.Position.Y-9.3f)<.002f,"Flight frame-rate drift.");last=player.Position.Y;
            player.Update(world,Vector3.Zero,false,.1f);Check(player.Position.Y==last,"Creative gravity active.");
            player.Settings.GameMode=GameMode.Survival;player.Update(world,Vector3.Zero,false,.01f);Check(player.Health==20 && player.Position.Y<last,"Mode change failed to restore gravity/reset fall.");
            var fast=new Player(new(20,5,20)){Settings=new(){GameMode=GameMode.Creative}};
            for(int i=0;i<fps;i++)fast.Update(world,Vector3.UnitX,false,1f/fps,swimUp:true,fastFlight:true);
            float travel=Player.MoveSpeed*Player.CreativeFlightBoostMultiplier;
            Check(Math.Abs(fast.Position.X-20-travel)<.003f && Math.Abs(fast.Position.Y-5-travel)<.003f,"Boosted flight depends on frame rate or misses vertical movement.");
            float x=fast.Position.X;fast.Update(world,Vector3.UnitX,false,1f/fps);
            Check(Math.Abs(fast.Position.X-x-Player.MoveSpeed/fps)<.0001f,"Flight boost remained after releasing Ctrl.");
            foreach(bool crouch in new[]{false,true})
            {
                var normal=new Player(new(10,1,10));var control=new Player(new(10,1,10));
                for(int i=0;i<fps;i++)
                {normal.Update(world,Vector3.UnitX,false,1f/fps,crouch);control.Update(world,Vector3.UnitX,false,1f/fps,crouch,fastFlight:true);}
                Check(normal.Position==control.Position && normal.HealthValue==control.HealthValue,"Ctrl changed Survival walking/crouching.");
            }
        }
        world.SetBlock(22,2,20,BlockType.Stone);var collision=new Player(new(20,2,20)){Settings=new(){GameMode=GameMode.Creative}};
        for(int i=0;i<120;i++)collision.Update(world,Vector3.UnitX,false,1f/120,fastFlight:true);Check(collision.Position.X<=21.7001f,"Boosted Creative flight became noclip.");
        var ceiling=new Player(new(20,World.Height+15,20)){Settings=new(){GameMode=GameMode.Creative}};
        for(int i=0;i<10;i++)ceiling.Update(world,Vector3.Zero,false,.1f,swimUp:true);
        Check(ceiling.Position.Y<=World.Height+16,"Flight escaped saveable world height.");
    }
    public static void Steps()
    {
        foreach(int fps in new[]{30,60,144,360})
        {
            var world=PigChecks.Flat();world.SetBlock(20,0,20,BlockType.FarmlandDry);var player=new Player(new(20.5f,15f/16,20.5f));
            for(int i=0;i<fps;i++)player.Update(world,Vector3.UnitX,false,1f/fps);
            Check(player.Position.X>22 && Math.Abs(player.Position.Y-1)<.001f && player.Health==20,$"Farmland step failed at {fps}: {player.Position}, grounded={player.Grounded}.");
        }
        foreach(BlockType obstacle in new[]{BlockType.Stone,BlockType.Fence,BlockType.FenceGate})
        {
            var world=PigChecks.Flat();world.SetBlock(21,1,20,obstacle);var player=new Player(new(20.3f,1,20.5f));
            for(int i=0;i<120;i++)player.Update(world,Vector3.UnitX,false,1f/120);
            Check(player.Position.X<21.5f && player.Position.Y<1.1f,"Step bypassed full block or fence.");
        }
        var low=PigChecks.Flat();low.SetBlock(20,0,20,BlockType.FarmlandDry);low.SetBlock(21,2,20,BlockType.Stone);
        var blocked=new Player(new(20.4f,15f/16,20.5f));for(int i=0;i<120;i++)blocked.Update(low,Vector3.UnitX,false,1f/120);
        Check(blocked.Position.X<=20.7001f,"Stepped into inadequate headroom.");
        var diagonalWorld=PigChecks.Flat();diagonalWorld.SetBlock(20,0,20,BlockType.FarmlandDry);
        foreach(bool crouch in new[]{false,true})
        {
            var diagonal=new Player(new(20.5f,15f/16,20.5f));
            for(int i=0;i<240;i++)diagonal.Update(diagonalWorld,new(1,0,1),false,1f/120,crouch);
            Check(diagonal.Position.X>21 && diagonal.Position.Z>21 && Math.Abs(diagonal.Position.Y-1)<.001f,"Diagonal/crouched Farmland stepping failed.");
        }
    }
    public static void Plants()
    {
        var world=new World(173,WorldSize.Small,false);var mesh=new ChunkMeshBuilder();Point3 cell=new(5,5,5);
        for(int stage=0;stage<7;stage++)
        {
            BlockType block=(BlockType)((int)BlockType.Wheat1+stage);world.SetBlock(5,5,5,block);mesh.Build(world,world.GetChunk(0,0));
            Check(mesh.VertexCount==48 && !Blocks.IsSolid(block),"Crop geometry not four double-sided planes.");
            for(int i=0;i<48;i+=12)
            {
                Vector3[] points=mesh.Vertices.Skip(i).Take(12).Select(v=>v.Position).ToArray();
                Check(points.All(p=>p.X==5.25f)||points.All(p=>p.X==5.75f)||points.All(p=>p.Z==5.25f)||points.All(p=>p.Z==5.75f),"Crop plane is not # aligned.");
            }
            Check(Math.Abs(mesh.Vertices.Take(48).Max(v=>v.Position.Y)-(5+Blocks.PlantHeight(block)))<1e-6 && mesh.Vertices.Take(48).All(v=>v.Color.A==255),"Crop height/alpha incorrect.");
        }
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        foreach(BlockType block in new[]{BlockType.RedFlower,BlockType.YellowFlower,BlockType.TallGrass,BlockType.Sapling,BlockType.Wheat3})
        {
            world.SetBlock(5,5,5,block);
            Check(world.BlockBoxes(cell,boxes,true)==0 && world.BlockBoxes(cell,boxes)==1 && boxes[0].Min.X>5 && boxes[0].Max.Y<6,"Plant targeting/collision bounds incorrect.");
            Check(BlockRaycaster.Cast(world,new(5.01f,5.5f,3),Vector3.UnitZ)==null && BlockRaycaster.Cast(world,new(5.5f,5.2f,3),Vector3.UnitZ)?.Cell==cell,"Plant ray does not follow selection bounds.");
        }
    }
    public static void Grass()
    {
        var world=PigChecks.Flat();world.SetBlock(20,0,20,BlockType.Grass);var items=new DroppedItems(world);world.LiveDrops=items;
        Check(world.SetBlock(20,1,20,BlockType.DoubleTallGrassLower) && world.GetBlock(20,2,20)==BlockType.DoubleTallGrassUpper,"Lower did not create upper.");
        var mining=new MiningController(1);mining.Update(world,items,new(new(20,2,20),default,1),true,0);
        Check(world.GetBlock(20,1,20)==BlockType.Air && world.GetBlock(20,2,20)==BlockType.Air && items.Entities.Sum(d=>d.Count)<=1,"Upper break orphaned plant or duplicated loot.");
        world.SetBlock(20,1,20,BlockType.DoubleTallGrassLower);int oldDrops=items.Entities.Sum(d=>d.Count);mining.Update(world,items,new(new(20,1,20),default,1),true,0);
        Check(world.GetBlock(20,1,20)==BlockType.Air && world.GetBlock(20,2,20)==BlockType.Air && items.Entities.Sum(d=>d.Count)-oldDrops<=1,"Lower break duplicated loot.");
        world.SetBlock(20,1,20,BlockType.DoubleTallGrassLower);world.SetBlock(20,0,20,BlockType.Dirt);
        Check(world.GetBlock(20,1,20)==BlockType.Air && world.GetBlock(20,2,20)==BlockType.Air,"Support loss orphaned grass.");
        world.SetBlock(20,0,20,BlockType.Grass);world.SetBlock(20,1,20,BlockType.DoubleTallGrassLower);
        using(var stream=new MemoryStream()){WorldSerializer.Write(stream,new(world,"Paired",new(30,1,30),0,0));stream.Position=0;var loaded=WorldSerializer.Read(stream);Check(loaded.World.GetBlock(20,1,20)==BlockType.DoubleTallGrassLower && loaded.World.GetBlock(20,2,20)==BlockType.DoubleTallGrassUpper,"Paired grass lost on load.");}
        var random=new Random(173);Span<ItemId> loot=stackalloc ItemId[5];int singles=0,doubles=0;
        for(int i=0;i<100000;i++){singles+=MiningRules.Drops(BlockType.TallGrass,default,random,loot);doubles+=MiningRules.Drops(BlockType.DoubleTallGrassLower,default,random,loot);}
        Check(singles is >9500 and <10500 && doubles is >19500 and <20500,"Grass seed probabilities incorrect.");
        world.SetGeneratedBlock(23,5,23,BlockType.DoubleTallGrassUpper);bool rejected=false;
        try{world.ValidatePairedPlants();}catch(InvalidDataException){rejected=true;}Check(rejected,"Orphaned plant snapshot accepted.");
        var a=new World(173,WorldSize.Small);var b=new World(173,WorldSize.Small);int grass=0,tall=0,paired=0;
        for(int i=0;i<a.ChunkCount;i++)
        {
            Check(a.Chunks[i].Blocks.SequenceEqual(b.Chunks[i].Blocks),"Vegetation changed for same seed.");
            foreach(byte block in a.Chunks[i].Blocks){if(block==(byte)BlockType.Grass)grass++;if(block==(byte)BlockType.TallGrass)tall++;if(block==(byte)BlockType.DoubleTallGrassLower)paired++;}
        }
        Check(tall>grass*.18 && paired>200 && paired<tall*.15,"Grass abundance/double rarity incorrect.");
    }
    public static void Hostiles()
    {
        var world=PigChecks.Flat();var settings=new GameSettings();var player=new Player(new(20,1,20)){Settings=settings};
        var hostile=new HostilePopulation(world,new[]{new MobState(MobSpecies.Zombie,new(21,1,20),0,20),new MobState(MobSpecies.Skeleton,new(200,1,200),0,20)});
        var arrows=new Projectiles(world);settings.GameMode=GameMode.Creative;hostile.ApplySettings(settings);hostile.Update(.1f,player,arrows);
        Check(player.Health==20 && arrows.Arrows.Count==0,"Creative was attacked.");
        settings.Difficulty=Difficulty.Peaceful;hostile.ApplySettings(settings);Check(hostile.Entities.Count==0,"Peaceful left dormant hostiles alive.");
        for(int i=0;i<100;i++)hostile.Update(1,player,arrows);Check(hostile.Entities.Count==0 && hostile.LastSpawnAttempts==0,"Peaceful spawned hostiles.");
        settings.Difficulty=Difficulty.Normal;settings.GameMode=GameMode.Survival;hostile.Update(6,player,arrows);Check(hostile.LastSpawnAttempts>0,"Ordinary spawning did not resume.");
        var attacker=new HostilePopulation(world,new[]{new MobState(MobSpecies.Zombie,new(21,1,20),0,20)});
        settings.GameMode=GameMode.Creative;attacker.ApplySettings(settings);for(int i=0;i<120;i++)attacker.Update(1f/120,player,arrows);
        Check(player.Health==20,"Hostile pursued/attacked Creative player.");settings.GameMode=GameMode.Survival;attacker.ApplySettings(settings);
        for(int i=0;i<60;i++)attacker.Update(1f/120,player,arrows);Check(player.Health<20,"Survival did not restore hostile targeting.");
    }
}
