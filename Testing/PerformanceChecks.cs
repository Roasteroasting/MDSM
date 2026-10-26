using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

// Opt-in, actual WorldRenderer benchmark: full generated storage, no reduced terrain fixture.
// CPU submission timings include driver stalls, not GPU timer-query measurements.
internal static class PerformanceChecks
{
    public static int Run(WorldSize size)
    {
        try { using var game = new Benchmark(size); game.Run(); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private sealed class Benchmark : Game
    {
        private readonly WorldSize _size;
        private Task<World> _generation;
        private World _world;
        private WorldRenderer _renderer;
        private PassiveMobRenderer _mobs;
        private PassiveMobPopulation _population;
        private TextureAtlas _atlas;
        private readonly Camera _camera = new();
        private readonly Stopwatch _stageTime = new();
        private int _stage = -1, _frames;
        private double _drawMs, _updateMs;
        private long _allocation, _allocations;
        private long _steps, _calls, _triangles;
        private readonly SimulationClock _clock = new();
        private readonly double[] _samples = new double[100000];
        public Benchmark(WorldSize size)
        {
            _size = size;
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 1920, PreferredBackBufferHeight = 1080,
                PreferredDepthStencilFormat = DepthFormat.Depth24, SynchronizeWithVerticalRetrace = false };
            IsFixedTimeStep = false; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
            Window.Title = "MDSM uncapped 1080p renderer benchmark";
        }
        protected override void LoadContent()
        {
            Console.WriteLine($"BENCH {_size}, seed=173, 1920x1080, uncapped, GL={RendererName()}");
            _atlas = new TextureAtlas(Content); _mobs = new PassiveMobRenderer(GraphicsDevice, Content);
            _generation = Task.Run(() => World.CreateNew(173, _size));
        }
        protected override void Update(GameTime time)
        {
            _allocation = GC.GetAllocatedBytesForCurrentThread();
            if (_stage < 0) return;
            long start = Stopwatch.GetTimestamp();
            if (_stage == 3)
            {
                int steps = _clock.Advance(time.ElapsedGameTime.TotalSeconds);
                _steps += steps;
                for (int i = 0; i < steps; i++)
                {
                    _population.Update((float)SimulationClock.StepSeconds, _camera.Position);
                    _world.Fluids.Update((float)SimulationClock.StepSeconds);
                    _world.Gravity.Update((float)SimulationClock.StepSeconds);
                }
            }
            _updateMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        protected override void Draw(GameTime time)
        {
            GraphicsDevice.Clear(WorldRenderer.SkyColor);
            if (_stage < 0)
            {
                if (_renderer == null && !_generation.IsCompleted) return;
                if (_renderer == null)
                {
                    _world = _generation.GetAwaiter().GetResult(); _generation = null;
                    _population = new PassiveMobPopulation(_world, PassiveMobPopulation.Generate(_world));
                    _camera.Position = _world.Spawn + Vector3.Up * 12;
                    _camera.RestoreOrientation(0, -0.25f);
                    _renderer = new WorldRenderer(GraphicsDevice, _atlas, _world);
                }
                if (!_renderer.PrepareInitialMeshes())
                {
                    // Safety stop for the old all-map renderer on limited hardware. Report, never hide it.
                    if (_renderer.BufferBytes > 3L * 1024 * 1024 * 1024)
                    { Console.WriteLine($"ABORT baseline allocation: prepared={_renderer.PreparedChunks}/{_world.ChunkCount}, estimated terrain bytes={_renderer.BufferBytes}; incomplete, no FPS claim."); Exit(); }
                    return;
                }
                Console.WriteLine($"READY totalChunks={_world.ChunkCount}, resident={_renderer.ResidentChunks}, meshed={_renderer.MeshedChunks}, gpu={_renderer.GpuChunks}, terrainVertices={_renderer.VertexCount}, estimatedTerrainBufferBytes={_renderer.BufferBytes}, mobs={_population.Entities.Count}, managedBytes={GC.GetTotalMemory(false)}, workingSet={Environment.WorkingSet}");
                _stage = 0; _stageTime.Restart(); return;
            }
            long start = Stopwatch.GetTimestamp();
            // Each isolated stage traverses the same four directions on a fixed two-second schedule.
            float yaw = (int)(_stageTime.Elapsed.TotalSeconds / 2) % 4 * MathHelper.PiOver2;
            _camera.RestoreOrientation(yaw == MathHelper.PiOver2 * 3 ? -MathHelper.PiOver2 : yaw, -0.25f);
            if (_stage != 2) _renderer.Draw(_camera, null);
            if (_stage >= 1 && _stage != 2) _renderer.DrawWater(_camera);
            if (_stage >= 2) _mobs.Draw(_camera, _population);
            _calls += GraphicsDevice.Metrics.DrawCount; _triangles += GraphicsDevice.Metrics.PrimitiveCount;
            _drawMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (_frames < _samples.Length) _samples[_frames] = time.ElapsedGameTime.TotalMilliseconds;
            _frames++; _allocations += GC.GetAllocatedBytesForCurrentThread() - _allocation;
            if (_stageTime.Elapsed.TotalSeconds < 8) return;
            int count = Math.Min(_frames, _samples.Length); Array.Sort(_samples, 0, count);
            Console.WriteLine($"STAGE {_stage} ({new[] { "terrain", "terrain+water", "mobs only", "terrain+water+mobs+120Hz simulation" }[_stage]}): fps={_frames / _stageTime.Elapsed.TotalSeconds:F1}, updateMs={_updateMs / _frames:F3}, drawCpuMs={_drawMs / _frames:F3}, p95FrameMs={_samples[(int)(count * .95)]:F2}, allocatedBytes/frame={_allocations / _frames}, resident={_renderer.ResidentChunks}, visibleChunks={(_stage == 2 ? 0 : _renderer.VisibleChunks)}, submittedVertices={(_stage == 2 ? 0 : _renderer.DrawnVertices)}, drawCalls/frame={_calls / _frames}, triangles/frame={_triangles / _frames}, estimatedTerrainBufferBytes={_renderer.BufferBytes}, estimatedWaterBufferBytes={_renderer.Water.BufferBytes}, cachedWaterFaces={_renderer.Water.CachedFaces}, simulationSeconds={_steps / 120.0:F2}, workingSet={Environment.WorkingSet}");
            _stage++; _frames = 0; _drawMs = _updateMs = 0; _allocations = _steps = _calls = _triangles = 0; _clock.Reset(); _stageTime.Restart();
            if (_stage == 4) Exit();
        }
        protected override void UnloadContent() { _renderer?.Dispose(); _mobs?.Dispose(); base.UnloadContent(); }
        private static string RendererName()
        {
            if (!OperatingSystem.IsWindows()) return "query unavailable";
            return Marshal.PtrToStringAnsi(GlGetString(0x1F01)) ?? "unknown";
        }
        [DllImport("opengl32.dll", EntryPoint = "glGetString")] private static extern IntPtr GlGetString(uint name);
    }
}

