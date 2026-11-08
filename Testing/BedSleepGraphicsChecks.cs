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
using MDSM.Input;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class BedSleepGraphicsChecks
{
    public static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".bed-sleep-input-"+Guid.NewGuid().ToString("N"));
        try{using var game=new CheckGame(root);game.Run();BedSleepChecks.Check(game.Verified,"Sleep graphics checks did not complete.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent))Directory.Delete(root,true);}
    }
    private sealed class CheckGame : Game1
    {
        private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        private readonly string _root;
        public bool Verified { get; private set; }
        public CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {_root=root;Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Flags).GetValue(this);
        private object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,Flags).Invoke(this,args);
        private GameSession Session=>Field<GameSession>("_session");
        private void Input(double elapsed=0,MouseState mouse=default,Point look=default,params Keys[] keys)
            =>Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(elapsed)),look);
        private static MouseState Mouse(bool left=false,bool right=false,int x=0,int y=0)
            =>new(x,y,0,left?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,right?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released);
        private void Begin(byte facing=0,MobState[] hostiles=null,bool wall=false)
        {
            var fixture=BedSleepChecks.Session(facing,hostiles);
            if(wall)for(int y=1;y<=5;y++)for(int z=18;z<=25;z++)fixture.World.SetBlock(25,y,z,BlockType.Stone);
            var save=fixture.Capture();var store=Field<WorldSaveManager>("_saves");Call("BeginWorld",store.Create(save),save);
            Aim();Input();
        }
        private void Aim()
        {
            Vector3 toward=new Vector3(BedSleepChecks.Foot.X+.5f,BedSleepChecks.Foot.Y+9f/16,BedSleepChecks.Foot.Z+.5f)-Session.Camera.Position;
            Session.Camera.RestoreOrientation(MathF.Atan2(toward.X,-toward.Z),MathF.Atan2(toward.Y,new Vector2(toward.X,toward.Z).Length()));
        }
        private void RightClick(){Input();Input(mouse:Mouse(right:true));}
        protected override void Update(GameTime time) { }
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}
            BedSleepChecks.Check(IsActive,"Sleep input window lost focus.");
            for(byte facing=0;facing<4;facing++)
            {
                Begin(facing);Vector3 body=Session.Player.Position,eye=Session.Camera.Position;float yaw=Session.Camera.Yaw,pitch=Session.Camera.Pitch;
                RightClick();BedSleepChecks.Check(State==GameState.Sleeping && Session.Sleep.IsSleeping && IsMouseVisible && !Field<MouseLookInput>("_mouseLook").Captured,"Actual RMB did not enter the sleeping state/release the cursor.");
                Vector3 pillow=Session.Camera.Position;
                Input(.5,Mouse(left:true,right:true),new(700,900),Keys.W,Keys.Space,Keys.LeftShift,Keys.A);
                BedSleepChecks.Check(Session.Player.Position==body && Session.Camera.Position==pillow && Session.World.Time.Seconds==600 && Session.Mining.Progress==0 && Session.Inventory[0].Count==7,"Sleeping accepted gameplay input or advanced world time.");
                var viewport=GraphicsDevice.Viewport;Rectangle leave=MenuController.SleepButtonBounds(viewport);
                Input();Input(mouse:Mouse(left:true,x:leave.Center.X,y:leave.Center.Y));
                BedSleepChecks.Check(State==GameState.Playing && !IsMouseVisible && Field<MouseLookInput>("_mouseLook").Captured && Session.Player.Position==body
                    && Session.Camera.Position==eye && Session.Camera.Yaw==yaw && Session.Camera.Pitch==pitch && Session.World.Time.Seconds==600,"Leave Bed failed to restore pose/time/capture.");
                Input(.1,Mouse(left:true));BedSleepChecks.Check(Session.Mining.Progress==0 && Blocks.Bed(Session.World.GetBlock(BedSleepChecks.Foot.X,BedSleepChecks.Foot.Y,BedSleepChecks.Foot.Z)),"Held Leave click leaked into mining.");
                Input();Aim();RightClick();BedSleepChecks.Check(State==GameState.Sleeping,"Could not reenter after cancelling.");
                Keys cancel=facing==3?Keys.Enter:facing%2==0?Keys.Escape:Keys.E;
                Input(0,keys:new[]{cancel});BedSleepChecks.Check(State==GameState.Playing && Session.World.Time.Seconds!=WorldTime.Morning,"Cancel key completed sleep instead of leaving: "+cancel);
            }
            Begin();RightClick();Vector3 originalBody=Session.Player.Position;float originalYaw=Session.Sleep.ReturnYaw,originalPitch=Session.Sleep.ReturnPitch;
            RenderingPerformanceChecks.Warm(Field<WorldRenderer>("_renderer"),Session.Camera);
            Color[] initial=Render("bed-sleep-start.png");Input(2);Color[] halfway=Render("bed-sleep-halfway.png");
            Rectangle button=MenuController.SleepButtonBounds(GraphicsDevice.Viewport);int darker=0;
            for(int i=0;i<initial.Length;i++)if(!button.Contains(i%GraphicsDevice.Viewport.Width,i/GraphicsDevice.Viewport.Width) && initial[i].R>8 && halfway[i].R<initial[i].R*.65f)darker++;
            BedSleepChecks.Check(darker>1000 && Session.World.Time.Seconds==600,"Composed sleep view did not darken before changing time.");
            foreach(Point size in new[]{new Point(320,240),new(1280,720)})
            {
                using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);
                Field<MenuController>("_menu").Draw(GraphicsDevice.Viewport,Session.Name,1);
                Rectangle bounds=MenuController.SleepButtonBounds(GraphicsDevice.Viewport);
                GraphicsDevice.SetRenderTarget(null);var pixels=new Color[size.X*size.Y];target.GetData(pixels);
                BedSleepChecks.Check(pixels[0]==Color.Black && pixels[(bounds.Y+1)*size.X+bounds.X+1]==new Color(235,216,135)
                    && pixels.Skip(bounds.Y*size.X).Take(bounds.Height*size.X).Count(c=>c==Color.White)>20,"Leave Bed button was hidden by the fade or clipped.");
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"bed-sleep-ui-{size.X}.png"));target.SaveAsPng(file,size.X,size.Y);
            }
            Input(0,keys:new[]{Keys.F1});Input(0,keys:new[]{Keys.F2});Color[] expected=Render();
            string screenshot=Directory.GetFiles(Path.Combine(_root,"screenshots"),"*.png").Single();using(var file=File.OpenRead(screenshot))using(var png=Texture2D.FromStream(GraphicsDevice,file))
            {var pixels=new Color[png.Width*png.Height];png.GetData(pixels);BedSleepChecks.Check(pixels.AsSpan().SequenceEqual(expected),"Sleeping screenshot differs from the final composed frame.");}
            Input(1.99);BedSleepChecks.Check(State==GameState.Sleeping && Session.Sleep.Fade>.99f,"Sleep ended before the fade completed.");
            Input(.01);BedSleepChecks.Check(State==GameState.Playing && Session.World.Time.Seconds==WorldTime.Morning && Session.Player.Position==originalBody
                && Session.Camera.Yaw==originalYaw && Session.Camera.Pitch==originalPitch && !IsMouseVisible,"Timed wake failed to restore normal gameplay.");
            Begin();RightClick();Call("Pause");BedSleepChecks.Check(State==GameState.Paused && !Session.Sleep.IsSleeping && Session.World.Time.Seconds==600,"Focus-loss pause completed the sleep.");
            Call("SetState",GameState.Playing);Input();Aim();RightClick();string savedId=Session.Id;Vector3 savedBody=Session.Player.Position;float savedYaw=Session.Sleep.ReturnYaw,savedPitch=Session.Sleep.ReturnPitch;
            Call("SaveCurrentWorld");var saved=Field<WorldSaveManager>("_saves").Load(savedId);
            BedSleepChecks.Check(State==GameState.Paused && saved.PlayerPosition==savedBody && saved.Yaw==savedYaw && saved.Pitch==savedPitch && saved.World.Time.Seconds==600,"Saving during the fade persisted a Bed pose or skipped night.");
            Begin();RightClick();Session.Player.TakeDamage(100,false);Input();
            BedSleepChecks.Check(State==GameState.Death && !Session.Sleep.IsSleeping && Session.World.Time.Seconds==600,"Death during sleep incorrectly completed night.");
            Begin(hostiles:new[]{new MobState(MobSpecies.Zombie,new(31.3f,1,20.5f),0,20)},wall:true);
            RightClick();BedSleepChecks.Check(State==GameState.Playing && Field<string>("_notice")==BedSleepController.MonsterNotice && !Session.Sleep.IsSleeping,"Actual monster denial message is wrong or missing.");
            // Restore HUD after the earlier F1 check, then check that the whole
            // message remains visible at the smallest supported resolution.
            if(!Field<PresentationInput>("_presentation").HudVisible)Input(0,keys:new[]{Keys.F1});
            using(var target=new RenderTarget2D(GraphicsDevice,320,240,false,SurfaceFormat.Color,DepthFormat.Depth24))
            {
                GraphicsDevice.SetRenderTarget(target);base.Draw(new GameTime());GraphicsDevice.SetRenderTarget(null);var pixels=new Color[320*240];target.GetData(pixels);
                BedSleepChecks.Check(pixels.Skip(54*320).Take(22*320).Count(c=>c==Color.White)>100,"Monster status was clipped/missing at 320x240.");
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,"bed-sleep-monster-notice.png"));target.SaveAsPng(file,320,240);
            }
            Input(2.99);BedSleepChecks.Check(Field<string>("_notice")==BedSleepController.MonsterNotice,"Monster notice expired early.");
            RightClick();Input(2.99);BedSleepChecks.Check(Field<string>("_notice")==BedSleepController.MonsterNotice,"Repeated denial failed to refresh the notice.");
            Input(.02);BedSleepChecks.Check(Field<string>("_notice")==null,"Monster notice persisted longer than three seconds.");
            Verified=true;Call("HandleMenuAction",MenuAction.SaveAndReturn);Exit();
            Console.WriteLine("PASS: Actual RMB/four pillow orientations, disabled gameplay input, Leave Bed/Escape/E/Enter, capture, smooth GPU fade/button layouts, sleeping PNG, timed morning/return, focus/death/save interruption and refreshed three-second monster notice.");
        }
        private Color[] Render(string name=null)
        {
            GraphicsDevice.SetRenderTarget(null);base.Draw(new GameTime());
            var pixels=new Color[GraphicsDevice.Viewport.Width*GraphicsDevice.Viewport.Height];GraphicsDevice.GetBackBufferData(pixels);
            if(name!=null)
            {
                using var image=new Texture2D(GraphicsDevice,GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height);image.SetData(pixels);
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name));image.SaveAsPng(file,image.Width,image.Height);
            }
            return pixels;
        }
    }
}
