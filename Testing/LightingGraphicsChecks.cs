using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class LightingGraphicsChecks
{
    public static int Run()
    {
        string root=Path.Combine(AppContext.BaseDirectory,".lighting-graphics-"+Guid.NewGuid().ToString("N"));
        try{using var game=new CheckGame(root);game.Run();if(!game.Verified)throw new Exception("Lighting graphics checks did not complete.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))Directory.Delete(root,true);}
    }
    private sealed class CheckGame : Game1
    {
        private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        public bool Verified { get; private set; }
        public CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Update(GameTime time) { }
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        private object Call(string method,params object[] args)=>typeof(Game1).GetMethod(method,Flags).Invoke(this,args);
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Flags).GetValue(this);
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}
            if(OperatingSystem.IsWindows())Console.WriteLine("LIGHTING GPU: "+Marshal.PtrToStringAnsi(GlGetString(0x1f01)));
            var device=GraphicsDevice;var atlas=Field<TextureAtlas>("_atlas");var settings=Field<GameSettings>("_settings");
            var menu=Field<MenuController>("_menu");
            void Click(Rectangle bounds)
            {
                Call("ProcessInput",default(KeyboardState),default(MouseState),false,new GameTime(),Point.Zero);
                Call("ProcessInput",default(KeyboardState),new MouseState(bounds.Center.X,bounds.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released),false,new GameTime(),Point.Zero);
            }
            Call("HandleMenuAction",MenuAction.OpenSettings);
            Rectangle brightness=SettingsLayout.Brightness(device.Viewport);Click(new(brightness.Right-9,brightness.Y,1,brightness.Height));
            Check(settings.Brightness>.98,"Brightness slider did not apply immediately.");
            Rectangle volume=SettingsLayout.Volume(device.Viewport,0);Click(new(volume.Left+8,volume.Y,1,volume.Height));
            Check(settings.MasterVolume<.02 && State==GameState.Settings,"Integrated sound slider failed.");
            foreach(Point size in new[]{new Point(320,240),new(1280,720)})
            {
                using var screen=new RenderTarget2D(device,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
                device.SetRenderTarget(screen);menu.Draw(device.Viewport);Save(screen,$"lighting-settings-{size.X}.png");
            }
            Call("HandleMenuAction",MenuAction.Back);Check(State==GameState.MainMenu,"Settings main-menu origin changed.");
            var room=PigChecks.Flat();
            for(int y=1;y<=5;y++)for(int z=58;z<=69;z++)for(int x=60;x<=69;x++)
                if(y==5||x==60||x==69||z==58||z==69)room.SetBlock(x,y,z,BlockType.WoodenPlanks);
            var camera=new Camera{Position=new(64.5f,2.5f,66.5f)};camera.RestoreOrientation(0,0);
            using var renderer=new WorldRenderer(device,atlas,room);RenderingPerformanceChecks.Warm(renderer,camera);
            using var target=new RenderTarget2D(device,320,240,false,SurfaceFormat.Color,DepthFormat.Depth24);
            Color[] Render(float brightnessValue,string name)
            {device.SetRenderTarget(target);device.Clear(room.Time.Sky);renderer.Brightness=brightnessValue;renderer.Draw(camera,null);return Save(target,name);}
            int effectiveBefore=room.Lighting.Effective(camera.Position),preparedBefore=renderer.PreparedChunks;
            var dark=Render(0,"lighting-dark.png");var bright=Render(1,"lighting-brightness.png");
            Check(dark[120*320+160].R>0 && bright[120*320+160].R>dark[120*320+160].R,"Darkness is black or Brightness slider has no visual effect.");
            Check(room.Lighting.Effective(camera.Position)==effectiveBefore && renderer.PreparedChunks==preparedBefore,"Brightness changed actual lighting or rebuilt meshes.");
            room.PlaceStructure(new(64,1,61),BlockType.Torch,0,new(0,-1,0),new Player(new(80,1,80)).Bounds);
            RenderingPerformanceChecks.Warm(renderer,camera);var lit=Render(1,"lighting-torch.png");
            Check(lit[120*320+160].R>bright[120*320+160].R*2,"Maximum Brightness erased Torch/dark distinction.");
            // Repeat a real readback, leave HUD out, and render again without repairing bindings in test code.
            var repeat=Render(1,"lighting-readback-repeat.png");Check(lit.AsSpan().SequenceEqual(repeat),"Readback corrupted lighting/terrain texture binding.");
            room.SetBlock(64,1,61,BlockType.Air);RenderingPerformanceChecks.Warm(renderer,camera);
            var removed=Render(1,"lighting-torch-removed.png");Check(removed.AsSpan().SequenceEqual(bright),"Removed Torch left stale GPU light.");
            camera.Position=new(64.5f,120,64.5f);camera.RestoreOrientation(0,Camera.PitchLimit);
            preparedBefore=renderer.PreparedChunks;
            room.Time.Restore(300);var sun=Render(.5f,"lighting-noon-sun.png");room.Time.Restore(900);var moon=Render(.5f,"lighting-midnight-moon.png");
            Check(sun.Count(c=>c.R>230 && c.G>200 && c.B<200)>100 && moon.Count(c=>c.R>180 && c.G>180 && c.B>200)>50,"Sun/Moon overhead pixels missing.");
            Check(renderer.PreparedChunks==preparedBefore,"Day/night transition rebuilt terrain meshes.");
            var bedWorld=PigChecks.Flat();for(byte facing=0;facing<4;facing++)bedWorld.PlaceBed(new(64+facing*4,1,64),facing,new Player(new(40,1,40)).Bounds);
            using(var beds=new WorldRenderer(device,atlas,bedWorld))for(byte facing=0;facing<4;facing++)
            {
                Point3 d=World.BedDirection(facing);Vector3 center=new(64.5f+facing*4+d.X*.5f,1.3f,64.5f+d.Z*.5f),offset=new(2,2.2f,2.8f);
                camera.Position=center+offset;Vector3 toward=-offset;camera.RestoreOrientation(MathF.Atan2(toward.X,-toward.Z),MathF.Atan2(toward.Y,new Vector2(toward.X,toward.Z).Length()));
                device.SetRenderTarget(target);RenderingPerformanceChecks.Warm(beds,camera);device.Clear(bedWorld.Time.Sky);beds.Draw(camera,null);var pixels=Save(target,$"bed-facing-{facing}.png");
                Check(pixels.Count(c=>c.R>c.G*1.6 && c.R>c.B*1.6)>100 && pixels.Count(c=>c.R>170 && c.G>160 && c.B>140)>40,"Bed blanket/pillow faces missing.");
            }
            var session=new GameSession("time",new(room,"Time",new(64.5f,1,66.5f),0,0));var store=Field<WorldSaveManager>("_saves");Call("BeginWorld",store.Create(session.Capture()),session.Capture());
            room.Time.Restore(800);Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.OpenSettings);Call("ProcessInput",default(KeyboardState),default(MouseState),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(10)),Point.Zero);
            Check(room.Time.Seconds==800,"Paused Settings advanced world time.");Call("HandleMenuAction",MenuAction.Back);Check(State==GameState.Paused,"Settings resumed gameplay.");
            Call("HandleMenuAction",MenuAction.Save);Call("ProcessInput",default(KeyboardState),default(MouseState),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(2.99)),Point.Zero);Check(menu.CurrentMessage!="","Save notice expired early.");
            Call("HandleMenuAction",MenuAction.Save);Call("ProcessInput",default(KeyboardState),default(MouseState),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(2.99)),Point.Zero);Check(menu.CurrentMessage!="","Repeated save failed to refresh notice.");
            Call("ProcessInput",default(KeyboardState),default(MouseState),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(.02)),Point.Zero);Check(menu.CurrentMessage=="","Save notice did not expire at three seconds.");
            Call("HandleMenuAction",MenuAction.SaveAndReturn);Verified=true;
            Console.WriteLine("PASS: Real DesktopGL flat darkness/Torch/brightness pixels, removal and readback state, Sun/Moon, all Bed orientations, combined Settings input/layout, pause clocks and refreshed three-second save notice.");
        }
        private Color[] Save(RenderTarget2D target,string name)
        {
            GraphicsDevice.SetRenderTarget(null);var pixels=new Color[target.Width*target.Height];target.GetData(pixels);
            using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name));target.SaveAsPng(file,target.Width,target.Height);return pixels;
        }
        [DllImport("opengl32.dll",EntryPoint="glGetString")]private static extern IntPtr GlGetString(uint name);
    }
}
