using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Input;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class OvernightChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Coalesced edit lighting exactly matches cold cache across seams",LightingCoalescing),
        ("Rectangular empty-slot arm, gameplay-only visibility and smooth swing",ArmAndSwing),
        ("Bounded elapsed-time break and landing particles",Particles),
        ("Registry-based Creative sorting/search and safe grants",Catalog),
        ("Contextual Tab completion, cycling and text preservation",Autocomplete)
        ,("Baked sign font matches UI glyphs and bounded glyph geometry",SignFont)
    };
    public static int Run()
    {int failures=0;foreach(var t in All)try{t.Check();Console.WriteLine("PASS: "+t.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+t.Name+": "+e);}return failures==0?0:1;}
    internal static void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    internal static byte[] WithoutFeatures(byte[] bytes)
    {
        int start=52+BitConverter.ToInt32(bytes,24)+48,at=start;
        using var output=new MemoryStream();output.Write(bytes,0,start);
        while(at<bytes.Length-32)
        {
            int count=56+BitConverter.ToInt32(bytes,at+20);
            if(BitConverter.ToUInt32(bytes,at)!=MDSM.Persistence.WorldSerializer.FeaturesRecord)output.Write(bytes,at,count);
            at+=count;
        }
        output.Write(new byte[32]);byte[] result=output.ToArray();SHA256.HashData(result.AsSpan(0,start-32)).CopyTo(result.AsSpan(start-32,32));SHA256.HashData(result.AsSpan(0,result.Length-32)).CopyTo(result.AsSpan(result.Length-32));return result;
    }
    public static int LightingEdits()
    {
        var world=new World(173,WorldSize.Small);
        for(int z=5;z<=7;z++)for(int x=5;x<=7;x++)world.Lighting.GetPage(x,z);
        long start=Stopwatch.GetTimestamp();int before=world.Lighting.BuiltPages;
        for(int i=0;i<12;i++)
        {
            int y=world.SurfaceY(104,104);
            world.SetBlock(104,y,104,i%2==0?BlockType.Air:BlockType.Stone);
            for(int z=5;z<=7;z++)for(int x=5;x<=7;x++)world.Lighting.GetPage(x,z);
        }
        Console.WriteLine($"Lighting edits: {Stopwatch.GetElapsedTime(start).TotalMilliseconds/12:F2} ms/edit; {world.Lighting.BuiltPages-before} neighborhood builds.");
        return 0;
    }
    private static void LightingCoalescing()
    {
        var a=new World(91,WorldSize.Small,false);var b=new World(91,WorldSize.Small,false);
        for(int z=0;z<64;z++)for(int x=0;x<64;x++){a.SetGeneratedBlock(x,40,z,BlockType.Stone);b.SetGeneratedBlock(x,40,z,BlockType.Stone);}
        for(int z=0;z<4;z++)for(int x=0;x<4;x++)a.Lighting.GetPage(x,z);
        foreach(var p in new[]{new Point3(15,6,15),new(30,40,30),new(32,7,32),new(47,40,47)})
        {a.SetBlock(p.X,p.Y,p.Z,p.Y==40?BlockType.Air:BlockType.Torch);b.SetBlock(p.X,p.Y,p.Z,p.Y==40?BlockType.Air:BlockType.Torch);}
        for(int z=0;z<64;z++)for(int x=0;x<64;x++)foreach(int y in new[]{0,6,7,26,39,40,41})
            Check(a.Lighting.Packed(new(x,y,z))==b.Lighting.Packed(new(x,y,z)), $"Patched light differs at {x},{y},{z}: {a.Lighting.Packed(new(x,y,z)):X2} vs {b.Lighting.Packed(new(x,y,z)):X2}.");
        a.SetBlock(15,6,15,BlockType.Air);b.SetBlock(15,6,15,BlockType.Air);
        Check(a.Lighting.Packed(new(16,6,15))==b.Lighting.Packed(new(16,6,15)),"Source removal left stale light.");
        var random=new Random(173);
        BlockType[] materials={BlockType.Air,BlockType.Stone,BlockType.Torch,BlockType.Glass,BlockType.Leaves,BlockType.WaterSource};
        for(int trial=0;trial<24;trial++)
        {
            Point3 edited=new(random.Next(14,49),random.Next(2,44),random.Next(14,49));
            a.SetBlock(edited.X,edited.Y,edited.Z,materials[trial%materials.Length]);
            var fresh=new BlockLighting(a);
            for(int i=0;i<40;i++)
            {Point3 p=new(edited.X+random.Next(-13,14),random.Next(0,50),edited.Z+random.Next(-13,14));Check(a.Lighting.Packed(p)==fresh.Packed(p),"Incremental lighting differs after random source/opacity edits.");}
        }
    }
    private static void ArmAndSwing()
    {
        PresentationChecks.ViewModels();
        var swing=new ViewModelSwing();Check(swing.Transform==Matrix.Identity,"Rest pose is not stable.");
        swing.Trigger();swing.Update(.08f,false);Check(swing.Transform!=Matrix.Identity,"Swing did not move.");
        swing.Update(1,false);Check(swing.Transform==Matrix.Identity,"Swing did not return to rest.");
        var w=PigChecks.Flat();var session=new MDSM.Persistence.GameSession("swing",new(w,"Swing",new(40,1,40),0,0));
        foreach(var block in new[]{BlockType.Torch,BlockType.Door,BlockType.Bed})
        {
            session.Swing.Reset();int count=Math.Min(2,Items.StackLimit(Items.FromBlock(block)));session.Inventory.SetSlot(0,new(Items.FromBlock(block),count));int x=20+(int)block;
            Check(SurvivalInteractions.Use(session,new(new(x,0,20),new(0,1,0),1)),"Special placement did not handle the input.");
            Check(w.GetBlock(x,1,20)==block && session.Inventory[0].Count==count-1,"Special placement changed item accounting.");
            session.Swing.Update(.08f,false);Check(session.Swing.Transform!=Matrix.Identity,"Special placement omitted the swing.");
        }
    }
    private static void Particles()
    {
        var w=new World(1,WorldSize.Small,false);
        for(int i=0;i<100;i++)w.Particles.Burst(BlockType.Stone,new(20,50,20));
        Check(w.Particles.Active.Length==BlockParticles.Capacity,"Particle pool grew without bound.");
        for(int i=0;i<240;i++)w.Particles.Update(w,1f/120);
        foreach(var p in w.Particles.Active)Check(p.Life==0,"Particles did not expire.");
    }
    private static void Catalog()
    {
        Check(CreativeCatalog.All.Length==Items.Obtainable.Length && CreativeCatalog.All.ToArray().Distinct().Count()==Items.Obtainable.Length,"Catalog lost/duplicated items.");
        Check(CreativeCatalog.Matches(Items.Tool(ToolTier.Obsidian,ToolKind.Hoe),"obsidian") && !CreativeCatalog.Matches(Items.Wheat,"iron"),"Search mismatch.");
        var all=CreativeCatalog.All.ToArray();
        foreach(var tier in new[]{ToolTier.Wood,ToolTier.Stone,ToolTier.Iron,ToolTier.Diamond,ToolTier.Obsidian})
        {var positions=all.Select((id,i)=>(id,i)).Where(p=>Items.Get(p.id).Tier==tier && Items.Get(p.id).Tool!=ToolKind.None).Select(p=>p.i).ToArray();Check(positions.Max()-positions.Min()==4,"A tier's hoe is separated from its other tools.");}
    }
    private static void Autocomplete()
    {
        var editor=new CommandLineInput();editor.Open();foreach(char c in "/give @p obsidian_ho")editor.TextInput(c);
        editor.Complete();Check(editor.Text=="/give @p obsidian_hoe","Item autocomplete failed.");
        editor.Open();foreach(char c in "/time set n")editor.TextInput(c);editor.Complete();string first=editor.Text;editor.Complete();Check(first!=editor.Text,"Repeated Tab did not cycle.");
        editor.Complete(true);Check(editor.Text==first,"Reverse Tab cycle failed.");
        editor.Update(new KeyboardState(Microsoft.Xna.Framework.Input.Keys.Home),default);
        editor.Complete();Check(editor.Text=="/gamemode"+first[first.IndexOf(' ')..],"Moving the caret reused a stale completion cycle.");
        var choices=CommandCompletion.Candidates("/spawn chick",12,out _,out _);Check(choices.SequenceEqual(new[]{"chicken"}),"Mob completion failed.");
    }
    private static void SignFont()
    {
        string root=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(root,"MDSM.csproj")))root=Directory.GetParent(root)?.FullName??throw new Exception("Missing source assets.");
        var atlas=ItemAtlasChecks.Read(Path.Combine(root,"Content/UI/text.png"));
        var terrain=ItemAtlasChecks.Read(Path.Combine(root,"Content/Blocks/terrain.png"));
        // Terrain tile 80 is now the new Dead Bush; font tiles remain isolated.
        for(int tile=81;tile<=174;tile++)
        {var source=MDSM.Rendering.TextureAtlas.Source(tile);for(int y=0;y<16;y++)for(int x=0;x<16;x++)Check(terrain.Pixels[((source.Y+y)*256+source.X+x)*4+3]==0,"A sign glyph remains in terrain.png.");}
        foreach(char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@[]<>+?!-.,'")
        {
            var tile=MDSM.Rendering.TextureAtlas.Source(MDSM.UI.PixelFont.SignTile(c));
            for(int y=0;y<7;y++)for(int x=0;x<5;x++)Check((atlas.Pixels[((tile.Y+y)*256+tile.X+x)*4+3]>=128)==MDSM.UI.PixelFont.Ink(c,y,x),"Sign glyph differs from the existing font: "+c);
        }
    }
}
