using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CompletionChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Complete survival recipe registry, offsets and sixfold Obsidian costs",Recipes),
        ("Armor slots, point totals, fractional defense, accepted-hit wear and death",Armor),
        ("Lava timing, bounded propagation, source rules, contact and item immunity",Fluids),
        ("Bucket transactions, capacity failures, sources and source regeneration",Buckets),
        ("Soil collision, localized hydration, wet-only seven-stage crops and loot",Farming),
        ("Fence barriers, gate/door integrity, accurate targeting and Torch radius",Structures),
        ("Breeding pairs, cooldowns, child models, maturation and population targets",Animals),
        ("Underground hostile sampling, caps, distance/LOS, combat and loot",Hostiles),
        ("Forward zombie shoulders and downward spider legs through turns and gait",()=>{HostileModelChecks.ZombieArms();HostileModelChecks.SpiderLegs();}),
        ("Nearby cave-floor spawning, 16-mob cap, 2-4s cycles, bounded diagnostics and saves",HostileSpawnChecks.Verify),
        ("Furnace variants, valid recipes, double speed, bucket return and fuel parity",Machines),
        ("Version 10 complete state roundtrip and genuine version 9 compatibility",Persistence),
        ("Seeded narrow caves, protected terrain, bedrock, exposure and lava pools",Caves)};
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static ItemId B(BlockType block)=>Items.FromBlock(block);
    private static ItemStack S(ItemId item,int count=1)=>new(item,count);
    public static int Run(){int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}Console.WriteLine($"{All.Length-failures}/{All.Length} completion checks passed.");return failures==0?0:1;}
    public static void Recipes()
    {
        Check(CraftingRecipes.All.Length==79,"Missing crafting recipes.");var outputs=new HashSet<ItemId>();
        foreach(var recipe in CraftingRecipes.All)
        {
            outputs.Add(recipe.Output.Item);var pattern=(CraftingIngredient[])typeof(ShapedRecipe).GetField("_ingredients",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(recipe);
            for(int width=2;width<=3;width++)if(recipe.Width<=width&&recipe.Height<=width)
            for(int oy=0;oy<=width-recipe.Height;oy++)for(int ox=0;ox<=width-recipe.Width;ox++)
            {
                var grid=new CraftingGrid(width);for(int y=0;y<recipe.Height;y++)for(int x=0;x<recipe.Width;x++){var ingredient=pattern[y*recipe.Width+x];if(ingredient!=default)grid.SetSlot((y+oy)*width+x+ox,S(ingredient.Item,ingredient.Count*2));}
                Check(grid.Preview.Item==recipe.Output.Item,"Recipe offset/output failed: "+Items.Name(recipe.Output.Item));
                int before=Enumerable.Range(0,grid.SlotCount).Sum(i=>grid[i].Count);var mouse=new InventoryInteraction(new Inventory());Check(grid.TryCraft(mouse),"Could not take output.");
                Check(Enumerable.Range(0,grid.SlotCount).Sum(i=>grid[i].Count)==before-pattern.Sum(i=>i==default?0:i.Count),"Craft consumed the wrong ingredient quantities.");
                Check(mouse.Cursor.Count==recipe.Output.Count,"Output quantity wrong.");
            }
        }
        foreach(var block in new[]{BlockType.Fence,BlockType.FenceGate,BlockType.Door,BlockType.Torch,BlockType.Smoker,BlockType.BlastFurnace})Check(outputs.Contains(B(block)),"Craftable block missing recipe.");
        for(int tier=1;tier<=5;tier++)Check(outputs.Contains(Items.Tool((ToolTier)tier,ToolKind.Hoe)),"Missing Hoe.");
        for(int material=0;material<6;material++)for(int slot=0;slot<4;slot++)Check(outputs.Contains(Items.Armor((ArmorMaterial)material,(ArmorSlot)slot)),"Missing armor recipe.");
        var obs=new CraftingGrid(3);for(int x=0;x<3;x++)obs.SetSlot(x,S(B(BlockType.Obsidian),5));obs.SetSlot(4,S(Items.Stick));obs.SetSlot(7,S(Items.Stick));Check(obs.Preview.IsEmpty,"Five Obsidian substituted for six.");
        for(int x=0;x<3;x++)obs.SetSlot(x,S(B(BlockType.Obsidian),6));Check(obs.Preview.Item==Items.Tool(ToolTier.Obsidian,ToolKind.Pickaxe),"Obsidian pickaxe pattern failed.");
        var old=new CraftingGrid(2);old.SetSlot(0,S(B(BlockType.WhiteWool)));Check(old.Preview.IsEmpty,"Removed wool-to-string recipe remains.");
        Check(MiningRules.Harvests(BlockType.Obsidian,S(Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe))) && !MiningRules.Harvests(BlockType.Obsidian,S(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe))),"Obsidian harvest tier wrong.");
        Check(MiningRules.Seconds(BlockType.Obsidian,S(Items.Tool(ToolTier.Obsidian,ToolKind.Pickaxe)))<MiningRules.Seconds(BlockType.Obsidian,S(Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe))),"Obsidian tools do not outperform Diamond.");
    }
    public static void Armor()
    {
        int[] totals={6,8,11,15,20,26};var inv=new Inventory();
        for(int material=0;material<6;material++)
        {
            for(int slot=0;slot<4;slot++)inv.SetEquipment(slot,S(Items.Armor((ArmorMaterial)material,(ArmorSlot)slot)));
            Check(inv.ArmorPoints==totals[material],"Armor points wrong.");
            for(int slot=0;slot<4;slot++)Check(inv.Equipped(slot).Limit==1 && inv.Equipped(slot).Durability==Items.Get(inv.Equipped(slot).Item).Durability,"Armor lost metadata.");
        }
        Check(ArmorHud.Icons(0)==0 && ArmorHud.Icons(20)==10 && ArmorHud.Icons(26)==13 && ArmorHud.Fill(21,10)==1,"Armor HUD overflow/half icons wrong.");
        var player=new Player(new(20,1,20),20,inv);int before=inv.Equipped(0).Durability;
        for(int i=0;i<5;i++)player.TakeDamage(1,false);Check(player.Health==19 && Math.Abs(player.HealthValue-18.9)<.00001 && inv.Equipped(0).Durability==before-5,"Fractional armor damage/wear incorrect.");
        player.TakeDamage(1);before=inv.Equipped(0).Durability;Check(!player.TakeDamage(1) && inv.Equipped(0).Durability==before,"Invulnerability consumed armor.");
        double health=player.HealthValue;player.TakeDamage(1,false,true);Check(Math.Abs(player.HealthValue-health+1)<1e-7 && inv.Equipped(0).Durability==before,"Rotten-flesh exception wore/reduced armor.");
        inv.SetEquipment(0,new(Items.Armor(ArmorMaterial.Obsidian,ArmorSlot.Helmet),1,1));player.TakeDamage(1,false);Check(inv.Equipped(0).IsEmpty && inv.ArmorPoints==22,"Broken armor retained protection.");
        var mouse=new InventoryInteraction(inv);inv.SetSlot(0,S(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Helmet)));mouse.Click(0,false);inv.ClickEquipment(0,mouse,false);Check(mouse.Cursor.IsEmpty && inv.Equipped(0).Item==Items.Armor(ArmorMaterial.Iron,ArmorSlot.Helmet),"Armor transfer failed.");
        var session=new GameSession("armor",new WorldSave(PigChecks.Flat(),"Armor",new(40,1,40),0,0,inv));int quantity=Enumerable.Range(0,36).Sum(i=>session.Inventory[i].Count)+Enumerable.Range(0,4).Sum(i=>session.Inventory.Equipped(i).Count);
        session.Player.TakeDamage(1000,false,true);session.HandleDeath();Check(session.Items.Entities.Sum(d=>d.Count)==quantity && Enumerable.Range(0,4).All(i=>session.Inventory.Equipped(i).IsEmpty),"Death lost/retained armor.");session.Respawn();Check(session.Inventory.ArmorPoints==0,"Respawn restored armor.");
    }
    public static void Fluids()
    {
        var world=PigChecks.Flat();world.SetBlock(15,3,16,BlockType.LavaSource);world.LavaFlow.Update(.2);Check(world.GetBlock(15,2,16)==BlockType.Air,"Lava moved at water speed.");world.LavaFlow.Update(.6);Check(Lava.IsLava(world.GetBlock(15,2,16)),"Lava did not flow downward.");
        for(int i=0;i<50;i++){world.LavaFlow.Tick();Check(world.LavaFlow.LastProcessedCount<=512,"Unbounded lava updates.");}
        Check(Lava.IsLava(world.GetBlock(16,1,16)) && !Blocks.IsSolid(BlockType.LavaSource),"Lava crossed chunks incorrectly or became solid.");
        world.SetBlock(15,3,16,BlockType.Air);for(int i=0;i<80;i++)world.LavaFlow.Tick();Check(!Lava.IsLava(world.GetBlock(15,1,16)),"Unsupported Lava did not recede.");
        world.SetBlock(20,1,20,BlockType.LavaSource);world.SetBlock(21,1,20,BlockType.WaterSource);Check(world.GetBlock(20,1,20)==BlockType.Obsidian && Water.IsSource(world.GetBlock(21,1,20)),"Water/lava order failed.");
        world.SetBlock(30,1,30,BlockType.WaterSource);world.SetBlock(31,1,30,BlockType.LavaFlow1);Check(world.GetBlock(31,1,30)==BlockType.Obsidian,"Flowing lava contact failed.");
        world.SetBlock(40,1,40,BlockType.LavaSource);world.SetBlock(42,1,40,BlockType.LavaSource);for(int i=0;i<10;i++)world.LavaFlow.Tick();Check(world.GetBlock(41,1,40)!=BlockType.LavaSource,"Infinite lava source regeneration.");
        var seam=new World(173,WorldSize.Small,false);int revision=seam.GetChunk(0,0).Revision;
        seam.SetBlock(16,4,16,BlockType.LavaSource);seam.SetBlock(15,4,16,BlockType.LavaFlow1);
        Check(seam.GetChunk(0,0).Revision>revision && Lava.CornerHeight(seam,16,4,16)>Lava.Height(BlockType.LavaFlow1),"Lava shared-corner heights or diagonal invalidation failed.");
        var items=new DroppedItems(world);items.Spawn(Items.IronIngot,1,new(40.5f,1.2f,40.5f));Check(items.Entities.Count==0,"Lava did not immediately destroy ordinary items.");
        foreach(var item in new[]{B(BlockType.Obsidian),Items.Tool(ToolTier.Obsidian,ToolKind.Hoe),Items.Armor(ArmorMaterial.Obsidian,ArmorSlot.Boots)})items.Spawn(item,1,new(40.5f,1.2f,40.5f));Check(items.Entities.Count==3,"Lava destroyed Obsidian items.");
        foreach(int fps in new[]{30,60,144,360})
        {
            var player=new Player(new(40.5f,1,40.5f));var clock=new SimulationClock();
            for(int frame=0;frame<fps*3;frame++)for(int steps=clock.Advance(1.0/fps);steps>0;steps--)player.Update(world,Vector3.Zero,false,(float)SimulationClock.StepSeconds);
            Check(player.Health==14,"Lava damage is not two HP/second or depends on FPS.");
        }
    }
    public static void Buckets()
    {
        var world=PigChecks.Flat();world.SetBlock(40,1,38,BlockType.WaterSource);var inv=new Inventory();inv.SetSlot(0,S(Items.Bucket,3));
        var session=new GameSession("buckets",new WorldSave(world,"Buckets",new(40.5f,1,40.5f),0,-.3f,inv));
        Check(SurvivalInteractions.UseBucket(session,null) && session.Inventory[0].Count==2 && session.Inventory[1].Item==Items.WaterBucket && world.GetBlock(40,1,38)==BlockType.Air,"Bucket pickup lost buckets/fluid.");
        session.Inventory.SelectedSlot=1;var hit=new BlockHit(new(41,0,40),new(0,1,0),2);
        Check(SurvivalInteractions.UseBucket(session,hit) && session.Inventory.Selected.Item==Items.Bucket && world.GetBlock(41,1,40)==BlockType.WaterSource,"Filled bucket placement failed.");
        world.SetBlock(40,1,38,BlockType.LavaSource);session.Inventory.SelectedSlot=0;for(int i=1;i<36;i++)session.Inventory.SetSlot(i,S(Items.Coal,64));
        Check(!SurvivalInteractions.UseBucket(session,null) && session.Inventory.Selected.Count==2 && world.GetBlock(40,1,38)==BlockType.LavaSource,"Full inventory consumed source/buckets.");
        session.Inventory.SetSlot(2,default);Check(SurvivalInteractions.UseBucket(session,null) && session.Inventory[2].Item==Items.LavaBucket,"Lava pickup failed.");
        world.SetBlock(50,1,50,BlockType.WaterSource);world.SetBlock(52,1,50,BlockType.WaterSource);for(int i=0;i<5;i++)world.Fluids.Tick();Check(world.GetBlock(51,1,50)==BlockType.WaterSource,"Water replenishment regressed.");
        session.Inventory.SetSlot(0,S(Items.Bucket));world.SetBlock(40,1,38,BlockType.WaterSource);world.SetBlock(40,1,37,BlockType.Chest);
        Check(SurvivalInteractions.BeforeBlocks(session) && session.Inventory.Selected.Item==Items.WaterBucket,"Container behind source stole bucket interaction.");
    }
    public static void Farming()
    {
        var world=PigChecks.Flat();var inv=new Inventory();inv.SetSlot(0,S(Items.Tool(ToolTier.Wood,ToolKind.Hoe)));Point3 soil=new(20,0,20);
        Check(world.Farming.Till(soil,inv) && world.GetBlock(20,0,20)==BlockType.FarmlandDry && inv.Selected.Durability==58,"Tilling or successful durability failed.");
        Check(!world.Farming.Till(soil,inv) && inv.Selected.Durability==58,"Unsuccessful tilling wore Hoe.");
        world.SetBlock(22,1,22,BlockType.WaterSource);world.Farming.Update(1);Check(world.GetBlock(20,0,20)==BlockType.FarmlandWet && !world.Farming.Hydrated(new(19,0,20)),"Hydration radius/height incorrect.");
        inv.SetSlot(0,S(Items.WheatSeeds,3));Check(world.Farming.Plant(soil,inv) && inv.Selected.Count==2 && !world.Farming.Plant(new(21,0,20),inv),"Seed planting restrictions failed.");
        double interval=world.Farming.Capture().Single().StageSeconds;Check(interval*6 is >=600 and <=2400,"Crop total outside 10-40 minutes.");
        world.Farming.Update(interval+.001);Check(world.GetBlock(20,1,20)==BlockType.Wheat2,"Crop skipped stages.");
        world.SetBlock(22,1,22,BlockType.Air);world.Farming.Update(1);BlockType paused=world.GetBlock(20,1,20);world.Farming.Update(1000);Check(world.GetBlock(20,1,20)==paused,"Dry crop continued growing.");
        world.SetBlock(22,1,22,BlockType.WaterSource);world.Farming.Update(1);for(int i=0;i<6;i++)world.Farming.Update(interval+.001);Check(world.GetBlock(20,1,20)==BlockType.Wheat7,"Wet crop did not mature.");
        var items=new DroppedItems(world);var mining=new MiningController(173);Check(mining.Update(world,items,new BlockHit(new(20,1,20),default,1),true,0),"Crop could not harvest.");
        Check(items.Entities.Count(d=>d.Item==Items.Wheat)==1 && items.Entities.Count(d=>d.Item==Items.WheatSeeds) is >=1 and <=4,"Mature harvest not guaranteed.");
        world.SetBlock(20,1,20,BlockType.Wheat1);int count=items.Entities.Count;mining.Update(world,items,new(new(20,1,20),default,1),true,0);Check(items.Entities.Count==count+1 && items.Entities[^1].Item==Items.WheatSeeds,"Immature crop did not return one seed.");
        var random=new Random(173);Span<ItemId> loot=stackalloc ItemId[5];int seeds=0;for(int i=0;i<10000;i++)seeds+=MiningRules.Drops(BlockType.TallGrass,default,random,loot);Check(seeds is >900 and <1100,"Tall Grass seed probability wrong.");
        var player=new Player(new(20.5f,2,20.5f));for(int i=0;i<240;i++)player.Update(world,Vector3.Zero,false,1f/120);Check(Math.Abs(player.Position.Y-15f/16)<.001f,"Farmland collision is not lowered.");
    }
    public static void Structures()
    {
        var world=PigChecks.Flat();var player=new Player(new(35,1,35));
        Check(world.PlaceStructure(new(20,1,20),BlockType.FenceGate,0,new(0,-1,0),player.Bounds),"Gate placement failed.");
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];int count=world.BlockBoxes(new(20,1,20),boxes,true);Check(count==1 && boxes[0].Max.Y==2.5f,"Gate collision not 1.5 high.");
        world.Toggle(new(20,1,20));Check(world.BlockBoxes(new(20,1,20),boxes,true)==0,"Open Gate still collides.");
        world.SetBlock(19,1,20,BlockType.Fence);world.SetBlock(21,1,20,BlockType.Fence);
        foreach(MobSpecies species in new[]{MobSpecies.Pig,MobSpecies.Cow,MobSpecies.Sheep,MobSpecies.Chicken})
        {var mob=new PassiveMob(new(species,new(20.5f,1,18),0,MobDefinitions.Get(species).MaximumHealth),1);for(int i=0;i<600;i++)mob.Update(world,1f/120,Vector3.UnitZ);Check(mob.Position.Z>21.3f,"Open gate does not pass "+species);}
        world.SetBlock(19,1,20,BlockType.Fence);world.SetBlock(18,1,20,BlockType.Fence);count=world.BlockBoxes(new(19,1,20),boxes);Check(count>=5 && boxes[..count].ToArray().All(b=>b.Max.Y<=2),"Fence rails/connectivity or visual height wrong.");
        Check(world.PlaceStructure(new(25,1,25),BlockType.Door,1,new(0,-1,0),player.Bounds) && world.GetBlock(25,2,25)==BlockType.DoorUpper,"Door placement lost a half.");
        world.Toggle(new(25,2,25));Check(world.Structure(new(25,1,25)).Open,"Upper Door toggle failed.");
        var items=new DroppedItems(world);Check(new MiningController().Update(world,items,new(new(25,2,25),default,1),true,4),"Could not break upper door.");Check(world.GetBlock(25,1,25)==BlockType.Air && items.Entities.Single().Item==B(BlockType.Door),"Door orphaned/duplicated drop.");
        Check(world.PlaceStructure(new(30,1,30),BlockType.Torch,0,new(0,-1,0),player.Bounds),"Torch placement failed.");
        Check(world.Torches.Protected(new(30.5f,16.5f,30.5f)) && !world.Torches.Protected(new(30.5f,16.6f,30.5f)),"Torch radius is not 3D fifteen blocks.");
        Check(BlockRaycaster.Cast(world,new(30.05f,1.5f,33),Vector3.Forward)==null,"Torch had an oversized hitbox.");
        var session=new GameSession("torch",new WorldSave(world,"Torch",player.Position,0,0));world.SetBlock(30,0,30,BlockType.Air);Check(world.GetBlock(30,1,30)==BlockType.Air && !world.Torches.Protected(new(30,1,30)) && session.Items.Entities.Single().Item==B(BlockType.Torch),"Support removal lost Torch or stale protection.");
    }
    public static void Animals()
    {
        var world=PigChecks.Flat();
        foreach(MobSpecies species in new[]{MobSpecies.Pig,MobSpecies.Cow,MobSpecies.Sheep,MobSpecies.Chicken})
        {
            var population=new PassiveMobPopulation(world,new[]{new MobState(species,new(20,1,20),0,MobDefinitions.Get(species).MaximumHealth),new MobState(species,new(21,1,20),0,MobDefinitions.Get(species).MaximumHealth)});
            var inv=new Inventory();inv.SetSlot(0,S(Items.Wheat,3));Check(population.Feed(population.Entities[0],inv) && !population.Feed(population.Entities[0],inv) && population.Feed(population.Entities[1],inv) && inv.Selected.Count==1,"Feeding consumed incorrectly.");
            population.Update(.5f,new(20,1,20));Check(population.Entities.Count==3 && population.Target(species)==2,"Breeding changed natural targets or did not produce one child.");
            var baby=population.Entities.Single(m=>m.Baby);Check(baby.BabySeconds is >=900 and <=2700 && baby.Bred && population.Entities.Where(m=>!m.Baby).All(m=>m.BreedCooldown==300 && m.LoveSeconds==0),"Breeding cooldown/age invalid.");
            Check(!population.Feed(baby,inv),"Baby could breed.");inv.SetSlot(0,S(Items.Shears));Check(!population.TryShear(baby,inv,new DroppedItems(world)),"Baby could be sheared.");
            var adult=new PassiveMob(new(species,new(20,1,20),0,baby.Definition.MaximumHealth),1);var mesh=new CuboidModelBuilder(64,16);PassiveMobModels.Append(mesh,adult);var large=mesh.Vertices.AsSpan(0,mesh.Count).ToArray();mesh.Clear();PassiveMobModels.Append(mesh,baby);
            Vector3 Size(ReadOnlySpan<Microsoft.Xna.Framework.Graphics.VertexPositionColorTexture> vertices){Vector3 min=new(float.MaxValue),max=new(float.MinValue);foreach(var v in vertices){min=Vector3.Min(min,v.Position);max=Vector3.Max(max,v.Position);}return max-min;}
            Check(Vector3.Distance(Size(large.AsSpan(0,36))*.5f,Size(mesh.Vertices.AsSpan(0,36)))<.001 && Vector3.Distance(Size(large.AsSpan(36,36)),Size(mesh.Vertices.AsSpan(36,36)))<.001,"Baby uniformly scaled its head.");
            var drops=new DroppedItems(world);population.Hit(baby,100,Vector3.Zero,drops);Check(drops.Entities.Count==0 && population.PendingRespawns.Count==0,"Baby death dropped items or scheduled natural replacement.");
            var grown=new PassiveMob(new(species,new(20,1,20),0,adult.Health,BabySeconds:1,Bred:true),1);grown.TickLife(1.1f);Check(!grown.Baby && !grown.Sheared,"Baby failed to mature intact.");
        }
        var far=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Sheep,new(20,1,20),0,10,true,1)});Vector3 start=far.Entities[0].Position;far.Update(2,new(200,1,200));Check(!far.Entities[0].Sheared && far.Entities[0].Position==start,"Distant AI moved or regrowth stopped.");
        var expiration=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Pig,new(20,1,20),0,10,LoveSeconds:1)});expiration.Update(2,new(200,1,200));Check(expiration.Entities[0].LoveSeconds==0,"Unpaired breeding mode did not expire.");
        // Pending natural replacements reserve capacity against player breeding.
        var packed=new List<MobState>();for(int i=0;i<31;i++)packed.Add(new(MobSpecies.Pig,new(20+i*.01f,1,20),0,10,LoveSeconds:i<2?60:0,Bred:i>=2));
        var reserved=new PassiveMobPopulation(world,packed.ToArray(),new(new[]{3,0,0,0},new[]{new MobRespawnState(MobSpecies.Pig,30)}));reserved.Update(.5f,new(20,1,20));Check(reserved.Entities.Count==31,"Breeding stole a pending replacement's capacity.");
    }
    public static void Hostiles()
    {
        var world=PigChecks.Flat();for(int z=10;z<100;z++)for(int x=10;x<100;x++)world.SetBlock(x,19,z,BlockType.Stone);
        var player=new Player(new(40,20,40));var population=new HostilePopulation(world,new[]{new MobState(MobSpecies.Zombie,new(41,20,40),0,20)});var arrows=new Projectiles(world);
        Check(population.Suitable(MobSpecies.Zombie,new(70,20,40),player.Position) && !population.Suitable(MobSpecies.Zombie,new(42,20,40),player.Position),"Hostile spawn distance constraints failed.");
        world.SetBlock(70,20,40,BlockType.Torch);Check(!population.Suitable(MobSpecies.Zombie,new(70,20,40),player.Position),"Torch did not prevent hostile spawning.");world.SetBlock(70,20,40,BlockType.Air);
        population.Update(1f/120,player,arrows);int health=player.Health;Check(health==17,"Zombie did not contact attack.");for(int i=0;i<20;i++)population.Update(1f/120,player,arrows);Check(player.Health==health,"Contact damage occurred every tick.");
        var active=new HostilePopulation(world,new[]{new MobState(MobSpecies.Zombie,new(80,20,40),0,20)});active.Update(1,player,arrows);Check(active.Entities[0].Position.X<80 && active.ActiveCount==1,"AI did not activate between 32 and 64 blocks.");
        var distant=new HostilePopulation(world,new[]{new MobState(MobSpecies.Zombie,new(110,20,40),0,20)});distant.Update(1,player,arrows);Check(distant.Entities[0].Position==new Vector3(110,20,40),"AI active beyond 64.");
        var farPlayer=new Player(new(250,20,250));distant.Update(.01f,farPlayer,arrows);Check(distant.Entities.Count==0,"Hostile did not immediately despawn beyond 128.");
        var skeleton=new HostilePopulation(world,new[]{new MobState(MobSpecies.Skeleton,new(40,20,30),0,20)});skeleton.Update(.01f,player,arrows);Check(arrows.Arrows.Count==1 && arrows.Arrows[0].Hostile,"Skeleton did not fire owned projectile.");
        var wall=new HostilePopulation(world,new[]{new MobState(MobSpecies.Skeleton,new(40,20,30),0,20)});for(int y=20;y<24;y++)world.SetBlock(40,y,35,BlockType.Stone);var blocked=new Projectiles(world);wall.Update(.01f,player,blocked);Check(blocked.Arrows.Count==0,"Skeleton shot through terrain.");
        var drops=new DroppedItems(world);foreach(MobSpecies species in new[]{MobSpecies.Zombie,MobSpecies.Skeleton,MobSpecies.Spider})
        {var group=new HostilePopulation(world,new[]{new MobState(species,new(60,20,60),0,MobDefinitions.Get(species).MaximumHealth)});var mob=group.Entities[0];int before=drops.Entities.Count;Check(group.Hit(mob,100,Vector3.Zero,drops) && !group.Hit(mob,100,Vector3.Zero,drops),"Hostile died twice.");Check(drops.Entities.Skip(before).Any(d=>d.Item==mob.Definition.Drop && d.Count is >=1 and <=4),"Guaranteed hostile loot missing.");}
        for(int i=0;i<100;i++){population.Update(10,player,arrows);Check(population.Entities.Count<=HostilePopulation.Maximum && population.LastSpawnAttempts<=HostilePopulation.MaximumSpawnAttempts,"Hostile cap/work budget exceeded.");}
        var projectiles=new Projectiles(world);projectiles.ShootHostile(new(40,21,43),new(0,0,-18));var healthy=new Player(new(40,20,40));projectiles.Update(.2f,new PassiveMobPopulation(world),drops,_=>{},player:healthy);Check(healthy.Health==18,"Hostile projectile failed to damage player.");
        for(int y=20;y<=28;y++)for(int z=55;z<=59;z++)world.SetBlock(61,y,z,BlockType.Stone);
        var spider=new PassiveMob(new(MobSpecies.Spider,new(60,20,57),0,16),1);for(int i=0;i<300;i++)spider.Update(world,1f/120,Vector3.Right);Check(spider.Position.Y>22,"Spider did not climb a vertical wall.");
        var capStates=Enumerable.Range(0,HostilePopulation.Maximum).Select(i=>new MobState(MobSpecies.Zombie,new Vector3(60+i,20,70),0,20)).ToArray();var cap=new HostilePopulation(world,capStates);cap.Update(20,player,new Projectiles(world));Check(cap.Entities.Count==HostilePopulation.Maximum && cap.LastSpawnAttempts==0,"Full hostile population attempted more spawning.");
    }
    public static void Machines()
    {
        foreach(BlockType machine in new[]{BlockType.Furnace,BlockType.Smoker,BlockType.BlastFurnace})
        {
            ItemId input=machine==BlockType.Smoker?Items.RawPorkchop:B(BlockType.IronOre);var f=new BlockContainer(default,machine,null);f.SetSlot(0,S(input));f.SetSlot(1,S(Items.LavaBucket));f.Update(machine==BlockType.Furnace?10:5);
            Check(f[2].Count==1 && f[1].Item==Items.Bucket && Math.Abs(f.Burn-990)<.001,"Machine speed/lava-bucket recovery/fuel parity failed.");
            var state=f.Capture();var restarted=new BlockContainer(default,machine,null);restarted.Restore(state);Check(restarted.Burn==f.Burn,"Machine lost active fuel capacity.");
            if(machine!=BlockType.Furnace)
            {
                var invalid=new BlockContainer(default,machine,null);invalid.SetSlot(0,S(B(BlockType.Sand)));invalid.SetSlot(1,S(Items.Coal));invalid.Update(100);Check(invalid[0].Count==1 && invalid[1].Count==1 && invalid[2].IsEmpty,"Invalid specialized recipe consumed fuel/input.");
            }
        }
        var normal=new BlockContainer(default,BlockType.Furnace,null);normal.SetSlot(0,S(B(BlockType.Sand),64));normal.SetSlot(1,S(Items.LavaBucket));normal.Update(640);Check(normal[2].Count==64 && normal.Burn==360,"Lava fuel first batch failed.");normal.SetSlot(2,default);normal.SetSlot(0,S(B(BlockType.Sand),36));normal.Update(360);Check(normal[2].Count==36 && normal.Burn==0 && normal[1].Item==Items.Bucket,"Lava did not smelt exactly 100 items.");
    }
    public static void Persistence()
    {
        var world=PigChecks.Flat();world.SetBlock(20,0,20,BlockType.FarmlandWet);world.SetBlock(20,1,20,BlockType.Wheat3);world.SetBlock(21,0,20,BlockType.WaterSource);
        world.PlaceStructure(new(25,1,25),BlockType.Door,2,new(0,-1,0),new Player(new(40,1,40)).Bounds);world.Toggle(new(25,1,25));world.PlaceStructure(new(30,1,30),BlockType.Torch,0,new(0,-1,0),new Player(new(40,1,40)).Bounds);
        world.SetBlock(35,1,35,BlockType.Smoker);world.Containers.Get(new(35,1,35)).SetSlot(0,S(Items.RawPorkchop));world.Containers.Get(new(35,1,35)).SetSlot(1,S(Items.LavaBucket));world.Containers.Update(2);
        var inv=new Inventory();inv.SetEquipment(0,S(Items.Armor(ArmorMaterial.Obsidian,ArmorSlot.Helmet)));inv.SetSlot(0,S(Items.Tool(ToolTier.Obsidian,ToolKind.Hoe)));inv.SetSlot(1,S(Items.WaterBucket));
        var save=new WorldSave(world,"Complete",new(40,1,40),0,0,inv,Health:19,Pigs:new[]{new PigState(new(50,1,50),0,10,500,0,0,true)},Mobs:new[]{new MobState(MobSpecies.Sheep,new(60,1,60),0,10,true,100,LoveSeconds:12)},
            Hostiles:new[]{new MobState(MobSpecies.Spider,new(70,1,70),1,12)},ExactHealth:18.7,Arrows:new[]{new ArrowState(new(40,3,40),Vector3.Forward*10,.1f,2,Guid.NewGuid(),true)});
        byte[] bytes=RefinementChecks.Encode(save);WorldSave restored=WorldSerializer.Read(new MemoryStream(bytes));
        Check(restored.Pigs.AsSpan().SequenceEqual(save.Pigs) && restored.Mobs.AsSpan().SequenceEqual(save.Mobs) && restored.Hostiles.AsSpan().SequenceEqual(save.Hostiles) && restored.Arrows.AsSpan().SequenceEqual(save.Arrows),"Entity life/ownership changed in save.");
        Check(restored.ExactHealth==18.7 && restored.Inventory.Equipped(0)==inv.Equipped(0) && restored.World.Structure(new(25,1,25)).Open && restored.World.Farming.Capture().Length==1 && restored.World.Containers.Get(new(35,1,35)).Burn==996,"Extended world/equipment/furnace state lost.");
        Check(bytes.AsSpan().SequenceEqual(RefinementChecks.Encode(restored)),"Save/load duplicated/changed state.");
        // Save before the one-second hydration batch, including unplanted soil.
        world.SetBlock(22,0,22,BlockType.FarmlandDry);world.SetBlock(23,0,22,BlockType.WaterSource);
        var pending=WorldSerializer.Read(new MemoryStream(RefinementChecks.Encode(save)));
        pending.World.Farming.Update(1);Check(pending.World.GetBlock(22,0,22)==BlockType.FarmlandWet,"Unplanted soil lost its pending hydration on load.");
        // Independent old-format truncation: no version-10 fields, no regeneration or equipment grants.
        var oldSave=new WorldSave(PigChecks.Flat(),"Version9",new(40,1,40),0,0,new Inventory());byte[] modern=RefinementChecks.Encode(oldSave);
        int offset=RefinementChecks.RefinementOffset(modern);int end=offset+16; // empty natural/decay/sapling/sheep lists
        byte[] legacy=new byte[end+32];modern.AsSpan(0,end).CopyTo(legacy);BitConverter.GetBytes(9).CopyTo(legacy,4);SHA256.HashData(legacy.AsSpan(0,end)).CopyTo(legacy.AsSpan(end));
        var older=WorldSerializer.Read(new MemoryStream(legacy));Check(older.Hostiles.Length==0 && older.ExactHealth==20 && older.Inventory.ArmorPoints==0 && older.World.GeneratedCaves.Carved==0,"Version 9 loading regenerated/granted state.");
    }
    public static void Caves()
    {
        var a=World.CreateNew(173,WorldSize.Small);var b=World.CreateNew(173,WorldSize.Small);
        Check(a.GeneratedCaves.Systems>10 && a.GeneratedCaves.Carved>10000 && a.GeneratedCaves.Carved<a.BlockCount*.04 && a.GeneratedCaves.LavaPools>0 && a.GeneratedCaves.ExposedOres>0,"Cave/lava/exposure density invalid.");
        Check(a.LavaFlow.PendingCount==0 && b.LavaFlow.PendingCount==0,"Natural lava pools start with unsupported active flow.");
        var terrain=new TerrainGenerator(173);
        for(int z=0;z<a.Depth;z+=31)for(int x=0;x<a.Width;x+=31)
        {int ground=terrain.SurfaceY(x,z);if(a.GetBlock(x,ground,z)!=BlockType.Grass)continue;for(int y=ground-terrain.DirtDepth(x,z);y<ground;y++)Check(a.GetBlock(x,y,z)==BlockType.Dirt,"Cave damaged soil beneath intact Grass.");Check(a.GetBlock(x,ground-6,z)==BlockType.Stone,"Cave damaged the protected shallow roof.");}
        for(int i=0;i<a.ChunkCount;i++)Check(a.Chunks[i].Blocks.SequenceEqual(b.Chunks[i].Blocks),"Cave generation nondeterministic.");
        int air=0,lava=0;foreach(Chunk chunk in a.Chunks)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            Check(chunk.GetBlock(x,0,z)==BlockType.Bedrock,"Cave destroyed Bedrock.");
            for(int y=1;y<63;y++){BlockType block=chunk.GetBlock(x,y,z);if(block==BlockType.Air)air++;if(Lava.IsLava(block)){lava++;Check(y is >=3 and <=18,"Lava pool outside low elevations.");}}
        }
        Check(air>10000 && lava>0,"No navigable caves/lava generated.");for(int y=1;y<60;y++)Check(a.IsSolid(a.Width/2,y,a.Depth/2),"Caves damaged protected spawn.");
        var c=World.CreateNew(-907,WorldSize.Small);Check(c.GeneratedCaves.Carved!=a.GeneratedCaves.Carved,"Different seeds share cave layouts.");
        Check(a.GeneratedCaves.SurfaceOpenings+c.GeneratedCaves.SurfaceOpenings>0,"No occasional surface entrances generated.");
        Console.WriteLine($"  Caves Small 173: {a.GeneratedCaves}; low lava cells={lava}, underground air={air}.");
    }
}
