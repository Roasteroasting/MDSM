using System;
using System.Collections.Generic;
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

internal static class MobGraphicsChecks
{
    public static int Run()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-mob-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var game = new CheckGame(root); game.Run(); Require(game.Verified, "Mixed-mob graphics checks did not complete.");
            Console.WriteLine("PASS: All four textured/animated mobs, resource cutouts and wool, depth/state preservation, water/HUD/screenshots, input combat, paused/persistent replenishment, gravity, hotbar scrolling, and revised new-world stock.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped root.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class CheckGame : Game1
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly WorldSaveManager _store;
        private readonly string _root;
        private readonly List<(string Path, Color[] Pixels)> _shots = new();
        private int _frame;
        private Color[] _visible, _hidden;
        public bool Verified { get; private set; }
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private object Call(string method, params object[] args) => typeof(Game1).GetMethod(method, Private).Invoke(this, args);
        private void WarmRenderer() => RenderingPerformanceChecks.Warm(Field<WorldRenderer>("_renderer"), Session.Camera);
        private GameSession Session => Field<GameSession>("_session");
        public CheckGame(string root) : this(root, new WorldSaveManager(Path.Combine(root, "Worlds"))) { }
        private CheckGame(string root, WorldSaveManager store) : base(store, new SettingsStore(Path.Combine(root, "settings.json")))
        { _root = root; _store = store; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content"); }
        protected override void Update(GameTime time) { }
        private void Input(bool left = false, float seconds = 0, params Keys[] keys)
        {
            var mouse = new MouseState(15, 20, 0, left ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Call("ProcessInput", new KeyboardState(keys), mouse, false, new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)), Point.Zero);
        }
        private void Press(Keys key) { Input(); Input(keys: new[] { key }); }
        private Color[] Pixels() { var pixels = new Color[1280 * 720]; GraphicsDevice.GetBackBufferData(pixels); return pixels; }
        protected override void Draw(GameTime time)
        {
            Require(IsActive, "Mixed-mob graphics window lost focus."); _frame++;
            bool capture = _frame is 1 or 2 or 4 or 6 or 7 or 8;
            switch (_frame)
            {
                case 1:
                    VerifyGpu();
                    World world = PigChecks.Flat();
                    world.SetGeneratedBlock(64, 1, 62, BlockType.WhiteWool);
                    for (int z = 57; z <= 61; z++) for (int x = 57; x <= 71; x++) world.SetGeneratedBlock(x, 1, z, BlockType.WaterSource);
                    world.SetBlock(61, 3, 62, BlockType.Sand); world.SetBlock(67, 3, 62, BlockType.Gravel); world.Gravity.Tick();
                    var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.FromBlock(BlockType.WhiteWool), 64));
                    inventory.SetSlot(1, new ItemStack(Items.Leather, 3)); inventory.SetSlot(2, new ItemStack(Items.Feather, 4));
                    inventory.SetSlot(3, new ItemStack(Items.Coal, 23));
                    var save = new WorldSave(world, "Mixed runtime", new Vector3(64.5f, 1, 65.5f), 0, -0.1f, inventory, Health: 10,
                        Pigs: new[] { new PigState(new Vector3(59.5f, 1, 60.5f), MathHelper.Pi, 10) }, Mobs: new[] {
                            MobChecks.State(MobSpecies.Cow, new Vector3(62.5f, 1, 60.5f)) with { Yaw = MathHelper.Pi },
                            MobChecks.State(MobSpecies.Sheep, new Vector3(65.5f, 1, 60.5f)) with { Yaw = MathHelper.Pi },
                            MobChecks.State(MobSpecies.Chicken, new Vector3(68.5f, 1, 60.5f)) with { Yaw = MathHelper.Pi } });
                    Call("BeginWorld", _store.Create(save), save); WarmRenderer(); break;
                case 2: Press(Keys.F1); break;
                case 5: Press(Keys.F1); break;
                case 6: Press(Keys.D2); break;
                case 7: Press(Keys.D3); break;
                case 8: Press(Keys.D1); break;
                case 9:
                    foreach (var shot in _shots)
                    {
                        using var stream = File.OpenRead(shot.Path); using var png = Texture2D.FromStream(GraphicsDevice, stream);
                        var data = new Color[1280 * 720]; png.GetData(data);
                        Require(png.Width == 1280 && png.Height == 720 && data.AsSpan().SequenceEqual(shot.Pixels), "Mixed-mob/resource PNG pixels/resolution changed.");
                    }
                    VerifyInputCombat(); VerifyWorldUpdates(); VerifyFallingGpu(); break;
                case 10:
                    Call("SetState", GameState.WorldCreation); MenuController menu = Field<MenuController>("_menu");
                    foreach (char ch in "Mixed new world") menu.TextInput(ch);
                    Press(Keys.Tab); foreach (char ch in "173") menu.TextInput(ch); Press(Keys.Tab);
                    foreach (WorldSize size in new[] { WorldSize.Large, WorldSize.Massive, WorldSize.Small, WorldSize.Normal })
                    { Press(Keys.Right); Require(menu.SelectedSize == size, "Create menu did not cycle all four sizes."); }
                    Press(Keys.Left);
                    Call("HandleMenuAction", MenuAction.CreateWorld);
                    var creationWait = System.Diagnostics.Stopwatch.StartNew();
                    while (State != GameState.Playing && creationWait.Elapsed.TotalSeconds < 120) { Input(); System.Threading.Thread.Sleep(1); }
                    Require(State == GameState.Playing && Session.Mobs.Entities.Count == 32 && Session.Mobs.Entities.Select(m => m.Species).Distinct().Count() == 4,
                        "Creation menu did not use one shared mixed population budget.");
                    Require(Enumerable.Range(0, 36).All(i => Session.Inventory[i].IsEmpty), "New-world menu granted starting items.");
                    WorldSave initial = _store.Load(Session.Id); Require(initial.Pigs.Length + initial.Mobs.Length == 32, "Initial save omitted mixed species.");
                    string reloadId = Session.Id; Vector3 reloadPosition = Session.Player.Position;
                    Session.World.SetBlock(15, 125, 15, BlockType.WhiteWool);
                    Call("HandleMenuAction", MenuAction.SaveAndReturn);
                    Require(State == GameState.MainMenu && ChunkMesh.LiveBufferBytes == 0, $"Save/return retained terrain GPU buffers: state={State}, bytes={ChunkMesh.LiveBufferBytes}.");
                    Call("RefreshWorlds", reloadId); Call("HandleMenuAction", MenuAction.LoadWorld);
                    var loadingWait = System.Diagnostics.Stopwatch.StartNew();
                    while (State != GameState.Playing && loadingWait.Elapsed.TotalSeconds < 120) { Input(); System.Threading.Thread.Sleep(1); }
                    Require(State == GameState.Playing && Session.Id == reloadId && Session.Player.Position == reloadPosition
                        && Session.World.GetBlock(15, 125, 15) == BlockType.WhiteWool && Session.Mobs.Entities.Count == 32
                        && Field<WorldRenderer>("_renderer").PendingChunks == 0, "Background loading/nearby mesh preparation changed saved state or entered an incomplete world.");
                    break;
                case 11: Verified = true; Exit(); break;
            }
            if (capture) Press(Keys.F2);
            base.Draw(time);
            if (_frame == 1) _visible = Pixels();
            if (_frame == 2) _hidden = Pixels();
            if (_frame is 3 or 4) Require(Pixels().AsSpan().SequenceEqual(_hidden), "Mixed mobs/water changed after HUD-hidden screenshot.");
            if (_frame is 5 or 8) Require(Pixels().AsSpan().SequenceEqual(_visible), "Resource texture switching/HUD restoration corrupted wool or world textures.");
            if (capture)
            {
                string[] files = Directory.GetFiles(Path.Combine(_root, "screenshots"), "*.png"); Array.Sort(files, StringComparer.Ordinal);
                Require(files.Length == _shots.Count + 1, "Mixed-mob screenshot repeated/missing."); _shots.Add((files[^1], Pixels()));
            }
        }
        private void VerifyInputCombat()
        {
            foreach (MobSpecies species in new[] { MobSpecies.Cow, MobSpecies.Sheep, MobSpecies.Chicken })
            {
                World world = PigChecks.Flat(); var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.Leather, 2));
                inventory.SetSlot(4, new ItemStack(Items.Coal, 23));
                var save = new WorldSave(world, "Input " + species, new Vector3(64.5f, 1, 64.5f), 0, -0.1f, inventory, Health: 11,
                    Mobs: new[] { MobChecks.State(species, new Vector3(64.5f, 1, 61.5f), 3) });
                Call("BeginWorld", _store.Create(save), save); WarmRenderer(); Input();
                for (int i = 0; i < 1200 && Session.Mobs.Entities.Count > 0; i++)
                {
                    PassiveMob target = Session.Mobs.Entities[0]; Vector3 delta = target.Position + Vector3.Up * (target.Definition.Height * 0.6f) - Session.Camera.Position;
                    Session.Camera.RestoreOrientation(MathF.Atan2(delta.X, -delta.Z), MathF.Atan2(delta.Y, new Vector2(delta.X, delta.Z).Length()));
                    Input(left: true, seconds: 1f / 60);
                }
                Require(Session.Mobs.Entities.Count == 0 && Session.Items.Entities.Count == 1 && Session.Items.Entities[0].Item == MobDefinitions.Get(species).Drop
                    && Session.Items.Entities[0].Count is >= 1 and <= 4, "Real input path failed to kill/drop species " + species);
                _store.Save(Session.Id, Session.Capture()); string id = Session.Id; Call("BeginWorld", id, _store.Load(id)); WarmRenderer();
                Require(Session.Mobs.Entities.Count == 0 && Session.Items.Entities.Count == 1 && Session.Inventory[4].Count == 23 && Session.Player.Health == 11,
                    "Saved species death changed existing food/health or regenerated entities.");
            }
        }

        private void VerifyWorldUpdates()
        {
            World world = PigChecks.Flat();
            var save = new WorldSave(world, "World updates", new Vector3(64.5f, 1, 64.5f), 0, 0, new Inventory(),
                Mobs: new[] { MobChecks.State(MobSpecies.Chicken, new Vector3(30.5f, 1, 30.5f), 1) });
            Call("BeginWorld", _store.Create(save), save); WarmRenderer(); Input();
            Require(Session.Mobs.Punch(Session.Mobs.Entities[0], Session.Player.Position, Session.Items), "Could not schedule runtime replacement.");
            double delay = Session.Mobs.PendingRespawns[0].RemainingSeconds;
            world.SetBlock(15, 8, 15, BlockType.Sand); world.SetBlock(15, 9, 15, BlockType.Gravel);
            Call("SetState", GameState.Paused); Input(seconds: 60);
            Require(Session.Mobs.PendingRespawns[0].RemainingSeconds == delay && world.GetBlock(15, 8, 15) == BlockType.Sand,
                "Pause advanced population or gravity.");
            Call("SetState", GameState.Playing); Input(seconds: 1);
            Require(Math.Abs(Session.Mobs.PendingRespawns[0].RemainingSeconds - (delay - 1)) < 1e-6
                && world.GetBlock(15, 1, 15) == BlockType.Sand && world.GetBlock(15, 2, 15) == BlockType.Gravel,
                "Gameplay did not resume timers and stacked gravity through its fixed-step loop.");
            Press(Keys.E); Require(State == GameState.Inventory, "Inventory did not open for paused timer test.");
            double inventoryDelay = Session.Mobs.PendingRespawns[0].RemainingSeconds; Input(seconds: 60);
            Require(Session.Mobs.PendingRespawns[0].RemainingSeconds == inventoryDelay, "Inventory advanced replacement timer.");
            Press(Keys.E);

            void Wheel(int position, params Keys[] keys)
            {
                var mouse = new MouseState(15, 20, position, ButtonState.Released, ButtonState.Released,
                    ButtonState.Released, ButtonState.Released, ButtonState.Released);
                Call("ProcessInput", new KeyboardState(keys), mouse, false, new GameTime(), Point.Zero);
            }
            Session.Inventory.SelectedSlot = 8; Wheel(-120);
            Require(Session.Inventory.SelectedSlot == 0, "Wheel down did not wrap right from slot 9 to slot 1.");
            Wheel(0); Require(Session.Inventory.SelectedSlot == 8, "Wheel up did not wrap left from slot 1 to slot 9.");
            Wheel(0, Keys.D4); Require(Session.Inventory.SelectedSlot == 3, "Number-key selection changed.");
            Press(Keys.E); Wheel(-120);
            Require(State == GameState.Inventory && Session.Inventory.SelectedSlot == 3, "Inventory wheel changed hotbar selection.");
            Press(Keys.E); Wheel(0);
            Require(Session.Inventory.SelectedSlot == 3, "Closing inventory replayed wheel input.");

            string id = Session.Id;
            Call("SetState", GameState.Paused);
            Call("HandleMenuAction", MenuAction.SaveAndReturn);
            Require(State == GameState.MainMenu && Session == null, "Save and Return did not preserve pending world updates.");
            WorldSave restored = _store.Load(id);
            Require(restored.Population.Targets[(int)MobSpecies.Chicken] == 1 && restored.Population.Pending.Length == 1
                && restored.Population.Pending[0].RemainingSeconds == inventoryDelay, "Safe menu save changed population metadata.");
            Call("BeginWorld", id, restored); WarmRenderer();
            Require(Session.Mobs.Entities.Count == 0 && Session.Mobs.PendingRespawns.Count == 1, "Loading duplicated a pending mob.");
            for (int i = 0; i < 61 * 60; i++) Input(seconds: 1f / 60);
            Require(State == GameState.Playing && Session.Mobs.Entities.Count == 1 && Session.Mobs.Entities[0].Species == MobSpecies.Chicken
                && Session.Mobs.PendingRespawns.Count == 0, "Loaded timer did not create exactly one replacement through gameplay.");
            Session.World.SetBlock(16, 12, 15, BlockType.Gravel); Input(seconds: 0.2f);
            FallingBlockState falling = Session.World.Gravity.CaptureActive().Single();
            Require(falling.Y < 12 && falling.Y > 11 && Session.World.GetBlock(16, 12, 15) == BlockType.Air,
                "Input path teleported an animated block instead of starting its fall.");
            Call("SetState", GameState.Paused); Input(seconds: 60);
            Require(Session.World.Gravity.CaptureActive().Single() == falling, "Pause moved an active falling block.");
            _store.Save(id, Session.Capture()); Call("BeginWorld", id, _store.Load(id)); WarmRenderer();
            Require(Session.World.Gravity.CaptureActive().Single() == falling, "Safe save/load changed an active falling block.");
            Input(seconds: 0.1f);
            Require(Session.World.Gravity.Active.Single().Y < falling.Y, "Loaded active fall did not resume.");
            Input(seconds: 2);
            Require(Session.World.Gravity.Active.Count == 0 && Session.World.GetBlock(16, 1, 15) == BlockType.Gravel,
                "Reloaded falling block failed to settle once through gameplay.");
            Console.WriteLine("PASS: Game input loop pauses/reloads replacement timers, settles stacked sand/gravel, and scrolls down/right and up/left without menu replay.");
        }

        private void VerifyFallingGpu()
        {
            TextureAtlas atlas = Field<TextureAtlas>("_atlas"); var world = new World(173, WorldSize.Small, false);
            world.SetBlock(15, 0, 16, BlockType.Stone); world.SetBlock(17, 0, 16, BlockType.Stone);
            world.SetBlock(15, 12, 16, BlockType.Sand); world.SetBlock(17, 12, 16, BlockType.Gravel); world.Gravity.Tick();
            using var renderer = new FallingBlockRenderer(GraphicsDevice, atlas);
            using var target = new RenderTarget2D(GraphicsDevice, 320, 240, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Camera { Position = new Vector3(16.5f, 11.5f, 22) }; camera.RestoreOrientation(0, 0);
            Color background = new(30, 70, 100);
            Color[] Draw(float fraction, bool occlude = false)
            {
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(background);
                if (occlude)
                {
                    using var depthOnly = new BlendState { ColorWriteChannels = ColorWriteChannels.None };
                    using var effect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
                    GraphicsDevice.BlendState = depthOnly; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                    GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                    var quad = new[] { new VertexPositionColor(new(-1,-1,0.01f), Color.White), new VertexPositionColor(new(-1,1,0.01f), Color.White),
                        new VertexPositionColor(new(1,-1,0.01f), Color.White), new VertexPositionColor(new(1,1,0.01f), Color.White) };
                    foreach (EffectPass pass in effect.CurrentTechnique.Passes) { pass.Apply(); GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2); }
                }
                GraphicsDevice.BlendState = BlendState.AlphaBlend; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone; GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp; GraphicsDevice.Textures[0] = atlas.Texture;
                renderer.Draw(camera, world.Gravity, fraction);
                Require(GraphicsDevice.BlendState == BlendState.AlphaBlend && GraphicsDevice.DepthStencilState == DepthStencilState.Default
                    && GraphicsDevice.RasterizerState == RasterizerState.CullNone && GraphicsDevice.SamplerStates[0] == SamplerState.LinearClamp
                    && GraphicsDevice.Textures[0] == atlas.Texture, "Falling-block pass leaked graphics state.");
                GraphicsDevice.SetRenderTarget(null); var pixels = new Color[320 * 240]; target.GetData(pixels);
                GraphicsDevice.Textures[0] = null; GraphicsDevice.Textures[0] = atlas.Texture; return pixels;
            }
            Color[] start = Draw(1); Require(start.Count(c => c != background) > 500, "Full-size falling cube textures were not rendered.");
            int revision = world.Revision; world.Gravity.Update(0.2);
            Color[] half = Draw(0.5f), end = Draw(1);
            Require(!start.AsSpan().SequenceEqual(half) && !half.AsSpan().SequenceEqual(end) && world.Revision == revision
                && world.Gravity.Active.All(b => Math.Abs(b.Y - Math.Round(b.Y)) > 0.1), "GPU fall did not move through distinct fractional heights without mesh edits.");
            Require(Draw(1, occlude: true).All(c => c == background), "Falling cubes rendered through foreground terrain depth.");
            Require(Draw(1).AsSpan().SequenceEqual(end), "Falling rendering did not recover after a different graphics pass.");
            Console.WriteLine("PASS: Sand/gravel GPU cubes move between voxel heights with interpolation, atlas shading, foreground depth, and restored texture/state; paused mid-fall save/load resumes safely.");
        }

        private void VerifyGpu()
        {
            PassiveMobRenderer renderer = Field<PassiveMobRenderer>("_mobRenderer"); TextureAtlas atlas = Field<TextureAtlas>("_atlas");
            World world = PigChecks.Flat();
            using var target = new RenderTarget2D(GraphicsDevice, 320, 240, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Camera(); camera.RestoreOrientation(0, 0);
            Color[] Read()
            {
                GraphicsDevice.SetRenderTarget(null); var pixels = new Color[320 * 240]; target.GetData(pixels);
                GraphicsDevice.Textures[0] = null; GraphicsDevice.Textures[0] = atlas.Texture; return pixels;
            }
            Color background = new(30, 70, 100);
            Color[] Draw(PassiveMobPopulation population)
            {
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(background);
                GraphicsDevice.BlendState = BlendState.AlphaBlend; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone; GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp; GraphicsDevice.Textures[0] = atlas.Texture;
                renderer.Draw(camera, population);
                Require(GraphicsDevice.BlendState == BlendState.AlphaBlend && GraphicsDevice.DepthStencilState == DepthStencilState.Default
                    && GraphicsDevice.RasterizerState == RasterizerState.CullNone && GraphicsDevice.SamplerStates[0] == SamplerState.LinearClamp
                    && GraphicsDevice.Textures[0] == atlas.Texture && GraphicsDevice.Viewport.Width == 320, "Mixed mob pass leaked graphics state.");
                return Read();
            }
            foreach (MobSpecies species in Enum.GetValues<MobSpecies>())
            {
                var population = new PassiveMobPopulation(world, new[] { MobChecks.State(species, new Vector3(10, 1, 10)) with { Yaw = MathHelper.Pi } });
                PassiveMob mob = population.Entities[0]; camera.Position = new Vector3(10, 1 + mob.Definition.Height * 0.55f, 13);
                Color[] normal = Draw(population); Require(normal.Count(c => c != background) > 40, "Species not rendered: " + species);
                mob.Damage(1, Vector3.Right); Color[] hurt = Draw(population);
                Require(hurt.Count(c => c != background && c.R > c.G * 2) > normal.Count(c => c != background && c.R > c.G * 2), "GPU hurt flash missing: " + species);
                for (int i = 0; i < 1200 && (!mob.Walking || Math.Abs(MathF.Sin(mob.WalkPhase)) < 0.6f || mob.HurtSeconds > 0); i++) population.Update((float)SimulationClock.StepSeconds);
                camera.Position = mob.Position + new Vector3(0, mob.Definition.Height * 0.55f, 3);
                Color[] moving = Draw(population); var idle = new PassiveMobPopulation(world, population.CaptureAll()); Color[] still = Draw(idle);
                Require(!moving.AsSpan().SequenceEqual(still), "GPU walking animation changed no pixels: " + species);
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(background);
                using (var depthOnly = new BlendState { ColorWriteChannels = ColorWriteChannels.None })
                using (var effect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true })
                {
                    GraphicsDevice.BlendState = depthOnly; GraphicsDevice.DepthStencilState = DepthStencilState.Default; GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                    var quad = new[] { new VertexPositionColor(new(-1,-1,0.01f), Color.White), new VertexPositionColor(new(-1,1,0.01f), Color.White),
                        new VertexPositionColor(new(1,-1,0.01f), Color.White), new VertexPositionColor(new(1,1,0.01f), Color.White) };
                    foreach (EffectPass pass in effect.CurrentTechnique.Passes) { pass.Apply(); GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2); }
                    renderer.Draw(camera, population);
                }
                Require(Read().All(c => c == background), "Species rendered through foreground depth: " + species);
            }
            Color[] last = null;
            foreach (ItemId item in new[] { Items.Leather, Items.Feather, Items.FromBlock(BlockType.WhiteWool) })
            {
                var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(item, 4));
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(background); Field<FirstPersonRenderer>("_firstPerson").Draw(GameState.Playing, inventory);
                Color[] held = Read(); Require(held.Count(c => c != background) > 10 && (last == null || !held.AsSpan().SequenceEqual(last)), "Resource/wool held representation missing."); last = held;
                if (Items.IsSprite(item))
                {
                    var data = new Color[256]; atlas.ItemTexture(item).GetData(0,atlas.ItemSource(item),data,0,data.Length);
                    Require(data.Any(c => c.A == 0) && data.Any(c => c.A == 255), "Resource icon lost its binary transparency.");
                    GraphicsDevice.Textures[0] = null; GraphicsDevice.Textures[0] = atlas.Texture;
                }
            }
            var drops = new DroppedItems(world); int revision = world.Revision;
            drops.Spawn(Items.Leather, 3, new Vector3(9.5f, 1.5f, 10), 0); drops.Spawn(Items.Feather, 2, new Vector3(10.5f, 1.5f, 10), 0);
            drops.Spawn(Items.FromBlock(BlockType.WhiteWool), 4, new Vector3(11.5f, 1.5f, 10), 0);
            using var items = new DroppedItemRenderer(GraphicsDevice, atlas);
            camera.Position = new Vector3(10.5f, 1.5f, 13); camera.RestoreOrientation(0, 0);
            GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(background);
            GraphicsDevice.BlendState = BlendState.Opaque; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise; GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
            items.Draw(camera, drops); Color[] itemPixels = Read();
            Require(itemPixels.Count(c => c != background) > 30 && world.Revision == revision, "Batched resource/wool drops missing or dirtied terrain.");
            Console.WriteLine("PASS: Four species GPU models, hurt/limb animation, foreground depth, restored state, and resource/wool held/drop textures.");
        }
    }
}
