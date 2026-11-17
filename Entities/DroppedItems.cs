using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;

using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;
using MDSM.Audio;

namespace MDSM.Entities;

public readonly record struct DroppedItemState(ItemId Item, int Count, Vector3 Position, float VerticalVelocity, float PickupDelay,
    int Durability = 0, Guid InstanceId = default)
{
    public ItemStack Stack => new(Item,Count,Durability,InstanceId);
}

public sealed class DroppedItem
{
    public const float Radius = 0.125f;
    public ItemId Item { get; }
    public int Count { get; internal set; }
    public int Durability { get; private set; }
    public Guid InstanceId { get; }
    public ItemStack Stack => new(Item,Count,Durability,InstanceId);
    private FramedPosition _pose;
    private FramedPosition _previousPose;
    private float _phase, _previousPhase;
    // Toss direction and presentation are transient; existing saves retain their
    // exact item state and vertical motion without a save-layout change.
    internal Vector2 HorizontalVelocity { get; private set; }
    public Vector3 Position {get=>_pose.Local;private set=>_pose.Local=value;}
    public MDSM.Worlds.Streaming.WorldPosition AbsolutePosition=>_pose.Absolute;
    internal void Bind(World world){_pose.Bind(world);_previousPose=_pose;}
    internal Vector3 RenderPosition(float fraction)=>Vector3.Lerp(_previousPose.Local,Position,Math.Clamp(fraction,0,1));
    internal float RenderPhase(float fraction)=>MathHelper.Lerp(_previousPhase,_phase,Math.Clamp(fraction,0,1));
    internal Vector3 VisualPosition(float fraction)=>RenderPosition(fraction)+new Vector3(0,.13f+.06f*MathF.Sin(RenderPhase(fraction)),0);
    internal void Throw(Vector3 velocity){HorizontalVelocity=new(velocity.X,velocity.Z);VerticalVelocity=velocity.Y;}
    public float VerticalVelocity { get; private set; }
    public float PickupDelay { get; internal set; }
    public DroppedItemState Capture() => new(Item, Count, Position, VerticalVelocity, PickupDelay,Durability,InstanceId);
    internal void Wear() { var next=Stack.Wear();Count=next.Count;Durability=next.Durability; }

    internal DroppedItem(DroppedItemState state)
    {
        Item = state.Item; Count = state.Count; Position = state.Position;Durability=state.Durability;InstanceId=state.InstanceId;
        VerticalVelocity = state.VerticalVelocity; PickupDelay = state.PickupDelay;
        _previousPose=_pose;
        float variation=state.Position.X*.73f+state.Position.Z*.51f+state.Item.Value*.013f;
        _phase=_previousPhase=(variation-MathF.Floor(variation))*MathHelper.TwoPi;
    }

    internal void Update(World world, float elapsed)
    {
        _previousPose=_pose;_previousPhase=_phase;_phase+=elapsed*2;
        if(_phase>=MathHelper.TwoPi*2){_phase-=MathHelper.TwoPi*2;_previousPhase-=MathHelper.TwoPi*2;}
        float before=VerticalVelocity;
        VerticalVelocity = Math.Max(-40, VerticalVelocity - 18 * elapsed);
        Vector3 movement=new(HorizontalVelocity.X*elapsed,(before+VerticalVelocity)*.5f*elapsed,HorizontalVelocity.Y*elapsed);
        int steps = Math.Max(1, (int)MathF.Ceiling(movement.Length() / 0.1f));
        bool grounded=false;
        for (int i = 0; i < steps; i++)
        {
            if(Move(world,movement.X/steps,0)){HorizontalVelocity=new(0,HorizontalVelocity.Y);movement.X=0;}
            if(Move(world,movement.Z/steps,2)){HorizontalVelocity=new(HorizontalVelocity.X,0);movement.Z=0;}
            if(Move(world,movement.Y/steps,1)){grounded=movement.Y<0;VerticalVelocity=0;movement.Y=0;}
        }
        HorizontalVelocity*=MathF.Exp(-(grounded?8:.8f)*elapsed);
        // Keep void items at their original X/Z, never teleport death drops to spawn.
        if (Position.Y < -16 + Radius) { Position = new Vector3(Position.X, -16 + Radius, Position.Z); VerticalVelocity = 0; }
    }
    private bool Move(World world,float amount,int axis)
    {
        if(amount==0)return false;
        Vector3 next=Position;if(axis==0)next.X+=amount;else if(axis==1)next.Y+=amount;else next.Z+=amount;
        var bounds=new BoundingBox(next-new Vector3(Radius),next+new Vector3(Radius));bool hit=false;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(int x=Math.Max(world.LocalMinX,(int)MathF.Floor(bounds.Min.X));x<=Math.Min(world.LocalMaxX-1,(int)MathF.Floor(bounds.Max.X));x++)
        for(int z=Math.Max(world.LocalMinZ,(int)MathF.Floor(bounds.Min.Z));z<=Math.Min(world.LocalMaxZ-1,(int)MathF.Floor(bounds.Max.Z));z++)
        for(int y=Math.Max(0,(int)MathF.Floor(bounds.Min.Y)-1);y<=Math.Min(World.Height-1,(int)MathF.Floor(bounds.Max.Y));y++)
        {
            int count=world.BlockBoxes(world.GlobalCell(x,y,z),boxes,true);
            for(int i=0;i<count;i++)if(World.Overlaps(bounds,boxes[i]))
            {
                var box=boxes[i];hit=true;
                if(axis==0)next.X=amount>0?Math.Min(next.X,box.Min.X-Radius):Math.Max(next.X,box.Max.X+Radius);
                else if(axis==1)next.Y=amount>0?Math.Min(next.Y,box.Min.Y-Radius):Math.Max(next.Y,box.Max.Y+Radius);
                else next.Z=amount>0?Math.Min(next.Z,box.Min.Z-Radius):Math.Max(next.Z,box.Max.Z+Radius);
            }
        }
        next.X=Math.Clamp(next.X,world.LocalMinX+Radius,world.LocalMaxX-Radius);next.Z=Math.Clamp(next.Z,world.LocalMinZ+Radius,world.LocalMaxZ-Radius);
        Position=next;return hit;
    }
}

public sealed partial class DroppedItems
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
    internal int RemoveAll()
    {
        int count = _items.Count;
        _items.Clear(); _mergeHeads.Clear(); _mergeElapsed = 0;
        return count;
    }

    public DroppedItems(World world, ReadOnlySpan<DroppedItemState> saved = default)
    {
        _world = world;
        if (saved.Length > MaximumEntities) throw new InvalidDataException("Too many dropped items.");
        foreach (DroppedItemState state in saved)
        {
            Validate(state, world.Size);
            _items.Add(Make(state));
        }
    }

    public void Spawn(ItemId item, int count, Vector3 position, float pickupDelay = 0.35f)
    {
        if(!Items.IsDefined(item) || count<1)throw new ArgumentException("Invalid dropped item quantity/type.");
        int limit=Items.StackLimit(item);
        if(count>limit)
        {
            if(!CanSpawn((count+limit-1)/limit))throw new InvalidOperationException("Dropped-item limit reached.");
            while(count>0){int quantity=Math.Min(count,limit);Spawn(item,quantity,position,pickupDelay);count-=quantity;}
            return;
        }
        var stack=new ItemStack(item,count);
        var state = new DroppedItemState(item, count,
            new Vector3(Math.Clamp(position.X, _world.LocalMinX + DroppedItem.Radius, _world.LocalMaxX - DroppedItem.Radius), position.Y,
                Math.Clamp(position.Z, _world.LocalMinZ + DroppedItem.Radius, _world.LocalMaxZ - DroppedItem.Radius)), 0, pickupDelay,stack.Durability,stack.InstanceId);
        Validate(state, _world.Size);
        if (!CanSpawn()) throw new InvalidOperationException("Dropped-item limit reached.");
        if(!Items.LavaResistant(item) && Lava.Intersects(_world,new BoundingBox(state.Position-new Vector3(DroppedItem.Radius),state.Position+new Vector3(DroppedItem.Radius))))return;
        _items.Add(Make(state));
    }

    public bool TrySpawn(ItemStack stack, Vector3 position, float pickupDelay,Vector3 velocity=default)
    {
        if (stack.IsEmpty || !CanSpawn() || !float.IsFinite(velocity.LengthSquared()) || velocity.LengthSquared()>1600) return false;
        var state=new DroppedItemState(stack.Item,stack.Count,position,0,pickupDelay,stack.Durability,stack.InstanceId);
        Validate(state,_world.Size);
        if(!Items.LavaResistant(stack.Item) && Lava.Intersects(_world,new BoundingBox(position-new Vector3(DroppedItem.Radius),position+new Vector3(DroppedItem.Radius))))return true;
        var entity=Make(state);entity.Throw(velocity);_items.Add(entity);
        return true;
    }
    public bool Wear(Guid id)
    {
        for(int i=0;i<_items.Count;i++)if(id!=Guid.Empty && _items[i].InstanceId==id){_items[i].Wear();if(_items[i].Count==0)_items.RemoveAt(i);return true;}
        return false;
    }

    public void Update(Player player, Inventory inventory, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0) return;
        float dt = Math.Min(elapsed, 0.1f);
        for (int i = 0; i < _items.Count; i++)
        {
            DroppedItem current=_items[i];
            if(!_world.IsSimulated(_world.GlobalCell(current.Position)))continue;
            if(!Items.LavaResistant(current.Item) && Lava.Intersects(_world,new BoundingBox(current.Position-new Vector3(DroppedItem.Radius),current.Position+new Vector3(DroppedItem.Radius))))
            {_items[i]=_items[^1];_items.RemoveAt(_items.Count-1);i--;continue;}
            _items[i].PickupDelay = Math.Max(0, _items[i].PickupDelay - elapsed);
            _items[i].Update(_world, dt);
            current=_items[i];
            if(!Items.LavaResistant(current.Item) && Lava.Intersects(_world,new BoundingBox(current.Position-new Vector3(DroppedItem.Radius),current.Position+new Vector3(DroppedItem.Radius))))
            {_items[i]=_items[^1];_items.RemoveAt(_items.Count-1);i--;}
        }
        _mergeElapsed += elapsed;
        if (_mergeElapsed >= 0.25f)
        {
            MergeNearby();
            _mergeElapsed %= 0.25f;
        }
        BoundingBox playerBounds = player.Bounds;
        bool collected=false;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            DroppedItem item = _items[i];
            Vector3 nearest = Vector3.Clamp(item.Position, playerBounds.Min, playerBounds.Max);
            if (player.IsDead || item.PickupDelay > 0 || Vector3.DistanceSquared(item.Position, nearest) > PickupRadius * PickupRadius) continue;
            int before=item.Count;item.Count = inventory.Add(item.Stack).Count;
            collected|=item.Count<before;
            if (item.Count != 0) continue;
            // Swap removal avoids shifting the list; the swapped entry was already updated.
            _items[i] = _items[^1];
            _items.RemoveAt(_items.Count - 1);
        }
        if(collected)_world.Sounds?.Local(SoundKind.Pickup);
    }

    private void MergeNearby()
    {
        _mergeHeads.Clear();
        if (_mergeNext.Length < _items.Count) Array.Resize(ref _mergeNext, Math.Max(_items.Count, Math.Max(64, _mergeNext.Length * 2)));
        for (int i = 0; i < _items.Count; i++)
        {
            DroppedItem item = _items[i];
            if(Items.StackLimit(item.Item)==1)continue;
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

    private DroppedItem Make(DroppedItemState state){var item=new DroppedItem(state);item.Bind(_world);return item;}
    public DroppedItemState[] Capture()
    {
        var states = new DroppedItemState[_items.Count];
        for (int i = 0; i < states.Length; i++) states[i] = _items[i].Capture();
        return states;
    }

    public static void Validate(DroppedItemState state, WorldSize size)
    {
        Vector3 p = state.Position;
        int maximum=Items.Get(state.Item).Durability;
        if(maximum>0 ? state.Durability<1 || state.Durability>maximum || state.InstanceId==Guid.Empty : state.Durability!=0 || state.InstanceId!=Guid.Empty)
            throw new InvalidDataException("Invalid dropped-item durability/identity.");
        try { _=state.Stack; }
        catch(ArgumentException e){throw new InvalidDataException("Invalid dropped-item metadata.",e);}
        if(Items.Get(state.Item).Durability>0 && state.InstanceId==Guid.Empty)throw new InvalidDataException("Missing equipment identity.");
        if (!Items.IsDefined(state.Item) || state.Count < 1 || state.Count > ItemStack.Maximum
            || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
            || size!=WorldSize.Infinite && (p.X < DroppedItem.Radius || p.X > size.Width() - DroppedItem.Radius
            || p.Z < DroppedItem.Radius || p.Z > size.Width() - DroppedItem.Radius) || p.Y < -16 || p.Y > World.Height + 16
            || !float.IsFinite(state.VerticalVelocity) || MathF.Abs(state.VerticalVelocity) > 40
            || !float.IsFinite(state.PickupDelay) || state.PickupDelay < 0 || state.PickupDelay > DeliberatePickupDelay)
            throw new InvalidDataException("Save contains an invalid dropped item.");
    }
}
