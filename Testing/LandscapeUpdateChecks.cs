using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MDSM.Gameplay;
using MDSM.Input;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class LandscapeUpdateChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Resumable exact lighting/mesh work, intervening queries and cancelled edits",Incremental),
        ("Visible cached command candidates and highlighted forward/reverse cycling",Completion),
        ("Fire floor/wall selection, damage bounds, underside rejection and saved attachment",Fire),
        ("Cactus columns, support-loss accounting, bush geometry and item persistence",Plants),
        ("Desert-only deterministic vegetation and reduced desert water candidates",Desert),
        ("Mixed low/high mountain noise, actual cliff gradients and protected spawn",Mountains)
    };
    private static void C(bool ok,string message)=>OvernightChecks.Check(ok,message);
    public static int Run(){int failed=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}return failed==0?0:1;}
    private static void Incremental()
    {
        var w=PigChecks.Flat();for(int z=10;z<40;z++)for(int x=10;x<40;x++)w.SetBlock(x,12,z,BlockType.Stone);w.SetBlock(20,5,20,BlockType.Torch);
        bool first=w.Lighting.PreparePage(1,1,.05);C(!first && w.Lighting.CachedChunks==0,"Partial lighting was published before completion.");
        w.Lighting.Packed(new(180,4,180)); // Exact foreground query cancels the scratch job safely.
        w.SetBlock(25,12,25,BlockType.Air);int steps=0;while(!w.Lighting.PreparePage(1,1,.05))C(++steps<2000,"Lighting prefetch did not finish.");
        var fresh=new BlockLighting(w);for(int y=0;y<16;y++)for(int z=15;z<=32;z+=3)for(int x=15;x<=32;x+=3)C(w.Lighting.Packed(new(x,y,z))==fresh.Packed(new(x,y,z)),"Resumed light differs from a cold reference.");
        var complete=new ChunkMeshBuilder();complete.Build(w,w.GetChunk(1,1));var split=new ChunkMeshBuilder();split.BeginBuild(w,w.GetChunk(1,1));steps=0;
        while(!split.AdvanceBuild(Stopwatch.GetTimestamp()))C(++steps<=World.Height*16+1,"Mesh step lost its cursor.");
        C(complete.VertexCount==split.VertexCount && complete.TextVertexCount==split.TextVertexCount && complete.Vertices.AsSpan(0,complete.VertexCount).SequenceEqual(split.Vertices.AsSpan(0,split.VertexCount)),"Sliced mesh changed triangles/UVs/light.");
    }
    private static void Completion()
    {
        var input=new CommandLineInput();input.Open();foreach(char c in "/spawn c")input.TextInput(c);
        C(input.Suggestions.SequenceEqual(new[]{"chicken","cow"}) && input.SuggestionIndex==-1,"Candidates are hidden until Tab or incorrect.");
        var cached=input.Suggestions;C(ReferenceEquals(cached,input.Suggestions),"Unchanged command candidates allocate every frame.");
        input.Complete();C(input.SuggestionIndex==0 && input.Text=="/spawn chicken" && input.Suggestions.Count==2,"Completing erased the other choices.");
        input.Complete();C(input.SuggestionIndex==1 && input.Suggestions[input.SuggestionIndex]=="cow","Candidate highlight did not track Tab.");input.Complete(true);C(input.SuggestionIndex==0,"Reverse highlight failed.");
        input.TextInput('x');C(input.Suggestions.Count==0 && input.SuggestionIndex==-1,"Typing retained stale options.");
    }
    private static void Fire()
    {
        var w=PigChecks.Flat();Point3 cell=new(20,1,20);Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        C(!w.Fire.Ignite(cell,new(0,1,0)),"Explicit underside ignition was accepted despite ground below.");
        C(w.Fire.Ignite(cell,new(0,-1,0)),"Top ignition failed.");C(w.BlockBoxes(cell,boxes)==1 && boxes[0]==World.Bounds(cell),"Floor fire lost its full selection box.");w.SetBlock(20,1,20,BlockType.Air);
        for(int i=0;i<4;i++)
        {
            Point3 d=World.FacingDirection(i),fuel=cell+d;w.SetBlock(fuel.X,fuel.Y,fuel.Z,BlockType.WoodLog);C(w.Fire.Ignite(cell,d),"Wall ignition failed.");
            C(w.BlockBoxes(cell,boxes)==1 && w.BlockBoxes(cell,boxes,true)==0,"Fire became solid.");w.BlockBoxes(cell,boxes);Vector3 size=boxes[0].Max-boxes[0].Min;
            C(Math.Abs((d.X!=0?size.X:size.Z)-.32f)<.0001f && size.Y<1,"Wall fire retained a full-block outline.");
            C(!w.Fire.Contact(new(new(20.4f,1.1f,20.4f),new(20.6f,1.7f,20.6f))),"Empty space behind the wall sheet causes burns.");
            using var stream=new MemoryStream();WorldSerializer.Write(stream,new(w,"Wall",new(40,1,40),0,0));stream.Position=0;var saved=WorldSerializer.Read(stream);
            C(saved.World.Fire.Attachment(cell)==w.Fire.Attachment(cell),"Saved wall fire became floor fire.");
            if(i==0)
            {
                using var stored=new MemoryStream();WorldSerializer.WriteCompressed(stored,new(w,"Old fire",new(40,1,40),0,0),SaveCodec.Stored);byte[] bytes=stored.ToArray();
                int at=52+BitConverter.ToInt32(bytes,24)+48;while(BitConverter.ToUInt32(bytes,at)!=WorldSerializer.FeaturesRecord)at+=56+BitConverter.ToInt32(bytes,at+20);
                int length=BitConverter.ToInt32(bytes,at+20),end=at+56+length;
                byte[] old=new byte[bytes.Length-1];bytes.AsSpan(0,end-1).CopyTo(old);bytes.AsSpan(end).CopyTo(old.AsSpan(end-1));
                BitConverter.GetBytes(1).CopyTo(old,at+56);BitConverter.GetBytes(length-1).CopyTo(old,at+16);BitConverter.GetBytes(length-1).CopyTo(old,at+20);
                SHA256.HashData(old.AsSpan(at+56,length-1)).CopyTo(old.AsSpan(at+24,32));SHA256.HashData(old.AsSpan(0,old.Length-32)).CopyTo(old.AsSpan(old.Length-32));
                var legacy=WorldSerializer.Read(new MemoryStream(old));C(legacy.World.Fire.Count==1 && legacy.World.Fire.Attachment(cell)==FireAttachment.North,"Old feature schema 1 did not infer its side fire safely.");
            }
            w.SetBlock(20,1,20,BlockType.Air);w.SetBlock(fuel.X,fuel.Y,fuel.Z,BlockType.Air);
        }
        var empty=new World(1,WorldSize.Small,false);empty.SetBlock(20,2,20,BlockType.WoodLog);C(!empty.Fire.Ignite(new(20,1,20)),"Fire attached only to a ceiling.");
    }
    private static void Plants()
    {
        var w=PigChecks.Flat();var session=new GameSession("plants",new(w,"Plants",new(40,1,40),0,0));w.SetBlock(20,0,20,BlockType.Sand);
        for(int y=1;y<=4;y++)C(w.SetBlock(20,y,20,BlockType.Cactus),"Supported cactus placement failed.");
        C(!w.SetBlock(21,1,20,BlockType.Cactus),"Cactus accepted an incompatible support/adjacent trunk.");
        w.SetBlock(20,0,20,BlockType.Air);C(w.GetBlock(20,4,20)==BlockType.Air && session.Items.Entities.Sum(d=>d.Item==Items.FromBlock(BlockType.Cactus)?d.Count:0)==4,"Cactus support loss lost/duplicated segments.");
        w.SetBlock(25,0,25,BlockType.Sand);C(w.TryPlace(new(25,1,25),BlockType.DeadBush,session.Player.Bounds),"Bush refused desert soil.");
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];C(w.BlockBoxes(new(25,1,25),boxes,true)==0 && w.BlockBoxes(new(25,1,25),boxes)==1 && boxes[0].Max.Y<2,"Bush collision/selection is not a small non-solid plant.");
        var mesh=new ChunkMeshBuilder();mesh.Build(w,w.GetChunk(1,1));C(mesh.Vertices.Take(mesh.VertexCount).Count(v=>SignFireChecks.Tile(v)==80)==24,"Bush does not use two crossed, double-sided planes.");
        C(CreativeCatalog.All.Contains(Items.FromBlock(BlockType.Cactus)) && CreativeCatalog.All.Contains(Items.FromBlock(BlockType.DeadBush)),"New block missing from registry-based browser.");
        using var data=new MemoryStream();WorldSerializer.Write(data,session.Capture());data.Position=0;var restored=WorldSerializer.Read(data);C(restored.World.GetBlock(25,1,25)==BlockType.DeadBush && restored.DroppedItems.Sum(d=>d.Count)==4,"Desert items changed during persistence.");
    }
    private static void Desert()
    {
        var climate=new Climate(173){VariedGeneration=true};int desert=0,water=0;
        for(int z=0;z<1024;z+=3)for(int x=0;x<1024;x+=3)
        {if(climate.At(x,z)==Biome.Desert){desert++;if(climate.WaterCandidate(x,z))water++;}else C(climate.WaterCandidate(x,z),"Non-desert water was suppressed.");}
        C(desert>1000 && water/(double)desert is >.12 and <.18,"Desert water candidates are not substantially reduced.");
        int totalCacti=0,totalBushes=0;
        foreach(int seed in new[]{173,137,-907})
        {
            var w=new World(seed,WorldSize.Small,false);w.Climate.VariedGeneration=true;new TerrainGenerator(seed,true,w.Width).Generate(w);DesertVegetation.Generate(w);int cacti=0,bushes=0;
            for(int z=0;z<w.Depth;z++)for(int x=0;x<w.Width;x++)
            {
                int y=w.SurfaceY(x,z);if(w.GetBlock(x,y,z)==BlockType.Cactus)
                {
                    int height=0;while(w.GetBlock(x,y-height,z)==BlockType.Cactus){C(w.CanCactus(new(x,y-height,z)),"Generated cactus intersects a neighbor.");height++;}
                    C(height is >=2 and <=4 && w.Climate.At(x,z)==Biome.Desert,"Cactus height/biome incorrect.");cacti++;
                }
                if(w.GetBlock(x,y+1,z)==BlockType.DeadBush){C(w.Climate.At(x,z)==Biome.Desert && w.GetBlock(x,y,z)==BlockType.Sand,"Bush outside a desert.");bushes++;}
            }
            totalCacti+=cacti;totalBushes+=bushes;Console.WriteLine($"  Desert {seed}: {cacti} cactus columns, {bushes} bushes.");
        }
        C(totalCacti>10 && totalBushes>20,"Natural desert vegetation is missing.");Console.WriteLine($"  Desert water candidate acceptance {water/(double)desert:P1}.");
    }
    private static void Mountains()
    {
        var terrain=new TerrainGenerator(173,true,1024);int cliffs=0,maximumStep=0;
        for(int z=0;z<1024;z+=2)for(int x=0;x<1024;x+=2)
        {int y=terrain.SurfaceY(x,z),step=Math.Abs(y-terrain.SurfaceY(x+1,z));maximumStep=Math.Max(maximumStep,step);if(step>=3)cliffs++;C(y is >=63 and <=121,"Mountain exceeds world height bounds.");}
        C(cliffs>500 && maximumStep>=4 && terrain.MountainStrength(512,512)==0,"Terrain is still a smooth cone or damages spawn.");Console.WriteLine($"  Mountain cliffs: {cliffs} sampled edges >=3 blocks, maximum step {maximumStep}.");
    }
}
