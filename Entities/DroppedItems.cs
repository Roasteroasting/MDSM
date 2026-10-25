using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;

using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Entities;

public readonly record struct DroppedItemState(ItemId Item, int Count, Vector3 Position, float VerticalVelocity, float PickupDelay);

public sealed class DroppedItem
{
    public const float Radius = 0.125f;
    public ItemId Item { get; }
    public int Count { get; internal set; }
    public Vector3 Position { get; private set; }
    public float VerticalVelocity { get; private set; }
    public float PickupDelay { get; internal set; }
    public DroppedItemState Capture() => new(Item, Count, Position, VerticalVelocity, PickupDelay);

    internal DroppedItem(DroppedItemState state)
    {
        Item = state.Item; Count = state.Count; Position = state.Position;
        VerticalVelocity = state.VerticalVelocity; PickupDelay = state.PickupDelay;
    }

    internal void Update(World world, float elapsed)
    {
        VerticalVelocity = Math.Max(-40, VerticalVelocity - 18 * elapsed);
        float movement = VerticalVelocity * elapsed;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(movement) / 0.1f));
        float step = movement / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 next = Position + new Vector3(0, step, 0);
            var bounds = new BoundingBox(next - new Vector3(Radius), next + new Vector3(Radius));
            bool collided = false;
            for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
            for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
            for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
                if (world.IsSolid(x, y, z) && World.Overlaps(bounds, World.Bounds(new Point3(x, y, z))))
                {
                    next.Y = step < 0 ? Math.Max(next.Y, y + 1 + Radius) : Math.Min(next.Y, y - Radius);
                    collided = true;
                }
            Position = next;
            if (collided) { VerticalVelocity = 0; break; }
        }
        // Keep void items at their original X/Z, never teleport death drops to spawn.
        if (Position.Y < -16 + Radius) { Position = new Vector3(Position.X, -16 + Radius, Position.Z); VerticalVelocity = 0; }
    }
}

public sealed class DroppedItems
{
    // Corrupt-file/allocation bound, also enforced before mining changes any blocks.
    public const int MaximumEntities = 1_000_000;
    public const float PickupRadius = 1.25f;
    public const float DeliberatePickupDelay = 1f;
    public const float MergeRadius = 0.75f;
    private readonly List<DroppedItem> _items = new();
    private readonly World _world;
    private readonly Dictionary<(Point3 Cell, ItemId Item), int> _mergeHeads = new();
    private int[] _mergeNext = Array.Empty<int>();
    private float _mergeElapsed;
    public IReadOnlyList<DroppedItem> Entities => _items;
    public bool CanSpawn(int count = 1) => count >= 0 && count <= MaximumEntities - _items.Count;

    public DroppedItems(World world, ReadOnlySpan<DroppedItemState> saved = default)
    {
        _world = world;
        if (saved.Length > MaximumEntities) throw new InvalidDataException("Too many dropped items.");
        foreach (DroppedItemState state in saved)
        {
            Validate(state, world.Size);
            _items.Add(new DroppedItem(state));
        }
    }

    public void Spawn(ItemId item, int count, Vector3 position, float pickupDelay = 0.35f)
    {
        var state = new DroppedItemState(item, count,
            new Vector3(Math.Clamp(position.X, DroppedItem.Radius, _world.Width - DroppedItem.Radius), position.Y,
                Math.Clamp(position.Z, DroppedItem.Radius, _world.Depth - DroppedItem.Radius)), 0, pickupDelay);
        Validate(state, _world.Size);
        if (!CanSpawn()) throw new InvalidOperationException("Dropped-item limit reached.");
        _items.Add(new DroppedItem(state));
    }

    public bool TrySpawn(ItemStack stack, Vector3 position, float pickupDelay)
    {
        if (stack.IsEmpty || !CanSpawn()) return false;
        Spawn(stack.Item, stack.Count, position, pickupDelay);
        return true;
    }

    public void Update(Player player, Inventory inventory, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0) return;
        float dt = Math.Min(elapsed, 0.1f);
        for (int i = 0; i < _items.Count; i++)
        {
            _items[i].PickupDelay = Math.Max(0, _items[i].PickupDelay - elapsed);
            _items[i].Update(_world, dt);
        }
        _mergeElapsed += elapsed;
        if (_mergeElapsed >= 0.25f)
        {
            MergeNearby();
            _mergeElapsed %= 0.25f;
        }
        BoundingBox playerBounds = player.Bounds;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            DroppedItem item = _items[i];
            Vector3 nearest = Vector3.Clamp(item.Position, playerBounds.Min, playerBounds.Max);
            if (player.IsDead || item.PickupDelay > 0 || Vector3.DistanceSquared(item.Position, nearest) > PickupRadius * PickupRadius) continue;
            item.Count = inventory.Add(item.Item, item.Count);
            if (item.Count != 0) continue;
            // Swap removal avoids shifting the list; the swapped entry was already updated.
            _items[i] = _items[^1];
            _items.RemoveAt(_items.Count - 1);
        }
    }

    private void MergeNearby()
    {
        _mergeHeads.Clear();
        if (_mergeNext.Length < _items.Count) Array.Resize(ref _mergeNext, Math.Max(_items.Count, Math.Max(64, _mergeNext.Length * 2)));
        for (int i = 0; i < _items.Count; i++)
        {
            DroppedItem item = _items[i];
            var cell = new Point3((int)MathF.Floor(item.Position.X), (int)MathF.Floor(item.Position.Y), (int)MathF.Floor(item.Position.Z));
            for (int dz = -1; dz <= 1 && item.Count > 0; dz++)
            for (int dy = -1; dy <= 1 && item.Count > 0; dy++)
            for (int dx = -1; dx <= 1 && item.Count > 0; dx++)
            {
                var key = (new Point3(cell.X + dx, cell.Y + dy, cell.Z + dz), item.Item);
                if (!_mergeHeads.TryGetValue(key, out int index)) continue;
                int previous = -1;
                while (index >= 0 && item.Count > 0)
                {
                    DroppedItem other = _items[index];
                    int next = _mergeNext[index];
                    if (Vector3.DistanceSquared(item.Position, other.Position) <= MergeRadius * MergeRadius)
                    {
                        int moved = Math.Min(ItemStack.Maximum - other.Count, item.Count);
                        other.Count += moved; item.Count -= moved;
                        other.PickupDelay = item.PickupDelay = Math.Max(item.PickupDelay, other.PickupDelay);
                    }
                    if (other.Count == ItemStack.Maximum)
                    {
                        if (previous < 0) _mergeHeads[key] = next;
                        else _mergeNext[previous] = next;
                    }
                    else previous = index;
                    index = next;
                }
            }
            // Full stacks cannot receive more items, so omit them from the spatial index.
            if (item.Count > 0 && item.Count < ItemStack.Maximum)
            {
                var key = (cell, item.Item);
                _mergeNext[i] = _mergeHeads.TryGetValue(key, out int head) ? head : -1;
                _mergeHeads[key] = i;
            }
        }
        int write = 0;
        for (int read = 0; read < _items.Count; read++) if (_items[read].Count > 0) _items[write++] = _items[read];
        if (write < _items.Count) _items.RemoveRange(write, _items.Count - write);
    }

    public DroppedItemState[] Capture()
    {
        var states = new DroppedItemState[_items.Count];
        for (int i = 0; i < states.Length; i++) states[i] = _items[i].Capture();
        return states;
    }

    public static void Validate(DroppedItemState state, WorldSize size)
    {
        Vector3 p = state.Position;
        if (!Items.IsDefined(state.Item) || state.Count < 1 || state.Count > ItemStack.Maximum
            || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
            || p.X < DroppedItem.Radius || p.X > size.Width() - DroppedItem.Radius
            || p.Z < DroppedItem.Radius || p.Z > size.Width() - DroppedItem.Radius || p.Y < -16 || p.Y > World.Height + 16
            || !float.IsFinite(state.VerticalVelocity) || MathF.Abs(state.VerticalVelocity) > 40
            || !float.IsFinite(state.PickupDelay) || state.PickupDelay < 0 || state.PickupDelay > DeliberatePickupDelay)
            throw new InvalidDataException("Save contains an invalid dropped item.");
    }
}
