using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Persistence;

public sealed partial class GameSession
{
    internal WorldPosition? PendingTeleport {get;private set;}
    internal string StreamingNotice {get;private set;}
    internal bool RequestTeleport(WorldPosition target)
    {
        if(!World.IsInfinite||!target.Valid||target.Y<0||target.Y>Worlds.World.Height+16)return false;
        if(target.X< -Coordinates.MaximumBlock+Gameplay.Player.HalfWidth||target.Z< -Coordinates.MaximumBlock+Gameplay.Player.HalfWidth||target.X>Coordinates.MaximumBlock-Gameplay.Player.HalfWidth||target.Z>Coordinates.MaximumBlock-Gameplay.Player.HalfWidth)return false;
        PendingTeleport=target;Player.StopMovement();Mining.Reset();Bow.Cancel();Bobbing.Reset();Swing.Reset();return true;
    }
    internal bool AdvanceStreaming(out bool reframed)
    {
        reframed=false;if(!World.IsInfinite)return true;
        WorldPosition target=PendingTeleport??Player.AbsolutePosition;
        long ox=Coordinates.FloorDiv((long)Math.Floor(target.X),16)*16,oz=Coordinates.FloorDiv((long)Math.Floor(target.Z),16)*16;
        if(Math.Abs(target.X-World.OriginX)>64||Math.Abs(target.Z-World.OriginZ)>64)
        {
            Projectiles.PrepareReframe();
            Vector3 delta=new(World.OriginX-ox,0,World.OriginZ-oz);World.SetOrigin(ox,oz);Projectiles.FinishReframe();Hostiles.Reframe(delta);
            foreach(var mob in Mobs.Entities)mob.FrameChanged();World.Weather.Reframe(delta);World.Particles.Reframe(delta);Bobbing.Reframe(delta);Sounds.Clear();
            Camera.Position=Player.EyePosition;reframed=true;
        }
        var center=ChunkAddress.At((long)Math.Floor(target.X),(long)Math.Floor(target.Z));World.UpdateStreaming(center);
        if(PendingTeleport.HasValue)Camera.Position=World.LocalPosition(target)+Vector3.Up*Gameplay.Player.StandingEyeHeight;
        if(World.ChunkLifecycle.Error!=null){StreamingNotice="CHUNK LOAD/SAVE FAILED: "+World.ChunkLifecycle.Error.Message;return false;}
        bool ready=true;for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){long bx=(center.X+x)*16,bz=(center.Z+z)*16;if(Coordinates.Valid(bx,bz)&&Coordinates.Valid(bx+15,bz+15))ready&=World.IsLoaded(new(bx,0,bz));}
        if(!ready){StreamingNotice="LOADING NEARBY TERRAIN...";return false;}
        if(PendingTeleport is WorldPosition destination)
        {
            Vector3 local=World.LocalPosition(destination);var bounds=new BoundingBox(local+new Vector3(-Gameplay.Player.HalfWidth,0,-Gameplay.Player.HalfWidth),local+new Vector3(Gameplay.Player.HalfWidth,Gameplay.Player.BodyHeight,Gameplay.Player.HalfWidth));
            PendingTeleport=null;
            if(World.IntersectsSolid(bounds)||Water.Intersects(World,bounds)||Lava.Intersects(World,bounds)||World.Fire.Contact(bounds))
            {StreamingNotice="UNSAFE TELEPORT DESTINATION; POSITION RETAINED.";return false;}
            Player.SetAbsolute(destination);Camera.Position=Player.EyePosition;
        }
        StreamingNotice=null;return true;
    }
    private bool _streamClosed;
    internal void CloseStreaming(bool checkpointIsCurrent=false)
    {if(World.IsInfinite&&!_streamClosed){try{World.ChunkLifecycle.CloseAsync(checkpointIsCurrent).AsTask().GetAwaiter().GetResult();_streamClosed=true;}catch(AggregateException e){throw new System.IO.IOException("Could not close all Infinite chunk records. The session remains open.",e);}}}
}
