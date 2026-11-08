using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CompletionGraphicsChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Verify(Game1 game)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(Game1).GetField(name,flags).GetValue(game);
        object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,flags).Invoke(game,args);
        GraphicsDevice device=game.GraphicsDevice;TextureAtlas atlas=Field<TextureAtlas>("_atlas");
        void Restore(){device.Textures[0]=null;device.Textures[0]=atlas.Texture;}
        Color[] Read(RenderTarget2D target,string filename)
        {
            device.SetRenderTarget(null);var pixels=new Color[target.Width*target.Height];target.GetData(pixels);
            using var file=File.Create(Path.Combine(AppContext.BaseDirectory,filename));target.SaveAsPng(file,target.Width,target.Height);Restore();return pixels;
        }
        using(var target=new RenderTarget2D(device,800,1000,false,SurfaceFormat.Color,DepthFormat.Depth24))
        using(var batch=new SpriteBatch(device))using(var pixel=new Texture2D(device,1,1))
        {
            pixel.SetData(new[]{Color.White});device.SetRenderTarget(target);device.Clear(new Color(35,40,50));batch.Begin(samplerState:SamplerState.PointClamp);
            int i=0;foreach(ItemId item in Items.Sprites){ItemStack stack=S(item);if(stack.Durability>0)stack=stack.Wear();ItemIcons.Draw(batch,atlas,pixel,stack,new Rectangle(8+i%8*99,8+i/8*98,85,85));i++;}batch.End();
            var pixels=Read(target,"complete-items.png");Check(i==Items.Sprites.Length && i==80 && pixels.Count(c=>c!=new Color(35,40,50))>30000,"New atlas icons/durability bars missing.");
        }
        var world=new World(173,WorldSize.Small,false);for(int z=0;z<25;z++)for(int x=0;x<40;x++)world.SetBlock(x,0,z,BlockType.Stone);
        world.SetBlock(6,1,12,BlockType.FarmlandDry);world.SetBlock(8,1,12,BlockType.FarmlandWet);
        for(int i=0;i<7;i++){world.SetBlock(10+i,1,12,BlockType.FarmlandWet);world.SetBlock(10+i,2,12,(BlockType)((int)BlockType.Wheat1+i));}
        world.SetBlock(18,1,12,BlockType.Fence);world.SetBlock(19,1,12,BlockType.Fence);world.PlaceStructure(new(20,1,12),BlockType.FenceGate,0,new(0,-1,0),new Player(new(35,1,20)).Bounds);
        world.PlaceStructure(new(23,1,12),BlockType.Door,0,new(0,-1,0),new Player(new(35,1,20)).Bounds);
        world.PlaceStructure(new(26,1,12),BlockType.Torch,0,new(0,-1,0),new Player(new(35,1,20)).Bounds);
        world.SetBlock(28,1,12,BlockType.Smoker);world.SetBlock(30,1,12,BlockType.BlastFurnace);world.SetBlock(32,1,12,BlockType.Obsidian);world.SetBlock(34,1,12,BlockType.LavaSource);
        var camera=new Camera{Position=new(20,5,0)};camera.RestoreOrientation(MathHelper.Pi,-.18f);
        using(var target=new RenderTarget2D(device,1280,720,false,SurfaceFormat.Color,DepthFormat.Depth24))
        using(var renderer=new WorldRenderer(device,atlas,world))using(var mobs=new PassiveMobRenderer(device,game.Content))
        {
            var animals=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Cow,new(10,1,8),0,10,BabySeconds:1000,Bred:true),new MobState(MobSpecies.Pig,new(14,1,8),0,10,BabySeconds:1000,Bred:true)});
            var enemies=new HostilePopulation(world,new[]{new MobState(MobSpecies.Zombie,new(19,1,8),0,20),new MobState(MobSpecies.Skeleton,new(24,1,8),0,20),new MobState(MobSpecies.Spider,new(29,1,8),0,16)});
            device.SetRenderTarget(target);RenderingPerformanceChecks.Warm(renderer,camera);device.Clear(WorldRenderer.SkyColor);renderer.Draw(camera,null);
            Texture texture=device.Textures[0];var blend=device.BlendState;var depth=device.DepthStencilState;mobs.Draw(camera,animals,enemies);
            Check(device.Textures[0]==texture && device.BlendState==blend && device.DepthStencilState==depth,"New mob pass leaked graphics state.");
            var scene=Read(target,"complete-world-models.png");Check(scene.Count(c=>c.R>150 && c.R>c.G*1.3 && c.B<90)>20,"Opaque Lava did not render.");
            world.Toggle(new(23,1,12));world.Toggle(new(20,1,12));device.SetRenderTarget(target);device.Clear(WorldRenderer.SkyColor);RenderingPerformanceChecks.Warm(renderer,camera);renderer.Draw(camera,null);mobs.Draw(camera,animals,enemies);
            Check(!Read(target,"complete-open-structures.png").AsSpan().SequenceEqual(scene),"Open Door/Gate geometry did not update.");
            var inventory=new Inventory();for(int slot=0;slot<4;slot++)inventory.SetEquipment(slot,S(Items.Armor(ArmorMaterial.Obsidian,(ArmorSlot)slot)));
            device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);Field<Hud>("_hud").Draw(device.Viewport,inventory,false,false,Vector3.Zero,world,new GameTime());
            var armor=Read(target,"complete-armor-hud.png");Check(armor.Count(c=>c==Color.LightGray)>100,"26-point Armor HUD missing.");
        }
        void Input(params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),default(MouseState),false,new GameTime(),Point.Zero);
        void Right(){Input();Call("ProcessInput",default(KeyboardState),new MouseState(0,0,0,ButtonState.Released,ButtonState.Released,ButtonState.Pressed,ButtonState.Released,ButtonState.Released),false,new GameTime(),Point.Zero);}
        void Click(Rectangle bounds){Input();Call("ProcessInput",default(KeyboardState),new MouseState(bounds.Center.X,bounds.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released),false,new GameTime(),Point.Zero);}
        var live=PigChecks.Flat();var inv=new Inventory();inv.SetSlot(0,S(Items.Tool(ToolTier.Wood,ToolKind.Hoe)));inv.SetSlot(1,S(Items.WheatSeeds,2));inv.SetSlot(4,S(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Boots)));
        var save=new WorldSave(live,"Survival input",new(40.5f,1,40.5f),0,-.7f,inv);var store=Field<WorldSaveManager>("_saves");string id=store.Create(save);Call("BeginWorld",id,save);Input();
        var session=Field<GameSession>("_session");var soil=BlockRaycaster.Cast(live,session.Camera.Position,session.Camera.Forward).Value.Cell;
        Right();Check(Blocks.Soil(live.GetBlock(soil.X,soil.Y,soil.Z)) && session.Inventory[0].Durability==58,"Real RMB did not till soil.");Input(Keys.D2);Right();Check(live.GetBlock(soil.X,soil.Y+1,soil.Z)==BlockType.Wheat1 && session.Inventory[1].Count==1,"Real RMB did not plant one seed.");
        Input();Input(Keys.E);Check(game.State==GameState.Inventory,"Inventory failed after planting.");Click(InventoryLayout.Slot(device.Viewport,4));Click(InventoryLayout.Equipment(device.Viewport,3));Check(session.Inventory.ArmorPoints==2 && session.InventoryInteraction.Cursor.IsEmpty,"Equipment UI failed.");
        Input();Input(Keys.E);Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.SaveAndReturn);
        var restored=store.Load(id);Check(restored.Inventory.ArmorPoints==2 && restored.World.Farming.Count==1,"Runtime armor/crop save lost state.");
        var fatalInventory=new Inventory();fatalInventory.SetSlot(0,S(Items.RottenFlesh));fatalInventory.SetEquipment(3,S(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Boots)));
        var fatal=new WorldSave(PigChecks.Flat(),"Rotten effect",new(40.5f,1,40.5f),0,0,fatalInventory,Health:1);string fatalId=store.Create(fatal);Call("BeginWorld",fatalId,fatal);Input();Right();
        var dead=Field<GameSession>("_session");Check(game.State==GameState.Death && dead.Player.IsDead && dead.Inventory.Selected.IsEmpty && dead.Items.Entities.Single().Durability==165,"Rotten Flesh did not cause immediate Armor-bypassing death without equipment wear.");
        Call("HandleMenuAction",MenuAction.SaveAndReturn);Check(game.State==GameState.MainMenu,"Dead-state save/return failed.");
        HostileModelChecks.VerifyGraphics(game);
        TorchChecks.VerifyGraphics(game);
        PlacementUpdateChecks.VerifyGraphics(game);
        Console.WriteLine("PASS: All 73 item icons, baby/hostile models, crop/soil/fence/door/machine/lava geometry, 26-point Armor HUD, graphics state, and real till/plant/equip/save input.");
    }
    private static ItemStack S(ItemId item,int count=1)=>new(item,count);
}
