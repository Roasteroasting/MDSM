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
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CollisionGraphicsChecks
{
    public static int Run(bool diagnose = false)
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".collision-graphics-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var game = new CollisionGame(root, diagnose);
            game.Run();
            if (game.Failure != null) throw new InvalidOperationException("Scripted collision check failed.", game.Failure);
            if (!game.Verified) throw new InvalidOperationException("Scripted collision check did not finish.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped test directory.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class CollisionGame : Game1
    {
        private readonly bool _diagnose;
        private int _frame;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public Exception Failure { get; private set; }
        public bool Verified { get; private set; }
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private object Call(string name, params object[] args) => typeof(Game1).GetMethod(name, Private).Invoke(this, args);
        public CollisionGame(string root, bool diagnose) : base(new WorldSaveManager(Path.Combine(root, "Worlds")), new SettingsStore(Path.Combine(root, "settings.json")))
        { _diagnose = diagnose; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content"); }
        protected override void Update(GameTime time) { }
        private void Input(bool right = false, params Keys[] keys) => Call("ProcessInput", new KeyboardState(keys),
            new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released),
            false, new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / 60)), Point.Zero);
        protected override void Draw(GameTime time)
        {
            try
            {
                if (_frame++ == 0)
                {
                    Field<GameSettings>("_settings").GameMode = GameMode.Creative;
                    var world = new World(173, WorldSize.Small, false);
                    world.SavedGameMode=GameMode.Creative;
                    for (int z = 16; z < 25; z++) for (int x = 16; x < 25; x++) world.SetBlock(x, 0, z, BlockType.Stone);
                    Point3 door = new(20, 1, 20);
                    world.PlaceStructure(door, BlockType.Door, 0, new(0, -1, 0), new Player(new(24, 1, 24)).Bounds);
                    world.Toggle(door);
                    var save = new WorldSave(world, "Collision regression", new(20.35f, 1, 20.14f), MathHelper.PiOver2, 0);
                    string id = Field<WorldSaveManager>("_saves").Create(save);
                    Call("BeginWorld", id, save);
                    Input();
                    var session = Field<GameSession>("_session");
                    if (world.IntersectsSolid(session.Player.Bounds)) throw new InvalidOperationException("Open Door fixture intersects player.");
                    Input(true);
                    if (world.Structure(door).Open) throw new InvalidOperationException("Actual RMB did not close Door.");
                    Vector3 before = session.Player.Position;
                    Input(false, Keys.Space);
                    Vector3 delta = session.Player.Position - before;
                    Console.WriteLine($"Actual Creative RMB-close/Space: {before} -> {session.Player.Position}, delta={delta}, overlapping={world.IntersectsSolid(session.Player.Bounds)}, yaw={session.Camera.Yaw}, pitch={session.Camera.Pitch}");
                    if (!_diagnose && (delta.Y < -.001f || world.IntersectsSolid(session.Player.Bounds))) throw new InvalidOperationException("Door closing still corrupts Creative ascent.");
                    if (!_diagnose)
                    {
                        Vector3 start=session.Player.Position;
                        for(int i=0;i<60;i++)Input(false,Keys.W,Keys.Space);
                        Vector3 travel=session.Player.Position-start;
                        if(Math.Abs(travel.X-Player.MoveSpeed)>.08f || Math.Abs(travel.Y-Player.MoveSpeed)>.08f || Math.Abs(travel.Z)>.001f)
                            throw new InvalidOperationException("W/Space axes or flight speed corrupted after Door recovery.");
                        for(int i=0;i<60;i++)Input(false,Keys.S,Keys.LeftShift);
                        if(Vector3.Distance(session.Player.Position,start)>.1f || world.IntersectsSolid(session.Player.Bounds))
                            throw new InvalidOperationException("S/Shift return flight corrupted after Door recovery.");
                        if(session.Camera.Yaw!=MathHelper.PiOver2 || session.Camera.Pitch!=0)throw new InvalidOperationException("Collision changed camera orientation.");
                        Console.WriteLine("PASS: Actual Creative RMB-close/Space, W/S, ascent/descent, speed and camera orientation after recovery.");
                        VerifyMobPlacement(MobSpecies.Cow);
                        VerifyMobPlacement(MobSpecies.Zombie);
                        VerifyHeldJump();
                        VerifyArmorEquip();
                        VerifyIndustrialStations();
                        VerifyElectricalStations();
                        VerifyStreamedLiveInterface();
                        VerifyLiveInterfaces();
                    }
                }
                base.Draw(time);
                if (_frame > 1) { Verified = true; Exit(); }
            }
            catch (Exception e) { Failure = e; Exit(); }
        }

        private void VerifyStreamedLiveInterface()
        {
            var previous=Field<GameSession>("_session");var restore=previous.Capture();
            var saves=Field<WorldSaveManager>("_saves");string id=Guid.NewGuid().ToString("N");
            string directory=Path.Combine(saves.RootDirectory,id,"regions");
            using(var store=new ChunkRegionStore(directory))
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
                {
                    var bytes=new byte[Chunk.BlockCount];Array.Fill(bytes,(byte)BlockType.Stone,0,256);
                    var address=new MDSM.Worlds.Streaming.ChunkAddress(x,z);
                    store.WriteAsync(address,new MDSM.Worlds.Streaming.ChunkSnapshot(address,MDSM.Worlds.Streaming.BetaDensityGenerator.Version,bytes).Encode()).GetAwaiter().GetResult();
                }
            var world=World.OpenStreamed(173,directory,loaded:3,render:1,simulation:1);
            try
            {
                world.SavedGameMode=GameMode.Survival;world.SavedDifficulty=Difficulty.Peaceful;world.Time.Restore(200);
                Call("BeginWorld",id,new WorldSave(world,"Streamed live UI",new(8.5f,1,8.5f),0,0));
                Call("SetState",GameState.Inventory);
                var timer=System.Diagnostics.Stopwatch.StartNew();
                bool Ready(){for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)if(!world.IsLoaded(new(x*16,1,z*16)))return false;return true;}
                while(!Ready())
                {
                    Input();if(world.ChunkLifecycle.Error is Exception error)throw error;
                    if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException("Inventory stopped streamed chunk publication");
                    System.Threading.Thread.Sleep(1);
                }
                var session=Field<GameSession>("_session");
                var panel=new Point3(14,1,8);var solar=new Point3(15,1,8);
                world.SetBlock(panel.X,panel.Y,panel.Z,BlockType.AccessPanel);
                world.SetBlock(solar.X,solar.Y,solar.Z,BlockType.SolarPanel);
                world.SetBlock(16,1,8,BlockType.CopperWire);
                if(!session.TryOpenContainer(new(panel,default,1)))throw new InvalidOperationException("Could not monitor streamed panel");
                for(int i=0;i<20;i++)Input();double before=world.Electricity.Statistics(panel).Production;
                for(int i=0;i<60;i++)Input();
                if(State!=GameState.Inventory||world.Electricity.Statistics(panel).Production<=before)
                    throw new InvalidOperationException("Streamed panel wattage froze in its interface");
                if((ElectricalGeometry.Connections(world,new(16,1,8))&1)==0)throw new InvalidOperationException("Solar seam cable connection disappeared");
                Console.WriteLine("PASS: Open Infinite inventory publishes streamed chunks and advances cross-seam solar monitoring with its panel open.");
            }
            finally{Call("BeginWorld",previous.Id,restore);}
        }

        private void VerifyLiveInterfaces()
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            var world=PigChecks.Flat();world.Time.Restore(200);world.SavedGameMode=GameMode.Survival;
            var generator=new Point3(18,2,17);var solar=new Point3(19,2,17);var target=new Point3(20,2,17);
            world.SetBlock(generator.X,generator.Y,generator.Z,BlockType.CoalGenerator);
            world.Containers.Get(generator).SetSlot(0,new(Items.Coal,10));
            world.SetBlock(solar.X,solar.Y,solar.Z,BlockType.SolarPanel);
            world.SetBlock(19,2,18,BlockType.AccessPanel);
            var save=new WorldSave(world,"Live interfaces",new(20.5f,1,20.5f),0,0);
            string id=Field<WorldSaveManager>("_saves").Create(save);Call("BeginWorld",id,save);
            var session=Field<GameSession>("_session");session.Player.Settings.Difficulty=Difficulty.Peaceful;
            var modes=new[]{BlockType.Air,BlockType.CraftingTable,BlockType.MetalworkingBench,BlockType.Chest,
                BlockType.Furnace,BlockType.Smoker,BlockType.BlastFurnace,BlockType.BrickKiln,
                BlockType.AccessPanel,BlockType.OutputPanel,BlockType.CoalGenerator};
            foreach(var block in modes)
            {
                world.Containers.Detach(target);world.SetBlock(target.X,target.Y,target.Z,block);
                Input();if(block==BlockType.Air)Input(false,Keys.E);else Input(true);
                Check(State==GameState.Inventory,"Could not open live interface "+block);
                double clock=world.Time.Seconds,weather=world.Weather.MotionSeconds,power=world.Electricity.SolarOutput(solar);
                Vector3 position=session.Player.Position;float yaw=session.Camera.Yaw;
                for(int i=0;i<60;i++)Input(false,Keys.W,Keys.D,Keys.Space,Keys.LeftShift,Keys.LeftAlt,Keys.D2);
                Check(State==GameState.Inventory&&world.Time.Seconds>clock+.98&&world.Weather.MotionSeconds>weather+.98,
                    "World clock/weather paused in "+block);
                Check(world.Electricity.SolarOutput(solar)>power,"Solar wattage froze in "+block);
                Check(session.Player.Position==position&&session.Camera.Yaw==yaw&&session.Inventory.SelectedSlot==0,
                    "Interface keys leaked into movement/camera/hotbar: "+block);
                Check(world.Containers.Get(generator).Burn>0,"Generator did not run in "+block);
                Input(false,Keys.Escape);Input();
            }
            // Autosave must not silently empty a recipe or the mouse cursor.
            Input(false,Keys.E);session.Crafting.SetSlot(0,new(Items.Stick,2));
            session.Inventory.SetSlot(0,new(Items.CopperWire,3));session.InventoryInteraction.Click(0,false);
            typeof(Game1).GetField("_autosaveElapsed",Private).SetValue(this,300d);
            for(int i=0;i<30;i++)Input();
            Check(session.Crafting[0].Count==2&&session.InventoryInteraction.Cursor.Count==3,
                "Live-interface autosave consumed/returned UI-owned items");
            Call("Pause");double paused=world.Time.Seconds;
            for(int i=0;i<30;i++)Input();
            Check(State==GameState.Paused&&world.Time.Seconds==paused,"Pause menu did not freeze simulation");
            Input(false,Keys.Escape);Input();
            Check(State==GameState.Inventory&&session.Crafting[0].Count==2&&session.InventoryInteraction.Cursor.Count==3,
                "Resuming an interface lost cursor/recipe ownership");
            Check(session.TryReturnInterfaceItems(),"Could not return fixture recipe/cursor");Input(false,Keys.Escape);Input();
            Input(false,Keys.Escape);paused=world.Time.Seconds;
            for(int i=0;i<30;i++)Input();Check(world.Time.Seconds==paused,"Escape pause menu advanced time");
            Input(false,Keys.Escape);Input();
            // Gravity and hazards remain live; only player-directed actions stop.
            session.Player.SetAbsolute(new(20.5,4,20.5));session.Camera.Position=session.Player.EyePosition;
            Input(false,Keys.E);for(int i=0;i<70;i++)Input();
            Check(session.Player.Grounded&&session.Player.Position.Y==1,"Opening inventory suspended gravity");
            session.Player.Settings.Difficulty=Difficulty.Normal;session.Player.RestoreFraction(1);
            session.Player.Burning.Restore(new(3,0));
            for(int i=0;i<70&&State!=GameState.Death;i++)Input();
            Check(State==GameState.Death&&session.Player.IsDead,"Inventory suppressed environmental death");
            Console.WriteLine("PASS: Live inventory/all stations: time/weather/solar/fuel, neutral controls, gravity/death, autosave ownership and actual pause/resume.");
        }

        private void VerifyElectricalStations()
        {
            var atlas=Field<TextureAtlas>("_atlas");
            var world=PigChecks.Flat();world.Time.Restore(300);
            Point3 target=new(20,2,17),generator=new(19,2,17);
            world.SetBlock(generator.X,generator.Y,generator.Z,BlockType.CoalGenerator);
            world.Containers.Get(generator).SetSlot(0,new(Items.Charcoal,3));
            world.SetBlock(18,2,17,BlockType.SolarPanel);
            world.SetBlock(18,2,18,BlockType.AccessPanel);
            world.SetBlock(21,2,17,BlockType.CopperWire);
            world.SetBlock(22,2,17,BlockType.CopperWire);
            world.SetBlock(22,3,17,BlockType.CopperWire);
            world.SetBlock(22,4,17,BlockType.CopperWire);
            world.SetBlock(23,2,17,BlockType.PowerSwitch);
            var save=new WorldSave(world,"Electrical interaction",new(20.5f,1,20.5f),0,0);
            string id=Field<WorldSaveManager>("_saves").Create(save);Call("BeginWorld",id,save);
            var session=Field<GameSession>("_session");
            using var screen=new MDSM.UI.InventoryScreen(GraphicsDevice,atlas);
            foreach(var block in new[]{BlockType.AccessPanel,BlockType.OutputPanel,BlockType.CoalGenerator})
            {
                world.SetBlock(target.X,target.Y,target.Z,block);
                if(block==BlockType.CoalGenerator)world.Containers.Get(target).SetSlot(0,new(Items.Coal,2));
                world.Electricity.Update(.25);
                Input();Input(true);
                if(State!=GameState.Inventory||session.ActiveContainer?.Block!=block)
                    throw new InvalidOperationException("Actual RMB did not open electrical interface: "+block);
                foreach(var size in new[]{(320,240),(1280,720)})
                {
                    using var surface=new RenderTarget2D(GraphicsDevice,size.Item1,size.Item2,false,SurfaceFormat.Color,DepthFormat.Depth24);
                    GraphicsDevice.SetRenderTarget(surface);GraphicsDevice.Clear(Color.Black);screen.Open();
                    screen.Draw(session,default,GraphicsDevice.Viewport);
                    GraphicsDevice.SetRenderTarget(null);
                    var pixels=new Color[surface.Width*surface.Height];surface.GetData(pixels);
                    if(pixels.Count(p=>p!=Color.Black)<surface.Width*surface.Height/5)throw new InvalidOperationException("Electrical UI was not drawn.");
                    using(var png=File.Create(Path.Combine(AppContext.BaseDirectory,$"electrical-{block}-{surface.Width}.png")))surface.SaveAsPng(png,surface.Width,surface.Height);
                    GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
                }
                Input(false,Keys.Escape);Input();
                if(block==BlockType.CoalGenerator)world.Containers.Get(target).SetSlot(0,default);
            }
            world.SetBlock(target.X,target.Y,target.Z,BlockType.PowerSwitch);
            Input();Input(true);
            if(!world.Structure(target).Open)throw new InvalidOperationException("Actual RMB did not switch power off.");
            Input();Input(true);
            if(world.Structure(target).Open)throw new InvalidOperationException("Actual RMB did not switch power on.");
            var display=PigChecks.Flat();
            display.SetBlock(19,1,18,BlockType.SolarPanel);display.SetBlock(18,1,18,BlockType.AccessPanel);
            display.SetBlock(20,1,18,BlockType.CopperWire);display.SetBlock(21,1,18,BlockType.CopperWire);
            display.SetBlock(22,1,18,BlockType.OutputPanel);display.Structures[new(22,1,18)]=new(new(22,1,18),1,false,new(0,-1,0));
            display.SetBlock(23,1,18,BlockType.CopperWire);display.SetBlock(24,1,18,BlockType.PowerSwitch);
            display.SetBlock(19,1,20,BlockType.SolarPanel);display.SetBlock(19,2,20,BlockType.CopperWire);
            using var renderer=new WorldRenderer(GraphicsDevice,atlas,display);
            using var view=new RenderTarget2D(GraphicsDevice,960,540,false,SurfaceFormat.Color,DepthFormat.Depth24);
            var camera=new Camera{Position=new(26,5,24)};camera.RestoreOrientation(-.65f,-.4f);
            foreach(bool smooth in new[]{false,true})
            {
                GraphicsDevice.SetRenderTarget(view);GraphicsDevice.Clear(WorldRenderer.SkyColor);
                renderer.SmoothLighting=smooth;RenderingPerformanceChecks.Warm(renderer,camera);renderer.Draw(camera,null);
                GraphicsDevice.SetRenderTarget(null);
                using(var png=File.Create(Path.Combine(AppContext.BaseDirectory,$"electrical-world-{smooth}.png")))view.SaveAsPng(png,view.Width,view.Height);
                GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            }
            Console.WriteLine("PASS: Actual electrical RMB interfaces/switches, original artwork, connected wire meshes and 320/1280 UI renders.");
        }

        private void VerifyArmorEquip()
        {
            var session=Field<GameSession>("_session");
            var inventory=session.Inventory;
            var iron=new ItemStack(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Boots),1,100);
            var running=new ItemStack(Items.RunningBoots,1,140);
            inventory.SetSlot(0,iron);
            Input(true);
            if (inventory.Equipped((int)ArmorSlot.Boots)!=iron || !inventory.Selected.IsEmpty)
                throw new InvalidOperationException("Right-click did not equip held armor into an empty slot.");
            inventory.SetSlot(0,running);
            Input();
            Input(true);
            Input(true);
            if (inventory.Equipped((int)ArmorSlot.Boots)!=running || inventory.Selected!=iron)
                throw new InvalidOperationException("Right-click did not swap distinct armor stacks exactly once.");
            Console.WriteLine("PASS: Actual right-click equips or swaps armor and preserves both item identities and durability.");
        }

        private void VerifyIndustrialStations()
        {
            var world = PigChecks.Flat();
            world.SetBlock(20, 2, 17, BlockType.MetalworkingBench);
            world.SetBlock(20, 2, 23, BlockType.BrickKiln);
            world.SetBlock(18, 2, 18, DyePalette.Glass(DyeColor.Red));
            world.SetBlock(22, 2, 18, DyePalette.Glass(DyeColor.Blue, true));
            var inventory = new Inventory();
            inventory.SetSlot(0, new(Items.MortarAndPestle, 1));
            inventory.SetSlot(1, new(Items.CopperIngot, 3));
            var save = new WorldSave(world, "Industrial interaction", new(20.5f, 1, 20.5f), 0, 0, inventory);
            string id = Field<WorldSaveManager>("_saves").Create(save);
            Call("BeginWorld", id, save);
            var session = Field<GameSession>("_session");
            void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
            Input(); Input(true);
            Require(State == GameState.Inventory && session.Crafting.Station == CraftingStation.Mortar
                && session.Inventory.Selected.Item == Items.MortarAndPestle, "Actual RMB did not open and preserve the held mortar.");
            session.Crafting.SetSlot(0, new(Items.FromBlock(BlockType.Cornflower), 1));
            Require(session.Crafting.TryCraft(session.InventoryInteraction), "Open mortar could not grind a flower.");
            Input(false, Keys.Escape); Input();
            Require(State == GameState.Playing && session.InventoryInteraction.Cursor.IsEmpty
                && Enumerable.Range(0, Inventory.SlotCount).Any(i => session.Inventory[i].Item == Items.Dye(DyeColor.Blue)),
                "Closing the mortar failed to return its result to the player.");
            session.Inventory.SelectedSlot = 1;
            Input(true);
            Require(State == GameState.Inventory && session.Crafting.Width == 3 && session.Crafting.Station == CraftingStation.Metalworking,
                "Actual RMB did not open the metalworking bench.");
            Input(false, Keys.Escape); Input();
            session.Camera.RestoreOrientation(MathHelper.Pi, 0);
            Input(true);
            Require(State == GameState.Inventory && session.ActiveContainer?.Block == BlockType.BrickKiln,
                "Actual RMB did not open the brick kiln.");
            Input(false, Keys.Escape); Input();

            var atlas = Field<TextureAtlas>("_atlas");
            using var renderer = new WorldRenderer(GraphicsDevice, atlas, world);
            using var surface = new RenderTarget2D(GraphicsDevice, 640, 360, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Camera {Position = new(20.5f, 2.7f, 22.5f)};
            var pixels = new Color[surface.Width * surface.Height];
            try
            {
                foreach (bool smooth in new[]{false,true})
                {
                    GraphicsDevice.SetRenderTarget(surface);
                    renderer.SmoothLighting = smooth;
                    RenderingPerformanceChecks.Warm(renderer, camera);
                    GraphicsDevice.Clear(WorldRenderer.SkyColor);
                    renderer.Draw(camera, null);
                    GraphicsDevice.SetRenderTarget(null);
                    surface.GetData(pixels);
                    GraphicsDevice.Textures[0] = null; GraphicsDevice.Textures[0] = atlas.Texture;
                    Require(pixels.Count(p => p.R > 45 && p.R > 1.55f * p.G && p.R > 1.55f * p.B) > 100
                        && pixels.Count(p => p.B > 45 && p.B > 1.60f * p.G && p.B > 1.70f * p.R) > 100,
                        "Lit terrain shader lost stained cube/pane colors with smooth lighting " + smooth);
                }
            }
            finally { GraphicsDevice.SetRenderTarget(null); }
            Console.WriteLine("PASS: Actual RMB station interactions, mortar reuse and stained cube/pane color in both world lighting modes.");
        }

        private void VerifyHeldJump()
        {
            var world = PigChecks.Flat();
            var save = new WorldSave(world, "Held jump", new(20.5f, 1, 20.5f), 0, 0);
            string id = Field<WorldSaveManager>("_saves").Create(save);
            Call("BeginWorld", id, save);
            Input();
            var player = Field<GameSession>("_session").Player;
            int jumps = 0;
            bool rising = false;
            for (int frame = 0; frame < 150; frame++)
            {
                float before = player.Position.Y;
                Input(false, Keys.Space);
                bool ascending = player.Position.Y > before + .001f;
                if (ascending && !rising) jumps++;
                rising = ascending;
            }
            if (jumps < 3) throw new InvalidOperationException("Holding Space did not repeat grounded jumps.");
            for (int frame = 0; frame < 60; frame++) Input();
            if (!player.Grounded || player.Position.Y != 1)
                throw new InvalidOperationException("Releasing Space did not stop repeated jumps.");
            Console.WriteLine("PASS: Holding Space repeats Survival jumps and releasing it lets the player settle.");
        }

        private void VerifyMobPlacement(MobSpecies species)
        {
            var world=new World(173,WorldSize.Small,false);
            world.SavedGameMode=GameMode.Creative;
            for(int z=16;z<26;z++)for(int x=16;x<26;x++)world.SetBlock(x,0,z,BlockType.Grass);
            var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.Dirt),2));
            var state=new MobState(species,new(20.5f,1,20.3f),0,MobDefinitions.Get(species).MaximumHealth);
            var save=new WorldSave(world,"Mob placement",new(20.5f,3,20.5f),0,-1.5f,inventory,
                Mobs:species==MobSpecies.Cow?new[]{state}:null,Hostiles:species==MobSpecies.Zombie?new[]{state}:null);
            string id=Field<WorldSaveManager>("_saves").Create(save);Call("BeginWorld",id,save);Input();
            var session=Field<GameSession>("_session");
            var hit=BlockRaycaster.Cast(world,session.Camera.Position,session.Camera.Forward);
            if(hit?.PlacementCell!=new Point3(20,1,20))throw new InvalidOperationException("Scripted placement ray missed fixture.");
            int revision=world.Revision;Input(true);
            if(world.GetBlock(20,1,20)!=BlockType.Air || session.Inventory.Selected.Count!=2 || revision!=world.Revision)
                throw new InvalidOperationException("Actual RMB placed through "+species);
            var mob=species==MobSpecies.Cow?session.Mobs.Entities[0]:session.Hostiles.Entities[0];
            for(int tick=0;tick<480;tick++)mob.Update(world,1f/120,Vector3.UnitX);
            Input();Input(true);
            if(world.GetBlock(20,1,20)!=BlockType.Dirt || session.Inventory.Selected.Count!=2)
                throw new InvalidOperationException("Actual Creative RMB failed after mob moved out of the way.");
            Console.WriteLine("PASS: Actual RMB rejects placement within "+species+" and permits placement after it moves away.");
        }
    }
}
