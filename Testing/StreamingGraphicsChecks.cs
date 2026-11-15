using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

// Density-stage GPU probes only. This is not the game's live streaming renderer.
internal static class StreamingGraphicsChecks
{
    public static int Run()
    {
        try{using var game=new Probe();game.Run();return game.Passed?0:1;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private sealed class Probe : Game
    {
        private TextureAtlas _atlas;
        private int _case;
        private Task<(World World,ChunkMeshBuilder Builder)> _pending;
        private readonly (long X,long Z)[] _places={(0,0),(12550824,0),(12550824,12550824),(-12550824,0),(-12550824,-12550824),(25101648,0)};
        public bool Passed {get;private set;}
        internal Probe()
        {
            _=new GraphicsDeviceManager(this){PreferredBackBufferWidth=640,PreferredBackBufferHeight=480,PreferredDepthStencilFormat=DepthFormat.Depth24,SynchronizeWithVerticalRetrace=false};
            Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;
        }
        protected override void LoadContent(){_atlas=new(Content);Start();}
        private void Start()
        {
            var at=_places[_case];
            Console.WriteLine($"Preparing density GPU probe {at}.");
            _pending=Task.Run(()=>
            {
                var world=new World(173,WorldSize.Small,false);var generator=new BetaDensityGenerator(173);var center=ChunkAddress.At(at.X,at.Z);
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)world.GetChunk(8+x,8+z).RestoreBlocks(generator.Generate(new(center.X+x,center.Z+z)));
                var builder=new ChunkMeshBuilder();builder.Build(world,world.GetChunk(8,8));Console.WriteLine($"CPU probe ready {at}.");return(world,builder);
            });
        }
        protected override void Draw(GameTime time)
        {
            while(_case<_places.Length)ProbeOne();Passed=true;Exit();
        }
        private void ProbeOne()
        {
            var result=_pending.GetAwaiter().GetResult();
            var camera=new Camera{Position=new(136,140,169)};camera.RestoreOrientation(0,-.85f);
            long before=ChunkMesh.LiveBufferBytes;
            using(var mesh=new ChunkMesh(GraphicsDevice,result.World.GetChunk(8,8)))
            using(var effect=_atlas.LightingEffect.Clone())
            {
                Console.WriteLine($"Uploading {_places[_case]}: {result.Builder.VertexCount} vertices.");
                mesh.UploadPrepared(result.Builder,result.World.GetChunk(8,8).Revision);
                if(mesh.VertexCount>32768*36 || mesh.BufferBytes>40L*1024*1024)throw new Exception("Unbounded Far Lands mesh.");
                GraphicsDevice.SetRenderTarget(null);GraphicsDevice.Clear(WorldRenderer.SkyColor);
                GraphicsDevice.DepthStencilState=DepthStencilState.Default;GraphicsDevice.BlendState=BlendState.Opaque;
                GraphicsDevice.SamplerStates[0]=SamplerState.PointClamp;GraphicsDevice.RasterizerState=RasterizerState.CullCounterClockwise;
                FlatLighting.Apply(effect,GraphicsDevice,_atlas.Texture,result.World,camera,.5f,.5f);
                foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();mesh.Draw();}
                GraphicsDevice.SetVertexBuffer(null);
                Color[] pixels=new Color[640*480];GraphicsDevice.GetBackBufferData(pixels);
                int visible=0;foreach(var pixel in pixels)if(pixel!=WorldRenderer.SkyColor)visible++;
                if(visible<50)throw new Exception("Density chunk was not visible in its GPU probe.");
                using var image=new Texture2D(GraphicsDevice,640,480);image.SetData(pixels);
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"stream-density-{_case}.png"));image.SaveAsPng(file,640,480);
                Console.WriteLine($"DENSITY GPU {_places[_case]}: vertices={mesh.VertexCount}, estimated bytes={mesh.BufferBytes}, visible pixels={visible}; worker CPU generation/mesh, main-thread upload.");
            }
            if(ChunkMesh.LiveBufferBytes!=before)throw new Exception("Density probe leaked GPU buffers.");
            if(++_case<_places.Length)Start();
        }
    }
}
