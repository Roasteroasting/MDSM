using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MDSM.Persistence;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class GenerationBalanceChecks
{
    internal static int Run()
    {
        try
        {
            int patches=0,roomsBefore=0,roomsAfter=0;
            foreach(int seed in new[]{173,-907})
            {
                var old=new BetaDensityGenerator(seed,3);
                var updated=new BetaDensityGenerator(seed);
                var climate=new Climate(seed){VariedGeneration=true,NoiseBiomes=true};
                int diamonds=0,oldChests=0,newChests=0,crops=0,areas=0;
                var watch=Stopwatch.StartNew();
                for(int z=-8;z<8;z++)for(int x=-8;x<8;x++)
                {
                    var address=new ChunkAddress(x,z);
                    var prior=old.GenerateComplete(address);
                    var snapshot=updated.GenerateComplete(address);
                    var previous=StreamingJson.Decode<ChunkGameplay>(prior.Metadata);
                    var state=StreamingJson.Decode<ChunkGameplay>(snapshot.Metadata);
                    diamonds+=snapshot.Blocks.Count(b=>b==(byte)BlockType.DiamondOre);
                    oldChests+=previous.Containers.Length;newChests+=state.Containers.Length;
                    foreach(var chest in state.Containers)
                        Check(chest.Slots.Count(s=>!s.IsEmpty) is >=4 and <=8,"Chest loot was lost when scattered.");
                    if(state.Crops.Length==0)continue;
                    areas++;crops+=state.Crops.Length;
                    VerifyPatch(seed,updated,snapshot,state.Crops,climate);
                }
                Console.WriteLine($"Seed {seed}, 256 chunks: {diamonds/256.0:F2} diamond ore/chunk, {oldChests} -> {newChests} dungeon chests, {areas} crop patches ({crops} plants), {watch.Elapsed.TotalSeconds:F1}s.");
                Check(diamonds is >=768 and <=1024,"Diamond ore density outside 3–4 per chunk.");
                patches+=areas;roomsBefore+=oldChests;roomsAfter+=newChests;
            }
            Check(roomsBefore>8 && roomsAfter<=roomsBefore*.5 && roomsAfter>0,"Dungeon density did not drop.");
            Check(patches<14,"Pond crop patches are too common in the sample.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }

    internal static int RunShore()
    {
        try
        {
            int found=0;
            foreach(int seed in new[]{173,-907,137})
            {
                var generator=new BetaDensityGenerator(seed);
                var climate=new Climate(seed){VariedGeneration=true,NoiseBiomes=true};
                var tested=new HashSet<ChunkAddress>();
                bool detected=false;
                // Test real pond neighborhoods. A window near spawn can
                // contain an ocean or a desert instead of suitable ponds.
                for(int z=-5;z<=5 && !detected;z++)for(int x=-5;x<=5 && !detected;x++)
                {
                    ulong h=InfiniteFeatures.Hash(seed^0x504f4e44,x,0,z);
                    if(h%100>=65)continue;
                    long px=x*128+32+(long)((h>>8)%64),pz=z*128+32+(long)((h>>20)%64);
                    if(climate.At(px,pz)!=Biome.Temperate)continue;
                    var profile=new InfiniteFeatures(generator,seed,ChunkAddress.At(px,pz),default).WaterProfile(px,pz);
                    if(!profile.Wet||profile.Kind!=NaturalWater.Pond)continue;
                    for(long cz=Coordinates.FloorDiv(pz-17,16);cz<=Coordinates.FloorDiv(pz+17,16)&&!detected;cz++)
                    for(long cx=Coordinates.FloorDiv(px-17,16);cx<=Coordinates.FloorDiv(px+17,16)&&!detected;cx++)
                    {
                        var address=new ChunkAddress(cx,cz);
                        // Exercise shores admitted by the increase, outside the original one-in-eight group.
                        if(InfiniteFeatures.Hash(seed^0x43524f50,cx,0,cz)%24!=4 || !tested.Add(address))continue;
                        var snapshot=generator.GenerateComplete(address);
                        var crops=StreamingJson.Decode<ChunkGameplay>(snapshot.Metadata).Crops;
                        if(crops.Length==0)continue;
                        VerifyPatch(seed,generator,snapshot,crops,climate);
                        var again=generator.GenerateComplete(address);
                        Check(snapshot.Blocks.SequenceEqual(again.Blocks)&&snapshot.Metadata.SequenceEqual(again.Metadata),"Crop patch is not deterministic.");
                        Console.WriteLine($"Seed {seed}: {crops.Length} natural crops in chunk {cx}, {cz}.");
                        found++;detected=true;
                    }
                }
            }
            Check(found>=2,"Could not find natural patches on multiple seeds.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }

    private static void VerifyPatch(int seed,BetaDensityGenerator generator,ChunkSnapshot snapshot,CropState[] crops,Climate climate)
    {
        Check(crops.Length is >=2 and <=5,"Natural crop patch size changed.");
        var address=snapshot.Address;
        var shape=new InfiniteFeatures(generator,seed,address,default);
        var stored=ChunkSnapshot.Decode(address,snapshot.Encode());
        Check(StreamingJson.Decode<ChunkGameplay>(stored.Metadata).Crops.SequenceEqual(crops),"Generated crop timers changed after persistence.");
        bool potato=At(crops[0].Cell)>=BlockType.Potato1;
        foreach(CropState crop in crops)
        {
            Point3 p=crop.Cell;
            Check(Blocks.Crop(At(p)) && (At(p)>=BlockType.Potato1)==potato && At(p+new Point3(0,-1,0))==BlockType.FarmlandWet
                && climate.At(p.X,p.Z)==Biome.Temperate && crop.Remaining is >=100 and <=400,"Inconsistent, dry or non-forest generated patch.");
            bool nearPond=false;
            for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
            {
                var column=shape.WaterProfile(p.X+dx,p.Z+dz);
                nearPond|=column.Wet && column.Kind==NaturalWater.Pond && Math.Abs(column.Level-(p.Y-1))<=1;
            }
            Check(nearPond,"Natural crop not on a pond coast.");
        }
        Check(snapshot.Blocks.Count(b=>b==(byte)BlockType.FarmlandWet)==crops.Length,"Generated unplanted soil.");

        var world=new World(seed,WorldSize.Small,false);
        Point3 offset=new(address.BlockX,0,address.BlockZ);
        var local=Array.ConvertAll(crops,c=>c with{Cell=new(c.Cell.X-offset.X,c.Cell.Y,c.Cell.Z-offset.Z)});
        foreach(var crop in local)
        {
            world.SetGeneratedBlock(crop.Cell.X,crop.Cell.Y-1,crop.Cell.Z,BlockType.FarmlandWet);
            world.SetGeneratedBlock(crop.Cell.X,crop.Cell.Y,crop.Cell.Z,At(crop.Cell+offset));
        }
        world.Farming.Restore(local);
        world.Farming.Update(401);
        foreach(var crop in local)
            Check(world.GetBlock(crop.Cell.X,crop.Cell.Y,crop.Cell.Z)>At(crop.Cell+offset),"Natural crops did not resume growth.");
        BlockType At(Point3 p)=>(BlockType)snapshot.Blocks[Coordinates.Index(Coordinates.Mod(p.X,16),p.Y,Coordinates.Mod(p.Z,16))];
    }

    private static void Check(bool valid,string message)
    {if(!valid)throw new InvalidOperationException(message);}
}
