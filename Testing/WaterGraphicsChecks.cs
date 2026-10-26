using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WaterGraphicsChecks
{
    public static int Run()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-water-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var game = new CheckGame(root); game.Run();
            Require(game.Verified, "Water graphics checks did not complete.");
            Console.WriteLine("PASS: Water GPU alpha/depth/state, unchanged opaque pass, repeated F1/F2/F3 frames, PNG pixels/resizing, swimming input, and new lakes.");
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
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly string _root;
        private readonly List<(string File, Color[] Pixels, int Width, int Height)> _shots = new();
        private int _frame;
        private Color[] _visible, _hidden, _resized;
        private readonly WorldSaveManager _store;
        public bool Verified { get; private set; }
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private object Call(string name, params object[] args) => typeof(Game1).GetMethod(name, Private).Invoke(this, args);
        private GameSession Session => Field<GameSession>("_session");
        public CheckGame(string root) : this(root, new WorldSaveManager(Path.Combine(root, "Worlds"))) { }
        private CheckGame(string root, WorldSaveManager store) : base(store, new SettingsStore(Path.Combine(root, "settings.json")))
        { _root = root; _store = store; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content"); }
        protected override void Update(GameTime time) { }
        private void Input(float elapsed = 0, params Keys[] keys) => Call("ProcessInput", new KeyboardState(keys), default(MouseState), false,
            new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(elapsed)), Point.Zero);
        private void Press(Keys key) { Input(); Input(keys: new[] { key }); }
        private Color[] Pixels()
        {
            var pixels = new Color[GraphicsDevice.Viewport.Width * GraphicsDevice.Viewport.Height]; GraphicsDevice.GetBackBufferData(pixels); return pixels;
        }
        protected override void Draw(GameTime time)
        {
            Require(IsActive, "Water graphics window lost focus.");
            _frame++;
            bool capture = _frame is 1 or 2 or 4 or 6 or 7 or 8 or 11;
            switch (_frame)
            {
                case 1:
                    VerifyGpu();
                    World world = WaterChecks.Floor();
                    for (int z = 55; z <= 66; z++) for (int x = 58; x <= 70; x++)
                    for (int y = 1; y <= 3; y++) world.SetGeneratedBlock(x, y, z, BlockType.WaterSource);
                    var inventory = Inventory.CreateNewWorld();
                    var save = new WorldSave(world, "Water runtime", new Vector3(64.5f, 3, 70.5f), 0, -0.3f, inventory);
                    Call("BeginWorld", _store.Create(save), save); break;
                case 2: Press(Keys.F1); break;
                case 5: Press(Keys.F1); break;
                case 6: Press(Keys.F3); break;
                case 7: Press(Keys.F1); break;
                case 8:
                    var graphics = Field<GraphicsDeviceManager>("_graphics");
                    graphics.PreferredBackBufferWidth = 801; graphics.PreferredBackBufferHeight = 603; graphics.ApplyChanges(); break;
                case 10:
                    foreach (var shot in _shots)
                    {
                        using var stream = File.OpenRead(shot.File); using var png = Texture2D.FromStream(GraphicsDevice, stream);
                        var data = new Color[shot.Width * shot.Height]; png.GetData(data);
                        Require(png.Width == shot.Width && png.Height == shot.Height && data.AsSpan().SequenceEqual(shot.Pixels), "Water PNG dimensions/orientation/pixels differ from final frame.");
                    }
                    // Decode only after consecutive-frame comparisons: texture readback can mask a stale GL binding.
                    World wet = WaterChecks.Floor();
                    for (int z = 60; z <= 70; z++) for (int x = 60; x <= 70; x++) for (int y = 1; y <= 5; y++) wet.SetGeneratedBlock(x, y, z, BlockType.WaterSource);
                    var swimSave = new WorldSave(wet, "Swimming input", new Vector3(64.5f, 1, 64.5f), 0, 0);
                    Call("BeginWorld", _store.Create(swimSave), swimSave);
                    for (int i = 0; i < 120; i++) Input(1f / 60, Keys.Space);
                    Require(Session.Player.Position.Y > 5 && Session.Player.Health == 20, "Held Space input did not swim upward.");
                    break;
                case 11:
                    World lake = World.CreateNew(173, WorldSize.Small); Point3 surface = default;
                    foreach (Chunk chunk in lake.Chunks)
                    {
                        if (surface != default) break;
                        for (int z = chunk.OriginZ; z < chunk.OriginZ + 16 && surface == default; z++)
                        for (int x = chunk.OriginX; x < chunk.OriginX + 16 && surface == default; x++)
                        for (int y = 1; y < World.Height; y++)
                            if (Water.IsSource(lake.GetBlock(x, y, z)) && !Water.IsWater(lake.GetBlock(x, y + 1, z))) { surface = new Point3(x, y, z); break; }
                    }
                    var lakeSave = new WorldSave(lake, "Natural lake runtime", new Vector3(surface.X + 0.5f, surface.Y + 5, surface.Z + 8.5f), 0, -0.6f);
                    Call("BeginWorld", _store.Create(lakeSave), lakeSave); break;
                case 12:
                    var reloaded = _store.Load(Session.Id); Require(reloaded.World.GetBlock((int)Session.Player.Position.X, 0, (int)Session.Player.Position.Z) == BlockType.Bedrock,
                        "Natural lake new-world save changed bedrock.");
                    Verified = true; Exit(); break;
            }
            if (capture) Press(Keys.F2);
            base.Draw(time);
            if (_frame == 1) _visible = Pixels();
            if (_frame == 2) { _hidden = Pixels(); Require(!_hidden.AsSpan().SequenceEqual(_visible), "F1 failed to hide the HUD over water."); }
            if (_frame is 3 or 4 or 7) Require(Pixels().AsSpan().SequenceEqual(_hidden), "Water/terrain changed after HUD-hidden screenshot.");
            if (_frame == 5) Require(Pixels().AsSpan().SequenceEqual(_visible), "Restoring HUD changed water/held rendering.");
            if (_frame == 8) _resized = Pixels();
            if (_frame == 9) Require(Pixels().AsSpan().SequenceEqual(_resized), "Resized water screenshot corrupted following frame.");
            if (capture)
            {
                string[] files = Directory.GetFiles(Path.Combine(_root, "screenshots"), "*.png"); Array.Sort(files, StringComparer.Ordinal);
                Require(files.Length == _shots.Count + 1, "F2 missed or repeated a water screenshot.");
                _shots.Add((files[^1], Pixels(), GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height));
            }
            if (_frame == 12)
            {
                var shot = _shots[^1]; using var stream = File.OpenRead(shot.File); using var png = Texture2D.FromStream(GraphicsDevice, stream);
                var data = new Color[shot.Width * shot.Height]; png.GetData(data);
                Require(data.AsSpan().SequenceEqual(shot.Pixels), "Natural lake screenshot differs from composed frame.");
            }
        }

        private void VerifyGpu()
        {
            var world = WaterChecks.Floor();
            for (int z = 10; z <= 20; z++) for (int x = 10; x <= 20; x++) world.SetGeneratedBlock(x, 1, z, BlockType.WaterSource);
            TextureAtlas atlas = Field<TextureAtlas>("_atlas");
            using var renderer = new WaterRenderer(GraphicsDevice, atlas, world);
            using var terrain = new WorldRenderer(GraphicsDevice, atlas, world);
            using var target = new RenderTarget2D(GraphicsDevice, 320, 240, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Camera { Position = new Vector3(15.5f, 4, 22) }; camera.RestoreOrientation(0, -0.5f);
            Color[] Draw(Color background, bool opaque, bool water)
            {
                GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(background);
                if (opaque) terrain.Draw(camera, null);
                GraphicsDevice.BlendState = BlendState.AlphaBlend; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise; GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
                GraphicsDevice.Textures[0] = atlas.Texture;
                if (water) renderer.Draw(camera);
                Require(GraphicsDevice.BlendState == BlendState.AlphaBlend && GraphicsDevice.DepthStencilState == DepthStencilState.Default
                    && GraphicsDevice.RasterizerState == RasterizerState.CullCounterClockwise && GraphicsDevice.SamplerStates[0] == SamplerState.LinearClamp
                    && GraphicsDevice.Textures[0] == atlas.Texture && GraphicsDevice.Viewport.Width == 320, "Water pass leaked graphics state.");
                GraphicsDevice.SetRenderTarget(null); var pixels = new Color[320 * 240]; target.GetData(pixels);
                // Test-only render-target readback has the same native GL binding side effect as PNG encoding.
                GraphicsDevice.Textures[0] = null; GraphicsDevice.Textures[0] = atlas.Texture;
                return pixels;
            }
            Color[] black = Draw(Color.Black, false, true), white = Draw(Color.White, false, true);
            int middle = 120 * 320 + 160;
            int alphaDifference = white[middle].R - black[middle].R;
            Require(black[middle].B > black[middle].R && alphaDifference > 90 && alphaDifference < 130,
                "Water is not blue and translucent with the expected alpha.");
            Color[] ground = Draw(Color.CornflowerBlue, true, false), composed = Draw(Color.CornflowerBlue, true, true);
            Require(ground[middle] != composed[middle] && composed[middle].B > ground[middle].B, "Water did not blend over its visible lake bed.");
            for (int x = 10; x <= 20; x++) for (int y = 1; y <= 5; y++) world.SetBlock(x, y, 21, BlockType.Stone);
            camera.Position = new Vector3(15.5f, 4, 24); // Keep the foreground face beyond the camera's near plane.
            Color[] wall = Draw(Color.CornflowerBlue, true, false), behind = Draw(Color.CornflowerBlue, true, true);
            Require(wall[middle] == behind[middle], $"Water rendered through an opaque foreground wall: {wall[middle]} versus {behind[middle]}.");
            // A transparent pass must not occlude later geometry through depth writes.
            for (int x = 10; x <= 20; x++) for (int y = 1; y <= 5; y++) world.SetBlock(x, y, 21, BlockType.Air);
            camera.Position = new Vector3(15.5f, 4, 22);
            Color[] floor = Draw(Color.Black, true, false);
            GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.Black); renderer.Draw(camera);
            GraphicsDevice.BlendState = BlendState.Opaque; GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            terrain.Draw(camera, null); GraphicsDevice.SetRenderTarget(null);
            var after = new Color[320 * 240]; target.GetData(after);
            Require(after[middle] == floor[middle], "Water pass wrote depth and occluded the later lake bed.");
        }
    }
}
