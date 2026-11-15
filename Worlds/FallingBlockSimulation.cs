using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Audio;

namespace MDSM.Worlds;

// The existing column frontier starts falls; only active objects move between voxel edits.
public sealed partial class FallingBlockSimulation
{
    public const double TickSeconds = 0.05;
    public const int MaximumColumnsPerTick = 32;
    public const int MaximumActiveBlocks = 4096;
    public const double Acceleration = 24, TerminalSpeed = 20;
    private readonly World _world;
    private readonly Queue<Point3> _columns = new();
    private readonly HashSet<Point3> _queued=new();
    private double _clock;
    private Point3? _updating;
    private readonly List<FallingBlock> _active = new();
    private readonly Dictionary<Point3, List<FallingBlock>> _fallingColumns = new();
    private readonly List<Point3> _emptyColumns = new();
    public IReadOnlyList<FallingBlock> Active => _active;
    public int PendingCount => _columns.Count;
    public int LastProcessedColumns { get; private set; }
    public int LastMovedBlocks { get; private set; }
    // Only active entities are selected by /kill. Queued terrain checks and
    // ordinary placed blocks remain unchanged; removed cubes cannot settle later.
    internal int RemoveActive()
    {
        int count = _active.Count;
        _active.Clear(); _fallingColumns.Clear(); _emptyColumns.Clear();
        return count;
    }
    internal FallingBlockSimulation(World world) => _world = world;

    private void Enqueue(Point3 column)
    {
        if (column == _updating) return;

        if (!_queued.Add(column)) return;
        _columns.Enqueue(column);
    }
    internal void Changed(long x, long z, BlockType before, BlockType after)
    {
        if (Blocks.Falls(before) || Blocks.Falls(after) || Blocks.IsSolid(before) != Blocks.IsSolid(after))
            Enqueue(new(x,0,z));
    }
    public int[] CaptureColumns() {var values=new int[_columns.Count];int i=0;foreach(var cell in _columns)values[i++]=checked((int)(cell.Z*_world.Width+cell.X));return values;}
    public FallingBlockState[] CaptureActive()
    {
        var result = new FallingBlockState[_active.Count];
        for (int i = 0; i < result.Length; i++) result[i] = _active[i].Capture();
        return result;
    }
    internal void Restore(ReadOnlySpan<int> columns, ReadOnlySpan<FallingBlockState> falling = default)
    {
        int count = _world.Width * _world.Depth;
        if (columns.Length > count) throw new InvalidDataException("Too many pending gravity columns.");
        _columns.Clear(); _queued.Clear(); _clock = 0; _active.Clear(); _fallingColumns.Clear();
        foreach (int column in columns)
        {
            if ((uint)column >= count || _queued.Contains(new(column%_world.Width,0,column/_world.Width))) throw new InvalidDataException("Invalid or duplicate gravity column.");
            Enqueue(new(column%_world.Width,0,column/_world.Width));
        }
        if (falling.Length > MaximumActiveBlocks) throw new InvalidDataException("Too many active falling blocks.");
        foreach (FallingBlockState state in falling)
        {
            if (!Blocks.Falls(state.Block) || (uint)state.X >= _world.Width || (uint)state.Z >= _world.Depth
                || !double.IsFinite(state.Y) || state.Y < 0 || state.Y > World.Height - 1
                || !double.IsFinite(state.DownwardSpeed) || state.DownwardSpeed < 0 || state.DownwardSpeed > TerminalSpeed)
                throw new InvalidDataException("Invalid falling block state.");
            var position = new Vector3(state.X, (float)state.Y, state.Z);
            if (_world.IntersectsSolid(new BoundingBox(position, position + Vector3.One))) throw new InvalidDataException("Falling block overlaps terrain.");
            Add(state);
        }
        foreach (List<FallingBlock> stack in _fallingColumns.Values)
            for (int i = 1; i < stack.Count; i++)
                if (stack[i].Y < stack[i - 1].Y + 1 - 1e-8) throw new InvalidDataException("Overlapping falling blocks.");
    }
    public void Update(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0) return;
        _clock += elapsed;
        if (_clock + 1e-9 >= TickSeconds)
        {
            // One bounded activation batch even for a stalled caller; motion still consumes all elapsed time.
            _clock = _clock >= TickSeconds ? _clock % TickSeconds : 0; Tick();
        }
        Move(elapsed);
    }
    public void Tick()
    {
        LastMovedBlocks = 0; LastProcessedColumns = Math.Min(_columns.Count, MaximumColumnsPerTick);
        for (int i = 0; i < LastProcessedColumns; i++)
        {
            Point3 column = _columns.Dequeue(); _queued.Remove(column); _updating = column;
            if(!_world.IsSimulated(column)){_updating=null;Enqueue(column);continue;}
            long x = column.X, z = column.Z;
            bool retry = false;
            try
            {
                // Removing the lower block exposes the next, launching stacks bottom-up in the same batch.
                for (int y = 1; y < World.Height; y++)
                {
                    BlockType block = _world.GetBlock(x, y, z);
                    if (!Blocks.Falls(block) || _world.SupportsVoxelTop(new(x,y-1,z))) continue;
                    if (_active.Count == MaximumActiveBlocks) { retry = true; break; }
                    if (_world.SetBlock(x, y, z, BlockType.Air))
                    {
                        Add(new FallingBlockState(block, x, z, y, 0));
                        LastMovedBlocks++;
                    }
                    else { retry = true; break; } // A blocked loot reservation must not duplicate a falling block.
                }
            }
            finally { _updating = null; }
            if (retry) Enqueue(column); // Leave excess blocks in storage until capacity is available.
        }
    }

    private void Add(FallingBlockState state)
    {
        var block = new FallingBlock(state) { Index = _active.Count }; _active.Add(block);
        Point3 column = new(state.X,0,state.Z);
        if (!_fallingColumns.TryGetValue(column, out List<FallingBlock> stack))
            _fallingColumns.Add(column, stack = new List<FallingBlock>());
        int at = 0; while (at < stack.Count && stack[at].Y < state.Y) at++;
        stack.Insert(at, block);
    }
    public bool Intersects(BoundingBox bounds)
    {
        int minX = Math.Max(_world.LocalMinX, (int)MathF.Floor(bounds.Min.X)), maxX = Math.Min(_world.LocalMaxX - 1, (int)MathF.Floor(bounds.Max.X));
        int minZ = Math.Max(_world.LocalMinZ, (int)MathF.Floor(bounds.Min.Z)), maxZ = Math.Min(_world.LocalMaxZ - 1, (int)MathF.Floor(bounds.Max.Z));
        for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            if (_fallingColumns.TryGetValue(_world.GlobalCell(x,0,z), out List<FallingBlock> stack))
                foreach (FallingBlock block in stack) if (World.Overlaps(bounds,new BoundingBox(_world.LocalPosition(new Point3(block.X,0,block.Z))+Vector3.Up*(float)block.Y,_world.LocalPosition(new Point3(block.X,0,block.Z))+Vector3.Up*(float)block.Y+Vector3.One))) return true;
        return false;
    }
    private void Move(double elapsed)
    {
        _emptyColumns.Clear();
        foreach (var column in _fallingColumns)
        {
            if(!_world.IsSimulated(column.Key))continue;
            List<FallingBlock> stack = column.Value;
            FallingBlock below = null;
            for (int i = 0; i < stack.Count;)
            {
                FallingBlock block = stack[i]; block.PreviousY = block.Y;
                // Integrate acceleration exactly, including the instant terminal velocity is reached.
                double accelerating = Math.Min(elapsed, (TerminalSpeed - block.DownwardSpeed) / Acceleration);
                double distance = block.DownwardSpeed * accelerating + 0.5 * Acceleration * accelerating * accelerating
                    + TerminalSpeed * (elapsed - accelerating);
                double speed = Math.Min(TerminalSpeed, block.DownwardSpeed + Acceleration * elapsed);
                double next = block.Y - distance, floor = 0;
                for (int y = Math.Min(World.Height - 1, (int)Math.Ceiling(block.Y) - 1); y >= Math.Max(0, (int)Math.Floor(next)); y--)
                    if (_world.IsSolid(block.X, y, block.Z)) { floor = y + 1; break; }
                bool movingSupport = below != null && below.Y + 1 > floor;
                double support = movingSupport ? below.Y + 1 : floor;
                if (next <= support + 1e-9)
                {
                    next = support;
                    if (!movingSupport)
                    {
                        Point3 ground=new(block.X,(int)floor-1,block.Z);
                        bool partial=Blocks.Get(_world.GetBlock(ground.X,ground.Y,ground.Z)).Shape is BlockShape.Slab or BlockShape.Stairs
                            && !_world.SupportsVoxelTop(ground);
                        bool settled=partial?_world.LiveDrops?.TrySpawn(new(MDSM.Inventories.Items.FromBlock(block.Block),1),_world.LocalPosition(new Point3(block.X,0,block.Z))+new Vector3(.5f,(float)floor+.2f,.5f),.35f)==true
                            :_world.SetBlock(block.X, (int)floor, block.Z, block.Block);
                        if(!settled){block.Y=next;block.DownwardSpeed=0;below=block;i++;continue;}
                        if(!partial)_world.Sounds?.Block(SoundKind.Land,block.Block,_world.LocalPosition(new Point3(block.X,0,block.Z))+new Vector3(.5f,(float)floor,.5f));
                        int last = _active.Count - 1;
                        _active[block.Index] = _active[last]; _active[block.Index].Index = block.Index; _active.RemoveAt(last);
                        stack.RemoveAt(i); continue;
                    }
                    speed = Math.Min(speed, below.DownwardSpeed);
                }
                block.Y = next; block.DownwardSpeed = speed; below = block; i++;
            }
            if (stack.Count == 0) _emptyColumns.Add(column.Key);
        }
        foreach (Point3 column in _emptyColumns) _fallingColumns.Remove(column);
    }
}
