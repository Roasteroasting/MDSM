using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;
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
            Console.WriteLine("Generating panorama world, seed "+seed);
            var world=World.CreateNew(seed,WorldSize.Normal,Console.WriteLine);
            // Prefer a shore within 60 blocks of spawn, with a clear elevated camera.
            Vector3 position=world.Spawn+new Vector3(0,8,0);float best=float.MinValue;
            for(int z=Math.Max(40,(int)world.Spawn.Z-60);z<Math.Min(world.Depth-40,world.Spawn.Z+60);z+=4)
            for(int x=Math.Max(40,(int)world.Spawn.X-60);x<Math.Min(world.Width-40,world.Spawn.X+60);x+=4)
            {
                int h=world.SurfaceY(x,z);if(h<0||h>108||Water.IsWater(world.GetBlock(x,h+1,z)))continue;
                int wet=0,relief=0;for(int dz=-28;dz<=28;dz+=7)for(int dx=-28;dx<=28;dx+=7)
                {int y=world.SurfaceY(x+dx,z+dz);if(Water.IsWater(world.GetBlock(x+dx,y+1,z+dz)))wet++;relief+=Math.Abs(y-h);}
                float score=wet*4+Math.Min(relief,700)*.03f-Vector2.Distance(new(x,z),new(world.Spawn.X,world.Spawn.Z))*.04f;
                if(score>best){best=score;position=new(x+.5f,h+5,z+.5f);}
            }
            Console.WriteLine($"Panorama near spawn {world.Spawn}: eye {position}");
            using var atlas=new TextureAtlas(Content);using var renderer=new WorldRenderer(GraphicsDevice,atlas,world);
            var camera=new Camera{Position=position,FieldOfView=90};
            RenderingPerformanceChecks.Warm(renderer,camera);
            string output=Path.GetFullPath("Content/UI/Panorama");Directory.CreateDirectory(output);
            using var target=new RenderTarget2D(GraphicsDevice,1024,1024,false,SurfaceFormat.Color,DepthFormat.Depth24);
            for(int face=0;face<6;face++)
            {
                camera.PresentationTransform=Matrix.Identity;
                camera.PresentationTransform=Matrix.Invert(camera.View)*Matrix.CreateLookAt(position,position+MenuPanorama.Directions[face],MenuPanorama.Up(face));
                RenderingPerformanceChecks.Warm(renderer,camera);
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(WorldRenderer.SkyColor);
                renderer.Draw(camera,null);renderer.DrawWater(camera);GraphicsDevice.SetRenderTarget(null);
                using var file=File.Create(Path.Combine(output,face+".png"));target.SaveAsPng(file,1024,1024);
                GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            }
            File.WriteAllText(Path.Combine(output,"capture.txt"),$"Generated in-game panorama. Seed {seed}; finite Normal; spawn {world.Spawn}; eye {position}; 1024x1024 per face; direction order -Z,+X,+Z,-X,+Y,-Y. Regenerate with --capture-menu-panorama from the project root.\n");
            Console.WriteLine("PASS: Six world-rendered panorama faces captured at "+output);Exit();
        }
    }
}




