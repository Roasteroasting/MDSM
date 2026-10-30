using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WorldExpansionChecks
{
    // Retain the two supported legacy horizontal extents for existing per-mechanic fixtures.
    // Larger sizes get streamed integration checks, avoiding multiple huge byte-array copies.
    public static readonly WorldSize[] RoutineSizes = { WorldSize.Small, WorldSize.Normal };
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static byte[] Hash(World world)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Chunk chunk in world.Chunks) hash.AppendData(chunk.Blocks);
        return hash.GetHashAndReset();
    }
    public static void DimensionsAndTerrain()
    {
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            int width = 256 << (int)size;
            Require(size.Width() == width && size.BlockCount() == (long)width*128*width, "Dimension/count overflow.");
            CheckBounds(size);
        }
        foreach (int seed in new[] { 173, -907, int.MinValue, int.MaxValue })
        {
            var terrain = new TerrainGenerator(seed); var same = new TerrainGenerator(seed);
            int min = 128, max = 0; long sum = 0; int count = 0;
            // Full-resolution strips include every chunk seam and broad fields across Massive extents.
            for (int z = 0; z < 2048; z += 7) for (int x = 0; x < 2048; x++)
            {
                int y = terrain.SurfaceY(x,z); min = Math.Min(min,y); max = Math.Max(max,y); sum += y; count++;
                Require(y is >= 63 and <= 114 && y == same.SurfaceY(x,z), "Height bounds/determinism failed.");
                Require(Math.Abs(y-terrain.SurfaceY(x+1,z)) <= 2 && Math.Abs(y-terrain.SurfaceY(x,z+1)) <= 2,
                    "Unwalkable local gradient or chunk seam.");
            }
            Require(max-min >= 20 && (double)sum/count is > 78 and < 98, "Terrain lost broad variation or useful average elevation.");
            Console.WriteLine($"  Terrain seed {seed}: Y {min}-{max}, mean {(double)sum/count:F1}, neighboring steps <=2.");
        }
        foreach ((int Width,int Height) obsolete in new[] { (128,64), (128,128), (1024,64), (2048,64) })
        {
            try { WorldSizes.FromDimensions(obsolete.Width,obsolete.Height,obsolete.Width); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Obsolete/invalid dimensions accepted.");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckBounds(WorldSize size)
    {
        var world = new World(173,size,false); int width = world.Width;
        Require(world.ChunkCount == width/16*(width/16) && world.Chunks.Length == world.ChunkCount, "Incorrect chunk count.");
        foreach (Chunk chunk in world.Chunks)
        {
            int x=chunk.OriginX+15,z=chunk.OriginZ+15;
            world.SetBlock(x,127,z,BlockType.Stone);
            Require(world.GetBlock(x,127,z)==BlockType.Stone && world.SurfaceY(x,z)==127 && chunk.Blocks.Length==32768, "High-Y index/cache failure.");
            world.SetBlock(x,127,z,BlockType.Air); Require(world.SurfaceY(x,z)==-1,"Surface cache did not invalidate.");
        }
        Require(world.GetChunk(world.ChunksX-1,world.ChunksZ-1).Bounds.Max==new Vector3(width,128,width),"Last chunk bounds incorrect.");
        Require(!world.SetBlock(width,127,0,BlockType.Stone) && !world.SetBlock(0,128,0,BlockType.Stone),"Finite boundary escaped.");
        Require(world.TryPlace(new Point3(width-1,127,width-1),BlockType.Stone,new Player(new Vector3(2,2,2)).Bounds),"High-Y placement rejected.");
        Require(BlockRaycaster.Cast(world,new Vector3(width-0.5f,130,width-0.5f),Vector3.Down)?.Cell.Y==127,"High-Y raycast failed.");
        Console.WriteLine($"  {size}: {width}x128x{width}, {world.ChunkCount} chunks, {world.BlockCount/1048576} MiB raw blocks.");
    }

    private sealed record Snapshot(byte[] Hash, byte[] NaturalHash, WorldSize Size, Vector3 Position, PigState[] Pigs, MobState[] Mobs,
        MobPopulationState Population, DroppedItemState[] Drops, FallingBlockState[] Falling, int[] Queued, ItemStack[] Slots, WaterFeatureCounts Features, int Trees, int Plants);
    public static void GenerationAndStreaming()
    {
        string path=Path.Combine(AppContext.BaseDirectory,".mdsm-generation-"+Guid.NewGuid().ToString("N")+".dat");
        int previousTrees=0,previousPlants=0;
        try
        {
            foreach (WorldSize size in Enum.GetValues<WorldSize>())
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                Snapshot snapshot=GenerateAndSave(size,path);
                Require(snapshot.Trees>previousTrees && snapshot.Plants>previousPlants,"Decoration counts did not scale with area.");
                previousTrees=snapshot.Trees;previousPlants=snapshot.Plants;
                GC.Collect();GC.WaitForPendingFinalizers();
                Regenerate(snapshot);
                GC.Collect();GC.WaitForPendingFinalizers();
                ReadAndCheck(snapshot,path);
            }
        }
        finally { File.Delete(path); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Snapshot GenerateAndSave(WorldSize size,string path)
    {
        var watch=Stopwatch.StartNew();
        World world=World.CreateNew(173,size,phase=>Console.WriteLine($"  {size}: {phase}"));
        WaterFeatureCounts features=world.GeneratedWaterFeatures;
        Require(features.Ponds==LakeGenerator.MaximumLakes(size),"Missing pond population.");
        Require(size<WorldSize.Large ? features.Rivers==0 && features.Lakes==0 : features.Rivers>features.Lakes && features.Lakes>0,
            "River/lake size policy failed.");
        int trees=0,plants=0,sand=0,gravel=0,water=0;
        var terrain=new TerrainGenerator(173);
        for(int z=0;z<world.Depth;z++)for(int x=0;x<world.Width;x++)
        {
            int ground=terrain.SurfaceY(x,z);
            Require(world.GetBlock(x,0,z)==BlockType.Bedrock,"Incomplete bedrock layer.");
            if(world.GetBlock(x,ground+1,z)==BlockType.WoodLog)trees++;
            if(x%31==0 && z%31==0 && world.GetBlock(x,ground,z)==BlockType.Grass)
            {
                for(int y=ground-terrain.DirtDepth(x,z);y<ground;y++)Require(world.GetBlock(x,y,z)==BlockType.Dirt,"Dirt layer changed.");
                Require(world.GetBlock(x,ground-6,z)==BlockType.Stone,"Deep stone missing.");
            }
        }
        foreach(Chunk chunk in world.Chunks)for(int y=0;y<World.Height;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            BlockType block=chunk.GetBlock(x,y,z);
            if(Blocks.Get(block).NeedsGrass)plants++;
            if(Blocks.Falls(block))
            {
                Require(y>0 && world.IsSolid(chunk.OriginX+x,y-1,chunk.OriginZ+z),"Unsupported natural Sand/Gravel.");
                if(block==BlockType.Sand)sand++;else gravel++;
            }
            if(Water.IsWater(block))
            {
                water++; Require(Water.IsSource(block),"New water is not a stable source.");
                Require(chunk.OriginX+x>1 && chunk.OriginX+x<world.Width-2 && chunk.OriginZ+z>1 && chunk.OriginZ+z<world.Depth-2,"Truncated water feature.");
            }
        }
        Require(trees>world.Width*world.Depth/1500 && plants>world.Width*world.Depth/100 && sand>0 && gravel>0 && water>0,"Generation densities/materials invalid.");
        Require(world.Fluids.PendingCount==0 && world.Gravity.PendingCount==0,"New water/gravity is not initially stable.");
        CheckWaterShapes(world);
        byte[] naturalHash=Hash(world);
        int revision=world.Revision;world.Fluids.Update(10);Require(world.Revision==revision,"Unedited natural water changed.");
        // Generate representative chunk geometry using the unchanged meshing path, including high terrain.
        var builder=new ChunkMeshBuilder();
        foreach(int coordinate in new[]{0,world.ChunksX/2,world.ChunksX-1})
        { builder.Build(world,world.GetChunk(coordinate,coordinate));Require(builder.VertexCount>0,"Representative terrain mesh is empty."); }
        MobState[] generated=PassiveMobPopulation.Generate(world);
        Require(generated.Length==world.Width*world.Depth/2048 && generated.AsSpan().SequenceEqual(PassiveMobPopulation.Generate(world)),"Population scaling/determinism failed.");
        var population=new PassiveMobPopulation(world,generated);var drops=new DroppedItems(world);
        PassiveMob victim=population.Entities[0];while(!victim.IsDead)Require(population.Punch(victim,world.Spawn,drops),"Could not set pending respawn fixture.");
        drops.Spawn(Items.FromBlock(BlockType.Dirt),7,world.Spawn+Vector3.Up,0.5f);
        var inventory=Inventory.CreateNewWorld();inventory.SetSlot(15,new ItemStack(Items.RawPorkchop,23));inventory.SelectedSlot=8;
        world.SetBlock(world.Width-1,127,world.Depth-1,BlockType.Cobblestone);
        world.SetBlock(8,125,8,BlockType.WaterSource);
        world.SetBlock(15,126,16,BlockType.Gravel);world.Gravity.Update(0.1);
        var save=new WorldSave(world,"Expanded "+size,world.Spawn,0.7f,-0.2f,inventory,drops.Capture(),13,
            population.CapturePigs(),population.CaptureOthers(),population.CapturePopulation());
        var snapshot=new Snapshot(Hash(world),naturalHash,size,save.PlayerPosition,save.Pigs,save.Mobs,save.Population,save.DroppedItems,
            world.Gravity.CaptureActive(),world.Gravity.CaptureColumns(),Enumerable.Range(0,36).Select(i=>inventory[i]).ToArray(),features,trees,plants);
        using(var stream=File.Create(path))WorldSerializer.Write(stream,save);
        Console.WriteLine($"  {size}: {features.Ponds} ponds, {features.Rivers} rivers, {features.Lakes} lakes; {trees} trees, {plants} plants, {generated.Length} mobs; generated/checked/saved in {watch.Elapsed.TotalSeconds:F1}s.");
        return snapshot;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Regenerate(Snapshot expected)
    {
        World world=World.CreateNew(173,expected.Size);
        Require(Hash(world).AsSpan().SequenceEqual(expected.NaturalHash) && world.GeneratedWaterFeatures==expected.Features,
            "Complete generation including rivers/lakes is not deterministic.");
    }
    private static void CheckWaterShapes(World world)
    {
        var columns=new Dictionary<int,(int Top,int Depth)>();
        foreach(Chunk chunk in world.Chunks)
        {
            if(chunk.WaterCount==0)continue;
            for(int z=0;z<16;z++)for(int x=0;x<16;x++)
            {
                int top=-1,depth=0;
                for(int y=0;y<128;y++)if(Water.IsWater(chunk.GetBlock(x,y,z))){top=y;depth++;}
                if(top>=0)
                {
                    int wx=chunk.OriginX+x,wz=chunk.OriginZ+z;
                    columns.Add(wz*world.Width+wx,(top,depth));
                    Require(!Blocks.Get(world.GetBlock(wx,top+1,wz)).NeedsGrass,"Underwater vegetation.");
                }
            }
        }
        int Ground(int x,int z)
        {
            int y=world.SurfaceY(x,z);
            while(y>0 && world.GetBlock(x,y,z) is not (BlockType.Grass or BlockType.Dirt or BlockType.Stone
                or BlockType.Sand or BlockType.Gravel or BlockType.Bedrock))y--;
            return y;
        }
        foreach(int cell in columns.Keys.Where(k=>k%17==0))
        {
            int cx=cell%world.Width,cz=cell/world.Width;
            for(int dz=-10;dz<=10;dz++)for(int dx=-10;dx<=10;dx++)
            {
                int x=cx+dx,z=cz+dz;
                if(x<1 || z<1 || x>=world.Width-1 || z>=world.Depth-1 || columns.ContainsKey(z*world.Width+x))continue;
                int top=Ground(x,z);
                if(!columns.ContainsKey(z*world.Width+x+1))Require(Math.Abs(top-Ground(x+1,z))<=2,"Abrupt dry shoreline bank along X.");
                if(!columns.ContainsKey((z+1)*world.Width+x))Require(Math.Abs(top-Ground(x,z+1))<=2,"Abrupt dry shoreline bank along Z.");
            }
        }
        var queue=new Queue<int>();int ponds=0,rivers=0,lakes=0;var terrain=new TerrainGenerator(173);
        while(columns.Count>0)
        {
            int start=columns.Keys.First(),level=columns[start].Top;
            queue.Enqueue(start);int minX=int.MaxValue,maxX=0,minZ=int.MaxValue,maxZ=0,count=0,deepest=0,low=128,high=0;
            var queued=new HashSet<int>{start};
            while(queue.TryDequeue(out int cell))
            {
                var column=columns[cell];columns.Remove(cell);int x=cell%world.Width,z=cell/world.Width;
                Require(column.Top==level,"A generated body has inconsistent water levels.");
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
                count++;deepest=Math.Max(deepest,column.Depth);int original=terrain.SurfaceY(x,z);low=Math.Min(low,original);high=Math.Max(high,original);
                foreach(int next in new[]{cell-1,cell+1,cell-world.Width,cell+world.Width})
                    if(columns.ContainsKey(next) && queued.Add(next))queue.Enqueue(next);
            }
            int extent=Math.Max(maxX-minX+1,maxZ-minZ+1);
            if(deepest>3){lakes++;Require(deepest>=4 && deepest<=(world.Size==WorldSize.Massive?18:13) && deepest<=extent*.18f+1
                && extent>=(world.Size==WorldSize.Massive?80:40) && extent<=(world.Size==WorldSize.Massive?180:120)
                && high-low<=24,"A major lake is shallow, tiny, oversized, or crosses excessive terrain relief.");}
            else if(extent>LakeGenerator.MaximumRadius(world.Size)*2+1)
            {rivers++;Require(extent*extent/(float)count>2.5f && high-low<=4,"River is a round pond or crosses a steep hillside.");}
            else {ponds++;Require(high-low<=3,"Pond site is too steep.");}
        }
        Require(new WaterFeatureCounts(ponds,rivers,lakes)==world.GeneratedWaterFeatures,"Actual connected water shapes disagree with generation counts.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadAndCheck(Snapshot expected,string path)
    {
        using(var stream=File.OpenRead(path))
        {
            WorldMetadata meta=WorldSerializer.ReadMetadata(stream);
            Require(meta.Size==expected.Size && meta.StoredHeight==128 && stream.Position<256,"Listing read block payload or lost size.");
        }
        using var input=File.OpenRead(path);WorldSave save=WorldSerializer.Read(input);
        Require(Hash(save.World).AsSpan().SequenceEqual(expected.Hash) && save.PlayerPosition==expected.Position && save.Health==13,"Streamed restore changed world/pose/health.");
        Require(save.Pigs.AsSpan().SequenceEqual(expected.Pigs) && save.Mobs.AsSpan().SequenceEqual(expected.Mobs)
            && save.DroppedItems.AsSpan().SequenceEqual(expected.Drops),"Entity state changed in a large-world save.");
        Require(save.Population.Targets.AsSpan().SequenceEqual(expected.Population.Targets) && save.Population.Pending.AsSpan().SequenceEqual(expected.Population.Pending)
            && save.World.Gravity.CaptureActive().AsSpan().SequenceEqual(expected.Falling) && save.World.Gravity.CaptureColumns().AsSpan().SequenceEqual(expected.Queued),"Pending population/falling state changed.");
        Require(save.Inventory.SelectedSlot==8 && save.Inventory[15]==new ItemStack(Items.RawPorkchop,23)
            && save.World.GetBlock(save.World.Width-1,127,save.World.Depth-1)==BlockType.Cobblestone,"Inventory/high-Y data changed.");
        for(int i=0;i<36;i++)Require(save.Inventory[i]==expected.Slots[i],"A saved inventory slot changed in a large world.");
        Console.WriteLine($"  {expected.Size}: complete streamed restore and SHA-256 equality passed.");
    }

    public static void Migration()
    {
        foreach(WorldSize size in RoutineSizes)for(int version=1;version<=7;version++)LegacyCase(size,version);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LegacyCase(WorldSize size,int version)
    {
        World world=PigChecks.Flat(size);world.SetBlock(world.Width-1,63,world.Depth-1,BlockType.WhiteWool);
        world.SetBlock(15,31,16,BlockType.WaterSource);world.SetBlock(17,40,16,BlockType.Gravel);
        if(version==7)world.Gravity.Update(0.1);
        var population=new PassiveMobPopulation(world,new[]{MobChecks.State(MobSpecies.Pig),MobChecks.State(MobSpecies.Cow,new Vector3(30,1,30),1)});
        var drops=new DroppedItems(world);population.Punch(population.Entities[1],world.Spawn,drops);
        var inventory=Inventory.CreateNewWorld();inventory.SetSlot(15,new ItemStack(Items.RawPorkchop,23));inventory.SelectedSlot=5;
        var save=new WorldSave(world,"Legacy "+size,new Vector3(60,45,60),1.2f,-0.3f,inventory,drops.Capture(),13,
            population.CapturePigs(),population.CaptureOthers(),population.CapturePopulation());
        byte[] bytes=LegacyBytes(save,version);
        using(var metadata=new MemoryStream(bytes))
        {
            WorldMetadata info=WorldSerializer.ReadMetadata(metadata);
            Require(info.Size==size && info.StoredHeight==64 && info.ExpandsHeight,"Legacy dimensions misidentified.");
        }
        WorldSave loaded=WorldSerializer.Read(new MemoryStream(bytes));
        Require(Hash(loaded.World).AsSpan().SequenceEqual(Hash(world)) && loaded.PlayerPosition==save.PlayerPosition
            && loaded.Yaw==save.Yaw && loaded.Pitch==save.Pitch,"Migration shifted/regenerated blocks or pose.");
        foreach(Chunk chunk in loaded.World.Chunks)Require(chunk.Blocks[(64*16*16)..].IndexOfAnyExcept((byte)0)<0,"Expanded upper layers are not air.");
        Require(loaded.Health==(version>=3?13:20) && (version<2 || loaded.Inventory[15]==inventory[15]),"Legacy health/inventory changed.");
        if(version>=6)Require(loaded.Population.Targets.AsSpan().SequenceEqual(save.Population.Targets)
            && loaded.Population.Pending.AsSpan().SequenceEqual(save.Population.Pending)
            && loaded.World.Gravity.CaptureColumns().AsSpan().SequenceEqual(world.Gravity.CaptureColumns()),"Migration lost targets/timers/queued gravity.");
        if(version>=7)Require(loaded.World.Gravity.CaptureActive().AsSpan().SequenceEqual(world.Gravity.CaptureActive()),"Migration lost active falls.");
        using var upgraded=new MemoryStream();WorldSerializer.Write(upgraded,loaded);upgraded.Position=0;
        WorldMetadata now=WorldSerializer.ReadMetadata(upgraded);Require(now.StoredHeight==128 && !now.ExpandsHeight,"Upgrade retained old height.");
        upgraded.Position=0;Require(Hash(WorldSerializer.Read(upgraded).World).AsSpan().SequenceEqual(Hash(world)),"Migrated world changed on subsequent load.");
    }
    private static byte[] LegacyBytes(WorldSave save,int version)
    {
        byte[] current=MobChecks.Encode(save);int header=52+Encoding.UTF8.GetByteCount(save.Name),blockEnd=SaveLayout.BlockEnd(current);
        using var payload=new MemoryStream(save.World.Width*64*save.World.Depth+8192);
        byte[] prefix=current.AsSpan(0,header).ToArray();BitConverter.GetBytes(version).CopyTo(prefix,4);
        BitConverter.GetBytes(64).CopyTo(prefix,12);BitConverter.GetBytes(save.World.Width*64*save.World.Depth).CopyTo(prefix,header-4);payload.Write(prefix);
        foreach(Chunk chunk in save.World.Chunks)payload.Write(chunk.Blocks[..(64*16*16)]);
        int end=version switch {1=>blockEnd,2=>SaveLayout.Health(current),3=>SaveLayout.Health(current)+4,
            4=>SaveLayout.Health(current)+4,5=>SaveLayout.Population(current),6=>current.Length-32-4-save.World.Gravity.Active.Count*25,_=>current.Length-32};
        payload.Write(current.AsSpan(blockEnd,end-blockEnd));
        if(version==4)
        {
            using var writer=new BinaryWriter(payload,Encoding.UTF8,true);writer.Write(save.Pigs.Length);
            foreach(PigState pig in save.Pigs){writer.Write(pig.Position.X);writer.Write(pig.Position.Y);writer.Write(pig.Position.Z);writer.Write(pig.Yaw);writer.Write(pig.Health);}
        }
        byte[] data=payload.ToArray();using var result=new MemoryStream(data.Length+32);result.Write(data);result.Write(SHA256.HashData(data));return result.ToArray();
    }
}
