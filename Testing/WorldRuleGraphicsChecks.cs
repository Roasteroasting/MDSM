using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WorldRuleGraphicsChecks
{
    public static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".world-rules-input-"+Guid.NewGuid().ToString("N"));
        try{using var game=new CheckGame(root);game.Run();CommandChecks.Check(game.Verified,"World-rule input checks did not finish.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent))Directory.Delete(root,true);}
    }
    private sealed class CheckGame:Game1
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private readonly string _root;
        private int _stage,_frames;
        public bool Verified{get;private set;}
        public CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {_root=root;Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Private).GetValue(this);
        private object Call(string method,params object[] args)=>typeof(Game1).GetMethod(method,Private).Invoke(this,args);
        private GameSession Session=>Field<GameSession>("_session");
        private MenuController Menu=>Field<MenuController>("_menu");
        private GameSettings Settings=>Field<GameSettings>("_settings");
        private void Input(Point mouse=default,bool click=false,params Keys[] keys)
            =>Call("ProcessInput",new KeyboardState(keys),new MouseState(mouse.X,mouse.Y,0,click?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(.001)),Point.Zero);
        private void Click(Rectangle bounds){Input();Input(bounds.Center,true);}
        private void Press(params Keys[] keys){Input();Input(keys:keys);}
        private static void Check(bool ok,string message)=>CommandChecks.Check(ok,message);
        protected override void Update(GameTime time){}
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}
            Check(IsActive,"World rule graphics window lost focus.");
            if(_stage==0)
            {
                Call("HandleMenuAction",MenuAction.NewWorld);
                Check(!Menu.SelectedCheats && Menu.SelectedDifficulty==Difficulty.Normal && Menu.SelectedMode==GameMode.Survival,"Creation defaults depend on global settings.");
                foreach(char c in "World rules")Call("TextInput",c);
                Click(WorldCreationLayout.Option(GraphicsDevice.Viewport,0));Check(Menu.SelectedCheats,"Cheats button did not turn on.");
                Press(Keys.Left);Check(!Menu.SelectedCheats,"Cheats keyboard toggle did not turn off.");
                Click(WorldCreationLayout.Option(GraphicsDevice.Viewport,1));Click(WorldCreationLayout.Option(GraphicsDevice.Viewport,2));
                Check(Menu.SelectedDifficulty==Difficulty.Hard && Menu.SelectedMode==GameMode.Creative,"New creation options did not cycle.");
                Click(WorldCreationLayout.Field(GraphicsDevice.Viewport,2));Click(WorldCreationLayout.Field(GraphicsDevice.Viewport,2));Check(Menu.SelectedSize==WorldSize.Massive,"Size fixture failed.");
                foreach(Point size in new[]{new Point(320,240),new(640,480),new(1280,720)})Snapshot(size,"world-rules-creation");
                Click(WorldCreationLayout.Field(GraphicsDevice.Viewport,2));Check(Menu.SelectedSize==WorldSize.Small,"Small world selection failed.");
                Click(WorldCreationLayout.Button(GraphicsDevice.Viewport,0));Check(Field<object>("_creation")!=null,"Create button failed to start generation.");_stage=1;
            }
            else if(_stage==1)
            {
                Input();if(++_frames>3600)throw new InvalidOperationException("World creation did not complete.");
                if(State==GameState.Playing)
                {
                    Check(!Session.World.AllowCheats && Session.World.SavedDifficulty==Difficulty.Hard && Session.World.SavedGameMode==GameMode.Creative
                        && Session.Player.Creative && Session.Player.Settings.Difficulty==Difficulty.Hard && Session.Inventory.Selected.IsEmpty,"Actual new-world task lost selected rules or gave starter items.");
                    var store=Field<WorldSaveManager>("_saves");string lockedId=Session.Id;
                    Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.OpenSettings);
                    Check(!Menu.WorldOptionsEnabled,"Locked world's options enabled.");
                    foreach(int option in new[]{0,1})
                    {
                        Click(SettingsLayout.Choice(GraphicsDevice.Viewport,option));Press(Keys.Left);Press(Keys.Right);Press(Keys.Space);Press(Keys.Enter);
                        Check(State==GameState.Settings && Settings.Difficulty==Difficulty.Hard && Settings.GameMode==GameMode.Creative,"Disabled setting changed or keyboard activation escaped Settings.");
                    }
                    foreach(Point size in new[]{new Point(320,240),new(1280,720)})Snapshot(size,"world-rules-locked",true);
                    Click(SettingsLayout.Toggle(GraphicsDevice.Viewport));Check(Settings.UncappedFps && Settings.GameMode==GameMode.Creative,"World lock disabled unrelated graphics controls.");
                    Call("HandleMenuAction",MenuAction.Back);Call("HandleMenuAction",MenuAction.Resume);Press(Keys.T);
                    Check(State==GameState.Playing && Field<string>("_notice").Contains("DISABLED"),"T opened commands in a locked world.");
                    Call("SaveCurrentWorld");var locked=store.Load(lockedId);Check(!locked.World.AllowCheats && locked.World.SavedDifficulty==Difficulty.Hard && locked.World.SavedGameMode==GameMode.Creative,"Locked settings failed save/load.");
                    var allowed=CommandChecks.Session().Capture();allowed.World.AllowCheats=true;allowed.World.SavedDifficulty=Difficulty.Peaceful;allowed.World.SavedGameMode=GameMode.Survival;
                    string allowedId=store.Create(allowed);Call("BeginWorld",allowedId,allowed);Check(Settings.Difficulty==Difficulty.Peaceful && !Session.Player.Creative,"World switch inherited previous rules.");
                    Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.OpenSettings);Check(Menu.WorldOptionsEnabled,"Allowed world's options remained disabled.");
                    Click(SettingsLayout.Choice(GraphicsDevice.Viewport,0));Click(SettingsLayout.Choice(GraphicsDevice.Viewport,1));
                    Check(Session.World.SavedDifficulty==Difficulty.Easy && Session.Player.Creative && Session.World.SavedGameMode==GameMode.Creative,"Enabled choices failed to apply immediately.");
                    Call("SaveCurrentWorld");var changed=store.Load(allowedId);Check(changed.World.SavedDifficulty==Difficulty.Easy && changed.World.SavedGameMode==GameMode.Creative,"Enabled world rule changes failed to persist.");
                    Call("BeginWorld",lockedId,locked);Check(Settings.Difficulty==Difficulty.Hard && Settings.GameMode==GameMode.Creative && !Session.World.AllowCheats,"Returning to locked world lost its own rules.");
                    Call("HandleMenuAction",MenuAction.SaveAndReturn);Call("HandleMenuAction",MenuAction.OpenSettings);
                    Check(!Menu.WorldOptionsEnabled && Settings.GameMode==GameMode.Survival && Settings.Difficulty==Difficulty.Normal,"Main-menu settings expose/change world rules.");
                    string json=File.ReadAllText(Path.Combine(_root,"settings.json"));Check(!json.Contains("GameMode") && !json.Contains("Difficulty"),"World rules leaked into global JSON.");
                    Call("HandleMenuAction",MenuAction.Back);Verified=true;Exit();
                    Console.WriteLine("PASS: Actual new-world generation with selected locked Creative/Hard rules, responsive creation/greyed-setting renders, keyboard/mouse locks, unrelated FPS controls, T denial, enabled rule changes, per-world switching, persistence and main-menu isolation (synthetic input).");return;
                }
            }
            base.Draw(time);
        }
        private void Snapshot(Point size,string name,bool locked=false)
        {
            using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
            GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);Menu.Draw(GraphicsDevice.Viewport,locked?Session.Name:null);
            if(locked)
            {
                var bounds=SettingsLayout.Choice(GraphicsDevice.Viewport,0);GraphicsDevice.SetRenderTarget(null);var pixels=new Color[size.X*size.Y];target.GetData(pixels);
                Check(pixels.Skip(bounds.Y*size.X).Take(bounds.Height*size.X).Count(c=>c==Color.Gray)>10,"Disabled choices lack visible grey labels.");
            }
            GraphicsDevice.SetRenderTarget(null);using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"{name}-{size.X}.png"));target.SaveAsPng(file,size.X,size.Y);
        }
    }
}
