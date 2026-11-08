using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Input;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;

namespace MDSM.Testing;

// Real DesktopGL composition and Game1 routing; input is synthetic, not physical.
internal static class CommandGraphicsChecks
{
    public static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory), root=Path.Combine(parent,".command-input-"+Guid.NewGuid().ToString("N"));
        try { using var game=new CheckGame(root); game.Run(); CommandChecks.Check(game.Verified,"Command graphics checks did not complete."); return 0; }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent)) Directory.Delete(root,true); }
    }
    private sealed class CheckGame : Game1
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private readonly string _root;
        public bool Verified { get; private set; }
        public CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        { _root=root;Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content"); }
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Private).GetValue(this);
        private object Call(string method,params object[] args)=>typeof(Game1).GetMethod(method,Private).Invoke(this,args);
        private GameSession Session=>Field<GameSession>("_session");
        private CommandBar Bar=>Field<CommandBar>("_commandBar");
        private void Input(double seconds=0,MouseState mouse=default,Point look=default,params Keys[] keys)
            =>Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(seconds)),look);
        private void Text(string text) { foreach(char character in text)Call("TextInput",character); }
        private void Submit(string command) { Input();Text(command);Input(keys:new[]{Keys.Enter});Input(); }
        protected override void Update(GameTime time) { }
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}
            CommandChecks.Check(IsActive,"Command graphics window is not focused.");
            var fixture=CommandChecks.Session();var save=fixture.Capture();var store=Field<WorldSaveManager>("_saves");Call("BeginWorld",store.Create(save),save);
            RenderingPerformanceChecks.Warm(Field<WorldRenderer>("_renderer"),Session.Camera);
            Vector3 position=Session.Player.Position;float yaw=Session.Camera.Yaw,pitch=Session.Camera.Pitch;double clock=Session.World.Time.Seconds;
            Input(keys:new[]{Keys.T});Text("t");
            CommandChecks.Check(State==GameState.Command && Bar.Input.Text=="" && IsMouseVisible && !Field<MouseLookInput>("_mouseLook").Captured,"T did not open a clean command bar/release capture.");
            Input(.5,new(0,0,120,ButtonState.Pressed,ButtonState.Released,ButtonState.Pressed,ButtonState.Released,ButtonState.Released),new(100,100),Keys.W,Keys.E,Keys.Space,Keys.D2);
            CommandChecks.Check(Session.Player.Position==position && Session.Camera.Yaw==yaw && Session.Camera.Pitch==pitch && Session.World.Time.Seconds==clock && Session.Inventory.SelectedSlot==0 && Session.Mining.Progress==0,"Typing advanced gameplay or leaked controls.");
            Submit("/give @p door 3");CommandChecks.Check(Session.Inventory[0].Count==3 && State==GameState.Command,"Actual Enter route did not grant Door items.");
            Input(.1,keys:new[]{Keys.Enter});Input(.1,keys:new[]{Keys.Enter});CommandChecks.Check(Session.Inventory[0].Count==3,"Held Enter repeated a command.");
            Submit("/spawn sheep 2");CommandChecks.Check(Session.Mobs.Entities.Count==2,"Actual command routing did not spawn mobs.");
            Submit("/time set 18000");CommandChecks.Check(Session.World.Time.Seconds==900,"Actual command routing did not set time.");
            Submit("/gamemode creative");Input(.6);CommandChecks.Check(Session.Player.Creative && Session.World.SavedGameMode==GameMode.Creative
                && new SettingsStore(Path.Combine(_root,"settings.json")).Load().GameMode==GameMode.Survival,"Command mode failed to update world or leaked into global settings.");
            Submit("/help");
            foreach(Point size in new[]{new Point(320,240),new(800,600),new(1280,720)})
            {
                using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
                GraphicsDevice.SetRenderTarget(target);base.Draw(new GameTime());Rectangle bounds=CommandBar.Bounds(GraphicsDevice.Viewport);
                GraphicsDevice.SetRenderTarget(null);var pixels=new Color[size.X*size.Y];target.GetData(pixels);
                CommandChecks.Check(bounds.Left>=0 && bounds.Right<=size.X && bounds.Bottom<=size.Y && pixels.Skip(bounds.Y*size.X).Count(c=>c==Color.White)>0,"Command entry/caret clipped at "+size);
                CommandChecks.Check(pixels.Take(bounds.Y*size.X).Count(c=>c.R==215 && c.G==235 && c.B==215)>100,"Help text missing at "+size);
                using var output=File.Create(Path.Combine(AppContext.BaseDirectory,$"commands-{size.X}.png"));target.SaveAsPng(output,size.X,size.Y);
            }
            Submit("/give @p nonexistent");var expected=Render();
            CommandChecks.Check(expected.Count(c=>c.R==255 && c.G==175 && c.B==165)>50,"Command errors were not visibly rendered.");
            Input(keys:new[]{Keys.F2});expected=Render();string shot=Directory.GetFiles(Path.Combine(_root,"screenshots"),"*.png").Single();
            using(var file=File.OpenRead(shot))using(var image=Texture2D.FromStream(GraphicsDevice,file))
            {var actual=new Color[expected.Length];image.GetData(actual);CommandChecks.Check(actual.AsSpan().SequenceEqual(expected),"Screenshot did not capture composed command bar.");}
            Input();Input(keys:new[]{Keys.Escape});CommandChecks.Check(State==GameState.Playing && !IsMouseVisible && Field<MouseLookInput>("_mouseLook").Captured,"Escape failed to resume/capture.");
            CommandChecks.Check(Session.Player.Position==position && Session.Camera.Yaw==yaw && Session.Camera.Pitch==pitch,"Command bar changed player pose.");
            Input(keys:new[]{Keys.T});Call("Pause");CommandChecks.Check(State==GameState.Paused,"Deactivation failed to pause command entry.");
            Call("SetState",GameState.Playing);Input();Input(keys:new[]{Keys.T});Submit("/give @p apple 2");
            Call("SaveCurrentWorld");var loaded=store.Load(Session.Id);CommandChecks.Check(loaded.Inventory[0].Count==3 && loaded.Mobs.Length==2 && loaded.World.Time.Seconds==900,"Saving from command state lost data.");
            Call("HandleMenuAction",MenuAction.SaveAndReturn);Input();Input(keys:new[]{Keys.T});CommandChecks.Check(State==GameState.MainMenu,"T opened commands outside a world.");
            Text("/give @p stone");Call("BeginWorld",store.Create(loaded),loaded);Input();Input(keys:new[]{Keys.T});Input();Input(keys:new[]{Keys.Up});
            CommandChecks.Check(Bar.Input.Text=="","Command history leaked between worlds.");
            Submit("/kill @e type=sheep");CommandChecks.Check(Session.Mobs.Entities.Count==0 && !Session.Player.IsDead && State==GameState.Command,"Filtered kill left command interface or killed player.");
            Submit("/spawn pig 2");Submit("/spawn zombie");Submit("/kill @e");
            CommandChecks.Check(State==GameState.Death && Session.Player.IsDead && IsMouseVisible && !Field<MouseLookInput>("_mouseLook").Captured
                && Session.Mobs.Entities.Count==0 && Session.Hostiles.Entities.Count==0 && Session.Items.Entities.Count==0 && Session.Inventory.Selected.IsEmpty,"Actual Creative kill-all failed to enter death or clear entities.");
            base.Draw(new GameTime());Call("HandleMenuAction",MenuAction.Respawn);CommandChecks.Check(State==GameState.Playing && Session.Player.Health==20 && Session.Items.Entities.Count==0,"Kill-all respawn regenerated loot.");
            Input();Input(keys:new[]{Keys.T});Submit("/give @p apple 2");Submit("/kill @p");
            CommandChecks.Check(State==GameState.Death && Session.Items.Entities.Sum(i=>i.Count)==2,"Actual player-only kill lost normal death drops.");
            Call("HandleMenuAction",MenuAction.SaveAndReturn);
            Verified=true;Exit();Console.WriteLine("PASS: DesktopGL command layouts at 320/800/1280 widths, text routing, T/Enter/Escape/history, paused simulation/capture, mode persistence, commands, kill filters/Creative death/respawn/drops, error pixels, exact F2 screenshot and world switching (synthetic input).");
        }
        private Color[] Render()
        {GraphicsDevice.SetRenderTarget(null);base.Draw(new GameTime());var pixels=new Color[GraphicsDevice.Viewport.Width*GraphicsDevice.Viewport.Height];GraphicsDevice.GetBackBufferData(pixels);return pixels;}
    }
}
