using System;
using Microsoft.Xna.Framework;
using MDSM.Persistence;
using MDSM.Entities;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public sealed class EnvironmentalHazards
{
    private double _actors;
    private double _cactusSeconds;
    internal static bool CactusSideContact(World world,BoundingBox body)
    {
        const float skin=.012f;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(int x=(int)MathF.Floor(body.Min.X-skin);x<=(int)MathF.Floor(body.Max.X+skin);x++)
        for(int z=(int)MathF.Floor(body.Min.Z-skin);z<=(int)MathF.Floor(body.Max.Z+skin);z++)
        for(int y=Math.Max(0,(int)MathF.Floor(body.Min.Y));y<=Math.Min(World.Height-1,(int)MathF.Floor(body.Max.Y));y++)
        {
            if(world.GetBlock(x,y,z)!=BlockType.Cactus)continue;
            int count=world.BlockBoxes(world.GlobalCell(x,y,z),boxes,true);
            for(int i=0;i<count;i++)
            {
                var b=boxes[i];if(Math.Min(body.Max.Y,b.Max.Y)-Math.Max(body.Min.Y,b.Min.Y)<=.001f)continue;
                float ox=Math.Min(body.Max.X,b.Max.X)-Math.Max(body.Min.X,b.Min.X),oz=Math.Min(body.Max.Z,b.Max.Z)-Math.Max(body.Min.Z,b.Min.Z);
                if(ox> .001f&&oz>=-skin || oz>.001f&&ox>=-skin)return true;
            }
        }
        return false;
    }
    public void Update(GameSession session,float dt)
    {
        if(!float.IsFinite(dt)||dt<=0)return;
        if(session.Player.Creative||session.Player.IsDead||!CactusSideContact(session.World,session.Player.Bounds))_cactusSeconds=0;
        else
        {
            _cactusSeconds+=dt;
            while(_cactusSeconds>=1-1e-7){_cactusSeconds=Math.Max(0,_cactusSeconds-1);session.Player.TakeDamage(1,false);}
        }
        if(session.Player.Creative)session.Player.Burning.Reset();
        else {int damage=session.Player.Burning.Update(session.World,session.Player.Bounds,dt);if(damage>0)session.Player.TakeDamage(damage,false);}
        _actors+=dt;if(_actors<.25)return;double elapsed=_actors;_actors=0;
        for(int i=session.Mobs.Entities.Count-1;i>=0;i--)
        {
            var mob=session.Mobs.Entities[i];
            if(!session.World.IsSimulated(session.World.GlobalCell(mob.Position)))continue;
            int damage=mob.Burning.Update(session.World,mob.Bounds,elapsed);
            if(damage>0)session.Mobs.Hit(mob,damage,mob.Position,session.Items,false);
        }
        for(int i=session.Hostiles.Entities.Count-1;i>=0;i--)
        {
            var mob=session.Hostiles.Entities[i];Vector3 head=new(mob.Position.X,mob.Bounds.Max.Y-.05f,mob.Position.Z);
            if(!session.World.IsSimulated(session.World.GlobalCell(mob.Position)))continue;
            bool sun=session.World.Time.SunlightLevel>=11 && session.World.SurfaceY((int)head.X,(int)head.Z)<head.Y;
            int damage=mob.Burning.Update(session.World,mob.Bounds,elapsed,sun);
            if(damage>0)session.Hostiles.Hit(mob,damage,mob.Position,session.Items,false);
        }
    }
}
