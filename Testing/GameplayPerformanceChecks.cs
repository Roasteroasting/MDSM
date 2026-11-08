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
    public static int Run(WorldSize size,bool cave=false,bool traverse=false)
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-performance-" + Guid.NewGuid().ToString("N"));
        try { using var game = new Benchmark(root, size,cave,traverse); game.Run(); return 0; }
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
        private readonly bool _cave;
        private readonly bool _traverse;
        private Point3 _lavaCell;
        private int _fluidPulse=-1;
        private Task<WorldSave> _generation;
        private GameSession _session;
        private WorldRenderer _renderer;
        private Action<KeyboardState, MouseState, bool, GameTime, Point> _input;
        private readonly Stopwatch _clock = new();
        private readonly Stopwatch _warmup=new();
        private bool _ready;
        private int _phaseFrames;
        private readonly KeyboardState _walking = new(Keys.W);
        private readonly double[] _frames = new double[100000];
        private int _count, _phase;
        private double _updateMs, _drawMs;
        private double _maxPrepare,_maxTerrain,_maxWater;
        private int _builds;
        private long _allocations, _allocationStart;
        private Vector3 _startPosition;
        public Benchmark(string root, WorldSize size,bool cave=false,bool traverse=false) : base(new WorldSaveManager(Path.Combine(root, "Worlds")), new SettingsStore(Path.Combine(root, "settings.json")))
        {
            _size = size;_cave=cave;_traverse=traverse; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
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
                Vector3 position=_cave?FindCave(world):world.Spawn;var inventory=Inventory.CreateNewWorld();
                if(_traverse){world.SavedGameMode=GameMode.Creative;position=new(world.Width/2-240,132,world.Depth/2);}
                MobState[] hostile=Array.Empty<MobState>();
                if(_cave)
                {
                    for(int i=0;i<4;i++)inventory.SetEquipment(i,new(Items.Armor(ArmorMaterial.Obsidian,(ArmorSlot)i),1));
                    var spots=new System.Collections.Generic.List<MobState>();
                    for(int radius=5;radius<29 && spots.Count<8;radius++)for(int dy=-4;dy<=4 && spots.Count<8;dy++)for(int dz=-radius;dz<=radius && spots.Count<8;dz+=2)for(int dx=-radius;dx<=radius && spots.Count<8;dx+=2)
                    {
                        MobSpecies species=(MobSpecies)(4+spots.Count%3);Vector3 feet=new(MathF.Floor(position.X)+dx+.5f,position.Y+dy,MathF.Floor(position.Z)+dz+.5f);
                        if(feet.X<1 || feet.Z<1 || feet.X>world.Width-1 || feet.Z>world.Depth-1 || feet.Y<3 || feet.Y>=50)continue;
                        float distance=Vector3.DistanceSquared(feet,position);if(distance<16 || distance>32*32)continue;
                        var bounds=PassiveMob.BoundsAt(species,feet);if(!world.IsSolid((int)feet.X,(int)feet.Y-1,(int)feet.Z)||world.IntersectsSolid(bounds)||Water.Intersects(world,bounds)||Lava.Intersects(world,bounds))continue;
                        bool occupied=false;foreach(var other in spots)if(World.Overlaps(bounds,PassiveMob.BoundsAt(other.Species,other.Position)))occupied=true;
                        if(!occupied)spots.Add(new(species,feet,0,MobDefinitions.Get(species).MaximumHealth));
                    }
                    if(spots.Count!=8)throw new Exception("Diagnostic could not find eight cave positions.");hostile=spots.ToArray();
                }
                return new WorldSave(world, "Performance check", position, 0, -.25f, inventory,
                    Pigs: population.CapturePigs(), Mobs: population.CaptureOthers(), Population: population.CapturePopulation(),Hostiles:hostile);
            });
        }
        private Vector3 FindCave(World world)
        {
            foreach(var chunk in world.Chunks)
            {
                if(chunk.LavaCount==0)continue;
                for(int y=3;y<=18;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)if(chunk.GetBlock(x,y,z)==BlockType.LavaSource)
                for(int dz=-3;dz<=3;dz++)for(int dx=-3;dx<=3;dx++)
                {
                    Vector3 feet=new(chunk.OriginX+x+dx+.5f,y+1,chunk.OriginZ+z+dz+.5f);var bounds=new BoundingBox(feet+new Vector3(-Player.HalfWidth,0,-Player.HalfWidth),feet+new Vector3(Player.HalfWidth,Player.BodyHeight,Player.HalfWidth));
                    if(!world.Contains((int)feet.X,y+2,(int)feet.Z)||!world.IsSolid((int)feet.X,y,(int)feet.Z)||world.IntersectsSolid(bounds)||Water.Intersects(world,bounds)||Lava.Intersects(world,bounds))continue;
                    _lavaCell=new(chunk.OriginX+x,y,chunk.OriginZ+z);return feet;
                }
            }
            throw new Exception("No safe natural lava-cave view for diagnostics.");
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
                if(!_ready){_ready=true;_warmup.Restart();}
                if(_warmup.Elapsed.TotalSeconds>=2){_clock.Restart();_startPosition=_session.Player.Position;}
            }
            long start = Stopwatch.GetTimestamp();
            if(_cave)
            {
                // Test-only health control keeps this FPS sample active, rather than benchmarking a death menu.
                _session.Player.RestoreHealth(20);int pulse=(int)_clock.Elapsed.TotalSeconds/3;
                if(pulse!=_fluidPulse){_fluidPulse=pulse;_session.World.SetBlock(_lavaCell.X,_lavaCell.Y,_lavaCell.Z,BlockType.LavaSource);_session.World.SetBlock(_lavaCell.X+1,_lavaCell.Y,_lavaCell.Z,BlockType.WaterSource);}
            }
            float yaw = _traverse?MathHelper.PiOver2:(int)(_clock.Elapsed.TotalSeconds / 4) % 4 * MathHelper.PiOver2;
            _session.Camera.RestoreOrientation(yaw > MathHelper.Pi ? yaw - MathHelper.TwoPi : yaw, -.25f);
            _input(_phase == 0 ? default : _traverse?new KeyboardState(Keys.W,Keys.LeftControl):_walking, default, false, time, Point.Zero);
            _updateMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (State != GameState.Playing) throw new InvalidOperationException("Gameplay benchmark unexpectedly left active gameplay.");
        }
        protected override void Draw(GameTime time)
        {
            if (!_clock.IsRunning) { if(_ready)base.Draw(time);else GraphicsDevice.Clear(WorldRenderer.SkyColor);return; }
            long start = Stopwatch.GetTimestamp(); base.Draw(time);
            if(++_phaseFrames<=2)return; // Exclude startup/report-transition intervals, not gameplay stalls.
            _drawMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _maxPrepare=Math.Max(_maxPrepare,_renderer.PrepareMilliseconds);_maxTerrain=Math.Max(_maxTerrain,_renderer.TerrainBuildMilliseconds);_maxWater=Math.Max(_maxWater,_renderer.WaterBuildMilliseconds);_builds+=_renderer.RebuiltChunks;
            if (_count < _frames.Length) _frames[_count] = time.ElapsedGameTime.TotalMilliseconds;
            _count++; _allocations += GC.GetAllocatedBytesForCurrentThread() - _allocationStart;
            if (_clock.Elapsed.TotalSeconds < (_traverse && _phase==1?35:15)) return;
            int samples = Math.Min(_count, _frames.Length); Array.Sort(_frames, 0, samples);
            double Low(double fraction){int n=Math.Max(1,(int)Math.Ceiling(samples*fraction));double sum=0;for(int i=samples-n;i<samples;i++)sum+=_frames[i];return 1000*n/sum;}
            Console.WriteLine($"FRAME TAIL {_size} phase={_phase} traverse={_traverse}: 1%low={Low(.01):F1} FPS, 0.1%low={Low(.001):F1} FPS, p99={_frames[Math.Min(samples-1,(int)(samples*.99))]:F2}ms, p99.9={_frames[Math.Min(samples-1,(int)(samples*.999))]:F2}ms, worst={_frames[samples-1]:F2}ms, over16.7ms={Array.FindAll(_frames[..samples],v=>v>16.667).Length}, builds={_builds}, maxPrepare={_maxPrepare:F2}ms, maxTerrain={_maxTerrain:F2}ms, maxWater={_maxWater:F2}ms");
            Console.WriteLine($"GAMEPLAY {_size}, seed=173, 1080p uncapped, phase={(_phase == 0 ? "stationary" : _traverse?"fast Creative traversal":"walking turns")}, fps={_count / _clock.Elapsed.TotalSeconds:F1}, p95FrameMs={_frames[(int)(samples * .95)]:F2}, updateMs={_updateMs / _count:F3}, drawCpuMs={_drawMs / _count:F3}, allocatedBytes/frame={_allocations / _count}, travel={Vector3.Distance(_session.Player.Position, _startPosition):F1}, resident={_renderer.ResidentChunks}, retiring={_renderer.RetiringChunks}, drawn={_renderer.VisibleChunks}, estimatedTerrainBufferBytes={_renderer.BufferBytes}, mobs={_session.Mobs.Entities.Count}, workingSet={Environment.WorkingSet}, cave={_cave}, hostiles={_session.Hostiles.Entities.Count}, rebuildsThisFrame={_renderer.RebuiltChunks}, waterFrontier={_session.World.Fluids.PendingCount}, lavaFrontier={_session.World.LavaFlow.PendingCount}");
            _phase++; _phaseFrames=_count = 0; _updateMs = _drawMs = 0; _maxPrepare=_maxTerrain=_maxWater=0;_builds=0;_allocations = 0; _startPosition = _session.Player.Position; _clock.Restart();
            if (_phase == 2) Exit();
        }
    }
}
