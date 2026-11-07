using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using MDSM.Audio;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class AudioGraphicsChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".audio-runtime-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var game=new AudioGame(root);game.Run();
            if(game.Failure!=null)throw new InvalidOperationException("Audio runtime checks failed.",game.Failure);
            Check(game.Verified,"Audio runtime checks did not finish.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{Check(Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent),"Unsafe audio test cleanup.");if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class AudioGame : Game1
    {
        private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        private int _frame;
        public Exception Failure { get; private set; }
        public bool Verified { get; private set; }
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Private).GetValue(this);
        private object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,Private).Invoke(this,args);
        private AudioManager Audio=>Field<AudioManager>("_audio");
        private GameSettings Settings=>Field<GameSettings>("_settings");
        private GameSession Session=>Field<GameSession>("_session");
        public AudioGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Update(GameTime time) { }
        private void Pump(double dt=.1)=>Audio.Update(dt,Field<SoundEvents>("_uiSounds"),Session?.Sounds,Session?.Camera.Position??Vector3.Zero,Session?.Camera.Right??Vector3.Right,State==GameState.Playing);
        private void Input(MouseState mouse=default,params Keys[] keys)
        {Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(1d/60)),Point.Zero);Pump(1d/60);}
        private void Click(Rectangle bounds)
        {Input();Input(new MouseState(bounds.Center.X,bounds.Center.Y,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released));}
        protected override void Draw(GameTime time)
        {
            try
            {
                if(_frame++==0)
                {
                    Check(Audio.Available && Audio.LoadedClips==SoundCatalog.Groups.Sum(g=>g.Variations) && Audio.MissingClips==0,"Audio hardware unavailable or built WAVs could not all load.");
                    Settings.MasterVolume=.08f;VerifyMixer();VerifyNavigation();VerifyMiningAndCrafting();
                    Settings.MasterVolume=.08f;Audio.StopAll();
                }
                // Exercise every material/action, species/event and interaction group
                // on real MonoGame instances over rendered frames at quiet test volume.
                int group=_frame-2;
                if(group>=0 && group<SoundCatalog.Groups.Length)
                {
                    Audio.Update(.1,null,null,Vector3.Zero,Vector3.Right,true);
                    Check(Audio.Play(new(group,new(2,0,0),true),Vector3.Zero,Vector3.Right),"Representative sound did not start: "+SoundCatalog.Groups[group].Path);
                    Check(Audio.ActiveVoices<=AudioManager.MaximumVoices,"Unbounded active audio voices.");
                }
                base.Draw(time);
                if(group>=SoundCatalog.Groups.Length)
                {
                    Audio.StopAll();Check(Audio.ActiveVoices==0,"Audio voices not stopped.");
                    Console.WriteLine($"PASS: {Audio.LoadedClips} PCM WAVs loaded; all {SoundCatalog.Groups.Length} sound groups played on MonoGame; voice high-water {Audio.HighWaterVoices}/{AudioManager.MaximumVoices}; runtime volume/pan/mute, pause, UI, mining and crafting checks passed.");
                    Verified=true;Exit();
                }
            }
            catch(Exception e){Failure=e;Exit();}
        }
        private void VerifyMixer()
        {
            var texture=GraphicsDevice.Textures[0];var blend=GraphicsDevice.BlendState;var depth=GraphicsDevice.DepthStencilState;var sampler=GraphicsDevice.SamplerStates[0];
            var events=new SoundEvents();int kind=SoundCatalog.Find(SoundKind.Step);
            Audio.Update(10,null,null,default,Vector3.Right,true);Check(Audio.Play(new(kind,new(3,0,0),true),default,Vector3.Right),"Positional test sound did not start.");
            Array voices=(Array)typeof(AudioManager).GetField("_active",Private).GetValue(Audio);
            SoundEffectInstance Instance()=> (SoundEffectInstance)voices.GetValue(0).GetType().GetProperty("Instance").GetValue(voices.GetValue(0));
            var instance=Instance();Check(instance.Pan>0 && instance.Volume>0 && Math.Abs(instance.Pitch)<=.061,"Actual sound pan/volume/pitch incorrect.");
            Audio.Update(0,null,null,default,Vector3.Left,true);Check(instance.Pan<0,"Positional pan failed to follow listener orientation.");
            Settings.EffectsVolume=0;Audio.Update(0,null,null,default,Vector3.Right,true);Check(Audio.ActiveVoices==0,"Live category mute did not stop world sound.");Settings.EffectsVolume=1;
            int[] variants=(int[])typeof(AudioManager).GetField("_lastVariant",Private).GetValue(Audio);
            Audio.Update(1,null,null,default,Vector3.Right,true);Audio.Play(new(kind,default,false),default,Vector3.Right);int first=variants[kind];Audio.StopAll();
            Audio.Update(1,null,null,default,Vector3.Right,true);Audio.Play(new(kind,default,false),default,Vector3.Right);Check(variants[kind]!=first,"Consecutive audio variations repeated.");Audio.StopAll();
            events.Block(SoundKind.Break,BlockType.Stone,default);events.Local(SoundKind.Click);int before=Audio.Played;
            Audio.Update(1,null,events,default,Vector3.Right,false);Check(Audio.Played==before+1 && Audio.ActiveVoices==1,"Paused world audio not suppressed or interface feedback muted.");
            Audio.Update(0,null,null,default,Vector3.Right,false,false);Check(Audio.ActiveVoices==0,"Focus loss did not stop audio.");
            Audio.Update(10,null,null,default,Vector3.Right,true);
            foreach(var sound in SoundCatalog.Groups.Select((_,i)=>new SoundEvent(i,default,false)))Audio.Play(sound,default,Vector3.Right);
            Check(Audio.HighWaterVoices==32 && Audio.ActiveVoices<=32,"Voice budget/priority eviction failed.");Audio.StopAll();
            Settings.MasterVolume=0;events.Local(SoundKind.Click);before=Audio.Played;Audio.Update(1,events,null,default,Vector3.Right,true);
            Check(Audio.Played==before && events.Count==0,"Master mute accumulated/replayed sound events.");Settings.MasterVolume=.08f;
            for(int i=0;i<100;i++)Audio.Update(.01,null,null,default,Vector3.Right,true);
            long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)Audio.Update(.01,null,null,default,Vector3.Right,true);
            Check(GC.GetAllocatedBytesForCurrentThread()-allocated==0,"Idle mixer allocates each frame.");
            Check(GraphicsDevice.Textures[0]==texture && GraphicsDevice.BlendState==blend && GraphicsDevice.DepthStencilState==depth && GraphicsDevice.SamplerStates[0]==sampler,"Audio changed graphics state.");
        }
        private void VerifyNavigation()
        {
            Call("HandleMenuAction",MenuAction.OpenSettings);Check(State==GameState.Settings,"Integrated sound settings inaccessible.");
            Rectangle slider=SoundSettingsLayout.Slider(GraphicsDevice.Viewport,0);Click(new(slider.X+slider.Width/4,slider.Y,1,slider.Height));Check(Settings.MasterVolume is >.2f and <.3f,"Master volume slider not interactive.");
            Call("HandleMenuAction",MenuAction.Back);Check(State==GameState.MainMenu,"Main-menu origin lost.");
            var store=Field<SettingsStore>("_settingsStore");Check(Math.Abs(store.Load().MasterVolume-Settings.MasterVolume)<.0001,"Sound preference not saved.");
        }
        private void VerifyMiningAndCrafting()
        {
            var world=PigChecks.Flat();world.SetBlock(20,1,20,BlockType.Dirt);world.PlaceStructure(new(20,2,20),BlockType.Torch,0,new(0,-1,0),new Player(new(40,1,40)).Bounds);
            var save=new WorldSave(world,"Audio input",new(20.5f,1,23.5f),0,-.35f);var saves=Field<WorldSaveManager>("_saves");Call("BeginWorld",saves.Create(save),save);Input();
            int before=Audio.Played;
            var held=new MouseState(0,0,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
            for(int frame=0;frame<150 && world.GetBlock(20,1,20)!=BlockType.Air;frame++)Input(held);
            Check(world.GetBlock(20,1,20)==BlockType.Air && Audio.Played>before && Session.Items.Entities.Sum(e=>e.Count)==2,"Actual held mining/support drops did not produce audio.");
            Input();Input(default,Keys.E);Check(State==GameState.Inventory,"Inventory input failed.");
            for(int slot=0;slot<4;slot++)Session.Crafting.SetSlot(slot,new(Items.FromBlock(BlockType.WoodenPlanks),1));
            Pump();before=Audio.Played;Click(InventoryLayout.Output(GraphicsDevice.Viewport,2));Check(Session.InventoryInteraction.Cursor.Item==Items.FromBlock(BlockType.CraftingTable) && Audio.Played==before+1,"Successful crafting did not emit exactly one result sound.");
            before=Audio.Played;Click(InventoryLayout.Output(GraphicsDevice.Viewport,2));Check(Audio.Played==before,"Failed crafting produced sound.");
            Input();Input(default,Keys.E);Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.OpenSettings);
            Check(State==GameState.Settings,"Paused sound settings entry failed.");Call("HandleMenuAction",MenuAction.Back);Check(State==GameState.Paused,"Sounds resumed paused gameplay.");
            Call("HandleMenuAction",MenuAction.SaveAndReturn);Check(State==GameState.MainMenu,"Audio session save/return failed.");
        }
    }
}
