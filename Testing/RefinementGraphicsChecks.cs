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

internal static class RefinementGraphicsChecks
{
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Verify(Game1 game)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(Game1).GetField(name,flags).GetValue(game);
        object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,flags).Invoke(game,args);
        void Input(double elapsed=0,MouseState mouse=default,params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(elapsed)),Point.Zero);
        var device=game.GraphicsDevice;TextureAtlas atlas=Field<TextureAtlas>("_atlas");var menu=Field<MenuController>("_menu");var settings=Field<GameSettings>("_settings");
        void RestoreTexture(){device.Textures[0]=null;device.Textures[0]=atlas.Texture;}
        Color[] Read(RenderTarget2D target){device.SetRenderTarget(null);var pixels=new Color[target.Width*target.Height];target.GetData(pixels);RestoreTexture();return pixels;}
        using(var target=new RenderTarget2D(device,640,360,false,SurfaceFormat.Color,DepthFormat.Depth24))
        using(var sprites=new SpriteBatch(device))using(var pixel=new Texture2D(device,1,1))
        {
            pixel.SetData(new[]{Color.White});device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);sprites.Begin(samplerState:SamplerState.PointClamp);
            int i=0;foreach(ItemId item in Items.Sprites){var stack=new ItemStack(item,1);if(stack.Durability>0)stack=stack.Wear();ItemIcons.Draw(sprites,atlas,pixel,stack,new Rectangle(12+i%8*78,10+i/8*82,58,58));i++;}
            sprites.End();var pixels=Read(target);Require(pixels.Count(c=>c!=Color.CornflowerBlue)>5000,"Unified atlas icons/durability bars missing.");
            using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,"refinement-items.png")))target.SaveAsPng(file,640,360);RestoreTexture();
            foreach(ItemId item in new[]{Items.Sapling,Items.Apple,Items.Shears,Items.CookedPorkchop})
            {
                var inv=new Inventory();inv.SetSlot(0,new(item,1));device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);
                Field<FirstPersonRenderer>("_firstPerson").Draw(GameState.Playing,inv);
                Require(Read(target).Count(c=>c!=Color.CornflowerBlue)>20,"New held item invisible.");
            }
            var world=PigChecks.Flat();var camera=new Camera{Position=new(20,2,24)};camera.RestoreOrientation(0,-.1f);
            using var mobs=new PassiveMobRenderer(device,game.Content);
            var full=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Sheep,new(20,1,20),0,10)});
            var sheared=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Sheep,new(20,1,20),0,10,true,240)});
            Color[] Draw(PassiveMobPopulation population)
            {
                device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);
                Texture before=device.Textures[0];BlendState blend=device.BlendState;DepthStencilState depth=device.DepthStencilState;
                mobs.Draw(camera,population);
                Require(device.Textures[0]==before && device.BlendState==blend && device.DepthStencilState==depth,"Sheared rendering leaked device state.");return Read(target);
            }
            var normal=Draw(full);var shaved=Draw(sheared);
            Require(!normal.AsSpan().SequenceEqual(shaved) && shaved.Count(c=>c!=Color.CornflowerBlue)>200,"Sheared sheep appearance unchanged/missing.");
            using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,"sheared-sheep.png")))target.SaveAsPng(file,640,360);RestoreTexture();
        }
        // Menus use elapsed update time; changing FPS or drawing frames cannot expire messages.
        menu.Message("WORLD SAVED.",seconds:8);Input(7.999);Require(menu.CurrentMessage!="","Save notice expired early.");Input(.001);Require(menu.CurrentMessage=="","Save notice did not expire at eight seconds.");
        menu.Message("ERROR",true);Input(100);Require(menu.CurrentMessage=="ERROR","Error messages were given an unsolicited expiration.");
        Call("HandleMenuAction",MenuAction.OpenSettings);Require(game.State==GameState.Settings,"Settings could not open.");
        var viewport=device.Viewport;Rectangle bob=SettingsLayout.Bobbing(viewport);
        Input();Input(mouse:new MouseState(bob.Center.X,bob.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released));
        Require(!settings.ViewBobbing,"View bobbing toggle did not change shared settings.");
        foreach(var size in new[]{new Point(320,240),new Point(640,480),new Point(1280,720)})
        {
            viewport=new Viewport(0,0,size.X,size.Y);Rectangle a=SettingsLayout.Toggle(viewport),b=SettingsLayout.Bobbing(viewport),c=SettingsLayout.Information(viewport,0),d=SettingsLayout.Back(viewport);
            Require(a.Bottom<b.Top && b.Bottom<c.Top && c.Bottom<d.Top && d.Bottom<=size.Y,"New setting overlaps another control.");
        }
        Call("HandleMenuAction",MenuAction.Back);Input(1);Require(game.State==GameState.MainMenu,"Settings Back failed.");
        var store=Field<SettingsStore>("_settingsStore");Require(!store.Load().ViewBobbing,"View bobbing did not persist through real menu action.");settings.ViewBobbing=true;
        // Exercise the actual right-click priority and paused gameplay clocks.
        var liveWorld=PigChecks.Flat();liveWorld.SetBlock(50,1,50,BlockType.Sapling);
        var liveInventory=new Inventory();liveInventory.SetSlot(0,new(Items.Shears,1));liveInventory.SetSlot(1,new(Items.Bow,1));
        var liveSave=new WorldSave(liveWorld,"Shearing input",new(40.5f,1,40.5f),0,-.32f,liveInventory,
            Mobs:new[]{new MobState(MobSpecies.Sheep,new(40.5f,1,37.5f),0,10)});
        var saves=Field<WorldSaveManager>("_saves");string id=saves.Create(liveSave);Call("BeginWorld",id,liveSave);Input();
        var session=Field<GameSession>("_session");
        var right=new MouseState(0,0,0,ButtonState.Released,ButtonState.Released,ButtonState.Pressed,ButtonState.Released,ButtonState.Released);
        Input(mouse:right);Require(session.Mobs.Entities[0].Sheared && session.Inventory.Selected.Durability==237 && game.State==GameState.Playing,"Real RMB did not shear/prioritize the sheep.");
        int wool=session.Items.Entities.Sum(d=>d.Count);Input(mouse:right);Input();Input(mouse:right);
        Require(session.Items.Entities.Sum(d=>d.Count)==wool && session.Inventory.Selected.Durability==237,"Held/repeated input sheared twice.");
        Input();Input(keys:new[]{Keys.D2});Input(1,right);Require(session.Bow.Power==0 && session.Inventory[1].Durability==384,"Real no-ammunition RMB began drawing.");
        Call("SetState",GameState.Paused);double woolTimer=session.Mobs.Entities[0].WoolSeconds,growth=liveWorld.Vegetation.Capture().Saplings.Single().Remaining;
        Input(60);Require(session.Mobs.Entities[0].WoolSeconds==woolTimer && liveWorld.Vegetation.Capture().Saplings.Single().Remaining==growth,"Pause advanced sheep/sapling timers.");
        Call("HandleMenuAction",MenuAction.SaveAndReturn);var restored=saves.Load(id);
        Require(restored.Mobs.Single().Sheared && restored.Mobs.Single().WoolSeconds==woolTimer && restored.DroppedItems.Sum(d=>d.Count)==wool,"Real save/return changed sheared state or wool drops.");
        Input(7.999);Require(menu.CurrentMessage!="","Save/exit notice expired too early.");Input(.001);Require(menu.CurrentMessage=="","Real save/exit notice remained after eight seconds.");
        Console.WriteLine("PASS: Unified GPU atlas/new held items, visibly sheared geometry, texture/depth restoration, real bobbing Settings input/persistence, responsive layout and eight-second save message.");
    }
}
