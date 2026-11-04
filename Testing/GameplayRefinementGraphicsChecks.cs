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

internal static class GameplayRefinementGraphicsChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Verify(Game1 game)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(Game1).GetField(name,flags).GetValue(game);
        object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,flags).Invoke(game,args);
        GraphicsDevice device=game.GraphicsDevice;TextureAtlas atlas=Field<TextureAtlas>("_atlas");
        var world=PigChecks.Flat();var settings=new GameSettings{GameMode=GameMode.Creative};
        var session=new GameSession("graphics",new(world,"Creative",new(40,1,40),0,0));session.ApplySettings(settings);
        using var screen=new InventoryScreen(device,atlas);
        Color[] Read(RenderTarget2D target,string filename)
        {
            device.SetRenderTarget(null);var pixels=new Color[target.Width*target.Height];target.GetData(pixels);
            using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,filename)))target.SaveAsPng(file,target.Width,target.Height);
            device.Textures[0]=null;device.Textures[0]=atlas.Texture;return pixels;
        }
        foreach(Point size in new[]{new Point(320,240),new Point(640,360),new Point(1280,720)})
        {
            using var target=new RenderTarget2D(device,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
            device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);var viewport=device.Viewport;
            screen.Draw(session,default,viewport);var pixels=Read(target,$"gameplay-creative-{size.X}.png");
            Check(pixels.Distinct().Count()>50,"Creative panel/icons did not render.");
            device.SetRenderTarget(target);
            Rectangle first=InventoryLayout.BrowserSlot(viewport,0);int before=session.Inventory[0].Count;
            var click=new MouseState(first.Center.X,first.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
            screen.Update(session,click,default,viewport,true);
            Check(session.Inventory[0].Item==Items.Obtainable[0] && Enumerable.Range(0,36).Sum(i=>session.Inventory[i].Count)>=before,"Browser mouse did not grant registered item.");
            int selected=session.Inventory.SelectedSlot;
            Rectangle browser=InventoryLayout.Browser(viewport);
            screen.Update(session,new MouseState(browser.Center.X,browser.Center.Y,-2400,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released),default,viewport);
            Check(session.Inventory.SelectedSlot==selected,"Browser scrolling changed hotbar selection.");
            device.SetRenderTarget(null);
        }
        // Render crop planes and both halves of the actual atlas texture through the real terrain pass.
        for(int i=0;i<7;i++){world.SetBlock(30+i*2,0,48,BlockType.FarmlandWet);world.SetBlock(30+i*2,1,48,(BlockType)((int)BlockType.Wheat1+i));}
        world.SetBlock(45,1,48,BlockType.DoubleTallGrassLower);world.SetBlock(47,1,48,BlockType.TallGrass);
        var camera=new Camera{Position=new(38,4,37)};camera.RestoreOrientation(MathHelper.Pi,-.2f);
        using(var target=new RenderTarget2D(device,1280,720,false,SurfaceFormat.Color,DepthFormat.Depth24))
        using(var renderer=new WorldRenderer(device,atlas,world))
        {
            device.SetRenderTarget(target);RenderingPerformanceChecks.Warm(renderer,camera);device.Clear(WorldRenderer.SkyColor);renderer.Draw(camera,null);
            var pixels=Read(target,"gameplay-crop-and-grass.png");Check(pixels.Count(c=>c.G>c.R*1.2 && c.G>c.B*1.2)>300,"Crop/grass cutouts absent.");
            settings.GameMode=GameMode.Survival;
            world.SetBlock(40,2,40,BlockType.WaterSource);session.Player.Breath.Update(session.Player,world,8);
            device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);
            Field<Hud>("_hud").Draw(device.Viewport,session.Inventory,true,false,session.Player.Position,world,new GameTime(),breath:session.Player.Breath);
            var survival=Read(target,"gameplay-breath-hud.png");Check(survival.Contains(new Color(135,218,255)),"Breath bubbles not rendered.");
            device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);
            Field<Hud>("_hud").Draw(device.Viewport,session.Inventory,true,false,session.Player.Position,world,new GameTime(),creative:true,breath:session.Player.Breath);
            var creative=Read(target,"gameplay-creative-hud.png");Check(!creative.Contains(new Color(221,43,54)) && !creative.Contains(new Color(135,218,255)),"Creative rendered health/breath.");
        }
        // Exercise live Settings navigation from a paused session, then restore defaults for the rest of the suite.
        var store=Field<WorldSaveManager>("_saves");var save=new WorldSave(world,"Mode settings",new(40,1,40),0,0);string id=store.Create(save);Call("BeginWorld",id,save);Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.OpenSettings);
        var live=Field<GameSession>("_session");var options=Field<GameSettings>("_settings");
        var menu=Field<MenuController>("_menu");var viewportNow=device.Viewport;
        void Option(int index)
        {
            Rectangle button=SettingsLayout.Choice(viewportNow,index);
            MenuAction action=menu.Update(default,default,new MouseState(button.Center.X,button.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released),default,viewportNow);
            Check(action==MenuAction.SettingsChanged,"New Settings control did not report a change.");Call("HandleMenuAction",action);
        }
        Option(1);Check(options.GameMode==GameMode.Creative && live.Player.Creative && game.State==GameState.Settings,"Mode change resumed play or failed.");
        Call("SetState",GameState.Playing);
        foreach(var input in new[]
        {
            (Keys:new[]{Keys.Space},Multiplier:1f),
            (Keys:new[]{Keys.Space,Keys.LeftControl},Multiplier:Player.CreativeFlightBoostMultiplier),
            (Keys:new[]{Keys.Space,Keys.RightControl},Multiplier:Player.CreativeFlightBoostMultiplier),
            (Keys:new[]{Keys.Space,Keys.LeftControl,Keys.RightControl},Multiplier:Player.CreativeFlightBoostMultiplier),
            (Keys:new[]{Keys.Space},Multiplier:1f)
        })
        {
            float before=live.Player.Position.Y;
            // Exactly representable TimeSpan input avoids losing one 120 Hz step to tick truncation.
            for(int frame=0;frame<25;frame++)Call("ProcessInput",new KeyboardState(input.Keys),default(MouseState),false,
                new GameTime(TimeSpan.Zero,TimeSpan.FromMilliseconds(20)),Point.Zero);
            Check(Math.Abs(live.Player.Position.Y-before-Player.MoveSpeed*input.Multiplier*.5f)<.002f,
                $"Ctrl input [{string.Join(',',input.Keys)}]: moved {live.Player.Position.Y-before}, expected {Player.MoveSpeed*input.Multiplier*.5f}, state {game.State}.");
        }
        Call("SetState",GameState.Settings);
        Option(0);Check(options.Difficulty==Difficulty.Hard,"Difficulty control did not cycle.");Option(0);Check(options.Difficulty==Difficulty.Peaceful,"Difficulty did not reach Peaceful.");
        options.GameMode=GameMode.Survival;options.Difficulty=Difficulty.Normal;Call("HandleMenuAction",MenuAction.SettingsChanged);Call("HandleMenuAction",MenuAction.Back);
        Check(game.State==GameState.Paused && !live.Player.Creative,"Settings Back lost pause origin.");Call("HandleMenuAction",MenuAction.SaveAndReturn);
        Console.WriteLine("PASS: # crops, grass atlas cutouts, breath/Creative HUD, responsive Creative browser grants/scrolling and paused Settings navigation.");
    }
}
