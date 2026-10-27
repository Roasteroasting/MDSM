using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

// Representative real GPU draws from fully generated Large/Massive storage.
// This deliberately does not claim all-world GPU allocation or sustained gameplay performance.
internal static class WorldExpansionGraphicsChecks
{
    public static int Run()
    {
        try
        {
            using var game = new CheckGame(); game.Run();
            if (!game.Verified) throw new InvalidOperationException("Expanded graphics check did not complete.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private sealed class CheckGame : Game
    {
        private Task<World> _generation;
        private WorldSize _size = WorldSize.Large;
        private TextureAtlas _atlas;
        private World _lakeWorld;
        private WorldRenderer _lakeRenderer;
        private RenderTarget2D _lakeTarget;
        private Camera _lakeCamera;
        private readonly System.Diagnostics.Stopwatch _lakeTime = new();
        private int _lakeFrames;
        private int _lakePhase;
        private Vector3 _lakeOrigin;
        private double _lakeCpu;
        public bool Verified { get; private set; }
        public CheckGame()
        {
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 480,
                PreferredDepthStencilFormat = DepthFormat.Depth24, SynchronizeWithVerticalRetrace = false };
            IsFixedTimeStep = false;
            Content.RootDirectory = System.IO.Path.Combine(AppContext.BaseDirectory,"Content");
            Window.Title = "MDSM expanded world GPU verification";
        }
        protected override void LoadContent()
        {
            _atlas = new TextureAtlas(Content); StartGeneration();
        }
        private void StartGeneration()
        {
            WorldSize size = _size;
            _generation = Task.Run(() => World.CreateNew(173,size));
        }
        protected override void Draw(GameTime time)
        {
            GraphicsDevice.Clear(WorldRenderer.SkyColor);
            if (_lakeWorld != null) { DrawLake(); return; }
            if (!_generation.IsCompleted) return;
            World world = _generation.GetAwaiter().GetResult();
            VerifyTerrainAndWater(world); _generation = null;
            BeginLake(world);
        }
        private void BeginLake(World world)
        {
            _lakeWorld=world;NaturalLake lake=world.GeneratedLakes.OrderByDescending(l=>l.Area).First();
            _lakeCamera=new Camera{Position=new Vector3(lake.Bounds.Center.X,lake.WaterY+6,lake.Bounds.Bottom-3)};
            _lakeCamera.RestoreOrientation(0,-.15f);_lakeRenderer=new WorldRenderer(GraphicsDevice,_atlas,world);
            _lakeOrigin=_lakeCamera.Position;_lakePhase=0;
            _lakeTarget=new RenderTarget2D(GraphicsDevice,1920,1080,false,SurfaceFormat.Color,DepthFormat.Depth24);
            _lakeFrames=0;_lakeCpu=0;_lakeTime.Reset();
        }
        private void DrawLake()
        {
            GraphicsDevice.SetRenderTarget(_lakeTarget);GraphicsDevice.Clear(WorldRenderer.SkyColor);
            if (!_lakeTime.IsRunning)
            {
                if(!_lakeRenderer.PrepareInitialMeshes(_lakeCamera)){GraphicsDevice.SetRenderTarget(null);return;}
                _lakeTime.Restart();
            }
            _lakeCamera.RestoreOrientation(MathF.Sin((float)_lakeTime.Elapsed.TotalSeconds*1.3f)*.3f,-.15f);
            if(_lakePhase==1)_lakeCamera.Position=_lakeOrigin+new Vector3(MathF.Sin((float)_lakeTime.Elapsed.TotalSeconds)*2,0,MathF.Cos((float)_lakeTime.Elapsed.TotalSeconds*.7f));
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            _lakeRenderer.Draw(_lakeCamera,null);_lakeRenderer.DrawWater(_lakeCamera);
            _lakeCpu+=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;_lakeFrames++;
            GraphicsDevice.SetRenderTarget(null);
            if(_lakeTime.Elapsed.TotalSeconds<5)return;
            double benchmarkSeconds=_lakeTime.Elapsed.TotalSeconds;
            Console.WriteLine($"LAKE GPU {_size} {(_lakePhase==0?"rotating":"rotating+translating")}: {_lakeFrames/benchmarkSeconds:F1} offscreen FPS, CPU draw {_lakeCpu/_lakeFrames:F2} ms, resident {_lakeRenderer.ResidentChunks}, terrain {_lakeRenderer.BufferBytes/1048576.0:F1} MiB, water {_lakeRenderer.Water.BufferBytes/1048576.0:F2} MiB, faces {_lakeRenderer.Water.CachedFaces}.");
            if(_lakePhase==0){_lakePhase=1;_lakeFrames=0;_lakeCpu=0;_lakeTime.Restart();return;}
            var pixels=new Color[1920*1080];_lakeTarget.GetData(pixels);
            int blue=pixels.Count(c=>c!=WorldRenderer.SkyColor && c.B>c.G*1.1f && c.B>c.R*1.2f);
            if(blue<10000 || _lakeRenderer.BufferBytes>450L*1048576 || _lakeRenderer.Water.BufferBytes>16L*1048576)
                throw new InvalidOperationException("Major lake pixels missing or GPU residency exceeded its budget.");
            using(var png=System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory,$"lake-{_size}.png")))_lakeTarget.SaveAsPng(png,1920,1080);
            GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=_atlas.Texture;
            Console.WriteLine($"PASS: {_size} 1080p rotating/translating major lake view, bounded buffers and {blue} blue water pixels.");
            _lakeRenderer.Dispose();_lakeRenderer=null;_lakeTarget.Dispose();_lakeTarget=null;_lakeWorld=null;
            if(_size==WorldSize.Massive){Verified=true;Exit();}else{_size=WorldSize.Massive;StartGeneration();}
        }
        private void VerifyTerrainAndWater(World world)
        {
            // Find a real generated water cell; render its surrounding terrain through existing mesh classes.
            Point3 cell = default; bool found = false;
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.WaterCount == 0) continue;
                for (int z=0;z<16&&!found;z++)for(int x=0;x<16&&!found;x++)for(int y=127;y>=0;y--)
                    if(Water.IsSource(chunk.GetBlock(x,y,z))) {cell=new Point3(chunk.OriginX+x,y,chunk.OriginZ+z);found=true;break;}
                if(found)break;
            }
            if(!found)throw new InvalidOperationException("Generated water is missing.");
            var camera=new Camera {Position=new Vector3(cell.X+0.5f,cell.Y+10,cell.Z+14)};camera.RestoreOrientation(0,-0.6f);
            using var target=new RenderTarget2D(GraphicsDevice,320,240,false,SurfaceFormat.Color,DepthFormat.Depth24);
            using var effect=new AlphaTestEffect(GraphicsDevice) {Texture=_atlas.Texture,VertexColorEnabled=true,
                World=Matrix.Identity,View=camera.View,Projection=camera.Projection(320f/240),ReferenceAlpha=128};
            var builder=new ChunkMeshBuilder();
            GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(WorldRenderer.SkyColor);
            GraphicsDevice.BlendState=BlendState.Opaque;GraphicsDevice.DepthStencilState=DepthStencilState.Default;
            GraphicsDevice.RasterizerState=RasterizerState.CullCounterClockwise;GraphicsDevice.SamplerStates[0]=SamplerState.PointClamp;
            int draws=0;
            for(int cz=Math.Max(0,cell.Z/16-2);cz<=Math.Min(world.ChunksZ-1,cell.Z/16+2);cz++)
            for(int cx=Math.Max(0,cell.X/16-2);cx<=Math.Min(world.ChunksX-1,cell.X/16+2);cx++)
            {
                using var mesh=new ChunkMesh(GraphicsDevice,world.GetChunk(cx,cz));mesh.Update(world,builder);
                if(!mesh.HasGeometry)continue;
                foreach(EffectPass pass in effect.CurrentTechnique.Passes){pass.Apply();mesh.Draw();}draws++;
            }
            using(var water=new WaterRenderer(GraphicsDevice,_atlas,world))water.Draw(camera);
            GraphicsDevice.SetRenderTarget(null);var pixels=new Color[320*240];target.GetData(pixels);
            GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=_atlas.Texture;
            if(draws<9 || pixels.Count(c=>c!=WorldRenderer.SkyColor)<2000 || pixels.Count(c=>c!=WorldRenderer.SkyColor && c.B>c.G*1.1f && c.B>c.R*1.2f)<20)
                throw new InvalidOperationException("Expanded terrain/water GPU image is missing geometry or water pixels.");
            Console.WriteLine($"PASS: {_size} full-world generation and representative {draws}-chunk terrain/water GPU draw at Y={cell.Y} (not an all-world GPU/performance test).");
            RenderingPerformanceChecks.Graphics(GraphicsDevice, _atlas, world, camera);
        }
    }
}
