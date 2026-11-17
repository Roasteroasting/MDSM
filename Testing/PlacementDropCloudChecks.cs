using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class PlacementDropCloudChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static World Flat()
    {
        var world=new World(173,WorldSize.Small,false);
        for(int z=8;z<=30;z++)for(int x=8;x<=30;x++)world.SetGeneratedBlock(x,0,z,BlockType.Grass);
        return world;
    }
    internal static void Cpu()
    {
        Replacement();Flowers();Motion();Clouds();
    }
    private static void Replacement()
    {
        foreach(var plant in new[]{BlockType.Snow,BlockType.TallGrass,BlockType.RedFlower,BlockType.YellowFlower,BlockType.Sapling,BlockType.DeadBush,BlockType.DoubleTallGrassLower})
        foreach(bool creative in new[]{false,true})foreach(var normal in new[]{new Point3(0,1,0),new(1,0,0),new(0,-1,0)})
        {
            var world=Flat();var cell=new Point3(15,1,15);if(plant==BlockType.DeadBush)world.SetGeneratedBlock(15,0,15,BlockType.Dirt);
            Check(world.SetBlock(15,1,15,plant),"Replaceable fixture failed: "+plant);
            var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.Stone),2));
            var hit=new BlockHit(cell,normal,2);var player=new Player(new(25,1,25));
            int rx=world.GetChunk(1,0).Revision,rz=world.GetChunk(0,1).Revision;
            Check(BlockPlacement.TryPlace(world,inventory,hit,player.Bounds,creative),"Could not replace "+plant);
            Check(world.GetBlock(15,1,15)==BlockType.Stone&&world.GetBlock(15,2,15)==BlockType.Air&&inventory.Selected.Count==(creative?2:1),"Replacement used adjacent voxel, orphaned grass, or wrong consumption.");
            Check(world.GetChunk(1,0).Revision>rx&&world.GetChunk(0,1).Revision>rz&&world.IntersectsSolid(new(new(15.2f,1.2f,15.2f),new(15.8f,1.8f,15.8f))),"Replacement missed neighboring meshes or collision.");
            Check(world.Lighting.Effective(new Point3(15,1,15))<=14,"Replacement corrupted light values.");
        }
        var w=Flat();w.SetBlock(15,1,15,BlockType.Snow);var mesh=new ChunkMeshBuilder();mesh.BuildBlock(w,new(15,0,15));
        Check(mesh.Vertices.Take(mesh.VertexCount).Count(v=>v.Position.Y==1)==6,"Snow hides the supporting terrain top.");
        w.SetBlock(15,2,15,BlockType.Stone);mesh.BuildBlock(w,new(15,1,15));Check(mesh.Vertices.Take(mesh.VertexCount).Count(v=>Math.Abs(v.Position.Y-1.1875f)<.0001f)>=6,"An overhead block culled the distant, exposed Snow top.");w.SetBlock(15,2,15,BlockType.Air);
        w.SetBlock(15,1,15,BlockType.Air);mesh.BuildBlock(w,new(15,0,15));Check(mesh.VertexCount==12&&mesh.Vertices.Take(mesh.VertexCount).Count(v=>v.Position.Y==1)==6,"Snow removal opened terrain or changed buried side visibility.");
        var inv=new Inventory();inv.SetSlot(0,new(Items.FromBlock(BlockType.Dirt),2));w.SetBlock(15,1,15,BlockType.TallGrass);
        Check(!BlockPlacement.TryPlace(w,inv,new BlockHit(new(15,1,15),new(0,1,0),2),new Player(new(15.5f,1,15.5f)).Bounds)&&inv.Selected.Count==2&&w.GetBlock(15,1,15)==BlockType.TallGrass,"Rejected replacement changed items or vegetation.");
        w.SetBlock(15,1,15,BlockType.Air);Check(BlockPlacement.TryPlace(w,inv,new BlockHit(new(15,0,15),new(0,1,0),2),new Player(new(25,1,25)).Bounds)&&w.GetBlock(15,1,15)==BlockType.Dirt,"Ordinary solid-face placement changed.");
        Console.WriteLine("PASS: Snow/vegetation replacement, face targets, chunk seams, collision, item safety and retained terrain top.");
    }
    private static void Flowers()
    {
        foreach(var flower in new[]{BlockType.RedFlower,BlockType.YellowFlower})
        {
            var world=Flat();var drops=new DroppedItems(world);var inventory=new Inventory();var miner=new MiningController(173);var cell=new Point3(20,1,20);
            world.SetBlock(20,1,20,flower);var hit=BlockRaycaster.Cast(world,new(20.5f,2.5f,20.5f),Vector3.Down);
            Check(hit?.Cell==cell&&miner.Update(world,drops,hit,true,.01f,inventory),"Flower raycast/mining failed.");
            Check(drops.Entities.Count==1&&drops.Entities[0].Item==Items.FromBlock(flower)&&drops.Entities[0].Count==1,"Flower did not drop itself exactly once.");
            var player=new Player(new(20.5f,1,20.5f));for(int i=0;i<120;i++)drops.Update(player,inventory,1f/120);
            Check(drops.Entities.Count==0&&inventory.Selected.Item==Items.FromBlock(flower),"Flower could not be collected.");
            Check(BlockPlacement.TryPlace(world,inventory,cell,new Player(new(25,1,25)).Bounds)&&world.GetBlock(20,1,20)==flower&&inventory.Selected.IsEmpty,"Flower could not be replanted.");
            Check(miner.Update(world,drops,new(cell,new(0,1,0),1),true,1,inventory,true)&&drops.Entities.Count==0,"Creative flower mining produced loot.");
        }
        Console.WriteLine("PASS: Both flowers raycast, break, drop themselves, collect and replant; Creative remains drop-free.");
    }
    private static void Motion()
    {
        var world=Flat();var session=new GameSession("drop",new(world,"Drop",new(20.5f,1,20.5f),0,0));session.Camera.RestoreOrientation(MathHelper.PiOver2,.2f);session.Inventory.SetSlot(0,new(Items.Stick,3));
        Check(session.DropSelected()&&session.Inventory.Selected.Count==2,"Q did not preserve inventory count.");var item=session.Items.Entities.Single();var start=item.Position;
        Check(item.HorizontalVelocity.X>2&&Math.Abs(item.HorizontalVelocity.Y)<.001f&&item.VerticalVelocity>2,"Q did not use the viewing direction/upward toss.");
        item.Update(world,.05f);Check(item.Position.X>start.X&&item.Position.Y>start.Y,"Q toss has no forward/upward arc.");
        for(int i=0;i<300;i++)item.Update(world,1f/120);
        Check(Math.Abs(item.Position.Y-1.125f)<.0001f&&!world.IntersectsSolid(new(item.Position-new Vector3(.125f),item.Position+new Vector3(.125f))),"Dropped item did not land on ground.");
        var grounded=item.Position;float phase=item.RenderPhase(1);for(int i=0;i<120;i++)item.Update(world,1f/120);
        Check(Vector3.Distance(item.Position,grounded)<.002f&&item.RenderPhase(1)!=phase&&item.VisualPosition(1).Y>item.Position.Y+.06f,"Presentation changed collision pose or stopped animating.");
        Vector3? reference=null;
        foreach(int fps in new[]{30,60,144,360})
        {
            var drop=new DroppedItem(new(Items.Coal,1,new(20.5f,5,20.5f),0,1));drop.Throw(new(2,3,0));
            for(int i=0;i<fps;i++)drop.Update(world,1f/fps);
            if(reference.HasValue)Check(Vector3.Distance(reference.Value,drop.Position)<.04f,"Drop motion depends on frame count.");else reference=drop.Position;
        }
        // Swept substeps must stop a fast toss at a wall, and use thin-block surfaces.
        world.SetBlock(22,1,20,BlockType.Stone);world.SetBlock(22,2,20,BlockType.Stone);
        var fast=new DroppedItem(new(Items.Coal,1,new(21.5f,1.5f,20.5f),0,1));fast.Throw(new(20,0,0));fast.Update(world,.1f);Check(fast.Position.X<=21.8751f,"Toss tunneled through a wall.");
        world.PlaceBuilding(new(24,1,20),BlockType.WoodenSlab,0,false,new Player(new(28,1,28)).Bounds,new(0,-1,0));
        var slab=new DroppedItem(new(Items.Coal,1,new(24.5f,3,20.5f),0,1));for(int i=0;i<180;i++)slab.Update(world,1f/120);Check(Math.Abs(slab.Position.Y-1.625f)<.0001f,"Drop ignored slab geometry.");
        using var data=new MemoryStream();WorldSerializer.Write(data,session.Capture());data.Position=0;var saved=WorldSerializer.Read(data);Check(saved.DroppedItems.Length==1&&saved.DroppedItems[0].Count==1&&saved.Inventory.Selected.Count==2,"Drop save/load lost quantities.");
        Console.WriteLine("PASS: View-directed Q arc, time-based gravity/animation, frame rates, walls, slabs, and existing save layout.");
    }
    private static void Clouds()
    {
        foreach(int seed in new[]{0,173,-382,941})
        {
            int filled=0,agree=0,total=0;var map=new bool[80*80];
            for(int z=0;z<80;z++)for(int x=0;x<80;x++)
            {
                bool v=CloudPattern.Occupied(x-40,z-40,seed);map[z*80+x]=v;if(v)filled++;
                Check(v==CloudPattern.Occupied(x-40,z-40,seed),"Cloud coverage is nondeterministic.");
                if(x>0){if(v==map[z*80+x-1])agree++;total++;}if(z>0){if(v==map[(z-1)*80+x])agree++;total++;}
            }
            Check(filled>1000&&filled<5400&&agree>total*.85,"Clouds lack broad formations and recognizable gaps.");
            int different=0;for(int z=0;z<40;z++)for(int x=0;x<40;x++)if(CloudPattern.Occupied(x,z,seed)!=CloudPattern.Occupied(x+256,z+256,seed))different++;
            Check(different>200,"Cloud pattern visibly repeats over a fixed tile.");
        }
        Check(CloudPattern.Occupied(-12550824,12550824,173)==CloudPattern.Occupied(-12550824,12550824,173),"Extreme-coordinate clouds changed.");
        Console.WriteLine("PASS: Seeded coherent cloud coverage/gaps, negative coordinates, local continuity and no fixed texture tiling period.");
    }
    internal static int Run()
    {try{Cpu();using var preview=new Preview();preview.Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        internal Preview(){_ = new GraphicsDeviceManager(this){PreferredBackBufferWidth=854,PreferredBackBufferHeight=480,SynchronizeWithVerticalRetrace=false};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            using var atlas=new TextureAtlas(Content);var world=Flat();var camera=new Camera{Position=new(23,3,25)};camera.RestoreOrientation(-.45f,-.25f);
            using var renderer=new WorldRenderer(GraphicsDevice,atlas,world);RenderingPerformanceChecks.Warm(renderer,camera);
            var drops=new DroppedItems(world);drops.TrySpawn(new(Items.FromBlock(BlockType.Stone),1),new(20.5f,1.125f,20.5f),1);drops.TrySpawn(new(Items.Coal,1),new(21.5f,1.125f,20.5f),1);drops.TrySpawn(new(Items.FromBlock(BlockType.Torch),1),new(19.5f,1.125f,20.5f),1);drops.TrySpawn(new(Items.FromBlock(BlockType.RedFlower),1),new(22.5f,1.125f,20.5f),1);
            using var items=new DroppedItemRenderer(GraphicsDevice,atlas);using var target=new RenderTarget2D(GraphicsDevice,854,480,false,SurfaceFormat.Color,DepthFormat.Depth24);
            drops.TrySpawn(new(Items.FromBlock(BlockType.Door),1),new(18.5f,1.125f,20.5f),1);
            Color[] Render(bool draw)
            {
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(world.Time.Sky);renderer.Draw(camera,null);
                var texture=GraphicsDevice.Textures[0];var blend=GraphicsDevice.BlendState;var depth=GraphicsDevice.DepthStencilState;var sampler=GraphicsDevice.SamplerStates[0];
                if(draw)items.Draw(camera,drops,world);
                Check(GraphicsDevice.Textures[0]==texture&&GraphicsDevice.BlendState==blend&&GraphicsDevice.DepthStencilState==depth&&GraphicsDevice.SamplerStates[0]==sampler,"Dropped-item pass leaked graphics state.");
                GraphicsDevice.SetRenderTarget(null);var pixels=new Color[854*480];target.GetData(pixels);GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;return pixels;
            }
            void Save(string name){using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name));target.SaveAsPng(file,854,480);}
            var clean=Render(false);
            world.SetBlock(20,1,20,BlockType.Snow);RenderingPerformanceChecks.Warm(renderer,camera);Render(false);Save("snow-supported-terrain.png");
            world.SetBlock(20,1,20,BlockType.Air);RenderingPerformanceChecks.Warm(renderer,camera);Check(clean.SequenceEqual(Render(false)),"Removing Snow changed the visible ground pixels.");
            world.SetBlock(20,1,20,BlockType.Snow);var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.Stone),1));
            Check(BlockPlacement.TryPlace(world,inventory,new BlockHit(new(20,1,20),new(0,1,0),2),new Player(new(28,1,28)).Bounds),"GPU Snow replacement failed.");RenderingPerformanceChecks.Warm(renderer,camera);var replaced=Render(false);Save("snow-replaced-terrain.png");
            Check(!replaced.Where((p,i)=>p==world.Time.Sky&&clean[i]!=world.Time.Sky).Any(),"Replacing Snow exposed sky through previously visible ground.");
            world.SetBlock(20,1,20,BlockType.Air);RenderingPerformanceChecks.Warm(renderer,camera);
            var first=Render(true);Save("dropped-classic-start.png");Check(!clean.SequenceEqual(first)&&items.BlockVertices>36&&items.SpriteVertices==12&&items.ShadowVertices>=24,"Missing dropped block, sprite, flower or projected shadow.");
            var blockVertices=(VertexPositionColorTexture[])typeof(DroppedItemRenderer).GetField("_blocks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(items);
            Check(blockVertices.Take(items.BlockVertices).All(v=>v.Position.Y>=1),"A tall dropped model clips into the ground while bobbing.");
            var shadows=(VertexPositionColorTexture[])typeof(DroppedItemRenderer).GetField("_shadows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(items);
            Check(shadows.Take(items.ShadowVertices).All(v=>Math.Abs(v.Position.Y-1.003f)<.0001f),"Shadow floats with the bobbed item instead of following ground.");
            foreach(var item in drops.Entities)for(int tick=0;tick<60;tick++)item.Update(world,1f/120);
            Check(!first.SequenceEqual(Render(true)),"Dropped models/sprites do not animate.");Save("dropped-classic-turned.png");
            world.PlaceBuilding(new(24,1,20),BlockType.WoodenSlab,0,false,new Player(new(28,1,28)).Bounds,new(0,-1,0));
            drops.TrySpawn(new(Items.Apple,1),new(24.5f,1.625f,20.5f),1);RenderingPerformanceChecks.Warm(renderer,camera);Render(true);Save("dropped-step-shadows.png");
            shadows=(VertexPositionColorTexture[])typeof(DroppedItemRenderer).GetField("_shadows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(items);
            Check(shadows.Take(items.ShadowVertices).Any(v=>Math.Abs(v.Position.Y-1.503f)<.0001f),"Shadow missed the real slab top.");
            // Render new connected cloud coverage with the same snow artwork.
            camera.Position=new(0,134,0);camera.RestoreOrientation(0,.1f);using var sky=new SkyRenderer(GraphicsDevice,atlas.Texture);
            GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(world.Time.Sky);sky.Draw(camera,world.Time,173,0);GraphicsDevice.SetRenderTarget(null);Save("clouds-connected.png");
            camera.Position=new(4,200,74);camera.RestoreOrientation(0,-.7f);
            GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(world.Time.Sky);sky.Draw(camera,world.Time,173,0);GraphicsDevice.SetRenderTarget(null);Save("clouds-overhead.png");
            var snow=(Texture2D)typeof(SkyRenderer).GetField("_snow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sky);var actual=new Color[256];snow.GetData(actual);var expected=new Color[256];atlas.Texture.GetData(0,TextureAtlas.Source(Blocks.Get(BlockType.Snow).SideTile),expected,0,256);
            Check(actual.SequenceEqual(expected),"Cloud snow artwork changed.");var clouds=(CuboidModelBuilder)typeof(SkyRenderer).GetField("_clouds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sky);Check(clouds.Count>0&&clouds.Count<16000,"Cloud mesh exceeded its local complexity budget.");
            GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            Console.WriteLine($"PASS: GPU rotating block/sprite drops, ground shadows, graphics state, and {clouds.Count} cached cloud vertices using exact Snow pixels. Previews saved.");Exit();
        }
    }
}
