using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;
namespace MDSM.Testing;
internal static class VisualRefinementChecks
{
    private static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    internal static int Run(){try{Cpu();using var game=new Preview();game.Run();Console.WriteLine("PASS: Underwater eye detection/fog/overlay, ten live crack stages, no remeshing, fence/gate exterior geometry and device state.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    internal static void Cpu()
    {
        for(int stage=0;stage<10;stage++){Check(MiningCracks.Stage(stage/10f)==stage,"Wrong destruction boundary.");Check(MiningCracks.Stage((stage+.999f)/10)==stage,"Wrong destruction interval.");}
        Check(MiningCracks.Stage(1)==-1&&MiningCracks.Stage(float.NaN)==-1&&MiningCracks.Stage(-1)==-1,"Finished/invalid cracks remain visible.");
        var mesh=new ChunkMeshBuilder();foreach(var block in new[]{BlockType.Fence,BlockType.FenceGate}){mesh.AppendInventoryItem(block);NoCoplanarOverlap(mesh);}
        var world=new World(173,WorldSize.Small,false);Point3 cell=new(15,5,15);world.SetGeneratedBlock(15,5,15,BlockType.Fence);
        var directions=new[]{new Point3(-1,0,0),new(1,0,0),new(0,0,-1),new(0,0,1)};
        for(int mask=0;mask<16;mask++)
        {
            for(int n=0;n<4;n++){var p=cell+directions[n];world.SetGeneratedBlock(p.X,p.Y,p.Z,(mask&(1<<n))!=0?BlockType.Fence:BlockType.Air);}
            mesh.BuildBlock(world,cell);NoCoplanarOverlap(mesh);Check(mesh.VertexCount>=36,"Fence disappeared.");
        }
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(byte facing=0;facing<4;facing++)foreach(bool open in new[]{false,true})
        {world.SetGeneratedBlock(15,5,15,BlockType.FenceGate);world.Structures[cell]=new(cell,facing,open,new(0,-1,0));mesh.BuildBlock(world,cell);NoCoplanarOverlap(mesh);Check(world.BlockBoxes(cell,boxes,true)==(open?0:1),"Gate collision changed.");}
        var camera=new Camera{Position=new(20.5f,5.8f,20.5f)};
        for(int z=19;z<=22;z++)for(int x=19;x<=22;x++)for(int y=1;y<=5;y++)world.SetGeneratedBlock(x,y,z,BlockType.WaterSource);
        Check(UnderwaterView.Submerged(world,camera),"Eye below rendered water missed.");camera.Position=new(20.5f,5.95f,20.5f);Check(!UnderwaterView.Submerged(world,camera),"Feet/torso water triggered eye effect.");
        var uv=UnderwaterView.Offset(camera);camera.Look(50,20);Check(uv!=UnderwaterView.Offset(camera),"Overlay UVs ignore camera movement.");
        world.SetGeneratedBlock(15,5,15,BlockType.Stone);mesh.BuildBlock(world,cell);Check(mesh.VertexCount==36&&mesh.Vertices.Take(36).All(v=>v.Position.X>=15&&v.Position.X<=16&&v.Position.Y>=5&&v.Position.Y<=6),"Single-block mesh includes neighbor geometry.");
        // A focused resident-chunk fixture, without generation or disk I/O: geometry
        // must subtract the Int64 origin before converting coordinates to floats.
        var far=new World(173,WorldSize.Infinite,false);far.SetOrigin(12550816,-12550832);
        var resident=new Chunk(far.OriginX/16,far.OriginZ/16);
        var chunks=(System.Collections.Generic.Dictionary<MDSM.Worlds.Streaming.ChunkAddress,Chunk>)typeof(World).GetField("_resident",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(far);
        chunks.Add(new(resident.X,resident.Z),resident);var farMesh=new ChunkMeshBuilder();
        foreach(var block in new[]{BlockType.Stone,BlockType.Torch,BlockType.GlassPane,BlockType.Fence,BlockType.FenceGate,BlockType.Door,BlockType.Bed})
        {
            resident.SetBlock(8,5,8,block);world.SetGeneratedBlock(8,5,8,block);mesh.BuildBlock(world,new(8,5,8));farMesh.BuildBlock(far,new(far.OriginX+8,5,far.OriginZ+8));
            Check(mesh.VertexCount==farMesh.VertexCount,"Large-coordinate crack shape lost faces.");
            for(int i=0;i<mesh.VertexCount;i++)Check(Vector3.Distance(mesh.Vertices[i].Position,farMesh.Vertices[i].Position)<.00001f,"Crack geometry lost floating-origin alignment.");
        }
    }
    private static void NoCoplanarOverlap(ChunkMeshBuilder mesh)
    {
        for(int i=0;i<mesh.VertexCount;i+=6)for(int j=i+6;j<mesh.VertexCount;j+=6)
        {
            Vector3 amin=mesh.Vertices[i].Position,amax=amin,bmin=mesh.Vertices[j].Position,bmax=bmin;
            for(int n=1;n<6;n++){amin=Vector3.Min(amin,mesh.Vertices[i+n].Position);amax=Vector3.Max(amax,mesh.Vertices[i+n].Position);bmin=Vector3.Min(bmin,mesh.Vertices[j+n].Position);bmax=Vector3.Max(bmax,mesh.Vertices[j+n].Position);}
            var ae=amax-amin;var be=bmax-bmin;int axis=ae.X<.00001f?0:ae.Y<.00001f?1:2;
            float At(Vector3 p,int a)=>a==0?p.X:a==1?p.Y:p.Z;
            if(At(be,axis)>.00001f||Math.Abs(At(amin,axis)-At(bmin,axis))>.00001f)continue;
            int u=(axis+1)%3,v=(axis+2)%3;
            float width=Math.Min(At(amax,u),At(bmax,u))-Math.Max(At(amin,u),At(bmin,u)),height=Math.Min(At(amax,v),At(bmax,v))-Math.Max(At(amin,v),At(bmin,v));
            Check(width<.00001f||height<.00001f,"Wood model contains overlapping coplanar face areas.");
        }
    }
    private sealed class Preview:Game
    {
        internal Preview(){_ = new GraphicsDeviceManager(this){PreferredBackBufferWidth=640,PreferredBackBufferHeight=360,SynchronizeWithVerticalRetrace=false};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            using var atlas=new TextureAtlas(Content);var world=new World(173,WorldSize.Small,false);Point3 cell=new(20,5,20);world.SetGeneratedBlock(20,5,20,BlockType.Stone);
            using var renderer=new WorldRenderer(GraphicsDevice,atlas,world);var camera=new Camera{Position=new(22,7,24)};camera.RestoreOrientation(-.46f,-.32f);RenderingPerformanceChecks.Warm(renderer,camera);
            using var target=new RenderTarget2D(GraphicsDevice,640,360,false,SurfaceFormat.Color,DepthFormat.Depth24);
            Color[] Render(float progress,Point3? mining,bool overlay=false)
            {
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(WorldRenderer.SkyColor);renderer.Draw(camera,null,miningProgress:progress,miningTarget:mining);renderer.DrawWater(camera);
                var blend=GraphicsDevice.BlendState;var depth=GraphicsDevice.DepthStencilState;var sampler=GraphicsDevice.SamplerStates[0];var texture=GraphicsDevice.Textures[0];
                if(overlay)renderer.DrawUnderwater(camera);
                Check(GraphicsDevice.BlendState==blend&&GraphicsDevice.DepthStencilState==depth&&GraphicsDevice.SamplerStates[0]==sampler&&GraphicsDevice.Textures[0]==texture,"Overlay leaked graphics state.");
                GraphicsDevice.SetRenderTarget(null);var pixels=new Color[640*360];target.GetData(pixels);GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;return pixels;
            }
            void Save(string name){using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name));target.SaveAsPng(file,640,360);}
            var clean=Render(0,null);int revision=world.GetChunk(1,1).Revision;Color[] previous=null;
            for(int stage=0;stage<10;stage++)
            {
                var pixels=Render(stage/10f,cell);Check(!pixels.SequenceEqual(clean)&&(previous==null||!previous.SequenceEqual(pixels)),"Missing or repeated destruction stage.");
                Check(world.GetChunk(1,1).Revision==revision&&renderer.RebuiltChunks==0,"Mining overlay rebuilt terrain.");previous=pixels;Save($"destroy-stage-{stage}.png");
            }
            Check(Render(0,null).SequenceEqual(clean),"Stopped mining leaves cracks or graphics-state changes.");
            var cracks=(MiningCracks)typeof(WorldRenderer).GetField("_cracks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
            var shape=(ChunkMeshBuilder)typeof(MiningCracks).GetField("_shape",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cracks);
            var lifted=(CrackVertex[])typeof(MiningCracks).GetField("_vertices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cracks);
            for(int i=0;i<shape.VertexCount;i++)Check(Math.Abs(Vector3.Distance(lifted[i].Position,shape.Vertices[i].Position)-MiningCracks.Offset)<.00001f,"Crack face has no reliable surface offset.");
            foreach(var offset in new[]{new Vector3(0,1,4),new(0,1,-4),new(4,1,0),new(-4,1,0),new(1,4,1),new(1,-4,1)})
            {
                camera.Position=new Vector3(20.5f,5.5f,20.5f)+offset;var look=Vector3.Normalize(-offset);
                camera.RestoreOrientation(MathF.Atan2(look.X,-look.Z),MathF.Asin(look.Y));
                var unbroken=Render(0,null);var broken=Render(.95f,cell);Check(!unbroken.SequenceEqual(broken),"Crack faces disappeared from a viewing direction.");
            }
            camera.RestoreOrientation(-.46f,-.32f);
            camera.Position=new(22,7,24);world.SetBlock(20,5,20,BlockType.Leaves);RenderingPerformanceChecks.Warm(renderer,camera);
            var leaves=Render(0,null);var crackedLeaves=Render(.95f,cell);int escaped=0;
            Check(!leaves.SequenceEqual(clean),"Leaf fixture did not replace the resident stone mesh.");
            Check(!leaves.SequenceEqual(crackedLeaves),"Cutout-block cracks disappeared.");
            for(int i=0;i<leaves.Length;i++)if(leaves[i]==WorldRenderer.SkyColor&&crackedLeaves[i]!=WorldRenderer.SkyColor)
            {
                // The tiny geometric offset can move a leaf edge by one raster pixel.
                // Interior transparent holes must remain untouched.
                int x=i%640,y=i/640;bool edge=false;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(x+dx>=0&&x+dx<640&&y+dy>=0&&y+dy<360)edge|=leaves[(y+dy)*640+x+dx]!=WorldRenderer.SkyColor;
                if(!edge)escaped++;
            }
            Save("leaf-cracks.png");
            Check(escaped==0,$"Cracks fill transparent leaf holes instead of respecting the source texture ({escaped} pixels).");
            GraphicsDevice.SetRenderTarget(target);
            var oldTexture=GraphicsDevice.Textures[0];GraphicsDevice.Textures[1]=atlas.Water;var oldSampler=GraphicsDevice.SamplerStates[1];var oldDepth=GraphicsDevice.DepthStencilState;var oldBlend=GraphicsDevice.BlendState;
            cracks.Draw(world,camera,cell,.5f);
            Check(GraphicsDevice.Textures[0]==oldTexture&&GraphicsDevice.Textures[1]==atlas.Water&&GraphicsDevice.SamplerStates[1]==oldSampler&&GraphicsDevice.DepthStencilState==oldDepth&&GraphicsDevice.BlendState==oldBlend,"Crack shader leaked texture/sampler/depth state.");
            GraphicsDevice.SetRenderTarget(null);GraphicsDevice.Textures[1]=null;
            world.SetBlock(20,5,20,BlockType.Fence);RenderingPerformanceChecks.Warm(renderer,camera);Render(0,null);Save("fence-standalone.png");
            foreach(var p in new[]{new Point3(19,5,20),new(21,5,20),new(20,5,19),new(20,5,21)})world.SetBlock(p.X,p.Y,p.Z,BlockType.Fence);
            RenderingPerformanceChecks.Warm(renderer,camera);Render(0,null);Save("fence-connected.png");
            foreach(var p in new[]{new Point3(19,5,20),new(21,5,20),new(20,5,19),new(20,5,21)})world.SetBlock(p.X,p.Y,p.Z,BlockType.Air);
            world.SetBlock(20,5,20,BlockType.FenceGate);
            for(byte facing=0;facing<4;facing++)foreach(bool open in new[]{false,true})
            {
                world.Structures[cell]=new(cell,facing,open,new(0,-1,0));world.DirtyDetail(cell);RenderingPerformanceChecks.Warm(renderer,camera);
                var pixels=Render(0,null);Check(pixels.Any(p=>p!=WorldRenderer.SkyColor),"Gate model disappeared.");Save($"gate-{facing}-{open}.png");
            }
            world.Structures.Remove(cell);world.SetBlock(20,5,20,BlockType.Stone);
            for(int z=17;z<=26;z++)for(int x=17;x<=26;x++)for(int y=1;y<=8;y++)if(world.GetBlock(x,y,z)==BlockType.Air)world.SetBlock(x,y,z,BlockType.WaterSource);
            camera.Position=new(22,7,24);RenderingPerformanceChecks.Warm(renderer,camera);var fog=Render(0,null);Check(camera.Underwater,"Renderer ignored submerged camera.");Save("underwater-fog.png");
            var withOverlay=Render(0,null,true);Check(!fog.SequenceEqual(withOverlay),"Water overlay missing.");Save("underwater-overlay.png");
            camera.Position=new(22,10,24);Render(0,null,true);Check(!camera.Underwater,"Fog remained after surfacing.");
            using(var effect=atlas.LightingEffect.Clone())
            {
                var probe=new Camera{Position=Vector3.Zero,Underwater=true};probe.RestoreOrientation(0,0);
                Color FogSample(float distance)
                {
                    GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.Magenta);GraphicsDevice.BlendState=BlendState.Opaque;GraphicsDevice.DepthStencilState=DepthStencilState.None;GraphicsDevice.RasterizerState=RasterizerState.CullNone;
                    var uv=TextureAtlas.Uv(Blocks.Tile(BlockType.Stone,Vector3.Up),.5f,.5f);var color=FlatLighting.Encode(Color.White,0xe0);float s=distance*.25f;
                    var vertices=new[]{new VertexPositionColorTexture(new(-s,-s,-distance),color,uv),new(new(-s,s,-distance),color,uv),new(new(s,s,-distance),color,uv),new(new(-s,-s,-distance),color,uv),new(new(s,s,-distance),color,uv),new(new(s,-s,-distance),color,uv)};
                    FlatLighting.Apply(effect,GraphicsDevice,atlas.Texture,world,probe,.5f,.5f);foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList,vertices,0,2);}
                    GraphicsDevice.SetRenderTarget(null);var pixels=new Color[640*360];target.GetData(pixels);return pixels[180*640+320];
                }
                var near=FogSample(2).ToVector3();var far=FogSample(40).ToVector3();var blue=UnderwaterView.FogColor.ToVector3();
                Check(Vector3.Distance(far,blue)<Vector3.Distance(near,blue)*.15f,$"Underwater distance fog does not obscure distant terrain while retaining nearby contrast: {near}, {far}, {blue}.");
            }
            Exit();
        }
    }
}
