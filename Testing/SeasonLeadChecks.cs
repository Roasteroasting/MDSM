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
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class SeasonLeadChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
            ("Seeded broad-scale biomes, far coordinates and unchanged base density",Biomes),
            ("Calendar boundaries, creation layout, seasonal generation and save compatibility",Seasons),
            ("Cached enclosed glass and artificial light, winter crop timing and yields",Greenhouses),
            ("Winter livestock outdoors and in enclosed barns",Livestock),
            ("Snow-responsive mesh tiles and original flowers",SnowAndFlowers),
            ("Spring generation and immutable streamed season metadata",StreamedSeasons),
            ("Lead crafting, movement, attachment, breakage, death and persistence",Leashes)
    };

    internal static int Run()
    {
        int failed=0;
        foreach(var test in All)
        {
            try{test.Check();Console.WriteLine("PASS: "+test.Name);}
            catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+" - "+e);}
        }
        return failed==0?0:1;
    }

    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}

    private static void Biomes()
    {
        foreach(int seed in new[]{173,-907,16,int.MinValue})
        foreach(long offset in new[]{0L,12550824L,-1000000000016L})
        {
            var climate=new Climate(seed){VariedGeneration=true,NoiseBiomes=true,OriginX=offset,OriginZ=-offset};
            var twin=new Climate(seed){VariedGeneration=true,NoiseBiomes=true};
            int[] total=new int[3];int nearby=0;
            for(int z=-2400;z<2400;z+=59)for(int x=-2400;x<2400;x+=67)
            {
                Biome b=climate.At(x,z);total[(int)b]++;
                Require(b==twin.At(offset+x,-offset+z),"Relative and absolute climate coordinates disagreed.");
                if(climate.At(x+1,z)==b)nearby++;
            }
            int sum=total.Sum();
            Require(total.All(n=>n>sum/16) && total[(int)Biome.Temperate]>sum/3 && nearby>sum*.98,
                $"Biome mix or continuity failed for seed {seed} at {offset}: {string.Join('/',total)}.");
            Console.WriteLine($"Biome sample seed {seed} at {offset}: forest/desert/snowy = {string.Join('/',total)}.");
        }
        var previous=new BetaDensityGenerator(173,4);var current=new BetaDensityGenerator(173);
        foreach(var chunk in new[]{new ChunkAddress(0,0),new(-36,32),new(784426,-784427)})
            Require(previous.Generate(chunk).SequenceEqual(current.Generate(chunk)),"Biome update changed base density or the Far Lands.");
    }

    private static void Seasons()
    {
        (int Month,int Day,Season Expected)[] dates=
        {
            (3,19,Season.Winter),(3,20,Season.Spring),(6,19,Season.Spring),(6,20,Season.Summer),
            (9,19,Season.Summer),(9,20,Season.Autumn),(12,19,Season.Autumn),(12,20,Season.Winter),(1,1,Season.Winter)
        };
        foreach(var (month,day,expected) in dates)
        {
            DateTime date=new(2024,month,day);
            Require(SeasonManager.Resolve(SeasonsMode.On,date)==expected,"Calendar boundary changed.");
            Require(SeasonManager.Resolve(SeasonsMode.Off,date)==Season.Summer &&
                SeasonManager.Resolve(SeasonsMode.NuclearWinter,date)==Season.Winter,"Fixed season resolved incorrectly.");
        }
        var world=Flat(SeasonsMode.On);
        world.Climate.VariedGeneration=true;world.Climate.NoiseBiomes=true;
        world.Seasons.Refresh(new(2024,12,19));
        Require(world.Seasons.Current==Season.Autumn,"Calendar did not refresh the displayed season.");
        var root=new Point3(12,0,12);
        world.SetGeneratedBlock(12,0,12,BlockType.Grass);
        world.Seasons.Refresh(new(2024,12,20));
        Require(world.Seasons.Current==Season.Winter && world.GetBlock(12,1,12)==BlockType.Air,"Seasonal transition altered an existing chunk.");
        Require(WinterTree.TryPlace(root,40563421,world.GetBlock,world.SetGeneratedBlock),"Winter tree was rejected on open ground.");
        int logs=0,leaves=0;bool branch=false;
        for(int x=9;x<=15;x++)for(int z=9;z<=15;z++)for(int y=1;y<=9;y++)
        {
            var block=world.GetBlock(x,y,z);
            if(block==BlockType.WoodLog){logs++;branch|=x!=12||z!=12;}
            if(block==BlockType.Leaves)leaves++;
        }
        Require(logs>=12 && branch && leaves<=4,"Winter tree lacks branches or has a full canopy.");
        world.SetGeneratedBlock(21,0,21,BlockType.Grass);world.SetGeneratedBlock(21,1,21,BlockType.Sapling);
        Require(WorldDecorator.TryTree(world,21,0,21,5,2,17,growing:true)
            && world.GetBlock(21,5,21)==BlockType.WoodLog && world.GetBlock(22,4,21)==BlockType.Leaves,"Planted tree inherited the winter generation shape.");

        var save=new WorldSave(world,"Seasons",new(20.5f,1,20.5f),0,0);
        using var modern=new MemoryStream();WorldSerializer.Write(modern,save);modern.Position=0;
        var restored=WorldSerializer.Read(modern).World;
        Require(restored.Seasons.Mode==SeasonsMode.On && restored.Climate.NoiseBiomes,"Finite save lost season or biome selection.");
        world.AllowCheats=true;
        var session=new GameSession("season-rule",save);
        Require(!new WorldCommands().Execute("/seasons off",session,new GameSettings()).Success &&
            world.Seasons.Mode==SeasonsMode.On,"A cheat changed the locked season setting.");
        using var legacy=new MemoryStream();WorldSerializer.WriteLegacy(legacy,save);legacy.Position=0;
        var old=WorldSerializer.Read(legacy).World;
        Require(old.Seasons.Mode==SeasonsMode.Off && !old.Climate.NoiseBiomes,"Legacy finite save was upgraded to new seasons or biomes.");
        Require(StreamingJson.Decode<InfiniteManifest>(System.Text.Encoding.UTF8.GetBytes("{}")).Seasons==SeasonsMode.Off,"Older Infinite manifest defaults to seasons on.");
    }

    private static World Flat(SeasonsMode mode)
    {
        var world=new World(173,WorldSize.Small,false,mode);
        for(int z=0;z<56;z++)for(int x=0;x<56;x++)world.SetGeneratedBlock(x,0,z,BlockType.Stone);
        return world;
    }

    private static Point3 Room(World world,bool glass)
    {
        for(int z=14;z<=18;z++)for(int x=14;x<=18;x++)
        {
            world.SetBlock(x,1,z,BlockType.Stone);
            world.SetBlock(x,6,z,glass?BlockType.Glass:BlockType.Stone);
            if(x is not (14 or 18) && z is not (14 or 18))continue;
            for(int y=2;y<6;y++)if(x!=14 || z!=16 || y>3)world.SetBlock(x,y,z,BlockType.Stone);
        }
        var door=new Point3(14,2,16);
        Require(world.PlaceStructure(door,BlockType.Door,1,new(0,-1,0),new Player(new(22,1,22)).Bounds),"Shelter door fixture failed.");
        return door;
    }

    private static void Greenhouses()
    {
        var world=Flat(SeasonsMode.NuclearWinter);
        var door=Room(world,true);Point3 crop=new(16,2,16),outdoor=new(22,2,16);
        world.SetGeneratedBlock(22,6,16,BlockType.Glass);
        foreach(var p in new[]{crop,outdoor})
        {
            world.SetGeneratedBlock(p.X,1,p.Z,BlockType.FarmlandWet);
            world.SetGeneratedBlock(p.X,p.Y,p.Z,BlockType.Carrot1);
        }
        Require(world.Shelters.At(crop)==ShelterKind.Glasshouse &&
            !world.Shelters.ShelteredCrop(crop) && world.Shelters.At(outdoor)==ShelterKind.Outdoors,"Skylight or one suspended glass block counted as a protected crop.");
        Point3 torch=new(17,2,16);
        Require(world.PlaceStructure(torch,BlockType.Torch,0,new(0,-1,0),new Player(new(22,1,22)).Bounds)
            && world.Shelters.ShelteredCrop(crop),"Artificial block light did not enable greenhouse growth.");
        world.Toggle(door);
        Require(!world.Shelters.ShelteredCrop(crop),"An open door failed to invalidate a greenhouse across a chunk boundary.");
        world.Toggle(door);
        world.SetBlock(15,6,15,BlockType.Air);
        Require(!world.Shelters.ShelteredCrop(crop),"A hole in the glass roof did not invalidate the enclosure.");
        world.SetBlock(15,6,15,BlockType.Glass);
        Require(world.Shelters.ShelteredCrop(crop),"Repaired roof did not restore greenhouse protection.");
        world.SetBlock(17,2,16,BlockType.Air);
        Require(!world.Shelters.ShelteredCrop(crop),"Light removal failed to update greenhouse protection.");
        Require(world.PlaceStructure(torch,BlockType.Torch,0,new(0,-1,0),new Player(new(22,1,22)).Bounds),"Torch replacement failed.");

        world.Farming.Restore(new[]{new CropState(crop,100,100),new CropState(outdoor,100,100)});
        world.Farming.Update(101);
        Require(world.GetBlock(16,2,16)==BlockType.Carrot2 && world.GetBlock(22,2,16)==BlockType.Carrot1,"Winter growth rate or greenhouse growth was incorrect.");
        world.Farming.Update(300);
        Require(world.GetBlock(22,2,16)==BlockType.Carrot2,"Outdoor crop did not grow at quarter speed.");
        Span<ItemId> loot=stackalloc ItemId[5];
        int protectedYield=MiningRules.Drops(BlockType.Potato7,default,new(173),loot,world.WinterCrop(crop));
        int reducedYield=MiningRules.Drops(BlockType.Potato7,default,new(173),loot,world.WinterCrop(outdoor));
        Require(reducedYield<protectedYield && reducedYield>0,"Winter yield did not distinguish sheltered and outdoor crops.");
    }

    private static void Livestock()
    {
        var world=Flat(SeasonsMode.NuclearWinter);Room(world,false);
        var inventory=new Inventory();
        inventory.SetSlot(0,new(Items.Wheat,64));
        var population=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Cow,new(16.1f,2,16.4f),0,10),
            new MobState(MobSpecies.Cow,new(17.1f,2,16.4f),0,10)});
        Require(population.Feed(population.Entities[0],inventory)&&population.Feed(population.Entities[1],inventory),"Sheltered cows refused food.");
        population.Update(.51f,new(16,2,16));
        Require(population.Entities.Count==3,"Sheltered winter animals failed to reproduce.");
        int refused=0,unsuccessful=0;
        for(int seed=0;seed<32;seed++)
        {
            var outside=new World(seed,WorldSize.Small,false,SeasonsMode.NuclearWinter);
            var animals=new PassiveMobPopulation(outside,new[]{new MobState(MobSpecies.Cow,new(20,1,20),0,10),
                new MobState(MobSpecies.Cow,new(21,1,20),0,10)});
            bool first=animals.Feed(animals.Entities[0],inventory),second=animals.Feed(animals.Entities[1],inventory);
            if(!first || !second)refused++;
            else
            {
                animals.Update(.51f,new(20,1,20));
                if(animals.Entities.Count==2)unsuccessful++;
            }
        }
        Require(refused>0 && unsuccessful>0,"Winter did not hinder feeding and mating outdoors.");
    }

    private static void SnowAndFlowers()
    {
        var world=Flat(SeasonsMode.Off);
        var grass=new Point3(15,1,15);
        world.SetBlock(15,1,15,BlockType.Grass);
        var mesh=new ChunkMeshBuilder();
        bool Tile(int tile)
        {
            var bounds=TextureAtlas.Source(tile);
            return mesh.Vertices.Take(mesh.VertexCount).Any(v=>bounds.Contains((int)(v.TextureCoordinate.X*256),(int)(v.TextureCoordinate.Y*256)));
        }
        mesh.BuildBlock(world,grass);Require(Tile(1) && !Tile(Blocks.SnowyGrassTile),"Ordinary grass selected snowy sides.");
        int edgeRevision=world.GetChunk(1,0).Revision;
        world.SetBlock(15,2,15,BlockType.Snow);
        mesh.BuildBlock(world,grass);Require(Tile(Blocks.SnowyGrassTile) && Tile(0) && !Tile(1),"Snowy grass did not update only its sides.");
        Require(world.GetChunk(1,0).Revision>edgeRevision,"Snow at a chunk boundary did not dirty the neighboring mesh.");
        world.SetBlock(15,2,15,BlockType.Air);
        mesh.BuildBlock(world,grass);Require(!Tile(Blocks.SnowyGrassTile),"Snow removal left snowy grass sides behind.");
        foreach(var flower in new[]{BlockType.Peony,BlockType.Violet})
        {
            Require(Items.IsDefined(Items.FromBlock(flower)) && Blocks.SupportsPlant(BlockType.Grass,flower)
                && !Blocks.IsSolid(flower) && Blocks.RejectsUnderside(flower)
                && MiningRules.Drop(flower,default,new(1))==Items.FromBlock(flower),"Spring flower missed normal flower integration.");
        }
        ItemAtlasChecks.LayoutAndTiles();
    }

    private static void StreamedSeasons()
    {
        var gen=new BetaDensityGenerator(173);
        int peonies=0,violets=0;
        for(int z=-1;z<=1;z++)for(int x=0;x<=3;x++)
        {
            var address=new ChunkAddress(x,z);
            var spring=gen.GenerateComplete(address,season:Season.Spring);
            var summer=gen.GenerateComplete(address,season:Season.Summer);
            peonies+=spring.Blocks.Count(b=>b==(byte)BlockType.Peony);
            violets+=spring.Blocks.Count(b=>b==(byte)BlockType.Violet);
            Require(!summer.Blocks.Any(b=>b==(byte)BlockType.Peony || b==(byte)BlockType.Violet),"Spring flowers generated in summer.");
        }
        Require(peonies>0 && violets>0,"Spring generation did not place both new flowers.");

        string parent=Path.GetFullPath(AppContext.BaseDirectory);
        var directory=Path.Combine(parent,".season-"+Guid.NewGuid().ToString("N"));
        World world=null,loaded=null;
        try
        {
            world=World.OpenStreamed(173,Path.Combine(directory,"regions"),2,0,0,seasons:SeasonsMode.NuclearWinter);
            InfiniteWorldPersistence.Save(directory,new(world,"Winter",world.Spawn,0,0,ExactPosition:world.InfiniteSpawn));
            loaded=InfiniteWorldPersistence.Load(directory).World;
            Require(loaded.Seasons.Mode==SeasonsMode.NuclearWinter && loaded.Seasons.Winter
                && loaded.Climate.NoiseBiomes,"Infinite save lost its season or biome version.");
        }
        finally
        {
            world?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            loaded?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if(Directory.Exists(directory) && Path.GetDirectoryName(directory)==Path.TrimEndingDirectorySeparator(parent))
                Directory.Delete(directory,true);
        }
    }

    private static void Leashes()
    {
        var grid=new CraftingGrid(3);
        foreach(int index in new[]{0,1,3})grid.SetSlot(index,new(Items.Leather,1));
        grid.SetSlot(4,new(Items.String,1));grid.SetSlot(8,new(Items.String,1));
        Require(grid.Preview.Item==Items.Lead && grid.Preview.Count==1,"Requested lead recipe did not match.");
        grid.SetSlot(8,default);grid.SetSlot(7,new(Items.String,1));
        Require(grid.Preview.IsEmpty,"Lead accepted an incorrect shape.");

        var world=Flat(SeasonsMode.NuclearWinter);var inventory=new Inventory();
        inventory.SetSlot(0,new(Items.Lead,2));
        var population=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Pig,new(30,1,32),0,10),
            new MobState(MobSpecies.Chicken,new(33,1,32),0,6)});
        var pig=population.Entities[0];var chicken=population.Entities[1];var drops=new DroppedItems(world);
        world.LiveMobs=population;
        Require(population.TryLeash(pig,inventory) && !population.TryLeash(pig,inventory) &&
            population.TryLeash(chicken,inventory) && inventory.Selected.IsEmpty && pig.Id!=chicken.Id,"Leashes did not consume one item per animal.");
        inventory.SelectedSlot=2;inventory.SetSlot(2,new(Items.Wheat,1));
        Vector3 player=new(38,1,32);
        for(int z=29;z<=35;z++)for(int y=1;y<=3;y++)world.SetBlock(32,y,z,BlockType.Stone);
        for(int i=0;i<200;i++)population.Update(1f/120,player,items:drops);
        Require(pig.Bounds.Max.X<=32.001f && pig.Leash.Active,"Taut leash pulled a mob through a wall.");
        for(int z=29;z<=35;z++)for(int y=1;y<=3;y++)world.SetBlock(32,y,z,BlockType.Air);
        for(int i=0;i<200;i++)population.Update(1f/120,player,items:drops,heldItem:inventory.Selected.Item);
        Require(pig.Position.X>31.5f && pig.Leash.Anchor==LeashAnchor.Player && inventory.Selected.Count==1,"A taut leash did not steer or survive a slot change.");
        Point3 fence=new(36,1,32);
        world.SetBlock(36,1,32,BlockType.Fence);
        Require(population.InteractFence(fence,inventory,drops) && pig.Leash.Fence==fence && chicken.Leash.Fence==fence,"Leashes did not transfer together.");
        Require(LeashRenderer.RopePoint(Vector3.Zero,new(3,0,0),.5f).Y<0,"The rope has no sag.");
        var save=new WorldSave(world,"Leashes",player,0,0,inventory,Pigs:population.CapturePigs(),Mobs:population.CaptureOthers(),Population:population.CapturePopulation());
        using var file=new MemoryStream();WorldSerializer.Write(file,save);file.Position=0;
        var restored=WorldSerializer.Read(file);var other=new PassiveMobPopulation(restored.World,restored.Pigs,restored.Mobs,restored.Population);
        Require(other.Entities[0].Id==pig.Id && other.Entities[0].Leash==pig.Leash &&
            other.Entities[1].Id==chicken.Id && other.Entities[1].Leash==chicken.Leash && restored.World.Seasons.Winter,"Leashed mob identities or coordinates changed on reload.");
        using var playerFile=new MemoryStream();
        WorldSerializer.Write(playerFile,save with{Pigs=new[]{save.Pigs[0] with{Leash=new(LeashAnchor.Player)}}});playerFile.Position=0;
        var playerBound=WorldSerializer.Read(playerFile).Pigs[0];
        Require(playerBound.Id==pig.Id && playerBound.Leash==new LeashState(LeashAnchor.Player),"Player anchor failed to survive reload.");
        Require(population.InteractFence(fence,inventory,drops) &&
            population.Entities.All(m=>!m.Leash.Active),"Fence interaction did not release its animals.");
        int leadSlot=Enumerable.Range(0,Inventory.SlotCount).Single(i=>inventory[i].Item==Items.Lead);
        Require(inventory[leadSlot].Count==2 && drops.Entities.Count==0,"Unleashing duplicated or lost a lead.");
        inventory.SelectedSlot=leadSlot;
        Require(population.TryLeash(pig,inventory),"Lead could not be used again.");
        population.Update(1f/120,new(53,1,32),items:drops);
        Require(!pig.Leash.Active && drops.Entities.Count(i=>i.Item==Items.Lead)==1,"Overstretch did not break and return exactly one lead.");
        Require(population.TryLeash(chicken,inventory) && population.InteractFence(fence,inventory,drops),"Fence reattachment failed.");
        world.SetBlock(36,1,32,BlockType.Air);
        world.SetBlock(36,1,32,BlockType.Fence);
        population.Update(1f/120,player,items:drops);
        Require(!chicken.Leash.Active && drops.Entities.Count(i=>i.Item==Items.Lead)==2,"Breaking and replacing an anchor did not release its leash.");
        inventory.SetSlot(0,new(Items.Lead,1));inventory.SelectedSlot=0;
        Require(population.TryLeash(chicken,inventory) && population.Hit(chicken,chicken.Health,player,drops)
            && drops.Entities.Count(i=>i.Item==Items.Lead)==3,"Leashed animal death lost or duplicated the lead.");
    }

    internal static int RunGraphics()
    {
        try{using var game=new Preview();game.Run();return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }

    private sealed class Preview : Game1
    {
        protected override void Update(GameTime time) {}
        protected override void Draw(GameTime time)
        {
            using var atlas=new TextureAtlas(Content);
            foreach(var season in new[]{Season.Spring,Season.Autumn,Season.Winter})
            {
                var variant=atlas.Terrain(season);
                Require(!Pixels(variant,0).SequenceEqual(Pixels(atlas.Texture,0)) &&
                    !Pixels(variant,6).SequenceEqual(Pixels(atlas.Texture,6)),"Seasonal grass or foliage color is missing.");
                Require(Pixels(variant,Blocks.SnowyGrassTile).SequenceEqual(Pixels(atlas.Texture,Blocks.SnowyGrassTile)),
                    "Seasonal tint colored white snow on the grass side.");
                Require(Pixels(variant,2).SequenceEqual(Pixels(atlas.Texture,2)),"Seasonal tint affected dirt.");
            }
            foreach(var block in new[]{BlockType.Peony,BlockType.Violet})
                Require(Pixels(atlas.Texture,Blocks.Get(block).SideTile).Any(p=>p.A==255),"A spring flower texture is missing.");
            var lead=Pixels(atlas.ItemAtlas,Items.Get(Items.Lead).Sprite);
            Require(lead.Any(p=>p.A==255) && lead.Any(p=>p.A==0),"Lead sprite is missing or lacks transparency.");

            var world=Flat(SeasonsMode.Off);
            var post=new Point3(23,1,20);world.SetBlock(23,1,20,BlockType.Fence);
            var mob=new MobState(MobSpecies.Cow,new(19,1,20.5f),0,10);
            var population=new PassiveMobPopulation(world,new[]{mob});
            var linked=new PassiveMobPopulation(world,new[]{mob with{Id=Guid.NewGuid(),Leash=new(LeashAnchor.Fence,post)}});
            using var renderer=new PassiveMobRenderer(GraphicsDevice,Content);
            using var target=new RenderTarget2D(GraphicsDevice,480,270,false,SurfaceFormat.Color,DepthFormat.Depth24);
            var camera=new Camera{Position=new(21,2.55f,27)};
            camera.RestoreOrientation(0,-.13f);
            var plain=DrawMobs(population);
            var rope=DrawMobs(linked);
            Require(plain.Zip(rope).Count(p=>p.First!=p.Second)>40,"Fence rope or knot was not visible in the composed frame.");
            world.SetBlock(23,1,20,BlockType.Air);
            Require(plain.SequenceEqual(DrawMobs(linked)),"Broken fence left a visible rope or orphaned knot.");
            Console.WriteLine("PASS: Seasonal atlas uploads, snow-white preservation, rendered leash and knot, and graphics state.");
            Exit();

            Color[] DrawMobs(PassiveMobPopulation animals)
            {
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);
                Texture before=GraphicsDevice.Textures[0];
                renderer.Draw(camera,animals,world:world);
                Require(GraphicsDevice.Textures[0]==before,"Rope draw did not restore the shared texture slot.");
                GraphicsDevice.SetRenderTarget(null);
                var pixels=new Color[target.Width*target.Height];target.GetData(pixels);return pixels;
            }
            static Color[] Pixels(Texture2D texture,int tile)
            {var pixels=new Color[256];texture.GetData(0,TextureAtlas.Source(tile),pixels,0,256);return pixels;}
        }
    }
}
