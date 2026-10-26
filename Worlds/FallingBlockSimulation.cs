using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// The existing column frontier starts falls; only active objects move between voxel edits.
public sealed class FallingBlockSimulation
{
    public const double TickSeconds = 0.05;
    public const int MaximumColumnsPerTick = 32;
    public const int MaximumActiveBlocks = 4096;
    public const double Acceleration = 24, TerminalSpeed = 20;
    private readonly World _world;
    private readonly Queue<int> _columns = new();
    private BitArray _queued;
    private double _clock;
    private int _updating = -1;
    private readonly List<FallingBlock> _active = new();
    private readonly Dictionary<int, List<FallingBlock>> _fallingColumns = new();
    private readonly List<int> _emptyColumns = new();
    public IReadOnlyList<FallingBlock> Active => _active;
    public int PendingCount => _columns.Count;
    public int LastProcessedColumns { get; private set; }
    public int LastMovedBlocks { get; private set; }
    internal FallingBlockSimulation(World world) => _world = world;

    private void Enqueue(int column)
    {
        if (column == _updating) return;
        _queued ??= new BitArray(_world.Width * _world.Depth);
        if (_queued[column]) return;
        _queued[column] = true; _columns.Enqueue(column);
    }
    internal void Changed(int x, int z, BlockType before, BlockType after)
    {
        if (Blocks.Falls(before) || Blocks.Falls(after) || Blocks.IsSolid(before) != Blocks.IsSolid(after))
            Enqueue(z * _world.Width + x);
    }
    public int[] CaptureColumns() => _columns.ToArray();
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
        _columns.Clear(); _queued?.SetAll(false); _clock = 0; _active.Clear(); _fallingColumns.Clear();
        foreach (int column in columns)
        {
            if ((uint)column >= count || (_queued != null && _queued[column])) throw new InvalidDataException("Invalid or duplicate gravity column.");
            Enqueue(column);
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
            int column = _columns.Dequeue(); _queued[column] = false; _updating = column;
            int x = column % _world.Width, z = column / _world.Width;
            bool retry = false;
            try
            {
                // Removing the lower block exposes the next, launching stacks bottom-up in the same batch.
                for (int y = 1; y < World.Height; y++)
                {
                    BlockType block = _world.GetBlock(x, y, z);
                    if (!Blocks.Falls(block) || _world.IsSolid(x, y - 1, z)) continue;
                    if (_active.Count == MaximumActiveBlocks) { retry = true; break; }
                    Add(new FallingBlockState(block, x, z, y, 0));
                    if (_world.SetBlock(x, y, z, BlockType.Air))
                    {
                        LastMovedBlocks++;
                    }
                }
            }
            finally { _updating = -1; }
            if (retry) Enqueue(column); // Leave excess blocks in storage until capacity is available.
        }
    }

    private void Add(FallingBlockState state)
    {
        var block = new FallingBlock(state) { Index = _active.Count }; _active.Add(block);
        int column = state.Z * _world.Width + state.X;
        if (!_fallingColumns.TryGetValue(column, out List<FallingBlock> stack))
            _fallingColumns.Add(column, stack = new List<FallingBlock>());
        int at = 0; while (at < stack.Count && stack[at].Y < state.Y) at++;
        stack.Insert(at, block);
    }
    public bool Intersects(BoundingBox bounds)
    {
        int minX = Math.Max(0, (int)MathF.Floor(bounds.Min.X)), maxX = Math.Min(_world.Width - 1, (int)MathF.Floor(bounds.Max.X));
        int minZ = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)), maxZ = Math.Min(_world.Depth - 1, (int)MathF.Floor(bounds.Max.Z));
        for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            if (_fallingColumns.TryGetValue(z * _world.Width + x, out List<FallingBlock> stack))
                foreach (FallingBlock block in stack) if (World.Overlaps(bounds, block.Bounds)) return true;
        return false;
    }
    private void Move(double elapsed)
    {
        _emptyColumns.Clear();
        foreach (var column in _fallingColumns)
        {
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
                        _world.SetBlock(block.X, (int)floor, block.Z, block.Block);
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
        foreach (int column in _emptyColumns) _fallingColumns.Remove(column);
    }
}
