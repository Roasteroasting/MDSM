using System;

using System.Collections.Generic;

namespace MDSM.Worlds;

// A frontier, not a flood fill: changes become visible together once every water tick.
public sealed class FluidSimulation
{
    public const double TickSeconds = 0.2;
    public const int MaximumUpdatesPerTick = 2048;
    private readonly World _world;
    private readonly Queue<Point3> _queue = new();
    private readonly HashSet<int> _queued=new(); // Sparse active frontier, shared implementation for both fluids.
    private readonly List<(Point3 Cell, BlockType Block)> _changes = new(MaximumUpdatesPerTick);
    private double _clock;
    public int PendingCount => _queue.Count;
    public int LastProcessedCount { get; private set; }
    private static readonly Point3[] Neighbors = { new(1,0,0), new(-1,0,0), new(0,0,1), new(0,0,-1), new(0,1,0), new(0,-1,0) };

    private readonly bool _lava;
    internal FluidSimulation(World world,bool lava=false) { _world = world;_lava=lava; }
    private int Index(Point3 p) => (p.Y * _world.Depth + p.Z) * _world.Width + p.X;
    private void Enqueue(Point3 cell)
    {
        if (!_world.Contains(cell.X, cell.Y, cell.Z)) return;

        int index = Index(cell);
        if (!_queued.Add(index)) return;
        _queue.Enqueue(cell);
    }
    private BlockType At(Point3 p) => _world.GetBlock(p.X, p.Y, p.Z);
    internal void Changed(Point3 cell, BlockType previous, BlockType current)
    {
        bool wet = IsFluid(previous) || IsFluid(current);
        foreach (Point3 offset in Neighbors) wet |= IsFluid(At(cell + offset));
        if (!wet) return;
        Enqueue(cell);
        foreach (Point3 offset in Neighbors) Enqueue(cell + offset);
        // A floor edit also changes the horizontal supply of the water immediately above it.
        for (int i = 0; i < 4; i++) Enqueue(cell + new Point3(0, 1, 0) + Neighbors[i]);
    }

    // One scan on creation/load, skipping dry chunks. Stable lakes subsequently go idle.
    public void Initialize()
    {
        _queue.Clear(); _queued.Clear(); _clock = 0;
        foreach (Chunk chunk in _world.Chunks)
        {
            if ((_lava?chunk.LavaCount:chunk.WaterCount) == 0) continue;
            for (int y = 0; y < World.Height; y++)
            for (int z = 0; z < Chunk.Size; z++)
            for (int x = 0; x < Chunk.Size; x++)
            {
                BlockType block = chunk.GetBlock(x, y, z);
                if (!IsFluid(block)) continue;
                Point3 cell = new(chunk.OriginX + x, y, chunk.OriginZ + z);
                if (!IsSource(block)) Enqueue(cell);
                foreach (Point3 offset in Neighbors)
                {
                    Point3 neighbor = cell + offset;
                    BlockType adjacent = At(neighbor);
                    if (offset.Y > 0 && !IsFluid(adjacent)) continue; // Water cannot flow into dry air above it.
                    // Stable source interiors need no work. Examine only flow/frontier cells on load.
                    if (Replaceable(adjacent) && !IsSource(adjacent)) Enqueue(neighbor);
                }
            }
        }
    }

    public void Update(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0) return;
        // Gameplay calls this from its fixed simulation. A stalled caller cannot create a catch-up spiral.
        double interval=_lava?.8:TickSeconds;
        _clock += Math.Min(elapsed, interval);
        if (_clock + 1e-9 < interval) return;
        _clock = Math.Max(0, _clock - interval);
        Tick();
    }

    public void Tick()
    {
        int count = Math.Min(_queue.Count, _lava?512:MaximumUpdatesPerTick);
        LastProcessedCount = count; _changes.Clear();
        for (int i = 0; i < count; i++)
        {
            Point3 cell = _queue.Dequeue(); _queued.Remove(Index(cell));
            BlockType before = At(cell), after = Desired(cell, before);
            if (before != after) _changes.Add((cell, after));
        }
        // Snapshot evaluation prevents FIFO direction or remaining budget from accelerating a wave.
        foreach (var change in _changes)
            _world.SetBlock(change.Cell.X, change.Cell.Y, change.Cell.Z, change.Block);
    }

    private BlockType Source=>_lava?BlockType.LavaSource:BlockType.WaterSource;
    private BlockType Falling=>_lava?BlockType.LavaFalling:BlockType.WaterFalling;
    private bool IsFluid(BlockType block)=>_lava?Lava.IsLava(block):Water.IsWater(block);
    private bool IsSource(BlockType block)=>block==Source;
    private bool Replaceable(BlockType block)=>block==BlockType.Air || IsFluid(block) || Blocks.Get(block).Shape==BlockShape.Cross;
    private int Level(BlockType block)=>IsSource(block)||block==Falling?0:IsFluid(block)?(int)block-(int)Source:8;
    private BlockType Flow(int level)=>(BlockType)((int)Source+level);
    private bool HorizontalSupply(Point3 cell)
    {
        BlockType block = At(cell);
        if (!IsFluid(block)) return false;
        Point3 below = cell + new Point3(0, -1, 0);
        // Falling columns feed their foot, not their airborne sides. Sources above a lake can spread.
        return !_world.Contains(below.X, below.Y, below.Z) || !Replaceable(At(below))
            || (IsFluid(At(below)) && At(below) != Falling && block != Falling);
    }

    private BlockType Desired(Point3 cell, BlockType before)
    {
        if (IsSource(before) || !Replaceable(before)) return before;
        if (IsFluid(At(cell + new Point3(0, 1, 0)))) return Falling;
        int best = 8, sources = 0;
        for (int i = 0; i < 4; i++)
        {
            Point3 neighbor = cell + Neighbors[i];
            BlockType block = At(neighbor);
            if (IsSource(block)) sources++;
            if (HorizontalSupply(neighbor)) best = Math.Min(best, Level(block) + 1);
        }
        BlockType below = At(cell + new Point3(0, -1, 0));
        // Classic two-source regeneration requires a solid floor or another source underneath.
        if (!_lava && sources >= 2 && (Blocks.IsSolid(below) || IsSource(below))) return Source;
        return best <= (_lava?3:Water.MaximumLevel) ? Flow(best) : IsFluid(before) ? BlockType.Air : before;
    }
}
