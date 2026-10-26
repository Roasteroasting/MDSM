using System;
using System.Collections;
using System.Collections.Generic;

namespace MDSM.Worlds;

// A frontier, not a flood fill: changes become visible together once every water tick.
public sealed class FluidSimulation
{
    public const double TickSeconds = 0.2;
    public const int MaximumUpdatesPerTick = 2048;
    private readonly World _world;
    private readonly Queue<Point3> _queue = new();
    private BitArray _queued; // Allocated only for worlds containing/receiving water (one bit/cell).
    private readonly List<(Point3 Cell, BlockType Block)> _changes = new(MaximumUpdatesPerTick);
    private double _clock;
    public int PendingCount => _queue.Count;
    public int LastProcessedCount { get; private set; }
    private static readonly Point3[] Neighbors = { new(1,0,0), new(-1,0,0), new(0,0,1), new(0,0,-1), new(0,1,0), new(0,-1,0) };

    internal FluidSimulation(World world) => _world = world;
    private int Index(Point3 p) => (p.Y * _world.Depth + p.Z) * _world.Width + p.X;
    private void Enqueue(Point3 cell)
    {
        if (!_world.Contains(cell.X, cell.Y, cell.Z)) return;
        _queued ??= new BitArray(_world.BlockCount);
        int index = Index(cell);
        if (_queued[index]) return;
        _queued[index] = true; _queue.Enqueue(cell);
    }
    private BlockType At(Point3 p) => _world.GetBlock(p.X, p.Y, p.Z);
    internal void Changed(Point3 cell, BlockType previous, BlockType current)
    {
        bool wet = Water.IsWater(previous) || Water.IsWater(current);
        foreach (Point3 offset in Neighbors) wet |= Water.IsWater(At(cell + offset));
        if (!wet) return;
        Enqueue(cell);
        foreach (Point3 offset in Neighbors) Enqueue(cell + offset);
        // A floor edit also changes the horizontal supply of the water immediately above it.
        for (int i = 0; i < 4; i++) Enqueue(cell + new Point3(0, 1, 0) + Neighbors[i]);
    }

    // One scan on creation/load, skipping dry chunks. Stable lakes subsequently go idle.
    public void Initialize()
    {
        _queue.Clear(); _queued?.SetAll(false); _clock = 0;
        foreach (Chunk chunk in _world.Chunks)
        {
            if (chunk.WaterCount == 0) continue;
            for (int y = 0; y < World.Height; y++)
            for (int z = 0; z < Chunk.Size; z++)
            for (int x = 0; x < Chunk.Size; x++)
            {
                BlockType block = chunk.GetBlock(x, y, z);
                if (!Water.IsWater(block)) continue;
                Point3 cell = new(chunk.OriginX + x, y, chunk.OriginZ + z);
                if (!Water.IsSource(block)) Enqueue(cell);
                foreach (Point3 offset in Neighbors)
                {
                    Point3 neighbor = cell + offset;
                    BlockType adjacent = At(neighbor);
                    // Stable source interiors need no work. Examine only flow/frontier cells on load.
                    if (Water.Replaceable(adjacent) && !Water.IsSource(adjacent)) Enqueue(neighbor);
                }
            }
        }
    }

    public void Update(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0) return;
        // Gameplay calls this from its fixed simulation. A stalled caller cannot create a catch-up spiral.
        _clock += Math.Min(elapsed, TickSeconds);
        if (_clock + 1e-9 < TickSeconds) return;
        _clock = Math.Max(0, _clock - TickSeconds);
        Tick();
    }

    public void Tick()
    {
        int count = Math.Min(_queue.Count, MaximumUpdatesPerTick);
        LastProcessedCount = count; _changes.Clear();
        for (int i = 0; i < count; i++)
        {
            Point3 cell = _queue.Dequeue(); _queued[Index(cell)] = false;
            BlockType before = At(cell), after = Desired(cell, before);
            if (before != after) _changes.Add((cell, after));
        }
        // Snapshot evaluation prevents FIFO direction or remaining budget from accelerating a wave.
        foreach (var change in _changes)
            _world.SetBlock(change.Cell.X, change.Cell.Y, change.Cell.Z, change.Block);
    }

    private bool HorizontalSupply(Point3 cell)
    {
        BlockType block = At(cell);
        if (!Water.IsWater(block)) return false;
        Point3 below = cell + new Point3(0, -1, 0);
        // Falling columns feed their foot, not their airborne sides. Sources above a lake can spread.
        return !_world.Contains(below.X, below.Y, below.Z) || !Water.Replaceable(At(below))
            || (Water.IsWater(At(below)) && At(below) != BlockType.WaterFalling && block != BlockType.WaterFalling);
    }

    private BlockType Desired(Point3 cell, BlockType before)
    {
        if (Water.IsSource(before) || !Water.Replaceable(before)) return before;
        if (Water.IsWater(At(cell + new Point3(0, 1, 0)))) return BlockType.WaterFalling;
        int best = 8, sources = 0;
        for (int i = 0; i < 4; i++)
        {
            Point3 neighbor = cell + Neighbors[i];
            BlockType block = At(neighbor);
            if (Water.IsSource(block)) sources++;
            if (HorizontalSupply(neighbor)) best = Math.Min(best, Water.Level(block) + 1);
        }
        BlockType below = At(cell + new Point3(0, -1, 0));
        // Classic two-source regeneration requires a solid floor or another source underneath.
        if (sources >= 2 && (Blocks.IsSolid(below) || Water.IsSource(below))) return BlockType.WaterSource;
        return best <= Water.MaximumLevel ? Water.Flow(best) : Water.IsWater(before) ? BlockType.Air : before;
    }
}
