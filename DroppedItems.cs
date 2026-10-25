using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;

namespace MDSM;

public readonly record struct DroppedItemState(ItemId Item, int Count, Vector3 Position, float VerticalVelocity, float PickupDelay);

public sealed class DroppedItem
{
    public const float Radius = 0.125f;
    public ItemId Item { get; }
    public int Count { get; internal set; }
    public Vector3 Position { get; private set; }
    public float VerticalVelocity { get; private set; }
    public float PickupDelay { get; private set; }
    public DroppedItemState Capture() => new(Item, Count, Position, VerticalVelocity, PickupDelay);

    internal DroppedItem(DroppedItemState state)
    {
        Item = state.Item; Count = state.Count; Position = state.Position;
        VerticalVelocity = state.VerticalVelocity; PickupDelay = state.PickupDelay;
    }

    internal void Update(World world, float elapsed)
    {
        PickupDelay = Math.Max(0, PickupDelay - elapsed);
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
        // Older worlds can contain holes through Y=0. Recover items like the player,
        // rather than allowing an unbounded fall or silently deleting their contents.
        if (Position.Y < -16) { Position = world.Spawn + Vector3.Up; VerticalVelocity = 0; }
    }
}

public sealed class DroppedItems
{
    // Corrupt-file/allocation bound, also enforced before mining changes any blocks.
    public const int MaximumEntities = 1_000_000;
    public const float PickupRadius = 1.25f;
    private readonly List<DroppedItem> _items = new();
    private readonly World _world;
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

    public void Spawn(ItemId item, int count, Vector3 position)
    {
        var state = new DroppedItemState(item, count,
            new Vector3(Math.Clamp(position.X, DroppedItem.Radius, _world.Width - DroppedItem.Radius), position.Y,
                Math.Clamp(position.Z, DroppedItem.Radius, _world.Depth - DroppedItem.Radius)), 0, 0.35f);
        Validate(state, _world.Size);
        if (!CanSpawn()) throw new InvalidOperationException("Dropped-item limit reached.");
        _items.Add(new DroppedItem(state));
    }

    public void Update(Player player, Inventory inventory, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0) return;
        float dt = Math.Min(elapsed, 0.1f);
        BoundingBox playerBounds = player.Bounds;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            DroppedItem item = _items[i];
            item.Update(_world, dt);
            Vector3 nearest = Vector3.Clamp(item.Position, playerBounds.Min, playerBounds.Max);
            if (item.PickupDelay > 0 || Vector3.DistanceSquared(item.Position, nearest) > PickupRadius * PickupRadius) continue;
            item.Count = inventory.Add(item.Item, item.Count);
            if (item.Count != 0) continue;
            // Swap removal avoids shifting the list; the swapped entry was already updated.
            _items[i] = _items[^1];
            _items.RemoveAt(_items.Count - 1);
        }
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
            || !float.IsFinite(state.PickupDelay) || state.PickupDelay < 0 || state.PickupDelay > 0.35f)
            throw new InvalidDataException("Save contains an invalid dropped item.");
    }
}
