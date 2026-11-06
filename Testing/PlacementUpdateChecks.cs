using System;
using System.IO;
using System.Linq;
using BitOperations=System.Numerics.BitOperations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.UI;
using Vector3=Microsoft.Xna.Framework.Vector3;

namespace MDSM.Testing;

internal static class PlacementUpdateChecks
{
    private static readonly Point3[] Directions={new(-1,0,0),new(1,0,0),new(0,0,-1),new(0,0,1)};
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static readonly (string Name,Action Check)[] All={
        ("Decorative underside rejection without consumption and valid Torch attachments",Placement),
        ("Glass Pane registry, offset recipe, stacks, Creative catalog and drops",RecipeAndDrops),
        ("All 16 pane connections, shape collision/raycast, mesh UVs and internal faces",PaneShapes),
        ("Pane neighbor edits, chunk boundaries, save/load and collision cache",PaneUpdates),
        ("Door edge strips, unchanged broad faces, both hinges and all open/closed orientations",DoorUvs),
        ("150ms mining cooldown, first/held/brief input, Survival clocks and durability",Cooldown)
    };
    public static int Run()
    {
        var checks=All.Concat(new (string Name,Action Check)[]{("Existing mining/drop regression",InventoryChecks.Mining),("Existing held-model regression",PresentationChecks.ViewModels),("Existing pig combat/mining regression",PigChecks.Combat),("Existing mob combat/drop regression",MobChecks.CombatAndDrops)});
        int failed=0;foreach(var test in checks)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        return failed==0?0:1;
    }
    public static void Placement()
    {
        var world=new World(173,WorldSize.Small,false);var player=new Player(new(40,1,40));Point3 cell=new(20,1,20);
        world.SetBlock(20,0,20,BlockType.Grass);world.SetBlock(20,2,20,BlockType.Stone);
        foreach(bool creative in new[]{false,true})foreach(BlockType block in new[]{BlockType.Torch,BlockType.TallGrass,BlockType.RedFlower,BlockType.YellowFlower})
        {
            var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(block),4));int revision=world.Revision;
            var underside=new BlockHit(new(20,2,20),new(0,-1,0),1);
            Check(!BlockPlacement.TryPlace(world,inventory,underside,player.Bounds,creative) && inventory.Selected.Count==4 && world.Revision==revision,"Underside placement changed blocks/inventory.");
            Check(!world.TryPlace(cell,block,player.Bounds,new(0,1,0)),"World placement bypasses underside validation.");
            if(block==BlockType.Torch)Check(!world.PlaceStructure(cell,block,0,new(0,1,0),player.Bounds),"Structure placement permits ceiling Torches.");
            Check(BlockPlacement.TryPlace(world,inventory,new BlockHit(new(20,0,20),new(0,1,0),1),player.Bounds,creative)
                && inventory.Selected.Count==(creative?4:3),"Valid floor decoration failed.");world.SetBlock(20,1,20,BlockType.Air);
        }
        TorchChecks.PlacementAndPersistence();TorchChecks.Uvs();
    }
    public static void RecipeAndDrops()
    {
        ItemId pane=Items.FromBlock(BlockType.GlassPane),glass=Items.FromBlock(BlockType.Glass);
        Check(pane.Value==60 && Items.StackLimit(pane)==64 && Items.Obtainable.Contains(pane),"Pane IDs/catalog/stack size incorrect.");
        for(int offset=0;offset<2;offset++)
        {
            var grid=new CraftingGrid(3);for(int row=0;row<2;row++)for(int x=0;x<3;x++)grid.SetSlot((row+offset)*3+x,new(glass,2));
            Check(grid.Preview.Item==pane && grid.Preview.Count==16 && grid[3*offset].Count==2,"Pane preview consumed ingredients or missed offset.");
            var mouse=new InventoryInteraction(new Inventory());Check(grid.TryCraft(mouse) && mouse.Cursor.Count==16 && grid.TryCraft(mouse) && mouse.Cursor.Count==32 && grid.Preview.IsEmpty,"Pane recipe accounting failed.");
        }
        var small=new CraftingGrid(2);for(int i=0;i<4;i++)small.SetSlot(i,new(glass,1));Check(small.Preview.IsEmpty,"Pane recipe fit a 2x2 grid.");
        var batch=new CraftingGrid(3);for(int i=0;i<6;i++)batch.SetSlot(i,new(glass,1));
        var slots=new Inventory();slots.SetSlot(0,new(pane,60));var cursor=new InventoryInteraction(slots);cursor.Click(0,false);
        Check(!batch.TryCraft(cursor) && cursor.Cursor.Count==60 && batch[0].Count==1,"Pane output overflow consumed ingredients.");
        cursor.Click(0,false);slots.SetSlot(0,new(pane,48));cursor.Click(0,false);
        Check(batch.TryCraft(cursor) && cursor.Cursor.Count==64 && batch.Preview.IsEmpty,"Pane batch did not merge to the 64-item limit.");
        var inventory=new Inventory();Check(CreativeCatalog.Grant(inventory,pane,true)==64 && inventory.Selected.Count==64,"Pane Creative grant failed.");
        var world=PigChecks.Flat();var items=new DroppedItems(world);Point3 cell=new(20,1,20);var player=new Player(new(40,1,40));
        Check(BlockPlacement.TryPlace(world,inventory,cell,player.Bounds) && inventory.Selected.Count==63,"Pane placement did not consume one.");
        Check(new MiningController().Update(world,items,new(cell,default,1),true,1) && items.Entities.Count==0,"Survival Pane dropped an item.");
        world.SetBlock(20,1,20,BlockType.GlassPane);Check(new MiningController().Update(world,items,new(cell,default,1),true,0,creative:true) && items.Entities.Count==0,"Creative Pane dropped an item.");
    }
    internal static World PaneFixture(int mask,Point3 cell)
    {
        var world=new World(173,WorldSize.Small,false);world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.GlassPane);
        for(int i=0;i<4;i++)if((mask&(1<<i))!=0){Point3 p=cell+Directions[i];world.SetBlock(p.X,p.Y,p.Z,BlockType.Stone);}
        return world;
    }
    public static void PaneShapes()
    {
        Point3 cell=new(15,5,15);Span<BoundingBox> boxes=stackalloc BoundingBox[9];var mesh=new ChunkMeshBuilder();
        for(int mask=0;mask<16;mask++)
        {
            var world=PaneFixture(mask,cell);int count=world.BlockBoxes(cell,boxes,true);
            Check(world.PaneConnections(cell)==mask && count==1+BitOperations.PopCount((uint)mask),"Pane connection boxes/mask incorrect.");
            Check(boxes[..count].ToArray().All(b=>b.Max.Y-b.Min.Y==1 && (b.Max.X-b.Min.X<=.1251f || b.Max.Z-b.Min.Z<=.1251f)),"Pane geometry became a full cube.");
            mesh.Build(world,world.GetChunk(0,0));Rectangle tile=TextureAtlas.Source(18);
            var vertices=mesh.Vertices.Take(mesh.VertexCount).Where(v=>v.TextureCoordinate.X>tile.X/256f && v.TextureCoordinate.X<tile.Right/256f && v.TextureCoordinate.Y>tile.Y/256f && v.TextureCoordinate.Y<tile.Bottom/256f).ToArray();
            Check(vertices.Length==36+18*BitOperations.PopCount((uint)mask),"Pane mesh has internal faces or missing external faces.");
            foreach(var v in vertices)Check(v.Color.A==255 && v.TextureCoordinate.X>=tile.X/256f && v.TextureCoordinate.X<=tile.Right/256f,"Pane alpha or UV tile incorrect.");
            for(int i=0;i<vertices.Length;i+=6)
            {
                var face=vertices.Skip(i).Take(6).ToArray();float height=face.Max(v=>v.Position.Y)-face.Min(v=>v.Position.Y);
                float width=Math.Max(face.Max(v=>v.Position.X)-face.Min(v=>v.Position.X),face.Max(v=>v.Position.Z)-face.Min(v=>v.Position.Z));
                float u=(face.Max(v=>v.TextureCoordinate.X)-face.Min(v=>v.TextureCoordinate.X))*256,v=(face.Max(v=>v.TextureCoordinate.Y)-face.Min(v=>v.TextureCoordinate.Y))*256;
                Check(u==MathF.Round(width*16)-1 && v==(height==0?1:15),"Pane edges stretch the full Glass tile.");
            }
            Check(!world.IntersectsSolid(new(new(15.1f,5.2f,15.1f),new(15.2f,5.8f,15.2f))),"Pane fills empty corner space.");
            if(mask==0)Check(BlockRaycaster.Cast(world,new(15.2f,5.5f,14),Vector3.Backward)?.Cell!=cell,"Isolated Pane targets empty corner space.");
            Check(BlockRaycaster.Cast(world,new(15.5f,5.5f,14.9f),Vector3.Backward)?.Cell==cell || (mask&4)!=0,"Pane center cannot be targeted.");
            for(int side=0;side<4;side++)if((mask&(1<<side))!=0)
            {
                Point3 p=cell+Directions[side];world.SetBlock(p.X,p.Y,p.Z,BlockType.GlassPane);
                Check((world.PaneConnections(p)&(1<<(side^1)))!=0,"Adjacent panes do not connect reciprocally.");
            }
        }
        foreach(BlockType block in new[]{BlockType.Air,BlockType.WaterSource,BlockType.LavaSource,BlockType.Torch,BlockType.TallGrass,BlockType.RedFlower,BlockType.Fence,BlockType.FenceGate,BlockType.Door,BlockType.FarmlandDry})
        {var world=PaneFixture(0,cell);world.SetBlock(16,5,15,block);Check(world.PaneConnections(cell)==0,"Pane connected to incompatible block: "+block);}
        foreach(BlockType block in new[]{BlockType.Stone,BlockType.Glass,BlockType.Leaves,BlockType.GlassPane})
        {var world=PaneFixture(0,cell);world.SetBlock(16,5,15,block);Check(world.PaneConnections(cell)==2,"Pane failed to connect to compatible block: "+block);}
    }
    public static void PaneUpdates()
    {
        var world=PigChecks.Flat();Point3 cell=new(15,1,15);world.SetBlock(15,1,15,BlockType.GlassPane);
        int revision=world.GetChunk(0,0).Revision;world.SetBlock(16,1,15,BlockType.GlassPane);world.SetBlock(15,1,16,BlockType.Stone);
        Check(world.GetChunk(0,0).Revision>revision && world.PaneConnections(cell)==10,"Cross-chunk neighbor edit did not invalidate connections.");
        var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.GlassPane),64));
        using var data=new MemoryStream();WorldSerializer.Write(data,new(world,"Panes",new(40,1,40),0,0,inventory));data.Position=0;WorldSave restored=WorldSerializer.Read(data);
        Check(restored.World.PaneConnections(cell)==10 && restored.Inventory.Selected==inventory.Selected,"Pane state/inventory did not persist.");
        world.SetBlock(16,1,15,BlockType.Air);Check(world.PaneConnections(cell)==8,"Removing a neighbor left a stale pane arm.");
        var player=new Player(new(15.5f,1,13));for(int i=0;i<180;i++)player.Update(world,Vector3.Backward,false,1f/120);
        Check(player.Position.Z<15.2f,"Player passed through connected pane.");
        foreach(MobSpecies species in new[]{MobSpecies.Pig,MobSpecies.Cow,MobSpecies.Sheep,MobSpecies.Chicken})
        {
            var mob=new PassiveMob(new(species,new(15.5f,1,13),0,MobDefinitions.Get(species).MaximumHealth),1);
            for(int i=0;i<300;i++)mob.Update(world,1f/120,Vector3.Backward);
            Check(mob.Position.Z<15.5f,"Mob passed through pane: "+species);
        }
    }
    public static void DoorUvs()
    {
        var mesh=new ChunkMeshBuilder();Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(byte facing=0;facing<4;facing++)foreach(bool hinge in new[]{false,true})foreach(bool open in new[]{false,true})
        {
            var world=PigChecks.Flat();Point3 cell=new(20,1,20);Vector3 feet=facing%2==0?new(20+(hinge?.25f:.75f),1,17):new(17,1,20+(hinge?.25f:.75f));
            Check(world.PlaceStructure(cell,BlockType.Door,facing,new(0,-1,0),new Player(feet).Bounds),"Door fixture placement failed.");if(open)world.Toggle(cell);
            world.BlockBoxes(cell,boxes);bool thinX=boxes[0].Max.X-boxes[0].Min.X<.2f;
            Vector3 closedHinge=facing switch{0=>Vector3.Right,1=>Vector3.Backward,2=>Vector3.Left,_=>Vector3.Forward};if(hinge)closedHinge=-closedHinge;
            Vector3 plane=facing switch{0=>Vector3.Forward,1=>Vector3.Right,2=>Vector3.Backward,_=>Vector3.Left};
            Vector3 anchor=new Vector3(20.5f,0,20.5f)+(closedHinge+plane)*.5f;
            mesh.Build(world,world.GetChunk(1,1));
            foreach(int tileIndex in new[]{41,42})
            {
                Rectangle tile=TextureAtlas.Source(tileIndex);var vertices=mesh.Vertices.Take(mesh.VertexCount).Where(v=>v.TextureCoordinate.X>tile.X/256f && v.TextureCoordinate.X<tile.Right/256f && v.TextureCoordinate.Y>tile.Y/256f && v.TextureCoordinate.Y<tile.Bottom/256f).ToArray();
                Check(vertices.Length==36,"Door lost geometry.");
                for(int face=0;face<6;face++)
                {
                    var quad=vertices.Skip(face*6).Take(6).ToArray();float u=(quad.Max(v=>v.TextureCoordinate.X)-quad.Min(v=>v.TextureCoordinate.X))*256,v=(quad.Max(v=>v.TextureCoordinate.Y)-quad.Min(v=>v.TextureCoordinate.Y))*256;
                    bool broad=thinX?face<2:face>=4;
                    Check(broad?u==15 && v==15:face is 2 or 3?u==15 && v==2:u==2 && v==15,"Door broad/edge UV region has incorrect proportions.");
                    if(broad)Check(quad[0].TextureCoordinate==TextureAtlas.Uv(tileIndex,0,1) && quad[1].TextureCoordinate==TextureAtlas.Uv(tileIndex,1,0)
                        && quad[2].TextureCoordinate==TextureAtlas.Uv(tileIndex,1,1),"Door main front/back texture orientation changed.");
                    else if(face is not (2 or 3))
                    {
                        Vector3 center=quad.Aggregate(Vector3.Zero,(sum,p)=>sum+p.Position)/6;center.Y=0;
                        float column=quad.Min(p=>p.TextureCoordinate.X)*256-tile.X;
                        Check(column==(Vector3.Distance(center,anchor)<.2f?13.5f:.5f),"Door hinge/handle strip swapped when turning or opening.");
                    }
                }
            }
        }
    }
    public static void Cooldown()
    {
        Check(MiningController.BreakCooldownSeconds==.15,"Cooldown default changed.");var world=PigChecks.Flat();var drops=new DroppedItems(world);var inventory=new Inventory();
        Point3 a=new(20,1,20),b=new(20,1,21);BlockHit first=new(a,default,1),next=new(b,default,2);
        void Blocks(){world.SetBlock(a.X,a.Y,a.Z,BlockType.Dirt);world.SetBlock(b.X,b.Y,b.Z,BlockType.Dirt);}
        Blocks();var mining=new MiningController();mining.Reset();Check(mining.Update(world,drops,first,true,0,inventory,true),"Creative first break was delayed.");
        Check(!mining.Update(world,drops,next,true,.10f,inventory,true),"Creative ignored cooldown.");mining.Reset();
        Check(!mining.Update(world,drops,next,false,.04f,inventory,true) && !mining.Update(world,drops,next,true,0,inventory,true),"Release/reset bypassed cooldown.");
        Check(mining.Update(world,drops,next,true,.011f,inventory,true),"Held Creative did not resume after cooldown.");
        Blocks();mining=new();float seconds=MiningRules.Seconds(BlockType.Dirt,default);
        Check(mining.Update(world,drops,first,true,seconds),"Survival mining duration changed.");
        Check(!mining.Update(world,drops,next,true,.1f) && mining.Progress==0,"Survival progressed during cooldown.");
        Check(!mining.Update(world,drops,next,true,.05f) && mining.Progress<.00001f,"Cooldown time counted as mining time.");
        Check(!mining.Update(world,drops,next,true,seconds-.01f) && mining.Update(world,drops,next,true,.02f),"Survival duration/drop accounting changed after cooldown.");
        foreach(int fps in new[]{5,30,60,144,360,2000})
        {
            Blocks();mining=new();var clock=new SimulationClock();int broken=0;
            for(int frame=0;frame<fps;frame++)for(int tick=clock.Advance(1d/fps);tick>0;tick--)
            {world.SetBlock(a.X,a.Y,a.Z,BlockType.Dirt);if(mining.Update(world,drops,first,true,(float)SimulationClock.StepSeconds,inventory,true))broken++;}
            Check(broken==7,"Creative break rate differs across render FPS: "+fps+" / "+broken);
            Blocks();mining=new();int brief=0;
            for(int tick=0;tick<120;tick++){world.SetBlock(a.X,a.Y,a.Z,BlockType.Dirt);if(mining.Update(world,drops,first,tick<3,1f/120,inventory,true))brief++;}
            Check(brief==1,"Brief click buffered later breaks.");
        }
        var tool=new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Shovel),1);inventory.SetSlot(0,tool);Blocks();mining=new();
        float duration=MiningRules.Seconds(BlockType.Dirt,tool);int count=drops.Entities.Count;
        Check(mining.Update(world,drops,first,true,duration,inventory),"Tool mining duration changed.");
        Check(!mining.Update(world,drops,next,true,.1f,inventory) && inventory.Selected.Durability==tool.Durability-1 && drops.Entities.Count==count+1,"Cooldown wore tools or produced drops.");
        mining.Update(world,drops,next,true,.05f,inventory);
        Check(mining.Update(world,drops,next,true,duration+.001f,inventory) && inventory.Selected.Durability==tool.Durability-2 && drops.Entities.Count==count+2,"Tool speeds/drops changed after cooldown.");
        mining=new();world.SetBlock(a.X,a.Y,a.Z,BlockType.Bedrock);
        Check(!mining.Update(world,drops,first,true,1) && mining.CooldownRemaining==0,"Failed mining started a cooldown.");
    }
    public static void VerifyGraphics(Game1 game)
    {
        GraphicsDevice device=game.GraphicsDevice;var atlas=new TextureAtlas(game.Content);
        var originalTargets=device.GetRenderTargets();Viewport viewport=device.Viewport;Texture texture=device.Textures[0];
        using var target=new RenderTarget2D(device,1024,1024,false,SurfaceFormat.Color,DepthFormat.Depth24);
        using var batch=new SpriteBatch(device);using var pixel=new Texture2D(device,1,1);pixel.SetData(new[]{Color.White});
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        try
        {
            for(int sheet=0;sheet<2;sheet++)
            {
                device.SetRenderTarget(target);device.Clear(new Color(45,55,65));
                for(int index=0;index<16;index++)
                {
                    Point3 cell=new(20,1,20);World world;
                    if(sheet==0)
                    {
                        world=PaneFixture(index,cell);
                        for(int side=0;side<4;side++)if((index&(1<<side))!=0)
                        {Point3 neighbor=cell+Directions[side];world.SetBlock(neighbor.X,neighbor.Y,neighbor.Z,BlockType.GlassPane);}
                    }
                    else
                    {
                        world=new World(173,WorldSize.Small,false);world.SetBlock(20,0,20,BlockType.Stone);
                        byte facing=(byte)(index/4);bool hinge=index%2==1,open=index%4>=2;
                        Vector3 feet=facing%2==0?new(20+(hinge?.25f:.75f),1,17):new(17,1,20+(hinge?.25f:.75f));
                        Check(world.PlaceStructure(cell,BlockType.Door,facing,new(0,-1,0),new Player(feet).Bounds),"GPU Door fixture failed.");if(open)world.Toggle(cell);
                    }
                    for(int z=18;z<=22;z++)for(int x=18;x<=22;x++)world.SetBlock(x,0,z,BlockType.Stone);
                    device.Viewport=new Viewport(index%4*256,index/4*256,256,256);
                    world.BlockBoxes(cell,boxes);bool thinX=boxes[0].Max.X-boxes[0].Min.X<.2f;
                    Vector3 center=new(20.5f,sheet==0?1.5f:2,20.5f);
                    Vector3 offset=sheet==0?new(2,1.5f,2.4f):thinX?new(-2.2f,.9f,1.5f):new(1.5f,.9f,-2.2f);
                    var camera=new Camera{Position=center+offset};Vector3 look=-offset;
                    camera.RestoreOrientation(MathF.Atan2(look.X,-look.Z),MathF.Atan2(look.Y,new Vector2(look.X,look.Z).Length()));
                    using var renderer=new WorldRenderer(device,atlas,world);RenderingPerformanceChecks.Warm(renderer,camera);renderer.Draw(camera,sheet==0?new BlockHit(cell,default,1):null);
                }
                device.Viewport=new Viewport(0,0,1024,1024);batch.Begin(samplerState:SamplerState.PointClamp);
                for(int index=0;index<16;index++)PixelFont.Draw(batch,pixel,sheet==0?$"PANE MASK {index}":$"FACING {index/4} / {(index%4>=2?"OPEN":"CLOSED")} / H{index%2}",index%4*256+8,index/4*256+8,1,Color.White);
                batch.End();device.SetRenderTarget(null);var pixels=new Color[1024*1024];target.GetData(pixels);
                for(int index=0;index<16;index++)
                {
                    int changed=0;for(int y=index/4*256+32;y<(index/4+1)*256;y++)for(int x=index%4*256;x<(index%4+1)*256;x++)if(pixels[y*1024+x]!=new Color(45,55,65))changed++;
                    Check(changed>200,"Pane/Door GPU configuration was not rendered.");
                }
                using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,sheet==0?"glass-pane-configurations.png":"door-edge-configurations.png")))target.SaveAsPng(file,1024,1024);
                device.Textures[0]=null;device.Textures[0]=texture;
            }
            using(var held=new FirstPersonRenderer(device,atlas))
            {
                var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.GlassPane),64));held.Draw(GameState.Playing,inventory);
                inventory.SetSlot(0,new(Items.FromBlock(BlockType.Door),64));held.Draw(GameState.Playing,inventory);
            }
        }
        finally{device.SetRenderTargets(originalTargets);device.Viewport=viewport;device.Textures[0]=null;device.Textures[0]=texture;}
        Console.WriteLine("PASS: GPU pane connections/selection, 16 Door orientation/hinge/edge cases, cutout textures and held Pane/Door models.");
    }
}
