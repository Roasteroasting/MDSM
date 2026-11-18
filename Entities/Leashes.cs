using System;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Entities;

public enum LeashAnchor : byte { None, Player, Fence, PendingReturn }

public readonly record struct LeashState(LeashAnchor Anchor,Point3 Fence=default)
{
    internal bool Active=>Anchor is LeashAnchor.Player or LeashAnchor.Fence;
    internal bool OwnsLead=>Anchor!=LeashAnchor.None;
    internal void Validate(Guid id)
    {
        if(!Enum.IsDefined(Anchor) || OwnsLead && id==Guid.Empty
            || Anchor!=LeashAnchor.Fence && Fence!=default
            || Anchor==LeashAnchor.Fence && (Fence.Y<0 || Fence.Y>=World.Height || !Worlds.Streaming.Coordinates.Valid(Fence.X,Fence.Z)))
            throw new InvalidDataException("Invalid passive mob leash.");
    }
}

public sealed partial class PassiveMobPopulation
{
    internal const float LeashLength=5, LeashBreakDistance=10;
    private readonly System.Collections.Generic.HashSet<Guid> _ids=new();

    internal bool TryLeash(PassiveMob mob,Inventory inventory)
    {
        if(mob==null || mob.IsDead || !_mobs.Contains(mob) || mob.Leash.OwnsLead || inventory.Selected.Item!=Items.Lead)return false;
        if(mob.Id==Guid.Empty)
        {
            do mob.Id=Guid.NewGuid();
            while(!_ids.Add(mob.Id));
        }
        // Each attachment owns a real item, including when created in Creative.
        inventory.ConsumeSelected();mob.Leash=new(LeashAnchor.Player);
        return true;
    }

    internal bool InteractFence(Point3 fence,Inventory inventory,DroppedItems drops)
    {
        if(!_world.IsLoaded(fence) || _world.GetBlock(fence.X,fence.Y,fence.Z)!=BlockType.Fence)return false;
        bool handled=false;
        Vector3 anchor=FenceTip(_world,fence);
        foreach(var mob in _mobs)
        {
            if(mob.Leash.Anchor!=LeashAnchor.Player || Vector3.DistanceSquared(mob.Position,anchor)>LeashBreakDistance*LeashBreakDistance)continue;
            mob.Leash=new(LeashAnchor.Fence,fence);handled=true;
        }
        if(handled)return true;
        foreach(var mob in _mobs)
        {
            if(mob.Leash.Anchor!=LeashAnchor.Fence || mob.Leash.Fence!=fence)continue;
            TryUnleash(mob,inventory,drops);handled=true;
        }
        return handled;
    }

    internal bool TryUnleash(PassiveMob mob,Inventory inventory,DroppedItems drops)
    {
        if(mob==null || !mob.Leash.OwnsLead)return false;
        if((inventory==null || inventory.Add(Items.Lead,1)!=0)
            && drops?.TrySpawn(new ItemStack(Items.Lead,1),mob.Position+Vector3.Up*.3f,.25f)!=true)return false;
        mob.Leash=default;
        return true;
    }

    internal void FenceRemoved(Point3 fence)
    {
        foreach(var mob in _mobs)
            if(mob.Leash.Anchor==LeashAnchor.Fence && mob.Leash.Fence==fence)
                mob.Leash=new(LeashAnchor.PendingReturn);
    }

    internal static Vector3 FenceTip(World world,Point3 fence)=>world.LocalPosition(fence)+new Vector3(.5f,.88f,.5f);
    private Vector3? LeashSteering(PassiveMob mob,Vector3? player,DroppedItems drops)
    {
        var leash=mob.Leash;
        if(!leash.OwnsLead)return null;
        if(leash.Anchor==LeashAnchor.PendingReturn)
        {TryUnleash(mob,null,drops);return null;}
        Vector3 target;
        if(leash.Anchor==LeashAnchor.Player)
        {
            if(!player.HasValue)return Vector3.Zero;
            target=player.Value;
        }
        else
        {
            target=FenceTip(_world,leash.Fence);
            if(Vector3.DistanceSquared(target,mob.Position)<=LeashBreakDistance*LeashBreakDistance)
            {
                if(!_world.IsLoaded(leash.Fence))return Vector3.Zero;
                if(_world.GetBlock(leash.Fence.X,leash.Fence.Y,leash.Fence.Z)!=BlockType.Fence)
                {BreakLeash(mob,drops);return null;}
            }
        }
        Vector3 delta=target-mob.Position;
        if(delta.LengthSquared()>LeashBreakDistance*LeashBreakDistance)
        {BreakLeash(mob,drops);return null;}
        if(delta.LengthSquared()>LeashLength*LeashLength)
        {
            delta.Y=0;
            return delta;
        }
        // A slack leash allows normal nearby wandering; a taut leash owns steering.
        return null;
    }
    private void BreakLeash(PassiveMob mob,DroppedItems drops)
    {
        mob.Leash=new(LeashAnchor.PendingReturn);
        TryUnleash(mob,null,drops);
    }
}
