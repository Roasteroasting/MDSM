using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
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

// Sustained real Game1 update/draw path, synthetic input, isolated saves/settings.
internal static class GameplayPerformanceChecks
{
    public static int Run(WorldSize size)
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-performance-" + Guid.NewGuid().ToString("N"));
        try { using var game = new Benchmark(root, size); game.Run(); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped test root.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Benchmark : Game1
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private readonly WorldSize _size;
        private Task<WorldSave> _generation;
        private GameSession _session;
        private WorldRenderer _renderer;
        private Action<KeyboardState, MouseState, bool, GameTime, Point> _input;
        private readonly Stopwatch _clock = new();
        private readonly KeyboardState _walking = new(Keys.W);
        private readonly double[] _frames = new double[100000];
        private int _count, _phase;
        private double _updateMs, _drawMs;
        private long _allocations, _allocationStart;
        private Vector3 _startPosition;
        public Benchmark(string root, WorldSize size) : base(new WorldSaveManager(Path.Combine(root, "Worlds")), new SettingsStore(Path.Combine(root, "settings.json")))
        {
            _size = size; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
            GraphicsDeviceManager graphics = Field<GraphicsDeviceManager>("_graphics");
            graphics.PreferredBackBufferWidth = 1920; graphics.PreferredBackBufferHeight = 1080;
            Window.Title = "MDSM actual gameplay benchmark (synthetic input)";
        }
        protected override void LoadContent()
        {
            base.LoadContent(); Field<GameSettings>("_settings").UncappedFps = true;
            _input = typeof(Game1).GetMethod("ProcessInput", Private).CreateDelegate<Action<KeyboardState, MouseState, bool, GameTime, Point>>(this);
            _generation = Task.Run(() =>
            {
                World world = World.CreateNew(173, _size);
                var population = new PassiveMobPopulation(world, PassiveMobPopulation.Generate(world));
                return new WorldSave(world, "Performance check", world.Spawn, 0, -.25f, Inventory.CreateNewWorld(),
                    Pigs: population.CapturePigs(), Mobs: population.CaptureOthers(), Population: population.CapturePopulation());
            });
        }
        protected override void Update(GameTime time)
        {
            _allocationStart = GC.GetAllocatedBytesForCurrentThread();
            if (_session == null)
            {
                if (!_generation.IsCompleted) return;
                WorldSave save = _generation.GetAwaiter().GetResult(); _generation = null;
                typeof(Game1).GetMethod("BeginWorld", Private).Invoke(this, new object[] { Guid.NewGuid().ToString("N"), save });
                _session = Field<GameSession>("_session"); _renderer = Field<WorldRenderer>("_renderer");
            }
            if (!_clock.IsRunning)
            {
                if (!_renderer.PrepareInitialMeshes(_session.Camera)) return;
                _clock.Restart(); _startPosition = _session.Player.Position;
            }
            long start = Stopwatch.GetTimestamp();
            float yaw = (int)(_clock.Elapsed.TotalSeconds / 4) % 4 * MathHelper.PiOver2;
            _session.Camera.RestoreOrientation(yaw > MathHelper.Pi ? yaw - MathHelper.TwoPi : yaw, -.25f);
            _input(_phase == 0 ? default : _walking, default, false, time, Point.Zero);
            _updateMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (State != GameState.Playing) throw new InvalidOperationException("Gameplay benchmark unexpectedly left active gameplay.");
        }
        protected override void Draw(GameTime time)
        {
            if (!_clock.IsRunning) { GraphicsDevice.Clear(WorldRenderer.SkyColor); return; }
            long start = Stopwatch.GetTimestamp(); base.Draw(time);
            _drawMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (_count < _frames.Length) _frames[_count] = time.ElapsedGameTime.TotalMilliseconds;
            _count++; _allocations += GC.GetAllocatedBytesForCurrentThread() - _allocationStart;
            if (_clock.Elapsed.TotalSeconds < 15) return;
            int samples = Math.Min(_count, _frames.Length); Array.Sort(_frames, 0, samples);
            Console.WriteLine($"GAMEPLAY {_size}, seed=173, 1080p uncapped, phase={(_phase == 0 ? "stationary turns" : "walking turns")}, fps={_count / _clock.Elapsed.TotalSeconds:F1}, p95FrameMs={_frames[(int)(samples * .95)]:F2}, updateMs={_updateMs / _count:F3}, drawCpuMs={_drawMs / _count:F3}, allocatedBytes/frame={_allocations / _count}, travel={Vector3.Distance(_session.Player.Position, _startPosition):F1}, resident={_renderer.ResidentChunks}, drawn={_renderer.VisibleChunks}, estimatedTerrainBufferBytes={_renderer.BufferBytes}, mobs={_session.Mobs.Entities.Count}, workingSet={Environment.WorkingSet}");
            _phase++; _count = 0; _updateMs = _drawMs = 0; _allocations = 0; _startPosition = _session.Player.Position; _clock.Restart();
            if (_phase == 2) Exit();
        }
    }
}
