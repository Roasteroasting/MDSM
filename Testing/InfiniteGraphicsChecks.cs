using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Input;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

// Uses the actual creation menu/Game1 session/renderer/save path. Synthetic inputs,
// isolated user data, no physical-input or subjective visual-playtesting claims.
internal static class InfiniteGraphicsChecks
{
    internal static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".infinite-graphics-"+Guid.NewGuid().ToString("N"));
        try{using var game=new CheckGame(root);try{game.Run();if(!game.Passed)throw new Exception("Live Infinite graphics did not finish.");return 0;}finally{game.Drain();}}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Path.GetDirectoryName(Path.GetFullPath(root))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe graphics cleanup.");if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class CheckGame:Game1
    {
        private readonly string _root;private readonly Stopwatch _timeout=Stopwatch.StartNew();
        private readonly List<double> _frames=new();private long _last;private readonly Stopwatch _exploring=new();private double _startingX,_startingZ;private int _stage,_travel,_far;private Color[] _hidden;private long _peakBytes;private int _peakChunks;private string _savedId;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private readonly Stopwatch _teleportClock=new();
        private readonly Stopwatch _saveClock=new();
        private int _airPass;private double _airSeconds,_commandTime;private int _loadingFrames;private bool _verifyHud;
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Private).GetValue(this);
        private object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,Private).Invoke(this,args);
        private GameSession Session=>Field<GameSession>("_session");
        private WorldRenderer Renderer=>Field<WorldRenderer>("_renderer");
        internal bool Passed {get;private set;}
        internal void Drain()
        {
            var save=Field<System.Threading.Tasks.Task>("_infiniteSave");
            if(save!=null){save.GetAwaiter().GetResult();Call("CompleteInfiniteSave");}
            Session?.CloseStreaming();
        }
        internal CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {_root=root;Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Update(GameTime time){} // Deliberately no physical keyboard/mouse.
        private void Input(params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),default(MouseState),false,new GameTime(TimeSpan.Zero,TimeSpan.Zero),Point.Zero);
        private void Press(params Keys[] keys){Input();Input(keys);}
        private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        private Color[] Pixels(){var pixels=new Color[GraphicsDevice.PresentationParameters.BackBufferWidth*GraphicsDevice.PresentationParameters.BackBufferHeight];GraphicsDevice.GetBackBufferData(pixels);return pixels;}
        protected override void Draw(GameTime time)
        {
            if(_timeout.Elapsed.TotalSeconds>360)throw new Exception("Live Infinite graphics timed out at stage "+_stage+" / "+Field<MenuController>("_menu").CurrentMessage);
            var settings=Field<GameSettings>("_settings");settings.UncappedFps=_stage!=14;if(_stage==14)settings.FpsCap=_airPass==0?60:144;
            if(_stage==0)
            {
                Console.WriteLine("Live Infinite GPU adapter: "+GraphicsAdapter.DefaultAdapter.Description);
                Call("SetState",GameState.WorldCreation);Press(Keys.Tab);Press(Keys.Tab);Press(Keys.Right);Press(Keys.Right);Press(Keys.Right);
                var menu=Field<MenuController>("_menu");Require(menu.SelectedSize==WorldSize.Infinite,"Infinite cannot be selected through real creation input.");
                typeof(MenuController).GetProperty(nameof(MenuController.NameText)).SetValue(menu,"Infinite graphics");typeof(MenuController).GetProperty(nameof(MenuController.SeedText)).SetValue(menu,"173");
                typeof(MenuController).GetProperty(nameof(MenuController.SelectedCheats)).SetValue(menu,true);typeof(MenuController).GetProperty(nameof(MenuController.SelectedMode)).SetValue(menu,GameMode.Creative);
                Call("HandleMenuAction",MenuAction.CreateWorld);_stage=1;
            }
            else if(_stage==1)
            {
                Input();
                if(Session!=null){Session.AdvanceStreaming(out _);if(Environment.GetEnvironmentVariable("MDSM_TELEPORT_PROBE")=="1"&&Renderer.PrepareInitialMeshes(Session.Camera)){_stage=7;}else if(Renderer.PendingChunks==0&&Session.World.ChunkCount==Session.World.ChunkLifecycle.MaximumResident&&Session.World.ChunkLifecycle.InFlight==0)
                {Console.WriteLine($"Live Infinite initialized: loaded={Session.World.ChunkCount}, meshes={Renderer.MeshedChunks}, estimated terrain bytes={Renderer.BufferBytes}, elapsed={_timeout.Elapsed.TotalSeconds:F2}s.");Press(Keys.F1);_stage=2;}}
            }
            else if(_stage==3)Press(Keys.F2);
            else if(_stage==5)
            {
                string shot=Directory.GetFiles(Path.Combine(_root,"screenshots"),"*.png").Single();using var stream=File.OpenRead(shot);using var image=Texture2D.FromStream(GraphicsDevice,stream);
                var pixels=new Color[image.Width*image.Height];image.GetData(pixels);Require(image.Width==1280&&image.Height==720&&pixels.AsSpan().SequenceEqual(_hidden),"Live Infinite final screenshot mismatch.");
                GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=Field<TextureAtlas>("_atlas").Texture;
                var graphics=Field<GraphicsDeviceManager>("_graphics");graphics.PreferredBackBufferWidth=1920;graphics.PreferredBackBufferHeight=1080;graphics.ApplyChanges();Session.Player.SetAbsolute(new(8.5,124,8.5));Session.Camera.Position=Session.Player.EyePosition;Session.Camera.RestoreOrientation(0,-.7f);_stage=6;_last=Stopwatch.GetTimestamp();_exploring.Restart();_startingX=Session.Player.AbsolutePosition.X;_startingZ=Session.Player.AbsolutePosition.Z;
            }
            else if(_stage==6)
            {
                long now=Stopwatch.GetTimestamp();_frames.Add(Stopwatch.GetElapsedTime(_last,now).TotalMilliseconds);_last=now;
                Call("UpdateGameplay",new KeyboardState(Keys.W,Keys.LeftControl),default(MouseState),new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(Math.Min(.1,_frames[^1]/1000))),Point.Zero);
                _peakBytes=Math.Max(_peakBytes,Renderer.BufferBytes+Renderer.DynamicBufferBytes);_peakChunks=Math.Max(_peakChunks,Session.World.ChunkCount);
                Require(Session.World.ChunkCount<=625&&Renderer.ResidentChunks<=529,"Live residency grew beyond configured distances and its hysteresis ring.");
                if(++_travel>=1200&&_exploring.Elapsed.TotalSeconds>=20)
                {
                    double[] sorted=_frames.OrderDescending().ToArray();double low(int n)=>1000/sorted.Take(Math.Max(1,(int)Math.Ceiling(sorted.Length*n/1000.0))).Average();
                    Console.WriteLine($"Live exploration {_travel} frames, {_exploring.Elapsed.TotalSeconds:F1}s, displacement={Math.Sqrt(Math.Pow(Session.Player.AbsolutePosition.X-_startingX,2)+Math.Pow(Session.Player.AbsolutePosition.Z-_startingZ,2)):F1} blocks: mean={_frames.Average():F2}ms, 1% low={low(10):F1}fps, 0.1% low={low(1):F1}fps; max resident={_peakChunks}, estimated renderer bytes={_peakBytes}, managed={GC.GetTotalMemory(false)}.");
                    _stage=14;_airPass=0;StartAirFlight();
                }
            }
            else if(_stage==14)
            {
                long now=Stopwatch.GetTimestamp();double elapsed=Stopwatch.GetElapsedTime(_last,now).TotalSeconds;_last=now;_frames.Add(elapsed*1000);_airSeconds+=Math.Min(.1,elapsed);
                Call("UpdateGameplay",new KeyboardState(Keys.W,Keys.LeftControl),default(MouseState),new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(Math.Min(.1,elapsed))),Point.Zero);
                if(Session.StreamingNotice!=null)_loadingFrames++;
                Require(Session.World.ChunkCount<=625&&Renderer.ResidentChunks<=529,"Airborne residency exceeded its bound.");
                if(_exploring.Elapsed.TotalSeconds>=20)
                {
                    double distance=Math.Sqrt(Math.Pow(Session.Player.AbsolutePosition.X-_startingX,2)+Math.Pow(Session.Player.AbsolutePosition.Z-_startingZ,2));
                    double[] sorted=_frames.OrderDescending().ToArray();double low(int n)=>1000/sorted.Take(Math.Max(1,(int)Math.Ceiling(sorted.Length*n/1000.0))).Average();
                    Console.WriteLine($"Airborne Creative cap={settings.FpsCap}: distance={distance:F1}/{_airSeconds*Player.MoveSpeed*Player.CreativeFlightBoostMultiplier:F1}, loading frames={_loadingFrames}, mean={_frames.Average():F2}ms, 1% low={low(10):F1}fps, 0.1% low={low(1):F1}fps, loaded={Session.World.ChunkCount}, meshes={Renderer.MeshedChunks}, pending={Renderer.PendingChunks}.");
                    Require(distance>=_airSeconds*Player.MoveSpeed*Player.CreativeFlightBoostMultiplier*.9,"Airborne streaming paused more than 10% of requested flight.");
                    if(++_airPass<2)StartAirFlight();else _stage=7;
                }
            }
            else if(_stage==7)
            {
                if(_far==0)
                {
                    _commandTime=Session.World.Time.Seconds;
                    Press(Keys.T);Input();
                    foreach(char c in "/tp @p 12550824 128 0")Field<CommandBar>("_commandBar").Input.TextInput(c);
                    Press(Keys.Enter);_teleportClock.Restart();_stage=13;
                }
                else
                {
                double coordinate=_far==1?-12550824:1000000000000.5;
                Require(new WorldCommands().Execute(FormattableString.Invariant($"/tp @p {coordinate} 132 {coordinate}"),Session,settings).Success,"Live extreme teleport command failed.");_stage=8;
                }
            }
            else if(_stage==13)
            {
                Input();
                if(!Session.PendingTeleport.HasValue)
                {
                    Require(Session.Player.AbsolutePosition==new MDSM.Worlds.Streaming.WorldPosition(12550824,128,0),"Command-bar teleport lost exact coordinates/Y.");
                    Require(Session.World.Time.Seconds==_commandTime,"Teleport loading advanced paused command-screen simulation.");
                    Press(Keys.Escape);
                    Call("UpdateGameplay",default(KeyboardState),default(MouseState),new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(1.0/60)),Point.Zero);
                    Require(Session.Player.AbsolutePosition==new MDSM.Worlds.Streaming.WorldPosition(12550824,128,0),"Gameplay moved the teleported Creative player.");
                    Console.WriteLine($"Actual command-bar teleport: {Session.Player.AbsolutePosition}, origin={Session.World.OriginX},{Session.World.OriginZ}, camera={Session.Camera.Position}, elapsed={_teleportClock.Elapsed.TotalSeconds:F2}s.");
                    if(!Field<PresentationInput>("_presentation").HudVisible)Press(Keys.F1);
                    if(!Field<PresentationInput>("_presentation").DebugEnabled)Press(Keys.F3);
                    Field<Hud>("_hud").InvalidateDebug();_verifyHud=true;
                    _stage=8;
                }
                else if(_teleportClock.Elapsed.TotalSeconds>20)throw new Exception($"Command-bar teleport never advanced: player={Session.Player.AbsolutePosition}, pending={Session.PendingTeleport}, origin={Session.World.OriginX},{Session.World.OriginZ}, state={State}.");
            }
            else if(_stage==8)
            {
                if(Session.AdvanceStreaming(out _)&&Renderer.PrepareInitialMeshes(Session.Camera))
                {
                    Console.WriteLine($"Live extreme rendering {Session.Player.AbsolutePosition}: loaded={Session.World.ChunkCount}, meshes={Renderer.MeshedChunks}, estimate={Renderer.BufferBytes} bytes.");
                    _stage=++_far<3?7:9;
                }
            }
            else if(_stage==9)
            {
                _saveClock.Restart();
                _savedId=Session.Id;Call("HandleMenuAction",MenuAction.Save);
                Session.Inventory.SetSlot(31,new(MDSM.Inventories.Items.IronIngot,7));
                Call("HandleMenuAction",MenuAction.SaveAndReturn);_stage=10;
            }
            else if(_stage==10)
            {
                Input();if(Session!=null){base.Draw(time);return;}Require(State==GameState.MainMenu,"Infinite Save and Return failed.");
                Console.WriteLine($"Two checkpoints and Save and Return: {_saveClock.Elapsed.TotalSeconds:F2}s.");
                var save=Field<WorldSaveManager>("_saves").Load(_savedId);Require(save.ExactPosition.HasValue&&save.ExactPosition.Value.X==1000000000000.5,"Live Save and Return rounded extreme coordinates.");
                Require(save.Inventory[31].Count==7,"Save and Return reused an older in-progress autosave snapshot.");
                save.World.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();Require(ChunkMesh.LiveBufferBytes==0,"World switching retained terrain GPU buffers.");
                Call("HandleMenuAction",MenuAction.SelectWorld);Call("HandleMenuAction",MenuAction.LoadWorld);_stage=11;
            }
            else if(_stage==11)
            {
                Input();if(Session==null){base.Draw(time);return;}
                Require(Session.Player.AbsolutePosition.X==1000000000000.5&&Session.Inventory[31].Count==7&&Session.World.IsInfinite,"Actual menu reload lost the streamed session.");
                Call("HandleMenuAction",MenuAction.SaveAndReturn);_stage=12;
            }
            else if(_stage==12)
            {
                Input();if(Session!=null){base.Draw(time);return;}Require(State==GameState.MainMenu&&ChunkMesh.LiveBufferBytes==0,"Reloaded Infinite world did not clean up on return.");
                Passed=true;Console.WriteLine("PASS: Real menu creation, default-distance async streaming, moving exploration, extreme teleport/render, hidden-HUD screenshots, Save and Return, reload and GPU disposal.");Exit();return;
            }
            base.Draw(time);
            if(_verifyHud)
            {
                string[] lines=(string[])typeof(Hud).GetField("_debugLines",Private).GetValue(Field<Hud>("_hud"));
                Require(lines[0]=="X 12550824.0 Y 128.0 Z 0.0","Actual F3 coordinates did not match the teleport: "+lines[0]);_verifyHud=false;
                var precise=Session.Player.AbsolutePosition;Session.Player.SetAbsolute(precise with{X=precise.X+.25});Session.Camera.Position=Session.Player.EyePosition;
                Require(new WorldCommands().Execute("/precision legacy",Session,settings).Success,"Legacy precision rendering command failed.");
            }
            else if(settings.LegacyVisualPrecision)
            {
                Require(Math.Abs(Session.Camera.PresentationOffset.X+.25f)<.00001f&&Session.Player.AbsolutePosition.X==12550824.25,"Legacy render snapping changed physics or failed to affect the rendered camera.");
                Require(new WorldCommands().Execute("/precision modern",Session,settings).Success,"Modern precision could not be restored.");
                Console.WriteLine("PASS: Actual F3 shows exact teleport coordinates; legacy visual snapping is isolated from authoritative position.");
            }
            if(_stage==2){_hidden=Pixels();Require(_hidden.Distinct().Count()>50,"Live world rendered no textured scene.");_stage=3;}
            else if(_stage==3){Require(Pixels().AsSpan().SequenceEqual(_hidden),"F2 changed hidden-HUD Infinite terrain.");_stage=4;}
            else if(_stage==4){Require(Pixels().AsSpan().SequenceEqual(_hidden),"Frame after screenshot lost textures.");_stage=5;}
        }
        private void StartAirFlight()
        {
            var at=Session.Player.AbsolutePosition;Session.Player.SetAbsolute(at with{Y=140});Session.Camera.Position=Session.Player.EyePosition;Session.Camera.RestoreOrientation(0,-.5f);
            _startingX=at.X;_startingZ=at.Z;_airSeconds=0;_loadingFrames=0;_frames.Clear();_exploring.Restart();_last=Stopwatch.GetTimestamp();
        }
    }
}
