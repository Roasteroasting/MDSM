using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class SmoothLightingGpuChecks
{
    private static void Check(bool condition,string message)=>SmoothLightingCpuChecks.Check(condition,message);
    internal static void Gradient(GraphicsDevice device,TextureAtlas atlas,RenderTarget2D target)
    {
        using var effect=atlas.LightingEffect.Clone();using var white=new Texture2D(device,1,1);white.SetData(new[]{Color.White});
        var world=new World(173,WorldSize.Small,false);var camera=new Camera{Position=Vector3.Zero};camera.RestoreOrientation(0,0);
        Color[] Draw(bool flip,bool daylightSplit=false)
        {
            var low=new CornerLight(daylightSplit?14:0,0,1).Encode(Color.White);var high=new CornerLight(daylightSplit?0:14,daylightSplit?14:0,1).Encode(Color.White);
            var a=new VertexPositionColorTexture(new(-2,-1,-4),low,new(.5f));var b=new VertexPositionColorTexture(new(2,-1,-4),high,new(.5f));
            var c=new VertexPositionColorTexture(new(2,1,-4),high,new(.5f));var d=new VertexPositionColorTexture(new(-2,1,-4),low,new(.5f));
            var vertices=flip?new[]{a,d,b,b,d,c}:new[]{a,c,b,a,d,c};
            device.SetRenderTarget(target);device.Clear(Color.Magenta);device.DepthStencilState=DepthStencilState.Default;device.RasterizerState=RasterizerState.CullNone;device.BlendState=BlendState.Opaque;
            FlatLighting.Apply(effect,device,white,world,camera,.5f,.5f);foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();device.DrawUserPrimitives(PrimitiveType.TriangleList,vertices,0,2);}
            device.SetRenderTarget(null);var pixels=new Color[target.Width*target.Height];target.GetData(pixels);device.Textures[0]=null;device.Textures[0]=atlas.Texture;return pixels;
        }
        var original=Draw(false);var flipped=Draw(true);int y=target.Height/2,x=target.Width/2;
        int left=original[y*target.Width+x-80].R,middle=original[y*target.Width+x].R,right=original[y*target.Width+x+80].R;
        Check(left<middle&&middle<right&&Math.Abs(middle-(left+right)*.5)<2,"Vertex brightness does not interpolate linearly across the face.");
        Check(original.Zip(flipped,(a,b)=>Math.Abs(a.R-b.R)).Max()<=1,"Triangle diagonal changes the smooth linear gradient.");
        world.Time.Restore(60);var day=Draw(false,true);world.Time.Restore(900);var night=Draw(false,true);
        Check(day[y*target.Width+x-80].R==255&&day[y*target.Width+x+80].R==255&&night[y*target.Width+x-80].R<night[y*target.Width+x+80].R,"Sunlight and artificial light do not respond independently to night.");
        Console.WriteLine($"PASS: Actual GPU vertex interpolation ({left}/{middle}/{right}), alternate diagonals and independent sky/block day-night response.");
    }
    internal static void Streaming(GraphicsDevice device,TextureAtlas atlas)
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".smooth-stream-"+Guid.NewGuid().ToString("N"));
        var world=World.OpenStreamed(173,root,3,1,1);world.SetOrigin(12550816,-12550832);
        var center=ChunkAddress.At(world.OriginX+8,world.OriginZ+8);var camera=new Camera{Position=new(8,80,8)};
        WorldRenderer renderer=null;long before=ChunkMesh.LiveBufferBytes;
        try
        {
            var watch=Stopwatch.StartNew();
            while(true)
            {
                world.UpdateStreaming(center);if(world.ChunkLifecycle.Error!=null)throw world.ChunkLifecycle.Error;
                if(world.ChunkLifecycle.InFlight==0&&world.ChunkCount==world.ChunkLifecycle.MaximumResident)break;
                if(watch.Elapsed.TotalSeconds>60)throw new Exception("Streaming AO fixture generation stalled.");Thread.Sleep(1);
            }
            renderer=new WorldRenderer(device,atlas,world);renderer.Prepare(camera);renderer.SmoothLighting=true;
            void Warm()
            {
                var timer=Stopwatch.StartNew();
                while(true)
                {
                    renderer.Prepare(camera,4,true);
                    var jobs=(List<StreamedMeshJob>)typeof(WorldRenderer).GetField("_jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
                    Check(jobs.Count<=2&&renderer.RebuiltChunks<=4,"Smooth mode exceeded bounded worker/upload budgets.");
                    if(renderer.PendingChunks==0&&jobs.Count==0)break;
                    if(timer.Elapsed.TotalSeconds>60)throw new Exception("Streaming AO mesh rebuild stalled.");Thread.Sleep(1);
                }
                var meshes=(ChunkMesh[])typeof(WorldRenderer).GetField("_meshes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
                Check(meshes.Where(m=>m!=null).All(m=>m.SmoothLighting==renderer.SmoothLighting),"A stale worker uploaded the wrong lighting mode.");
                Check(renderer.ResidentChunks<=9&&world.ChunkCount<=49,"Smooth meshes escaped streamed residency bounds.");
            }
            Warm();renderer.SmoothLighting=false;renderer.Prepare(camera);renderer.SmoothLighting=true;Warm();
            var edit=new Point3(world.OriginX+16,120,world.OriginZ+16);int revision=world.GetChunk(center.X,center.Z).Revision;
            Check(world.SetBlock(edit.X,edit.Y,edit.Z,BlockType.Glass)&&world.GetChunk(center.X,center.Z).Revision>revision,"Streamed corner edit omitted the diagonal chunk.");Warm();
            camera.Position=new(8,140,8);camera.RestoreOrientation(0,-.7f);
            using(var target=new RenderTarget2D(device,640,360,false,SurfaceFormat.Color,DepthFormat.Depth24))
            {
                device.SetRenderTarget(target);device.Clear(world.Time.Sky);renderer.Draw(camera,null);renderer.DrawWater(camera);device.SetRenderTarget(null);
                Check(renderer.VisibleChunks>0&&renderer.DrawnVertices>0,"Smooth Far Lands fixture drew no terrain.");
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,"smooth-farlands.png"));target.SaveAsPng(file,640,360);
                device.Textures[0]=null;device.Textures[0]=atlas.Texture;
            }
            Console.WriteLine($"PASS: Far Lands asynchronous smooth toggles/stale results/edits: {world.ChunkCount} loaded chunks, {renderer.GpuChunks} GPU chunks, {renderer.BufferBytes} estimated terrain bytes; {watch.Elapsed.TotalSeconds:F2}s fixture total.");
        }
        finally
        {
            renderer?.Dispose();world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if(Path.GetDirectoryName(Path.GetFullPath(root))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe smooth fixture cleanup.");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
        Check(ChunkMesh.LiveBufferBytes==before,"Streamed smooth meshes leaked GPU buffers.");
    }
}
