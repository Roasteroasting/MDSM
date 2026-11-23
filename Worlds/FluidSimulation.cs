using System;
using System.Collections.Generic;
using MDSM.Audio;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Beta's still/flowing distinction lives in the scheduled frontier. Settled
// cells keep their depth byte and sleep until a neighboring block changes.
public sealed partial class FluidSimulation
{
    public const double TickSeconds = 0.25;
    public const int MaximumUpdatesPerTick = 2048;
    internal const int MaximumUpdatesPerFrame = 96;
    private const int NoDrop = 1000;
    private static readonly Point3 Down = new(0,-1,0);
    private static readonly Point3 Up = new(0,1,0);
    internal static ReadOnlySpan<Point3> Horizontal => Neighbors.AsSpan(0,4);
    private static readonly Point3[] Neighbors = { new(-1,0,0), new(1,0,0), new(0,0,-1), new(0,0,1), new(0,1,0), new(0,-1,0) };

    private readonly World _world;
    private readonly bool _lava;
    private readonly PriorityQueue<Point3,(double Due,long Order)> _queue = new();
    private readonly HashSet<Point3> _queued = new();
    private readonly Random _random;
    private double _clock;
    private double? _scheduledTime;
    private long _order;
    private double _nextWaterSound;
    private Point3? _audible;
    private float _nearestSound;
    public int PendingCount => _queue.Count;
    public int LastProcessedCount { get; private set; }
    private double Interval => _lava ? 1.5 : TickSeconds;

    internal FluidSimulation(World world,bool lava=false)
    {
        _world=world; _lava=lava;
        if (lava) _random=new(unchecked(world.Seed ^ 0x4c415641));
    }

    private void Enqueue(Point3 cell)
    {
        if (!_world.Contains(cell.X,cell.Y,cell.Z) || !_world.IsSimulated(cell) || !_queued.Add(cell)) return;
        _queue.Enqueue(cell,((_scheduledTime ?? _clock)+Interval,_order++));
    }

    private BlockType At(Point3 p) => _world.GetBlock(p.X,p.Y,p.Z);
    private BlockType Source => _lava ? BlockType.LavaSource : BlockType.WaterSource;
    private bool IsFluid(BlockType block) => _lava ? Lava.IsLava(block) : Water.IsWater(block);
    private bool IsSource(BlockType block) => block == Source;
    private int Decay(BlockType block) => !IsFluid(block) ? -1 : _lava ? Lava.Decay(block) : Water.Decay(block);
    private BlockType FromDecay(int decay) => decay <= 8 ? (BlockType)((int)Source+decay)
        : (BlockType)((int)(_lava ? BlockType.LavaFalling1 : BlockType.WaterFalling1)+decay-9);

    internal void Changed(Point3 cell, BlockType current)
    {
        if (IsFluid(current)) Enqueue(cell);
        foreach (Point3 offset in Neighbors)
        {
            Point3 neighbor = cell + offset;
            if (IsFluid(At(neighbor))) Enqueue(neighbor);
        }
    }

    public void Initialize()
    {
        _queue.Clear();
        _queued.Clear();
        _clock=0;
        _nextWaterSound=0;
        foreach (Chunk chunk in _world.Chunks) InitializeChunk(chunk);
    }

    public void Update(double elapsed)
        => Update(elapsed, _lava ? 512 : MaximumUpdatesPerTick);

    internal void Update(double elapsed,int budget)
    {
        LastProcessedCount=0;
        if (!double.IsFinite(elapsed) || elapsed <= 0) return;
        _clock += elapsed;
        if (budget>0) ProcessDue(budget);
    }

    public void Tick() => Update(Interval);

    private void ProcessDue(int budget)
    {
        if (!_queue.TryPeek(out _,out var first) || first.Due>_clock+1e-9) return;
        budget = Math.Min(_queue.Count,budget);
        _audible=null; _nearestSound=12*12;
        while (LastProcessedCount < budget && _queue.TryPeek(out Point3 cell,out var next) && next.Due <= _clock+1e-9)
        {
            _queue.Dequeue();
            _queued.Remove(cell);
            LastProcessedCount++;
            if (!_world.IsSimulated(cell) || !IsFluid(At(cell))) continue;
            // Don't interpret a missing upstream chunk or downhill route as air.
            if (!NeighborhoodLoaded(cell)) { Enqueue(cell); continue; }
            _scheduledTime=next.Due;
            try { Step(cell); }
            finally { _scheduledTime=null; }
        }
        if (_audible.HasValue)
        {
            _world.Sounds?.At(SoundKind.WaterFlow,_world.Center(_audible.Value),.4f);
            _nextWaterSound=_clock+TickSeconds;
        }
    }

    private bool NeighborhoodLoaded(Point3 cell)
    {
        if (!_world.IsInfinite) return true;
        // A Beta flow search can look four steps beyond the first side cell.
        for (long z=cell.Z-5; z<=cell.Z+5; z+=10)
        for (long x=cell.X-5; x<=cell.X+5; x+=10)
        {
            long px=Math.Clamp(x,-Streaming.Coordinates.MaximumBlock,Streaming.Coordinates.MaximumBlock-1);
            long pz=Math.Clamp(z,-Streaming.Coordinates.MaximumBlock,Streaming.Coordinates.MaximumBlock-1);
            if (!_world.IsLoaded(new(px,cell.Y,pz))) return false;
        }
        return true;
    }

    private bool BlocksFlow(Point3 cell)
    {
        if (!_world.Contains(cell.X,cell.Y,cell.Z)) return true;
        BlockType block=At(cell);
        return Blocks.IsSolid(block) || block==BlockType.Sign;
    }

    private bool CanDisplace(Point3 cell)
    {
        if (BlocksFlow(cell)) return false;
        BlockType block=At(cell);
        return !IsFluid(block) && !Lava.IsLava(block);
    }

    private void Step(Point3 cell)
    {
        if (_lava && _world.HardenLava(cell)) return;
        int decay=Decay(At(cell));
        Point3 below=cell+Down;
        if (decay>0)
        {
            int best=8, sources=0;
            foreach (Point3 direction in Horizontal)
            {
                int neighbor=Decay(At(cell+direction));
                if (neighbor<0) continue;
                if (neighbor==0) sources++;
                best=Math.Min(best,neighbor>=8 ? 0 : neighbor);
            }
            int next=best+(_lava ? 2 : 1);
            if (next>=8) next=-1;
            int above=Decay(At(cell+Up));
            if (above>=0) next=above|8;
            // Beta 1.7.3 regenerates over solid ground. Its test for a
            // source below reads the current flowing cell's depth instead.
            if (!_lava && sources>=2 && Blocks.IsSolid(At(below))) next=0;
            if (_lava && decay<8 && next<8 && next>decay && _random.Next(4)!=0)
            {
                Enqueue(cell);
                next=decay;
            }
            if (next!=decay)
            {
                if (!Set(cell,next<0 ? BlockType.Air : FromDecay(next))) return;
                decay=Decay(At(cell)); // Contact can harden lava during the block update.
                if (decay<0) return;
            }
        }

        if (CanDisplace(below))
        {
            Set(below,FromDecay(decay|8));
            return;
        }

        int lateral=decay>=8 ? 1 : decay+(_lava ? 2 : 1);
        if (lateral>7 || (decay!=0 && !BlocksFlow(below))) return;
        Spread(cell,lateral);
    }

    private void Spread(Point3 cell,int decay)
    {
        // Existing non-source liquid can participate in routing, but new flow
        // only writes into a displaceable block. Tied routes spread together.
        bool canSpread=false;
        foreach (Point3 direction in Horizontal) canSpread |= CanDisplace(cell+direction);
        if (!canSpread) return;
        Span<int> costs=stackalloc int[4];
        int best=NoDrop;
        for (int i=0;i<4;i++)
        {
            Point3 neighbor=cell+Neighbors[i];
            costs[i]=BlocksFlow(neighbor) || IsSource(At(neighbor)) ? NoDrop
                : !BlocksFlow(neighbor+Down) ? 0 : DropCost(neighbor,1,i);
            best=Math.Min(best,costs[i]);
        }
        for (int i=0;i<4;i++)
        {
            Point3 neighbor=cell+Neighbors[i];
            if (costs[i]==best && CanDisplace(neighbor)) Set(neighbor,FromDecay(decay),dislodge:true);
        }
    }

    private int DropCost(Point3 cell,int distance,int incoming)
    {
        int best=NoDrop;
        for (int direction=0;direction<4;direction++)
        {
            if (direction==(incoming^1)) continue;
            Point3 step=cell+Neighbors[direction];
            if (BlocksFlow(step) || IsSource(At(step))) continue;
            if (!BlocksFlow(step+Down)) return distance;
            if (distance<4) best=Math.Min(best,DropCost(step,distance+1,direction));
        }
        return best;
    }

    private bool Set(Point3 cell,BlockType block,bool dislodge=false)
    {
        bool changed = dislodge ? _world.SpreadFluid(cell,block,_lava) : _world.SetBlock(cell.X,cell.Y,cell.Z,block);
        if (!changed) return false;
        if (!_lava && _world.Sounds!=null && _clock>=_nextWaterSound)
        {
            float distance=Vector3.DistanceSquared(_world.Center(cell),_world.Sounds.Listener);
            if (distance<_nearestSound) { _nearestSound=distance; _audible=cell; }
        }
        return true;
    }
}
