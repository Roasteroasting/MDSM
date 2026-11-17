using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class SmoothLightingChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    internal static World Scene()
    {
        var w=new World(173,WorldSize.Small,false);
        for(int z=14;z<=34;z++)for(int x=14;x<=34;x++)
        {
            for(int y=0;y<=3;y++)w.SetGeneratedBlock(x,y,z,BlockType.Stone);
            if(x>=18&&x<=30&&z>=18&&z<=30)
            {
                w.SetGeneratedBlock(x,11,z,BlockType.Stone);
                if(x==18||x==30||z==18||z==30)for(int y=4;y<=10;y++)if(z!=30||x<22||x>26||y>7)w.SetGeneratedBlock(x,y,z,BlockType.Stone);
            }
        }
        for(int y=4;y<=5;y++)w.SetGeneratedBlock(25,y,24,BlockType.Cobblestone);
        w.SetGeneratedBlock(24,4,24,BlockType.Cobblestone);w.SetGeneratedBlock(21,4,23,BlockType.Torch);w.SetGeneratedBlock(28,4,23,BlockType.Glass);
        return w;
    }
    internal static int Run(bool reference=false)
    {try{if(!reference)SmoothLightingCpuChecks.Cpu();using var game=new Preview(reference);game.Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        private readonly bool reference;
        internal Preview(bool capture){reference=capture;_=new GraphicsDeviceManager(this){PreferredBackBufferWidth=854,PreferredBackBufferHeight=480,SynchronizeWithVerticalRetrace=false};IsFixedTimeStep=false;}
        protected override void LoadContent(){Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Draw(GameTime time)
        {
            using var atlas=new TextureAtlas(Content);var world=Scene();var camera=new Camera{Position=new(24.5f,7,28.5f)};camera.RestoreOrientation(0,-.25f);
            using var renderer=new WorldRenderer(GraphicsDevice,atlas,world);RenderingPerformanceChecks.Warm(renderer,camera);
            int width=GraphicsDevice.Viewport.Width,height=GraphicsDevice.Viewport.Height;
            using var target=new RenderTarget2D(GraphicsDevice,width,height,false,SurfaceFormat.Color,DepthFormat.Depth24);
            Color[] Render()
            {
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(world.Time.Sky);renderer.Draw(camera,null);renderer.DrawWater(camera);GraphicsDevice.SetRenderTarget(null);
                var pixels=new Color[width*height];target.GetData(pixels);GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;return pixels;
            }
            void Save(string name){using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name));target.SaveAsPng(file,width,height);GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;}
            var flats=new Color[3][];
            int referencesCompared=0;
            for(int mode=0;mode<3;mode++)
            {
                world.Time.Restore(mode==0?60:900);renderer.Brightness=mode==2?1:.5f;
                flats[mode]=Render();
                string path=Path.Combine(AppContext.BaseDirectory,$"smooth-flat-reference-{mode}.png");
                if(reference){using var file=File.Create(path);target.SaveAsPng(file,width,height);}
                else if(File.Exists(path))
                {
                    var pixels=new Color[width*height];target.GetData(pixels);using var file=File.OpenRead(path);using var old=Texture2D.FromStream(GraphicsDevice,file);var prior=new Color[old.Width*old.Height];old.GetData(prior);
                    Check(old.Width==width&&old.Height==height&&pixels.SequenceEqual(prior),"Smooth OFF changed pre-update flat lighting pixels.");
                    referencesCompared++;
                    GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
                }
            }
            if(reference){Console.WriteLine("Saved original flat-lighting GPU references.");Exit();return;}
            Console.WriteLine(referencesCompared>0?$"PASS: Flat lighting retains exact pre-update GPU pixels ({referencesCompared} captured day/night/Brightness cases).":"NOTE: No pre-update PNG capture present; CPU flat-byte equality and live flat restoration are still checked.");
            SmoothLightingCpuChecks.Menu(GraphicsDevice,atlas);
            var menuPixels=new Color[width*height];GraphicsDevice.GetBackBufferData(menuPixels);
            using(var image=new Texture2D(GraphicsDevice,width,height))
            {image.SetData(menuPixels);using var file=File.Create(Path.Combine(AppContext.BaseDirectory,"smooth-settings.png"));image.SaveAsPng(file,width,height);}
            GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            int revision=world.Revision;long bytes=renderer.BufferBytes;var packed=(byte[])world.Lighting.GetPage(1,1).Data.Clone();
            renderer.SmoothLighting=true;var watch=Stopwatch.StartNew();RenderingPerformanceChecks.Warm(renderer,camera);
            Console.WriteLine($"Smooth fixture rebuild: {watch.Elapsed.TotalMilliseconds:F1} ms; {renderer.VertexCount} vertices, {renderer.BufferBytes} estimated terrain buffer bytes.");
            Check(renderer.BufferBytes==bytes&&world.Revision==revision,"Smooth toggle changed geometry residency or world state.");
            var meshes=(ChunkMesh[])typeof(WorldRenderer).GetField("_meshes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
            Check(meshes.Where(m=>m!=null).All(m=>m.SmoothLighting),"Visible terrain did not adopt smooth mesh lighting.");
            int prepared=renderer.PreparedChunks;
            for(int mode=0;mode<3;mode++)
            {
                world.Time.Restore(mode==0?60:900);renderer.Brightness=mode==2?1:.5f;
                var pixels=Render();Save($"smooth-lighting-{mode}.png");
                Check(pixels.Where((p,i)=>p!=flats[mode][i]).Count()>100,"Smooth lighting has no visible corner/gradient effect.");
                Check(renderer.PreparedChunks==prepared&&renderer.RebuiltChunks==0,"Day/night or Brightness rebuilt smooth meshes.");
            }
            Check(packed.SequenceEqual(world.Lighting.GetPage(1,1).Data),"Visual smoothing changed propagated/gameplay light values.");
            renderer.SmoothLighting=false;RenderingPerformanceChecks.Warm(renderer,camera);Check(Render().SequenceEqual(flats[2]),"Switching Smooth Lighting OFF did not restore exact flat pixels.");
            renderer.SmoothLighting=true;RenderingPerformanceChecks.Warm(renderer,camera);int diagonal=world.GetChunk(0,0).Revision;
            world.SetBlock(16,7,16,BlockType.Glass);Check(world.GetChunk(0,0).Revision>diagonal,"Smooth mode omitted diagonal edit invalidation.");
            renderer.SmoothLighting=false;diagonal=world.GetChunk(0,0).Revision;world.SetBlock(16,7,16,BlockType.Air);Check(world.GetChunk(0,0).Revision==diagonal,"Disabled smoothing retains unnecessary diagonal geometry work.");
            renderer.SmoothLighting=true;camera.Position=new(33,10,37);camera.RestoreOrientation(-.5f,-.3f);RenderingPerformanceChecks.Warm(renderer,camera);Render();Save("smooth-exposed-terrain.png");
            SmoothLightingGpuChecks.Gradient(GraphicsDevice,atlas,target);
            SmoothLightingGpuChecks.Streaming(GraphicsDevice,atlas);
            Console.WriteLine("PASS: Live smooth/flat toggles, constant vertex/buffer sizes, corners, torch-lit interiors, exposed terrain, day/night/Brightness without remeshing, and unchanged gameplay lighting.");Exit();
        }
    }
}
