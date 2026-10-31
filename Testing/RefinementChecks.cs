using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class RefinementChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Underground Stone-only deposits, supports, determinism and exact ore priority",Deposits),
        ("Bounded leaf support, localized gradual decay, placed leaves and no tall-grass loot",Leaves),
        ("Independent leaf loot probabilities and multiple simultaneous drops",Loot),
        ("Sapling placement, timed growth, blocked retries and natural tree leaves",Saplings),
        ("Shears recipes, efficient leaf harvesting and successful-use durability",Shears),
        ("Live sheep shearing, no repeat wool, timed regrowth and sheared geometry",Sheep),
        ("Vegetation/sheep version 9 persistence and corruption validation",Persistence),
        ("Food limits, healing, press edges, furnace cooking and exact item accounting",Food),
        ("Version 8 stacked-food migration in inventory, chest and drops without replenishment",LegacyFood),
        ("Empty-ammunition bow cannot start or continue drawing",Bow),
        ("Fixed-step acceleration, jump arc, stopping, view bob fade and logical aim",Movement),
        ("Slower tiered mining, unchanged hardness ratios and harvest restrictions",Mining),
        ("Pixel-exact original artwork migration and all 32 item-atlas indices",ItemAtlasChecks.Pixels),
        ("Distance-based bob cadence, smooth fades, airborne suppression and render rates",BobbingPacing),
        ("Gradual 20-45 second tree decay, persisted deadlines and older countdowns",DecayPacing)};
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static int Run()
    {
        int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failures}/{All.Length} refinement checks passed.");return failures==0?0:1;
    }
    public static void Deposits()
    {
        foreach(int seed in new[]{173,-907,137})
        {
            var a=new World(seed,WorldSize.Small,false);new TerrainGenerator(seed).Generate(a);
            var b=new World(seed,WorldSize.Small,false);new TerrainGenerator(seed).Generate(b);
            int count=UndergroundDeposits.Generate(a);Require(count>700 && count<5000,"Deposit density outside modest area-scaled range.");
            Require(UndergroundDeposits.Generate(b)==count,"Deposit count nondeterministic.");
            for(int i=0;i<a.ChunkCount;i++)Require(a.Chunks[i].Blocks.SequenceEqual(b.Chunks[i].Blocks),"Deposit bytes nondeterministic.");
            int found=0;foreach(Chunk chunk in a.Chunks)for(int y=0;y<World.Height;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
            {
                BlockType block=chunk.GetBlock(x,y,z);if(!Blocks.Falls(block))continue;found++;
                Require(y>=5 && y<=56 && a.IsSolid(chunk.OriginX+x,y-1,chunk.OriginZ+z),"Underground deposit escaped bands or lacks support.");
            }
            Require(found==count,"Deposit accounting failed.");
            var baseline=new World(seed,WorldSize.Small,false);new TerrainGenerator(seed).Generate(baseline);
            OreGenerationStats ore=OreGenerator.Generate(a),reference=OreGenerator.Generate(baseline);
            Require((ore.Coal,ore.Iron,ore.Diamond,ore.Veins)==(reference.Coal,reference.Iron,reference.Diamond,reference.Veins),"Deposits displaced ores or changed rarity.");
            for(int c=0;c<a.ChunkCount;c++)for(int i=0;i<Chunk.BlockCount;i++)
            {
                BlockType block=(BlockType)baseline.Chunks[c].Blocks[i];if(block is BlockType.CoalOre or BlockType.IronOre or BlockType.DiamondOre)
                    Require(a.Chunks[c].Blocks[i]==(byte)block,"Ore lost its exact priority/coordinates.");
            }
            Console.WriteLine($"  Deposits seed {seed}: {count} blocks; ore coordinates/counts match the deposit-free reference.");
        }
    }
    public static void Leaves()
    {
        var world=new World(173,WorldSize.Small,false);var items=new DroppedItems(world);
        world.SetBlock(15,10,16,BlockType.WoodLog);
        for(int x=16;x<=20;x++)world.SetGeneratedBlock(x,10,16,BlockType.Leaves);
        Require(world.Vegetation.Supported(new(19,10,16)) && !world.Vegetation.Supported(new(20,10,16)),"Support distance is not four face-neighbor steps.");
        world.SetBlock(15,10,16,BlockType.Air);world.SetBlock(18,10,17,BlockType.Leaves);
        world.Vegetation.Update(1,items);Require(world.GetBlock(16,10,16)==BlockType.Leaves,"Leaves decayed immediately.");
        int initial=world.Vegetation.NaturalCount;
        // The fifth leaf is outside the original four-step invalidation radius and
        // receives its own countdown after the nearer branch disappears.
        for(int i=0;i<180;i++){world.Vegetation.Update(.5,items);Require(world.Vegetation.LastProcessed<=VegetationSimulation.WorkBudget,"Unbounded leaf processing.");}
        Require(initial==5 && world.Vegetation.NaturalCount==0 && world.GetBlock(18,10,17)==BlockType.Leaves,"Decay lost natural ownership or removed placed leaves.");
        int revision=world.Revision;world.Vegetation.Update(600,items);Require(world.Revision==revision,"Idle vegetation changed terrain.");
        for(int i=0;i<100;i++)world.Vegetation.Update(1.0/120,items);
        long allocationStart=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)world.Vegetation.Update(1.0/120,items);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-allocationStart;
        Require(allocated==0,"Idle vegetation allocates per simulation tick.");
        Console.WriteLine($"  Idle vegetation: {allocated} bytes / 10000 ticks.");
        world.SetBlock(25,1,25,BlockType.TallGrass);var mining=new MiningController();int before=items.Entities.Count;
        Require(mining.Update(world,items,new BlockHit(new(25,1,25),default,1),true,0) && items.Entities.Count==before,"Tall grass dropped items.");
        // Cutting one branch schedules other natural leaves, without a global scan.
        world.SetBlock(30,10,30,BlockType.WoodLog);for(int x=31;x<=34;x++)world.SetGeneratedBlock(x,10,30,BlockType.Leaves);
        world.SetBlock(31,10,30,BlockType.Air);world.Vegetation.Update(45,items);
        Require(world.GetBlock(34,10,30)==BlockType.Air,"A disconnected branch retained stale support.");
    }
    public static void Loot()
    {
        var random=new Random(173);Span<ItemId> loot=stackalloc ItemId[3];int saplings=0,sticks=0,apples=0,multiple=0;
        for(int i=0;i<200000;i++)
        {
            int count=LeafLoot.Roll(random,loot);if(count>1)multiple++;
            foreach(ItemId item in loot[..count]){if(item==Items.Sapling)saplings++;else if(item==Items.Stick)sticks++;else if(item==Items.Apple)apples++;else throw new Exception("Unexpected leaf loot.");}
        }
        Require(saplings is >9500 and <10500 && sticks is >3700 and <4300 && apples is >850 and <1150 && multiple>100,"Independent leaf odds/combined drops incorrect.");
        Console.WriteLine($"  200000 leaves: {saplings} saplings, {sticks} sticks, {apples} apples, {multiple} multiple rolls.");
    }
    public static void Saplings()
    {
        var world=PigChecks.Flat();var player=new Player(new Vector3(30,1,30));Point3 cell=new(15,1,16);
        world.SetBlock(15,0,16,BlockType.Dirt);
        world.SetBlock(14,0,16,BlockType.Stone);
        Require(world.TryPlace(cell,BlockType.Sapling,player.Bounds),"Sapling cannot grow on dirt.");
        Require(!world.TryPlace(new(14,1,16),BlockType.Sapling,player.Bounds),"Sapling accepted stone.");
        double delay=world.Vegetation.Capture().Saplings.Single().Remaining;Require(delay>=60 && delay<=1800,"Growth not 1-30 minutes.");
        var items=new DroppedItems(world);world.SetBlock(15,3,16,BlockType.Stone);world.Vegetation.Update(delay,items);
        Require(world.GetBlock(15,1,16)==BlockType.Sapling && world.Vegetation.Capture().Saplings.Length==1,"Obstruction destroyed sapling or abandoned growth.");
        world.SetBlock(15,3,16,BlockType.Air);world.Vegetation.Update(61,items);
        Require(world.GetBlock(15,1,16)==BlockType.WoodLog && world.Vegetation.NaturalCount>20,"Sapling did not reuse natural tree style/provenance.");
        world.SetBlock(22,0,22,BlockType.Grass);world.TryPlace(new(22,1,22),BlockType.Sapling,player.Bounds);
        var mining=new MiningController();int count=items.Entities.Count;mining.Update(world,items,new BlockHit(new(22,1,22),default,1),true,0);
        Require(items.Entities.Count==count+1 && items.Entities[^1].Item==Items.Sapling,"Planted sapling did not return itself.");
    }
    public static void Shears()
    {
        for(int width=2;width<=3;width++)for(int y=0;y<=width-2;y++)for(int x=0;x<=width-2;x++)foreach(bool mirror in new[]{false,true})
        {
            var grid=new CraftingGrid(width);grid.SetSlot(y*width+x+(mirror?1:0),new(Items.IronIngot,2));grid.SetSlot((y+1)*width+x+(mirror?0:1),new(Items.IronIngot,2));
            var mouse=new InventoryInteraction(new Inventory());Require(grid.Preview.Item==Items.Shears && grid.TryCraft(mouse) && mouse.Cursor.Durability==238,"Shears diagonal recipe/offset failed.");
        }
        var world=new World(0,WorldSize.Small,false);world.SetBlock(16,4,15,BlockType.Leaves);var inv=new Inventory();inv.SetSlot(0,new(Items.Shears,1,1));
        var drops=new DroppedItems(world);var mining=new MiningController();var hit=new BlockHit(new(16,4,15),default,2);
        Require(!mining.Update(world,drops,hit,true,.05f,inv) && inv.Selected.Durability==1,"Incomplete leaf mining wore shears.");
        Require(mining.Update(world,drops,hit,true,.06f,inv) && inv.Selected.IsEmpty && drops.Entities.Single().Item==Items.FromBlock(BlockType.Leaves),"Leaf collection failed or did not break last-use shears.");
    }
    public static void Sheep()
    {
        World world=PigChecks.Flat();var population=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Sheep,new(20,1,20),0,10)});
        PassiveMob sheep=population.Entities.Single();var inv=new Inventory();inv.SetSlot(0,new(Items.Shears,1));var items=new DroppedItems(world);
        var model=new CuboidModelBuilder(64,16);PassiveMobModels.Append(model,sheep);int full=model.Count;
        Require(population.TryShear(sheep,inv,items) && sheep.Health==10 && sheep.Sheared && inv.Selected.Durability==237 && items.Entities.Single().Count is >=1 and <=4,"Shearing state/drop/wear incorrect.");
        model.Clear();PassiveMobModels.Append(model,sheep);Require(model.Count<full,"Sheared model retained fleece geometry.");
        Require(!population.TryShear(sheep,inv,items) && inv.Selected.Durability==237,"Repeated shearing produced wool/wear.");
        double remaining=sheep.WoolSeconds;Require(remaining is >=180 and <=360,"Regrowth not a few active minutes.");
        sheep.Update(world,(float)(remaining-1));Require(sheep.Sheared,"Wool regrew early.");sheep.Update(world,2);Require(!sheep.Sheared,"Wool did not regrow.");
        Require(population.TryShear(sheep,inv,items),"Regrown sheep cannot be sheared.");int before=items.Entities.Sum(d=>d.Count);
        population.Hit(sheep,20,Vector3.Zero,items);Require(items.Entities.Sum(d=>d.Count)==before && population.Entities.Count==0,"Sheared sheep dropped duplicate wool.");
    }
    internal static byte[] Encode(WorldSave save){using var stream=new MemoryStream();WorldSerializer.Write(stream,save);return stream.ToArray();}
    private static WorldSave Decode(byte[] bytes)=>WorldSerializer.Read(new MemoryStream(bytes));
    public static void Persistence()
    {
        var world=PigChecks.Flat();world.SetBlock(15,10,16,BlockType.WoodLog);world.SetGeneratedBlock(16,10,16,BlockType.Leaves);world.SetBlock(17,10,16,BlockType.Leaves);
        world.SetBlock(15,10,16,BlockType.Air);world.SetBlock(30,0,30,BlockType.Dirt);world.SetBlock(30,1,30,BlockType.Sapling);
        world.Vegetation.Update(1,new DroppedItems(world));var before=world.Vegetation.Capture();
        var state=new MobState(MobSpecies.Sheep,new(20,1,20),.7f,6,true,123.25);
        var inv=new Inventory();inv.SetSlot(0,new(Items.Shears,1,111));inv.SetSlot(1,new(Items.Apple,7));
        var save=new WorldSave(world,"Refinements",new(40,1,40),0,0,inv,Health:13,Mobs:new[]{state});
        byte[] encoded=Encode(save);WorldSave restored=Decode(encoded);var after=restored.World.Vegetation.Capture();
        Require(before.NaturalLeaves.AsSpan().SequenceEqual(after.NaturalLeaves) && before.Decay.AsSpan().SequenceEqual(after.Decay)
            && before.Saplings.AsSpan().SequenceEqual(after.Saplings) && restored.Mobs.Single()==state && restored.Inventory[0]==inv[0],"Sparse/timed refinement state changed during roundtrip.");
        Require(encoded.AsSpan().SequenceEqual(Encode(restored)),"Version 9 save/load duplicated state.");
        Require(!restored.World.Vegetation.IsNatural(new(17,10,16)) && restored.World.Vegetation.IsNatural(new(16,10,16)),"Player leaf ownership changed.");
        restored.World.Vegetation.Update(20,new DroppedItems(restored.World));Require(restored.World.GetBlock(17,10,16)==BlockType.Leaves,"Placed leaves decayed after load.");
        byte[] corrupt=(byte[])encoded.Clone();int extension=RefinementOffset(encoded);BitConverter.GetBytes(int.MaxValue).CopyTo(corrupt,extension);Sign(corrupt);
        try{Decode(corrupt);throw new Exception("Unbounded provenance accepted.");}catch(InvalidDataException){}
    }
    public static void Food()
    {
        foreach(var (item,healing) in new[]{(Items.RawPorkchop,2),(Items.CookedPorkchop,8),(Items.Apple,4)})foreach(int health in new[]{1,10,18,19,20})
        {
            var inv=new Inventory();inv.SetSlot(0,new(item,1));var player=new Player(new(30,1,30),health);
            Require(ItemUse.TryConsume(player,inv,true,false)==(health<20) && player.Health==Math.Min(20,health+healing) && inv.Selected.Count==(health<20?0:1),"Food healing/consumption wrong.");
            Require(!ItemUse.TryConsume(player,inv,true,true),"Held RMB ate repeatedly.");
        }
        foreach(ItemId food in new[]{Items.RawPorkchop,Items.CookedPorkchop})
        {
            var inv=new Inventory();Require(inv.Add(food,40)==4 && Enumerable.Range(0,36).All(i=>inv[i].Count==1),"Food nonstackable insertion lost quantity.");
            var mouse=new InventoryInteraction(inv);mouse.Click(0,false);mouse.Click(1,true);Require(mouse.Cursor.Count==1 && inv[1].Count==1,"Food merged through cursor.");
            var world=PigChecks.Flat();var items=new DroppedItems(world);items.Spawn(food,3,new(20,2,20),0);items.Update(new Player(new(50,1,50)),new Inventory(),1);
            Require(items.Entities.Count==3 && items.Entities.All(d=>d.Count==1),"World items bypassed food limit.");
        }
        var furnace=new BlockContainer(default,BlockType.Furnace,null);furnace.SetSlot(0,new(Items.RawPorkchop,1));furnace.SetSlot(1,new(Items.Coal,1));
        furnace.Update(10);Require(furnace[2]==new ItemStack(Items.CookedPorkchop,1) && furnace[0].IsEmpty,"Furnace did not cook pork 1:1.");
        furnace.SetSlot(0,new(Items.RawPorkchop,1));furnace.Update(10);Require(furnace[0].Count==1 && furnace[2].Count==1,"Full nonstackable output consumed raw food.");
        var receiver=new InventoryInteraction(new Inventory());furnace.Click(2,receiver,false);furnace.Update(10);Require(receiver.Cursor.Count==1 && furnace[2].Count==1 && furnace[0].IsEmpty,"Output retrieval/resumed cooking lost food.");
    }
    internal static int RefinementOffset(byte[] bytes)
    {
        int offset=SaveLayout.Survival(bytes);int equipment=BitConverter.ToInt32(bytes,offset);offset+=4+equipment*25;
        int containers=BitConverter.ToInt32(bytes,offset);offset+=4;
        for(int i=0;i<containers;i++){int slots=BitConverter.ToInt32(bytes,offset+13);offset+=17+slots*23+24;}
        return offset+4+BitConverter.ToInt32(bytes,offset)*48;
    }
    private static void Sign(byte[] bytes)=>SHA256.HashData(bytes.AsSpan(0,bytes.Length-32)).CopyTo(bytes.AsSpan(bytes.Length-32));
    public static void LegacyFood()
    {
        var world=PigChecks.Flat();world.SetBlock(20,1,20,BlockType.Chest);world.Containers.Get(new(20,1,20)).SetSlot(0,new(Items.RawPorkchop,1));
        var inv=new Inventory();inv.SetSlot(0,new(Items.RawPorkchop,1));inv.SetSlot(1,new(Items.Bow,1,19));
        var save=new WorldSave(world,"Old food",new(40,1,40),.2f,.3f,inv,new[]{new DroppedItemState(Items.RawPorkchop,1,new(30,2,30),0,.7f)},13);
        byte[] modern=Encode(save),legacy=new byte[RefinementOffset(modern)+32];modern.AsSpan(0,legacy.Length-32).CopyTo(legacy);BitConverter.GetBytes(8).CopyTo(legacy,4);
        int end=SaveLayout.BlockEnd(legacy);legacy[end+8+2]=64;int drop=end+8+36*3+4;legacy[drop+2]=64;
        int containers=SaveLayout.Survival(legacy)+4+25; // one bow metadata record
        legacy[containers+4+17+2]=64;Sign(legacy);
        WorldSave loaded=Decode(legacy);
        int Total(WorldSave s)=>Enumerable.Range(0,36).Where(i=>s.Inventory[i].Item==Items.RawPorkchop).Sum(i=>s.Inventory[i].Count)
            +s.DroppedItems.Where(d=>d.Item==Items.RawPorkchop).Sum(d=>d.Count)+s.World.Containers.Capture().Sum(c=>c.Slots.Where(v=>v.Item==Items.RawPorkchop).Sum(v=>v.Count));
        Require(Total(loaded)==192 && loaded.Inventory[1]==inv[1] && loaded.Health==13 && loaded.PlayerPosition==save.PlayerPosition,"Legacy food migration lost items/player/equipment.");
        Require(loaded.DroppedItems.All(d=>d.Count==1) && Enumerable.Range(0,36).All(i=>loaded.Inventory[i].Count<=1),"Legacy food remained illegally stacked.");
        Require(Total(Decode(Encode(loaded)))==192,"Migration repeated or lost food after saving.");
        Require(loaded.World.Vegetation.NaturalCount==0,"Old worlds acquired invented natural leaf metadata.");
    }
    public static void Bow()
    {
        var world=PigChecks.Flat();var inventory=new Inventory();inventory.SetSlot(0,new(Items.Bow,1));var arrows=new Projectiles(world);var bow=new BowController();
        for(int i=0;i<360;i++)bow.Update(inventory,arrows,new(30,2,30),Vector3.Forward,true,1f/360);
        Require(bow.Power==0 && !bow.Update(inventory,arrows,new(30,2,30),Vector3.Forward,false,0) && arrows.Arrows.Count==0,"No-ammunition bow drew/fired.");
        inventory.SetSlot(1,new(Items.Arrow,1));bow.Update(inventory,arrows,new(30,2,30),Vector3.Forward,true,.5f);Require(bow.Power==.5f,"Loaded bow charge changed.");
        inventory.SetSlot(1,default);bow.Update(inventory,arrows,new(30,2,30),Vector3.Forward,true,.1f);Require(bow.Power==0,"Removed ammunition retained drawing.");
    }
    public static void Movement()
    {
        var world=PigChecks.Flat();Vector3? reference=null;float? peakReference=null;
        foreach(int fps in new[]{30,60,144,360})
        {
            var clock=new SimulationClock();var player=new Player(new(40,1,40));var bob=new ViewBobbing();float peak=1;
            for(int frame=0;frame<fps*5;frame++)for(int i=clock.Advance(1.0/fps);i>0;i--)
            {
                float dt=(float)SimulationClock.StepSeconds;bool jump=frame>=fps && frame<fps+fps/10 && player.Grounded;
                player.Update(world,frame<fps*3?Vector3.Right:Vector3.Zero,jump,dt);bob.Update(player,true,dt);peak=Math.Max(peak,player.Position.Y);
                Require(bob.Offset.Length()<=ViewBobbing.VerticalAmplitude+.0001f,"View bob too large.");
            }
            Require(peak>2.15f && peak<2.35f && player.Grounded && bob.Offset.Length()<.00001,"Jump arc/ground landing/bob fade wrong.");
            if(reference.HasValue)Require(Vector3.Distance(reference.Value,player.Position)<.035f && Math.Abs(peak-peakReference.Value)<.01f,"Movement depends on rendered FPS.");
            reference=player.Position;peakReference=peak;
            Vector3 stopped=player.Position;for(int i=0;i<120;i++)player.Update(world,Vector3.Zero,false,1f/120);
            Require(Vector3.Distance(stopped,player.Position)<.001f,"Ground friction retained drifting.");
            bob.Update(player,false,1f/120);Require(bob.Offset==Vector2.Zero,"Disabled bobbing still moves view.");
        }
        var camera=new Camera{Position=new(20,3,20)};Vector3 position=camera.Position,forward=camera.Forward;
        camera.PresentationOffset=new(.01f,-.02f,0);Require(camera.Position==position && camera.Forward==forward,"Bobbing changed logical targeting/aim.");
        string file=Path.Combine(AppContext.BaseDirectory,".bobbing-check.json");
        try{var store=new SettingsStore(file);Require(store.Load().ViewBobbing,"Bobbing not enabled by default.");store.Save(new GameSettings{ViewBobbing=false});Require(!new SettingsStore(file).Load().ViewBobbing,"Bobbing setting not persistent.");}
        finally{if(File.Exists(file))File.Delete(file);}
    }
    public static void Mining()
    {
        foreach(var pair in new[]{(BlockType.Stone,ToolKind.Pickaxe,1.5f),(BlockType.WoodLog,ToolKind.Axe,2.5f/1.5f),(BlockType.Dirt,ToolKind.Shovel,1.25f/1.5f)})
        {
            string values="";
            foreach(ToolTier tier in new[]{ToolTier.Wood,ToolTier.Stone,ToolTier.Iron,ToolTier.Diamond})
            {
                var stack=new ItemStack(Items.Tool(tier,pair.Item2),1);float before=pair.Item3*1.5f/Items.Get(stack.Item).Efficiency,after=MiningRules.Seconds(pair.Item1,stack);
                Require(Math.Abs(after-before*1.6f)<.0001,"Tier slowdown changed ratios.");values+=$" {tier}:{before:F3}->{after:F3}s";
            }
            Console.WriteLine("  Mining "+pair.Item1+values);
        }
        Require(!MiningRules.Harvests(BlockType.DiamondOre,new(Items.Tool(ToolTier.Stone,ToolKind.Pickaxe),1)) && MiningRules.Harvests(BlockType.DiamondOre,new(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),1)),"Harvest eligibility changed.");
    }
    public static void BobbingPacing()
    {
        Vector2? reference=null;
        foreach(int fps in new[]{30,60,144,360})
        {
            var bob=new ViewBobbing();Vector3 position=Vector3.Zero;float dt=1f/fps;
            bob.UpdatePose(position,true,true,dt);Vector2 previous=bob.Offset;
            int crossings=0;double firstCross=0,lastCross=0;float maxX=0,maxY=0;
            for(int frame=1;frame<=fps*10;frame++)
            {
                position.X=Player.MoveSpeed*frame/fps;bob.UpdatePose(position,true,true,dt);
                Require(Vector2.Distance(previous,bob.Offset)/dt<1.25f,"Bobbing has an abrupt direction/offset change.");
                if(previous.X<0 && bob.Offset.X>=0)
                {crossings++;double at=frame/(double)fps;if(crossings==1)firstCross=at;lastCross=at;}
                maxX=Math.Max(maxX,Math.Abs(bob.Offset.X));maxY=Math.Max(maxY,Math.Abs(bob.Offset.Y));previous=bob.Offset;
            }
            double frequency=(crossings-1)/(lastCross-firstCross);
            Require(frequency>1.38 && frequency<1.49 && maxX>.055f && maxY>.069f,"Bobbing cadence/amplitude is not broad walking motion.");
            if(reference.HasValue)Require(Vector2.Distance(reference.Value,bob.Offset)<.0002f,"Bobbing depends on render/update frequency.");
            reference=bob.Offset;
            // Equal traveled distance has equal phase even when the speed differs.
            var slow=new ViewBobbing();slow.UpdatePose(Vector3.Zero,true,true,dt);
            for(int frame=1;frame<=fps*20;frame++)slow.UpdatePose(new Vector3(Player.MoveSpeed*.5f*frame/fps,0,0),true,true,dt);
            Require(Vector2.Distance(slow.Offset*2,bob.Offset)<.0003f,"Bobbing phase follows time instead of horizontal distance.");
            foreach(bool grounded in new[]{true,false})
            {
                var fade=new ViewBobbing();fade.UpdatePose(Vector3.Zero,true,true,dt);
                for(int frame=1;frame<=fps;frame++)fade.UpdatePose(new Vector3(Player.MoveSpeed*frame/fps,0,0),true,true,dt);
                float magnitude=fade.Offset.Length();Vector3 stopped=new(Player.MoveSpeed,0,0);
                for(int frame=0;frame<fps*2;frame++)
                {
                    if(!grounded)stopped.X+=Player.MoveSpeed*dt;
                    fade.UpdatePose(stopped,grounded,true,dt);
                    Require(fade.Offset.Length()<=magnitude+.000001f,"Stopped/airborne bobbing keeps oscillating.");magnitude=fade.Offset.Length();
                }
                Require(fade.Offset==Vector2.Zero,"Stationary/airborne bobbing retained residual motion.");
                fade.UpdatePose(stopped+Vector3.Right*Player.MoveSpeed*dt,true,true,dt);
                Require(fade.Offset.Length()>0 && fade.Offset.Length()<.02f,"Walking restart did not fade in.");
                fade.UpdatePose(stopped,true,false,dt);Require(fade.Offset==Vector2.Zero,"Setting did not disable bobbing immediately.");
                fade.UpdatePose(stopped,true,true,dt);Require(fade.Offset==Vector2.Zero,"Re-enabling at rest restored residual shaking.");
            }
            Console.WriteLine($"  Bobbing at {fps} updates/s: {frequency:F3} Hz; horizontal {maxX:F3}, vertical {maxY:F3} blocks.");
        }
    }
    public static void DecayPacing()
    {
        foreach(int seed in new[]{173,-907,137})
        {
            var world=new World(seed,WorldSize.Small,false);world.SetBlock(16,5,16,BlockType.Grass);
            Require(WorldDecorator.TryTree(world,16,5,16,6,2,123),"Could not create decay fixture tree.");
            int foliage=world.Vegetation.NaturalCount;
            world.SetBlock(20,10,16,BlockType.Leaves); // Player-placed, including after save/load.
            for(int y=6;y<=11;y++)world.SetBlock(16,y,16,BlockType.Air);
            var scheduled=world.Vegetation.Capture();
            Require(scheduled.Decay.Length==foliage && scheduled.Decay.All(t=>t.Remaining>=20 && t.Remaining<=45),"Tree leaves were not all scheduled in the longer window.");
            world.Vegetation.Update(19.9,new DroppedItems(world));Require(world.Vegetation.NaturalCount==foliage,"New unsupported foliage decayed before twenty seconds.");
            var pending=world.Vegetation.Capture();
            WorldSave restored=Decode(Encode(new WorldSave(world,"Slow decay",new(30,6,30),0,0)));
            Require(pending.Decay.AsSpan().SequenceEqual(restored.World.Vegetation.Capture().Decay),"Saving changed leaf deadlines.");
            world=restored.World;var drops=new DroppedItems(world);double time=19.9,first=0,last=0;int previous=foliage;
            while(time<45.1)
            {
                time+=.1;world.Vegetation.Update(.1,drops);int current=world.Vegetation.NaturalCount;
                Require(world.Vegetation.LastProcessed<=VegetationSimulation.WorkBudget,"Decay exceeded its localized work budget.");
                if(current<previous){if(first==0)first=time;last=time;}previous=current;
                if(Math.Abs(time-32.5)<.001)Require(current>0 && current<foliage,"Decay disappeared in one burst instead of spreading over time.");
            }
            Require(first>=20 && last<=45.1 && last-first>15 && world.Vegetation.NaturalCount==0,"Tree decay did not span the intended twenty-to-forty-five-second window.");
            Require(world.GetBlock(20,10,16)==BlockType.Leaves,"Long decay affected placed Leaves.");
            Console.WriteLine($"  Leaf decay seed {seed}: {foliage} leaves, first {first:F1}s, last {last:F1}s, exact saved countdowns.");
        }
        // Earlier version-9 saves can contain shorter deadlines; retain those exact countdowns.
        var old=new World(173,WorldSize.Small,false);Point3 cell=new(15,8,16);old.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Leaves);
        int key=old.Vegetation.Key(cell);old.Vegetation.Restore(new(new[]{key},new[]{new VegetationTimer(key,7.25)},Array.Empty<VegetationTimer>()));
        old=Decode(Encode(new WorldSave(old,"Old countdown",new(30,6,30),0,0))).World;
        Require(old.Vegetation.Capture().Decay.Single().Remaining==7.25,"Old countdown was retimed during load.");
        var items=new DroppedItems(old);old.Vegetation.Update(7.24,items);Require(old.Vegetation.NaturalCount==1,"Old countdown expired early.");
        old.Vegetation.Update(.02,items);Require(old.Vegetation.NaturalCount==0,"Old countdown no longer resumes.");
    }
}
