using System;
using Microsoft.Xna.Framework;
using MDSM.Persistence;
using MDSM.Entities;

namespace MDSM.Gameplay;

public sealed class EnvironmentalHazards
{
    private double _actors;
    public void Update(GameSession session,float dt)
    {
        if(!float.IsFinite(dt)||dt<=0)return;
        if(session.Player.Creative)session.Player.Burning.Reset();
        else {int damage=session.Player.Burning.Update(session.World,session.Player.Bounds,dt);if(damage>0)session.Player.TakeDamage(damage,false);}
        _actors+=dt;if(_actors<.25)return;double elapsed=_actors;_actors=0;
        for(int i=session.Mobs.Entities.Count-1;i>=0;i--)
        {
            var mob=session.Mobs.Entities[i];int damage=mob.Burning.Update(session.World,mob.Bounds,elapsed);
            if(damage>0)session.Mobs.Hit(mob,damage,mob.Position,session.Items,false);
        }
        for(int i=session.Hostiles.Entities.Count-1;i>=0;i--)
        {
            var mob=session.Hostiles.Entities[i];Vector3 head=new(mob.Position.X,mob.Bounds.Max.Y-.05f,mob.Position.Z);
            bool sun=session.World.Time.SunlightLevel>=11 && session.World.SurfaceY((int)head.X,(int)head.Z)<head.Y;
            int damage=mob.Burning.Update(session.World,mob.Bounds,elapsed,sun);
            if(damage>0)session.Hostiles.Hit(mob,damage,mob.Position,session.Items,false);
        }
    }
}
