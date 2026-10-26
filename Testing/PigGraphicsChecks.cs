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

internal static class PigGraphicsChecks
{
    public static int Run()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-pig-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var game = new CheckGame(root); game.Run();
            Require(game.Verified, "Pig graphics checks did not complete.");
            Console.WriteLine("PASS: Pig/food GPU rendering, depth/state preservation, screenshots, input-path combat/eating, new-world stock, and persistent deaths.");
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
        private int _frame, _punchedHealth;
        private Color[] _hidden;
        public bool Verified { get; private set; }
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private object Call(string method, params object[] args) => typeof(Game1).GetMethod(method, Private).Invoke(this, args);
        private GameSession Session => Field<GameSession>("_session");
        public CheckGame(string root) : this(root, new WorldSaveManager(Path.Combine(root, "Worlds"))) { }
        private CheckGame(string root, WorldSaveManager store) : base(store, new SettingsStore(Path.Combine(root, "settings.json")))
        { _root = root; _store = store; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content"); }
        protected override void Update(GameTime time) { }
        private void Input(bool left = false, bool right = false, float seconds = 0, params Keys[] keys)
        {
            var mouse = new MouseState(15, 20, 0, left ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Call("ProcessInput", new KeyboardState(keys), mouse, false, new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)), Point.Zero);
        }
        private void Press(Keys key) { Input(); Input(keys: new[] { key }); }
        private Color[] Pixels(int width, int height) { var pixels = new Color[width * height]; GraphicsDevice.GetBackBufferData(pixels); return pixels; }

        protected override void Draw(GameTime time)
        {
            Require(IsActive, "Pig graphics test window lost focus.");
            _frame++;
            switch (_frame)
            {
                case 1:
                    VerifyModels();
                    World world = PigChecks.Flat();
                    var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 2));
                    inventory.SetSlot(1, new ItemStack(Items.FromBlock(BlockType.Dirt), 1));
                    var save = new WorldSave(world, "Pig runtime", new Vector3(64.5f, 1, 64.5f), 0, -0.3f,
                        inventory, Health: 10, Pigs: new[] { new PigState(new Vector3(64.5f, 1, 61.5f), MathHelper.Pi, 10) });
                    Call("BeginWorld", _store.Create(save), save); Press(Keys.F2); break;
                case 2:
                    Press(Keys.F1); Press(Keys.F2); break;
                case 3: Input(); break;
                case 4: Press(Keys.F2); break;
                case 5:
                    Input(left: true, right: true); Require(Session.Player.Health == 13 && Session.Inventory[0].Count == 1,
                        "RMB did not heal/consume once without a block target, including while LMB is held."); break;
                case 6:
                    Input(right: true); Require(Session.Player.Health == 13 && Session.Inventory[0].Count == 1, "Holding RMB repeatedly ate food."); break;
                case 7:
                    Input(); Input(right: true); Require(Session.Player.Health == 16 && Session.Inventory[0].IsEmpty, "RMB release/repress did not consume last item."); break;
                case 8:
                    Input(left: true, seconds: 1f / 60); // TimeSpan quantization must span at least one full simulation tick.
                    _punchedHealth = Session.Mobs.Entities[0].Health;
                    Require(_punchedHealth is 8 or 9 && Session.Mining.Target == null, "Input path did not prioritize punching."); break;
                case 9:
                    for (int i = 0; i < 20; i++) Input(left: true, seconds: (float)SimulationClock.StepSeconds);
                    Require(Session.Mobs.Entities[0].Health == _punchedHealth, "Held LMB ignored attack cooldown."); break;
                case 10:
                    for (int i = 0; i < 1200 && Session.Mobs.Entities.Count > 0; i++)
                    {
                        Vector3 delta = Session.Mobs.Entities[0].Position + Vector3.Up * 0.5f - Session.Camera.Position;
                        Session.Camera.RestoreOrientation(MathF.Atan2(-delta.X, -delta.Z), MathF.Atan2(delta.Y, new Vector2(delta.X, delta.Z).Length()));
                        Input(left: true, seconds: (float)SimulationClock.StepSeconds);
                    }
                    Require(Session.Mobs.Entities.Count == 0 && Session.Items.Entities.Count == 1
                        && Session.Items.Entities[0].Item == Items.RawPorkchop, "Held LMB could not kill/drop porkchops.");
                    _store.Save(Session.Id, Session.Capture());
                    string id = Session.Id; Call("BeginWorld", id, _store.Load(id));
                    Require(Session.Mobs.Entities.Count == 0 && Session.Items.Entities.Count == 1 && Session.Inventory[0].IsEmpty
                        && Session.Player.Health == 16, "Reload resurrected a pig or changed food/player/drop state."); break;
                case 11:
                    Session.Player.ApplyDamage(20); Input(); Call("HandleMenuAction", MenuAction.Respawn);
                    Require(Session.Player.Health == 20 && Session.Inventory[0].IsEmpty && Session.Mobs.Entities.Count == 0,
                        "Respawn replenished food or pigs."); break;
                case 12:
                    Call("SetState", GameState.WorldCreation);
                    MenuController menu = Field<MenuController>("_menu");
                    foreach (char ch in "New pig world") menu.TextInput(ch);
                    Press(Keys.Tab); foreach (char ch in "173") menu.TextInput(ch);
                    Press(Keys.Tab); Press(Keys.Left); Call("HandleMenuAction", MenuAction.CreateWorld);
                    Require(State == GameState.Playing && Session.World.Size == WorldSize.Small && Session.Mobs.Entities.Count == 8
                        && Session.Inventory[13] == new ItemStack(Items.FromBlock(BlockType.WhiteWool), 64), "New-world menu did not grant mixed mobs and revised block stock exactly once.");
                    WorldSave initial = _store.Load(Session.Id); Require(initial.Pigs.Length + initial.Mobs.Length == 8 && initial.Mobs.Length > 0, "Initial new-world save omitted the shared mob population."); break;
                case 13:
                    Verified = true; Exit(); break;
            }
            base.Draw(time);
            if (_frame == 2) _hidden = Pixels(1280, 720);
            if (_frame is 3 or 4)
                Require(Pixels(1280, 720).AsSpan().SequenceEqual(_hidden), "Pig texture pass/screenshot corrupted following HUD-hidden frames.");
            // Decode only once consecutive frames have been checked, so GPU readback cannot mask the binding bug.
            if (_frame == 4)
            {
                string[] images = Directory.GetFiles(Path.Combine(_root, "screenshots"), "*.png");
                Require(images.Length == 3, "Pig screenshots repeated or were missed.");
                using var stream = File.OpenRead(images.OrderBy(File.GetLastWriteTimeUtc).Last());
                using var png = Texture2D.FromStream(GraphicsDevice, stream);
                var pixels = new Color[1280 * 720]; png.GetData(pixels);
                Require(png.Width == 1280 && png.Height == 720 && pixels.AsSpan().SequenceEqual(_hidden), "Pig screenshot pixels/resolution changed.");
            }
        }

        private void VerifyModels()
        {
            PassiveMobRenderer renderer = Field<PassiveMobRenderer>("_mobRenderer"); TextureAtlas atlas = Field<TextureAtlas>("_atlas");
            World world = PigChecks.Flat();
            var population = new PigPopulation(world, new[] { new PigState(new Vector3(10, 1, 10), MathHelper.Pi, 10) });
            var camera = new Camera { Position = new Vector3(10, 1.6f, 13) };
            using var target = new RenderTarget2D(GraphicsDevice, 320, 240, false, SurfaceFormat.Color, DepthFormat.Depth24);
            Color[] DrawPig()
            {
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.CornflowerBlue);
                GraphicsDevice.BlendState = BlendState.AlphaBlend; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone; GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
                GraphicsDevice.Textures[0] = atlas.Texture;
                renderer.Draw(camera, population);
                Require(GraphicsDevice.BlendState == BlendState.AlphaBlend && GraphicsDevice.DepthStencilState == DepthStencilState.Default
                    && GraphicsDevice.RasterizerState == RasterizerState.CullNone && GraphicsDevice.SamplerStates[0] == SamplerState.LinearClamp
                    && GraphicsDevice.Textures[0] == atlas.Texture && GraphicsDevice.Viewport.Width == 320, "Pig pass leaked graphics state.");
                GraphicsDevice.SetRenderTarget(null); var pixels = new Color[320 * 240]; target.GetData(pixels); return pixels;
            }
            Color[] normal = DrawPig();
            Require(normal.Count(c => c != Color.CornflowerBlue && c.R > c.G * 1.1f) > 100, "Pig model did not draw textured pink cuboids.");
            population.Entities[0].Damage(1, Vector3.Right); Color[] hurt = DrawPig();
            Require(!normal.AsSpan().SequenceEqual(hurt) && hurt.Count(c => c.R > c.G * 2) > normal.Count(c => c.R > c.G * 2), "Pig hurt flash was not rendered.");
            for (int i = 0; i < 1200 && (!population.Entities[0].Walking || MathF.Abs(MathF.Sin(population.Entities[0].WalkPhase)) < 0.6f
                || population.Entities[0].HurtSeconds > 0); i++) population.Update((float)SimulationClock.StepSeconds);
            Require(population.Entities[0].Walking, "Pig never walked for animation verification.");
            camera.Position = population.Entities[0].Position + new Vector3(0, 0.6f, 3);
            Color[] walking = DrawPig();
            // Same feet position/yaw/health, but no temporary limb phase or walking state.
            population = new PigPopulation(world, population.Capture());
            Color[] standing = DrawPig();
            Require(!walking.AsSpan().SequenceEqual(standing), "Moving pig leg animation changed no rendered pixels.");
            GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.CornflowerBlue);
            using (var depthOnly = new BlendState { ColorWriteChannels = ColorWriteChannels.None })
            using (var effect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true })
            {
                GraphicsDevice.BlendState = depthOnly; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                var quad = new[] { new VertexPositionColor(new(-1,-1,0.01f),Color.White), new VertexPositionColor(new(-1,1,0.01f),Color.White),
                    new VertexPositionColor(new(1,-1,0.01f),Color.White), new VertexPositionColor(new(1,1,0.01f),Color.White) };
                foreach (EffectPass pass in effect.CurrentTechnique.Passes) { pass.Apply(); GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2); }
                renderer.Draw(camera, population);
            }
            GraphicsDevice.SetRenderTarget(null); var obscured = new Color[320 * 240]; target.GetData(obscured);
            Require(obscured.All(c => c == Color.CornflowerBlue), "Pig rendered through foreground depth.");
            var inventory = new Inventory(); inventory.SetSlot(0, new ItemStack(Items.RawPorkchop, 1));
            GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.CornflowerBlue);
            Field<FirstPersonRenderer>("_firstPerson").Draw(GameState.Playing, inventory);
            GraphicsDevice.SetRenderTarget(null); var held = new Color[320 * 240]; target.GetData(held);
            Require(held.Count(c => c != Color.CornflowerBlue) > 20 && held.Count(c => c == Color.CornflowerBlue) > held.Length / 2,
                "Held food sprite/cutout failed.");
            using (var itemRenderer = new DroppedItemRenderer(GraphicsDevice, atlas))
            {
                var drops = new DroppedItems(world); drops.Spawn(Items.RawPorkchop, 2, new Vector3(10, 1.6f, 10));
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.CornflowerBlue);
                GraphicsDevice.BlendState = BlendState.Opaque; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise; GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
                itemRenderer.Draw(camera, drops); GraphicsDevice.SetRenderTarget(null);
                var pixels = new Color[320 * 240]; target.GetData(pixels);
                Require(pixels.Count(c => c != Color.CornflowerBlue) > 5, "Dropped food was invisible.");
            }
            var iconPixels = new Color[16 * 16]; atlas.Porkchop.GetData(iconPixels);
            Require(iconPixels.Any(c => c.A == 0) && iconPixels.Any(c => c.A == 255), "Food icon lost transparent/opaque pixels.");
            Console.WriteLine("PASS: Textured cuboid pig, hurt tint, animated legs, terrain depth occlusion, restored graphics state, and held/dropped food cutouts.");
        }
    }
}
