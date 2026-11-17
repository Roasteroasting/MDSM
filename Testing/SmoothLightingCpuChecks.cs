using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class SmoothLightingCpuChecks
{
    internal static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static Dictionary<Vector3,Color> Tops(ChunkMeshBuilder mesh,float y)
    {
        var result=new Dictionary<Vector3,Color>();
        for(int i=0;i<mesh.VertexCount;i+=6)
        {
            bool top=true;for(int n=0;n<6;n++)top&=Math.Abs(mesh.Vertices[i+n].Position.Y-y)<.0001f;
            var normal=Vector3.Cross(mesh.Vertices[i+2].Position-mesh.Vertices[i].Position,mesh.Vertices[i+1].Position-mesh.Vertices[i].Position);
            if(!top||normal.Y<=0)continue;
            for(int n=0;n<6;n++){var v=mesh.Vertices[i+n];if(result.TryGetValue(v.Position,out var color))Check(color==v.Color,$"Coplanar blocks disagree at {v.Position}: {color} / {v.Color}.");result[v.Position]=v.Color;}
        }
        return result;
    }
    internal static void Cpu()
    {
        var w=new World(173,WorldSize.Small,false);w.SetGeneratedBlock(15,5,15,BlockType.Stone);
        byte[] light=new byte[18*18*128];Array.Fill(light,(byte)0xe0);w.Lighting.SetSnapshot(light);
        var mesh=new ChunkMeshBuilder();mesh.Build(w,w.GetChunk(0,0));var original=mesh.Vertices.Take(mesh.VertexCount).ToArray();
        Check(original.All(v=>v.Color.G<=14&&v.Color.B<=14&&v.Color.A==255),"Default flat vertex encoding changed.");
        mesh.SmoothLighting=true;mesh.Build(w,w.GetChunk(0,0));Check(Tops(mesh,6).Values.All(c=>c.R==255&&c.G==56&&c.B==64),"Exposed terrain is darkened without occluders.");
        w.SetGeneratedBlock(16,6,16,BlockType.Stone);mesh.Build(w,w.GetChunk(0,0));var corners=Tops(mesh,6);
        Check(corners[new(16,6,16)].R==204&&corners[new(15,6,15)].R==255,"Diagonal AO does not darken the appropriate corner.");
        int topAt=Enumerable.Range(0,mesh.VertexCount/6).Select(i=>i*6).First(i=>Enumerable.Range(0,6).All(n=>mesh.Vertices[i+n].Position.Y==6));
        Check(mesh.Vertices[topAt+1].Position==new Vector3(15,6,15)&&mesh.Vertices[topAt+2].Position==new Vector3(16,6,16),"Asymmetric AO did not choose the less conspicuous triangle diagonal.");
        // Two touching solid neighbors seal off a bright diagonal completely.
        w.SetGeneratedBlock(16,6,15,BlockType.Stone);w.SetGeneratedBlock(15,6,16,BlockType.Stone);w.SetGeneratedBlock(16,6,16,BlockType.Air);
        light[((6*18+17)*18)+17]=0xee;w.Lighting.SetSnapshot(light);mesh.Build(w,w.GetChunk(0,0));
        var sealedCorner=mesh.Corner(new(15,6,15),new(1,0,0),new(0,0,1));Check(Math.Abs(sealedCorner.Ambient-.4f)<.0001f&&sealedCorner.Block==0,"Light leaked diagonally through two opaque blocks.");
        w.SetGeneratedBlock(16,6,15,BlockType.Air);w.SetGeneratedBlock(15,6,16,BlockType.Air);
        foreach(var block in new[]{BlockType.Glass,BlockType.GlassPane,BlockType.WoodenSlab,BlockType.Torch,BlockType.TallGrass,BlockType.Snow})
        {w.SetGeneratedBlock(16,6,16,block);mesh.Build(w,w.GetChunk(0,0));Check(Tops(mesh,6)[new(16,6,16)].R==255,"Transparent/thin block occludes as a full cube: "+block);}
        w.SetGeneratedBlock(16,6,16,BlockType.Leaves);mesh.Build(w,w.GetChunk(0,0));Check(Tops(mesh,6)[new(16,6,16)].R is >204 and <255,"Leaves do not attenuate AO gently.");
        w.SetGeneratedBlock(16,6,16,BlockType.Air);mesh.SmoothLighting=false;w.Lighting.SetSnapshot(Enumerable.Repeat((byte)0xe0,light.Length).ToArray());mesh.Build(w,w.GetChunk(0,0));
        Check(original.SequenceEqual(mesh.Vertices.Take(mesh.VertexCount)),"Disabling Smooth Lighting changed flat mesh bytes or triangulation.");
        // A gradient and a diagonal occluder must agree at adjacent faces' shared vertices.
        var shared=new World(173,WorldSize.Small,false);for(int x=6;x<=9;x++)shared.SetGeneratedBlock(x,5,7,BlockType.Stone);shared.SetGeneratedBlock(8,6,8,BlockType.Stone);
        for(int y=0;y<128;y++)for(int z=-1;z<=16;z++)for(int x=-1;x<=16;x++)light[((y*18+z+1)*18)+x+1]=(byte)((Math.Clamp(14-x/2,0,14)<<4)|Math.Clamp(x,0,14));
        shared.Lighting.SetSnapshot(light);mesh.SmoothLighting=true;mesh.Build(shared,shared.GetChunk(0,0));Check(Tops(mesh,6).Count>4,"Gradient fixture has no shared faces.");
        // True adjacent chunk pages (not fabricated samples) agree at their edge.
        var seam=new World(173,WorldSize.Small,false);for(int z=13;z<=15;z++)for(int x=13;x<=18;x++)seam.SetGeneratedBlock(x,3,z,BlockType.Stone);
        seam.SetGeneratedBlock(16,4,15,BlockType.Stone);seam.SetGeneratedBlock(14,4,14,BlockType.Torch);
        mesh.Build(seam,seam.GetChunk(0,0));var left=Tops(mesh,4);mesh.Build(seam,seam.GetChunk(1,0));var right=Tops(mesh,4);int compared=0;
        foreach(var p in left)if(right.TryGetValue(p.Key,out var value)){Check(value==p.Value,"Chunk-edge smooth lighting seam.");compared++;}Check(compared>0,"Chunk-edge fixture compared no shared vertices.");
        // Missing mesh neighbors are not fictitious bedrock AO occluders. Snapshot
        // rendering remains local and identical at positive/negative distant origins.
        VertexPositionColorTexture[] near=null;
        foreach(var address in new[]{new ChunkAddress(0,0),new(784426,-784427),new(-784427,784426)})
        {
            var world=new World(173,WorldSize.Infinite,false);var resident=new Chunk(address.X,address.Z);resident.SetBlock(15,5,15,BlockType.Stone);
            var chunks=(Dictionary<ChunkAddress,Chunk>)typeof(World).GetField("_resident",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);chunks.Add(address,resident);
            var snapshot=world.MeshSnapshot(resident,false);
            try
            {
                Check(!snapshot.MeshSampleKnown(new(16,6,16)),"Missing snapshot neighbor lost availability metadata.");mesh.Build(snapshot,snapshot.GetChunk(0L,0L));var top=Tops(mesh,6);Check(top[new(16,6,16)].R==255,"Unloaded neighbor created an artificial AO edge.");
                var vertices=mesh.Vertices.Take(mesh.VertexCount).ToArray();if(near==null)near=vertices;else Check(vertices.SequenceEqual(near),"Distant/negative snapshot lighting differs after integer origin subtraction.");
            }
            finally{snapshot.Lighting.ReleaseWorkerScratch();}
        }
        Settings();
        Performance();
        Console.WriteLine("PASS: Four-corner sky/block samples, AO, diagonal sealing, transparent/thin blocks, shared vertices/chunks, unloaded halos, Far Lands origins and exact flat mesh restoration.");
    }
    private static void Performance()
    {
        var world=SmoothLightingChecks.Scene();var flat=new ChunkMeshBuilder();var smooth=new ChunkMeshBuilder{SmoothLighting=true};var chunk=world.GetChunk(1,1);
        foreach(var builder in new[]{flat,smooth})
        {
            for(int i=0;i<5;i++)builder.Build(world,chunk);
            long bytes=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
            for(int i=0;i<5;i++)builder.Build(world,chunk);
            double ms=timer.Elapsed.TotalMilliseconds/5;long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Check(allocated<512,"Stable lighting mesh builds allocate per face/vertex.");
            Console.WriteLine($"CPU mesh fixture Smooth={builder.SmoothLighting}: {ms:F2} ms/chunk; {allocated} managed bytes for five builds; {builder.VertexCount} vertices.");
        }
        Check(flat.VertexCount==smooth.VertexCount,"AO inflated the geometry's vertex count.");
    }
    private static void Settings()
    {
        string path=Path.Combine(AppContext.BaseDirectory,".smooth-setting-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var store=new SettingsStore(path);Check(!store.Load().SmoothLighting,"Smooth Lighting changes the legacy default.");store.Save(new(){SmoothLighting=true,FpsCap=144,ViewBobbing=false,Brightness=.73f});var loaded=store.Load();
            Check(loaded.SmoothLighting&&loaded.FpsCap==144&&!loaded.ViewBobbing&&loaded.Brightness==.73f,"Smooth Lighting did not persist independently.");
            File.WriteAllText(path,"{\"SmoothLighting\":\"invalid\",\"FpsCap\":240}");loaded=store.Load();Check(!loaded.SmoothLighting&&loaded.FpsCap==240,"Invalid Smooth Lighting field damaged other settings.");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        foreach(Point size in new[]{new Point(320,240),new(640,480),new(1280,720),new(3440,1440)})
        {
            var v=new Viewport(0,0,size.X,size.Y);var toggle=SettingsLayout.SmoothLighting(v);Check(v.Bounds.Contains(toggle)&&!toggle.Intersects(SettingsLayout.Brightness(v))&&!toggle.Intersects(SettingsLayout.Volume(v,0)),"Smooth Lighting overlaps Settings or leaves the viewport.");
        }
    }
    internal static void Menu(GraphicsDevice device,TextureAtlas atlas)
    {
        var settings=new GameSettings();using var menu=new MenuController(device,atlas,settings);var v=device.Viewport;
        menu.Open(GameState.Settings);var p=SettingsLayout.SmoothLighting(v).Center;
        KeyboardState previousKeys=default;MouseState previousMouse=default;
        MenuAction Input(bool click,params Keys[] keys)
        {
            var keyboard=new KeyboardState(keys);var mouse=new MouseState(p.X,p.Y,0,click?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
            var action=menu.Update(keyboard,previousKeys,mouse,previousMouse,v);previousKeys=keyboard;previousMouse=mouse;return action;
        }
        Input(false);Check(Input(true)==MenuAction.SettingsChanged&&settings.SmoothLighting,"Settings mouse toggle did not activate.");Input(false);Check(Input(false,Keys.Space)==MenuAction.SettingsChanged&&!settings.SmoothLighting,"Settings keyboard toggle did not activate.");
        Input(false);menu.Draw(v);Console.WriteLine("PASS: Real Settings mouse/keyboard Smooth Lighting input, matching checkbox style and persistence defaults.");
    }
}
