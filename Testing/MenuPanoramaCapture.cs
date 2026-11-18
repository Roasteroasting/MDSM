using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;
namespace MDSM.Testing;

// Development-only capture: actual world rendering, before any menu/UI pass.
internal static class MenuPanoramaCapture
{
    internal static int Run()
    {try{using var game=new Capture();game.Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Capture:Game
    {
        internal Capture(){_ = new GraphicsDeviceManager(this){PreferredBackBufferWidth=1024,PreferredBackBufferHeight=1024,SynchronizeWithVerticalRetrace=false};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            int seed=Random.Shared.Next();
            Console.WriteLine("Generating Infinite panorama terrain, seed "+seed);
            string parent=Path.GetFullPath(AppContext.BaseDirectory);
            string root=Path.Combine(parent,".panorama-"+Guid.NewGuid().ToString("N"));
            var world=World.OpenStreamed(seed,root,simulation:0);
            try { CaptureWorld(world); }
            finally
            {
                // The disposable capture world has no changes worth saving.
                world.ChunkLifecycle.CloseAsync(checkpointIsCurrent:true).AsTask().GetAwaiter().GetResult();
                if(Directory.Exists(root) && Path.GetDirectoryName(root)==Path.TrimEndingDirectorySeparator(parent))
                    Directory.Delete(root,true);
            }
            Exit();
        }

        private void CaptureWorld(World world)
        {
            LoadArea(world,new(0,0),6);
            // Prefer a dry shore near the origin, with a clear elevated camera.
            Vector3 position=new(.5f,Math.Max(72,world.SurfaceY(0,0)+8),.5f);
            float best=float.MinValue;
            for(int z=-60;z<=60;z+=4)
            for(int x=-60;x<=60;x+=4)
            {
                int h=world.SurfaceY(x,z);if(h<0||h>108||Water.IsWater(world.GetBlock(x,h+1,z)))continue;
                int wet=0,relief=0;for(int dz=-28;dz<=28;dz+=7)for(int dx=-28;dx<=28;dx+=7)
                {int y=world.SurfaceY(x+dx,z+dz);if(Water.IsWater(world.GetBlock(x+dx,y+1,z+dz)))wet++;relief+=Math.Abs(y-h);}
                float score=wet*4+Math.Min(relief,700)*.03f-new Vector2(x,z).Length()*.04f;
                if(score>best){best=score;position=new(x+.5f,h+5,z+.5f);}
            }
            LoadArea(world,world.AbsolutePosition(position).Chunk,world.ChunkLifecycle.RenderDistance+1);
            Console.WriteLine($"Panorama near the Infinite world origin, eye {position}");
            using var atlas=new TextureAtlas(Content);using var renderer=new WorldRenderer(GraphicsDevice,atlas,world);
            var camera=new Camera{Position=position,FieldOfView=90};
            // Prepare the entire ring once so each face sees the same finished terrain.
            var started=Stopwatch.StartNew();
            do
            {
                renderer.Prepare(camera,initial:true);
                if(renderer.PendingChunks==0)break;
                if(started.Elapsed.TotalMinutes>3)throw new TimeoutException("Panorama meshes did not finish.");
                Thread.Sleep(1);
            }while(true);
            string output=Path.GetFullPath("Content/UI/Panorama");Directory.CreateDirectory(output);
            using var target=new RenderTarget2D(GraphicsDevice,1024,1024,false,SurfaceFormat.Color,DepthFormat.Depth24);
            for(int face=0;face<6;face++)
            {
                camera.PresentationTransform=Matrix.Identity;
                camera.PresentationTransform=Matrix.Invert(camera.View)*Matrix.CreateLookAt(position,position+MenuPanorama.Directions[face],MenuPanorama.Up(face));
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(WorldRenderer.SkyColor);
                renderer.Draw(camera,null);renderer.DrawWater(camera);GraphicsDevice.SetRenderTarget(null);
                using var file=File.Create(Path.Combine(output,face+".png"));target.SaveAsPng(file,1024,1024);
                GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            }
            File.WriteAllText(Path.Combine(output,"capture.txt"),$"Generated in-game panorama. Infinite world seed {world.Seed}, eye {position}, 1024x1024 per face. Direction order -Z,+X,+Z,-X,+Y,-Y. Regenerate with --capture-menu-panorama from the project root.\n");
            Console.WriteLine("PASS: Six world-rendered panorama faces captured at "+output);
        }

        private static void LoadArea(World world,ChunkAddress center,int radius)
        {
            var started=Stopwatch.StartNew();
            while(true)
            {
                world.UpdateStreaming(center);
                if(world.ChunkLifecycle.Error is Exception error)throw new IOException("Panorama terrain failed to load.",error);
                bool ready=true;
                for(int z=-radius;z<=radius && ready;z++)for(int x=-radius;x<=radius && ready;x++)
                    ready=world.IsLoaded(new(center.BlockX+x*Chunk.Size,0,center.BlockZ+z*Chunk.Size));
                if(ready)return;
                if(started.Elapsed.TotalSeconds>180)throw new TimeoutException("Panorama terrain did not finish.");
                Thread.Sleep(1);
            }
        }
    }
}




