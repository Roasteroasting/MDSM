using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;
using MDSM.Audio;

namespace MDSM.Entities;

public enum ProjectileKind:byte { Arrow, Snowball }
public readonly record struct ArrowState(Vector3 Position,Vector3 Velocity,float Age,int Damage,Guid Bow,bool Hostile=false,ProjectileKind Kind=ProjectileKind.Arrow);
public sealed class Projectiles
{
    public const int Maximum=256;
    public const float Lifetime=12,Gravity=18;
    public const float MaximumAltitude=World.Height+64;
    private readonly World _world;
    private readonly List<ArrowState> _arrows=new();
    public IReadOnlyList<ArrowState> Arrows=>_arrows;
    internal int RemoveAll(ProjectileKind? kind=null)
    {
        if(!kind.HasValue){int count=_arrows.Count;_arrows.Clear();return count;}
        int removed=0;for(int i=_arrows.Count-1;i>=0;i--)if(_arrows[i].Kind==kind.Value){_arrows.RemoveAt(i);removed++;}return removed;
    }
    internal SoundEvents Sounds=>_world.Sounds;
    public Projectiles(World world,ArrowState[] states=null,ArrowState[] snowballs=null)
    {
        if((states?.Length??0)+(snowballs?.Length??0)>Maximum)throw new InvalidDataException("Too many projectiles.");
        _world=world;if(snowballs!=null)foreach(var snowball in snowballs){Validate(snowball,world);_arrows.Add(snowball);}
        if(states==null)return;
        foreach(var state in states){Validate(state,world);_arrows.Add(state);}
    }
    public bool ThrowSnowball(Inventory inventory,Vector3 origin,Vector3 direction,bool creative=false)
    {
        if(inventory.Selected.Item!=Items.Snowball || _arrows.Count>=Maximum || !float.IsFinite(origin.LengthSquared()) || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared()<.001f)return false;
        var ball=new ArrowState(origin,Vector3.Normalize(direction)*18,0,0,Guid.NewGuid(),Kind:ProjectileKind.Snowball);
        Validate(ball,_world);_arrows.Add(ball);if(!creative)inventory.ConsumeSelected();return true;
    }
    public bool Shoot(Inventory inventory,Vector3 origin,Vector3 direction,float power,Guid bow)
    {
        if(_arrows.Count>=Maximum || bow==Guid.Empty || inventory.Selected.Item!=Items.Bow || inventory.Selected.InstanceId!=bow
            || !float.IsFinite(power) || power<=0 || !float.IsFinite(direction.LengthSquared()) || !float.IsFinite(origin.LengthSquared())
            || direction.LengthSquared()<.00001f || origin.X<0 || origin.X>=_world.Width || origin.Z<0 || origin.Z>=_world.Depth || origin.Y<0 || origin.Y>MaximumAltitude)return false;
        if(!inventory.Consume(Items.Arrow))return false;
        direction.Normalize();power=Math.Clamp(power,0,1);
        _arrows.Add(new(origin,direction*(12+18*power),0,1+(int)MathF.Round(power*4),bow));Sounds?.At(SoundKind.BowShoot,origin);return true;
    }
    public bool ShootHostile(Vector3 origin,Vector3 velocity)
    {if(_arrows.Count>=Maximum)return false;var arrow=new ArrowState(origin,velocity,0,2,Guid.NewGuid(),true);Validate(arrow,_world);_arrows.Add(arrow);Sounds?.At(SoundKind.BowShoot,origin);return true;}
    internal void RestoreHostileFlag(int index){if(index<0||index>=_arrows.Count)throw new InvalidDataException("Invalid arrow flag.");_arrows[index]=_arrows[index] with {Hostile=true};}
    public void Update(float elapsed,PassiveMobPopulation mobs,DroppedItems drops,Action<Guid> wear,HostilePopulation hostiles=null,Player player=null)
    {
        if(!float.IsFinite(elapsed) || elapsed<=0)return;
        for(int i=_arrows.Count-1;i>=0;i--)
        {
            ArrowState arrow=_arrows[i];float remaining=elapsed;bool removed=false;
            while(remaining>1e-7 && !removed)
            {
                float dt=Math.Min(remaining,.025f);remaining-=dt;
                Vector3 delta=arrow.Velocity*dt-Vector3.Up*(.5f*Gravity*dt*dt),next=arrow.Position+delta;
                float length=delta.Length();float limit=BlockRaycaster.Cast(_world,arrow.Position,delta,length,solidOnly:true)?.Distance??length+.001f;
                PassiveMob target=null;var ray=new Ray(arrow.Position,delta/Math.Max(length,.000001f));
                for(int mobIndex=0;!arrow.Hostile && mobIndex<mobs.Entities.Count;mobIndex++)
                {
                    var mob=mobs.Entities[mobIndex];if(Vector3.DistanceSquared(mob.Position,arrow.Position)>(length+2)*(length+2))continue;
                    float? hit=ray.Intersects(mob.Bounds);if(hit.HasValue && hit.Value<limit && hit.Value<=length && !mob.IsDead){limit=hit.Value;target=mob;}
                }
                if(!arrow.Hostile && hostiles!=null)for(int j=0;j<hostiles.Entities.Count;j++)
                {PassiveMob mob=hostiles.Entities[j];float? hit=ray.Intersects(mob.Bounds);if(hit.HasValue && hit.Value<limit && hit.Value<=length){limit=hit.Value;target=mob;}}
                float? playerHit=arrow.Hostile && player!=null?ray.Intersects(player.Bounds):null;
                if(playerHit.HasValue && playerHit.Value<limit && playerHit.Value<=length){player.TakeDamage(arrow.Damage);removed=true;}
                if(target!=null)
                {
                    if(arrow.Kind==ProjectileKind.Snowball)target.Knockback(arrow.Position-arrow.Velocity);
                    else if((int)target.Species>=4?hostiles.Hit(target,arrow.Damage,arrow.Position-arrow.Velocity,drops):mobs.Hit(target,arrow.Damage,arrow.Position-arrow.Velocity,drops))wear(arrow.Bow);
                    removed=true;
                }
                else if(limit<=length || arrow.Age+dt>=Lifetime || next.X<0 || next.X>=_world.Width || next.Z<0 || next.Z>=_world.Depth || next.Y<0 || next.Y>MaximumAltitude)removed=true;
                if(removed && (target!=null || playerHit.HasValue && playerHit.Value<=length || limit<=length))Sounds?.At(SoundKind.ArrowHit,arrow.Position+ray.Direction*Math.Min(length,limit));
                arrow=arrow with {Position=next,Velocity=arrow.Velocity-Vector3.Up*Gravity*dt,Age=arrow.Age+dt};
            }
            if(removed){_arrows[i]=_arrows[^1];_arrows.RemoveAt(_arrows.Count-1);}else _arrows[i]=arrow;
        }
    }
    public ArrowState[] Capture()=>CaptureKind(ProjectileKind.Arrow);
    public ArrowState[] CaptureSnowballs()=>CaptureKind(ProjectileKind.Snowball);
    private ArrowState[] CaptureKind(ProjectileKind kind)
    {int count=0;foreach(var arrow in _arrows)if(arrow.Kind==kind)count++;var result=new ArrowState[count];int index=0;foreach(var arrow in _arrows)if(arrow.Kind==kind)result[index++]=arrow;return result;}
    public static void Validate(ArrowState state,World world)
    {
        Vector3 p=state.Position,v=state.Velocity;
        if(!float.IsFinite(p.LengthSquared()) || !float.IsFinite(v.LengthSquared()) || p.X<0 || p.X>=world.Width || p.Z<0 || p.Z>=world.Depth
            || p.Y<0 || p.Y>MaximumAltitude || v.LengthSquared()>100000 || !float.IsFinite(state.Age) || state.Age<0 || state.Age>=Lifetime
            || (state.Kind==ProjectileKind.Arrow?state.Damage<1 || state.Damage>5:state.Kind!=ProjectileKind.Snowball || state.Damage!=0 || state.Hostile) || state.Bow==Guid.Empty)throw new InvalidDataException("Invalid projectile state.");
    }
}

public sealed class BowController
{
    private Guid _bow;
    public float DrawSeconds { get; private set; }
    public float Power=>Math.Clamp(DrawSeconds,0,1);
    public void Cancel(){_bow=Guid.Empty;DrawSeconds=0;}
    public bool Update(Inventory inventory,Projectiles projectiles,Vector3 origin,Vector3 direction,bool right,float elapsed)
    {
        if(inventory.Selected.Item!=Items.Bow || !float.IsFinite(elapsed) || elapsed<0){Cancel();return false;}
        bool ammunition=false;for(int i=0;i<Inventory.SlotCount;i++)if(inventory[i].Item==Items.Arrow && !inventory[i].IsEmpty){ammunition=true;break;}
        if(!ammunition){Cancel();return false;}
        if(_bow!=Guid.Empty && _bow!=inventory.Selected.InstanceId)Cancel();
        if(right){if(_bow==Guid.Empty)projectiles.Sounds?.At(SoundKind.BowDraw,origin);_bow=inventory.Selected.InstanceId;DrawSeconds=Math.Min(1,DrawSeconds+elapsed);return false;}
        bool shot=_bow!=Guid.Empty && DrawSeconds>=.1f && projectiles.Shoot(inventory,origin,direction,Power,_bow);
        Cancel();return shot;
    }
}
