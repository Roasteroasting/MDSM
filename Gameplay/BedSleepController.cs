using System;
using Microsoft.Xna.Framework;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Gameplay;

// A transient camera/menu transition. The body remains at its original position;
// normal simulation is suspended by the Sleeping game state, as in other menus.
public sealed class BedSleepController
{
    public const double FadeSeconds=4;
    public const float MonsterRadius=10;
    public const string MonsterNotice="you may not sleep now; there are monsters nearby";
    public bool IsSleeping { get; private set; }
    public double Elapsed { get; private set; }
    public float Fade
    {get{float t=(float)Math.Clamp(Elapsed/FadeSeconds,0,1);return t*t*(3-2*t);}}
    public Vector3 ReturnCameraPosition { get; private set; }
    public float ReturnYaw { get; private set; }
    public float ReturnPitch { get; private set; }
    public Point3 Head { get; private set; }
    private Point3 _foot;
    private byte _facing;
    private BoundingBox _bedBounds;
    private string _notice;

    public string TakeNotice(){string notice=_notice;_notice=null;return notice;}
    public bool TryBegin(GameSession session,BlockHit hit)
    {
        _notice=null;
        if(IsSleeping || session.Player.IsDead || !float.IsFinite(hit.Distance) || hit.Distance<0 || hit.Distance>BlockRaycaster.Reach
            || !ResolveBed(session.World,hit.Cell,out Point3 foot,out Point3 head,out byte facing))return false;
        var bounds=new BoundingBox(new Vector3(Math.Min(foot.X,head.X),foot.Y,Math.Min(foot.Z,head.Z)),
            new Vector3(Math.Max(foot.X,head.X)+1,foot.Y+9f/16,Math.Max(foot.Z,head.Z)+1));
        if(MonstersNearby(session,bounds)){_notice=MonsterNotice;return false;}
        if(!session.World.Time.CanSleep)return false;
        _foot=foot;Head=head;_facing=facing;_bedBounds=bounds;
        ReturnCameraPosition=session.Camera.Position;ReturnYaw=session.Camera.Yaw;ReturnPitch=session.Camera.Pitch;
        Elapsed=0;IsSleeping=true;
        session.Camera.PresentationOffset=Vector3.Zero;
        session.Camera.Position=new(head.X+.5f,head.Y+9f/16+.3f,head.Z+.5f);
        Point3 towardHead=World.BedDirection(facing);
        session.Camera.RestoreOrientation(MathF.Atan2(-towardHead.X,towardHead.Z),.08f);
        return true;
    }
    public void Update(GameSession session,double elapsed)
    {
        if(!IsSleeping || !double.IsFinite(elapsed) || elapsed<=0)return;
        if(session.Player.IsDead || !session.World.Time.CanSleep || !ResolveBed(session.World,_foot,out var foot,out var head,out byte facing)
            || foot!=_foot || head!=Head || facing!=_facing){Cancel(session.Camera);return;}
        if(MonstersNearby(session,_bedBounds)){_notice=MonsterNotice;Cancel(session.Camera);return;}
        Elapsed=Math.Min(FadeSeconds,Elapsed+elapsed);
        if(Elapsed+1e-9<FadeSeconds)return;
        session.World.Time.Sleep();
        Cancel(session.Camera);
    }
    public void Cancel(Camera camera)
    {
        if(!IsSleeping)return;
        camera.Position=ReturnCameraPosition;camera.RestoreOrientation(ReturnYaw,ReturnPitch);camera.PresentationOffset=Vector3.Zero;
        IsSleeping=false;Elapsed=0;
    }
    private static bool ResolveBed(World world,Point3 cell,out Point3 foot,out Point3 head,out byte facing)
    {
        foot=head=default;facing=0;BlockType block=world.GetBlock(cell.X,cell.Y,cell.Z);
        if(!Blocks.Bed(block) || !world.Structures.TryGetValue(cell,out var state) || state.Facing>3)return false;
        facing=state.Facing;Point3 direction=World.BedDirection(facing);
        foot=block==BlockType.Bed?cell:cell+new Point3(-direction.X,0,-direction.Z);head=foot+direction;
        return world.GetBlock(foot.X,foot.Y,foot.Z)==BlockType.Bed && world.GetBlock(head.X,head.Y,head.Z)==BlockType.BedHead
            && world.Structures.TryGetValue(foot,out var lower) && world.Structures.TryGetValue(head,out var upper)
            && lower.Facing==facing && upper.Facing==facing;
    }
    private static bool MonstersNearby(GameSession session,BoundingBox bed)
    {
        var mobs=session.Hostiles.Entities;
        for(int i=0;i<mobs.Count;i++)
        {
            var mob=mobs[i];
            if(mob.IsDead)continue;BoundingBox body=mob.Bounds;
            float x=Math.Max(0,Math.Max(bed.Min.X-body.Max.X,body.Min.X-bed.Max.X));
            float y=Math.Max(0,Math.Max(bed.Min.Y-body.Max.Y,body.Min.Y-bed.Max.Y));
            float z=Math.Max(0,Math.Max(bed.Min.Z-body.Max.Z,body.Min.Z-bed.Max.Z));
            if(x*x+y*y+z*z<=MonsterRadius*MonsterRadius+.0001f)return true;
        }
        return false;
    }
}
