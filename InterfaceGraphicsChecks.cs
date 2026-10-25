using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MDSM;

// Optional real DesktopGL verification; synthetic input, isolated saves/preferences.
internal static class InterfaceGraphicsChecks
{
    public static int Run()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-interface-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var game = new CheckGame(root);
            game.Run();
            Require(game.Verified, "Graphics checks did not finish.");
            Console.WriteLine("PASS: Final-frame PNG pixels/dimensions/orientation, F1/F3, held items, menus, resizing, FPS modes, and screenshot failures.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped root.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CheckGame : Game1
    {
        private readonly string _root;
        private int _frame, _shots;
        private bool _capture;
        private Color[] _withHud, _hidden;
        private Color[] _regressionHidden, _regressionVisible;
        private readonly List<(string Path, Color[] Pixels, int Width, int Height)> _regressionShots = new();
        public bool Verified { get; private set; }
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private object Call(string name, params object[] args) => typeof(Game1).GetMethod(name, Private).Invoke(this, args);
        private GameSession Session => Field<GameSession>("_session");
        private MenuController Menu => Field<MenuController>("_menu");
        private GameSettings Settings => Field<GameSettings>("_settings");
        private PresentationInput Presentation => Field<PresentationInput>("_presentation");
        private string Shots => Path.Combine(_root, "screenshots");

        public CheckGame(string root) : base(new WorldSaveManager(Path.Combine(root, "Worlds")), new SettingsStore(Path.Combine(root, "settings.json")))
        {
            _root = root;
            Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
            Components.Add(new FrameMarkers(this, () => _frame));
        }

        protected override void Update(GameTime time) { } // No physical keyboard/mouse input.
        private void Input(params Keys[] keys) => Call("ProcessInput", new KeyboardState(keys), default(MouseState), false,
            new GameTime(TimeSpan.Zero, TimeSpan.Zero), Point.Zero);
        private void Press(params Keys[] keys) { Input(); Input(keys); }
        private void Click(int button)
        {
            Input();
            Rectangle bounds = (Rectangle)typeof(MenuController).GetMethod("ButtonBounds", Private).Invoke(Menu, new object[] { GraphicsDevice.Viewport, button });
            var mouse = new MouseState(bounds.Center.X, bounds.Center.Y, 0, ButtonState.Pressed, ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Call("ProcessInput", default(KeyboardState), mouse, false, new GameTime(), Point.Zero);
        }
        private void Capture() { Press(Keys.F2); _capture = true; }
        private void Resize(int width, int height)
        {
            var graphics = Field<GraphicsDeviceManager>("_graphics");
            graphics.PreferredBackBufferWidth = width; graphics.PreferredBackBufferHeight = height;
            graphics.ApplyChanges();
        }
        private void Fixture()
        {
            var world = new World(173, WorldSize.Small, false);
            for (int z = 45; z < 78; z++)
            for (int x = 45; x < 78; x++) world.SetBlock(x, 0, z, BlockType.Bedrock);
            world.SetBlock(64, 2, 61, BlockType.Stone);
            var save = new WorldSave(world, "Interface check", new Vector3(64.5f, 1, 64.5f), 0, 0, Inventory.CreateStarter());
            var store = Field<WorldSaveManager>("_saves");
            Call("BeginWorld", store.Create(save), save);
        }

        protected override void Draw(GameTime time)
        {
            _frame++;
            Require(IsActive, "Graphics check window lost focus.");
            if (_frame >= 18) { DrawScreenshotRegression(time); return; }
            switch (_frame)
            {
                case 1:
                    Click(2); Require(State == GameState.Settings, "Main menu Settings entry failed.");
                    Press(Keys.End); Require(Settings.FpsCap == 360, "Slider no longer reaches 360.");
                    Press(Keys.Down); Press(Keys.Space); Require(Settings.UncappedFps, "Uncapped toggle failed.");
                    Click(0); Require(State == GameState.Controls, "Controls entry failed.");
                    Press(Keys.End); Require(FieldScroll() > 0, "Controls cannot scroll to last binding.");
                    Click(0); Require(State == GameState.Settings, "Controls Back skipped Settings.");
                    Press(Keys.Tab); Press(Keys.Tab); Press(Keys.Tab); Press(Keys.Enter);
                    Require(State == GameState.About, "Keyboard About entry failed.");
                    Capture(); break;
                case 2:
                    Input(Keys.F2); break; // Holding must not request a second PNG.
                case 3:
                    Press(Keys.Escape); Require(State == GameState.Settings, "About Escape skipped Settings.");
                    Click(2); Require(State == GameState.MainMenu, "Settings Back lost main-menu origin.");
                    Require(Settings.FpsCap == 360 && Settings.UncappedFps, "Information navigation changed FPS preferences.");
                    GameSettings persisted = new SettingsStore(Path.Combine(_root, "settings.json")).Load();
                    Require(persisted.FpsCap == 360 && persisted.UncappedFps, "Settings navigation did not preserve persisted preferences.");
                    Settings.UncappedFps = false; Settings.FpsCap = 60;
                    Fixture(); Capture(); break;
                case 4:
                    Input(Keys.F2); break;
                case 5:
                    Settings.FpsCap = 144; Press(Keys.F3);
                    Require(Presentation.DebugVisible, "F3 failed to enable debug."); Capture(); break;
                case 6:
                    Settings.FpsCap = 240; Press(Keys.F1); Press(Keys.D2);
                    Require(!Presentation.HudVisible && Presentation.DebugEnabled && Session.Inventory.SelectedSlot == 1,
                        "F1 disabled selection or reset debug."); Capture(); break;
                case 7:
                    Settings.FpsCap = 360; Press(Keys.F3); Press(Keys.D3);
                    Require(!Presentation.DebugEnabled && !Presentation.DebugVisible, "Hidden F3 toggle failed."); Capture(); break;
                case 8:
                    Settings.UncappedFps = true; Press(Keys.F1);
                    Session.Inventory.SetSlot(8, default); Press(Keys.D9);
                    Require(Presentation.HudVisible && !Presentation.DebugVisible, "Restoring HUD reset F3."); Capture(); break;
                case 9:
                    Resize(801, 603); Press(Keys.F3); Capture(); break;
                case 10:
                    Resize(320, 240); Press(Keys.Escape); Click(3); Click(0);
                    Require(State == GameState.Controls && IsMouseVisible, "Pause Settings Controls resumed or captured gameplay.");
                    Press(Keys.End); Require(FieldScroll() > 0, "Small Controls cannot scroll."); Capture(); break;
                case 11:
                    Click(0); Click(1); Press(Keys.End);
                    Require(State == GameState.About && IsMouseVisible, "Pause About entry/cursor failed."); Capture(); break;
                case 12:
                    Click(0); Click(2); Require(State == GameState.Paused, "Settings Back lost pause origin.");
                    Require(Settings.FpsCap == 360 && Settings.UncappedFps && Session.Player.Position == new Vector3(64.5f, 1, 64.5f),
                        "Menus changed FPS preferences or simulated gameplay.");
                    Click(0); Require(State == GameState.Playing && !IsMouseVisible, "Resume failed to capture mouse.");
                    Resize(1280, 720); Press(Keys.D1); Capture(); break;
                case 13:
                    Press(Keys.E); Require(State == GameState.Inventory, "Inventory failed."); Capture(); break;
                case 14:
                    Session.Player.ApplyDamage(20); Input(); Require(State == GameState.Death, "Death screen failed."); Capture(); break;
                case 15:
                    Directory.Move(Shots, Shots + ".saved"); File.WriteAllText(Shots, "block directory creation");
                    Press(Keys.F2); break;
                case 16:
                    string message = (string)typeof(MenuController).GetField("_message", Private).GetValue(Menu);
                    Require(State == GameState.Death && message.StartsWith("SCREENSHOT NOT SAVED.", StringComparison.Ordinal),
                        "Screenshot failure crashed or lacked feedback.");
                    Input(Keys.F2); File.Delete(Shots); Directory.Move(Shots + ".saved", Shots); break;
                case 17:
                    Capture(); break;
            }
            base.Draw(time);
            string[] files = Directory.Exists(Shots) ? Directory.GetFiles(Shots, "*.png") : Array.Empty<string>();
            if (_frame is 15 or 16) return; // Intentionally unavailable/restored directory.
            Require(files.Length == _shots + (_capture ? 1 : 0), "Screenshot press repeated, overwrote a file, or was missed.");
            if (!_capture) return;
            _capture = false; _shots++;
            int width = GraphicsDevice.PresentationParameters.BackBufferWidth, height = GraphicsDevice.PresentationParameters.BackBufferHeight;
            Require(width == Window.ClientBounds.Width && height == Window.ClientBounds.Height, "Backbuffer does not match client area.");
            var expected = new Color[width * height]; GraphicsDevice.GetBackBufferData(expected);
            using (var stream = File.OpenRead(files.OrderBy(path => File.GetLastWriteTimeUtc(path)).Last()))
            using (var png = Texture2D.FromStream(GraphicsDevice, stream))
            {
                Require(png.Width == width && png.Height == height, "Screenshot was resized or cropped.");
                var actual = new Color[expected.Length]; png.GetData(actual);
                Require(actual.AsSpan().SequenceEqual(expected), "PNG differs from composed backbuffer: orientation, stale frame, or pixel conversion.");
            }
            if (_frame == 3) _withHud = expected;
            if (_frame == 6)
            {
                _hidden = expected;
                Require(expected[height / 2 * width + width / 2] != Color.White, "F1 left the crosshair visible.");
                Require(!expected.AsSpan().SequenceEqual(_withHud), "F1 did not change rendering.");
                for (int y = 8; y < 80; y++)
                for (int x = 8; x < 590; x++) Require(expected[y * width + x] == _withHud[y * width + x], "Permanent controls overlay remains.");
            }
            if (_frame == 7)
                Require(expected.AsSpan(width * 5).SequenceEqual(_hidden.AsSpan(width * 5)), "Hidden HUD/debug/held item still rendered.");
            Console.WriteLine($"PASS: Frame {_frame}, {State}, {width}x{height}, HUD {Presentation.HudVisible}, debug {Presentation.DebugEnabled}, exact PNG pixels.");
        }

        private int FieldScroll() => (int)typeof(MenuController).GetField("_informationScroll", Private).GetValue(Menu);

        private void DrawScreenshotRegression(GameTime time)
        {
            // SpriteBatch markers and PNG decoding both change texture bindings.
            // Keep them out of consecutive frames, otherwise they mask this bug.
            if (_frame == 18)
            {
                Components.OfType<FrameMarkers>().Single().Visible = false;
                Fixture();
                if (Presentation.DebugEnabled) Press(Keys.F3);
                if (Presentation.HudVisible) Press(Keys.F1);
            }
            if (_frame is 23 or 27 or 30) Press(Keys.F1);
            if (_frame == 31) { Resize(801, 603); Press(Keys.F1); }
            if (_frame == 36) { Directory.Move(Shots, Shots + ".saved"); File.WriteAllText(Shots, "blocked"); }
            bool capture = _frame is 19 or 21 or 25 or 28 or 30 or 32 or 34 or 38;
            if (capture || _frame == 36) Press(Keys.F2);
            else Input();

            base.Draw(time);
            int width = GraphicsDevice.PresentationParameters.BackBufferWidth, height = GraphicsDevice.PresentationParameters.BackBufferHeight;
            var pixels = new Color[width * height];
            GraphicsDevice.GetBackBufferData(pixels);
            if (_frame is 18 or 31) _regressionHidden = pixels;
            if (_frame == 24) _regressionVisible = pixels;
            // Allow one frame when showing the HUD to obtain a clean visible baseline.
            if (_frame != 23 && _frame != 40)
            {
                Color[] expected = Presentation.HudVisible ? _regressionVisible : _regressionHidden;
                Require(expected != null && pixels.AsSpan().SequenceEqual(expected),
                    $"Screenshot changed world rendering on frame {_frame}, HUD {Presentation.HudVisible}.");
            }
            if (capture)
            {
                string[] files = Directory.GetFiles(Shots, "*.png");
                Require(files.Length == ++_shots, "Regression screenshot was missed or repeated.");
                _regressionShots.Add((files.OrderBy(File.GetLastWriteTimeUtc).Last(), pixels, width, height));
            }
            if (_frame == 37) { File.Delete(Shots); Directory.Move(Shots + ".saved", Shots); }
            if (_frame != 40) return;

            // Only decode after the following frames have been compared to their
            // pre-capture baselines, so texture readback cannot repair/mask a failure.
            foreach (var shot in _regressionShots)
            {
                using var stream = File.OpenRead(shot.Path);
                using var png = Texture2D.FromStream(GraphicsDevice, stream);
                Require(png.Width == shot.Width && png.Height == shot.Height, "Regression PNG resolution changed.");
                var actual = new Color[shot.Pixels.Length]; png.GetData(actual);
                Require(actual.AsSpan().SequenceEqual(shot.Pixels), "Regression PNG pixels changed.");
            }
            Console.WriteLine("PASS: Consecutive unmodified frames remain pixel-identical after repeated/alternating F1/F2, resize, and failed capture; all 8 regression PNGs match.");
            Verified = true; Exit();
        }
    }

    // Asymmetric final-pass corner pixels plus a frame marker detect flipped rows,
    // incomplete composition, and stale captures. These exist only in this check.
    private sealed class FrameMarkers : DrawableGameComponent
    {
        private readonly Func<int> _frame;
        private SpriteBatch _sprites;
        private Texture2D _pixel;
        public FrameMarkers(Game game, Func<int> frame) : base(game) => _frame = frame;
        protected override void LoadContent()
        {
            _sprites = new SpriteBatch(GraphicsDevice); _pixel = new Texture2D(GraphicsDevice, 1, 1); _pixel.SetData(new[] { Color.White });
        }
        public override void Draw(GameTime time)
        {
            Viewport v = GraphicsDevice.Viewport;
            _sprites.Begin(samplerState: SamplerState.PointClamp);
            _sprites.Draw(_pixel, new Rectangle(0, 0, 7, 3), Color.Red);
            _sprites.Draw(_pixel, new Rectangle(v.Width - 5, 0, 5, 4), Color.Lime);
            _sprites.Draw(_pixel, new Rectangle(0, v.Height - 4, 3, 4), Color.Blue);
            _sprites.Draw(_pixel, new Rectangle(v.Width - 4, v.Height - 2, 4, 2), Color.Yellow);
            _sprites.Draw(_pixel, new Rectangle(v.Width / 2, 0, 7, 3), new Color(_frame(), 37, 93));
            _sprites.End();
        }
        protected override void UnloadContent() { _sprites.Dispose(); _pixel.Dispose(); }
    }
}
