using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public readonly record struct BurningState(double Remaining,double DamageClock);
public sealed class BurningStatus
{
    public const double Duration=9;
    public double Remaining {get;private set;}
    private double _damage;
    public void Reset(){Remaining=_damage=0;}
    public int Update(World world,BoundingBox bounds,double dt,bool sunlight=false)
    {
        if(!double.IsFinite(dt)||dt<=0)return 0;
        Vector3 head=new((bounds.Min.X+bounds.Max.X)*.5f,bounds.Max.Y-.05f,(bounds.Min.Z+bounds.Max.Z)*.5f);
        if(Water.Intersects(world,bounds)||world.Weather.ExposedRain(head)){Reset();return 0;}
        if(world.Fire.Contact(bounds)||sunlight)Remaining=Duration;
        if(Remaining<=0)return 0;
        _damage+=Math.Min(dt,Remaining);Remaining=Math.Max(0,Remaining-dt);
        int result=(int)Math.Floor(_damage+1e-8);_damage=Remaining<=0?0:Math.Max(0,_damage-result);return result;
    }
    public BurningState Capture()=>new(Remaining,_damage);
    public void Restore(BurningState state)
    {if(!double.IsFinite(state.Remaining)||state.Remaining<0||state.Remaining>Duration||!double.IsFinite(state.DamageClock)||state.DamageClock<0||state.DamageClock>=1)throw new System.IO.InvalidDataException("Invalid burning state.");Remaining=state.Remaining;_damage=state.DamageClock;}
}
