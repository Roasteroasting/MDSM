using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public enum FireAttachment:byte { Automatic,Floor,North,East,South,West }
public readonly record struct FireState(Point3 Cell,double Remaining,double NextTick,FireAttachment Attachment=FireAttachment.Automatic);
public sealed class FireSimulation(World world)
{
    public const int Maximum=4096,Budget=64;
    public const double SpreadChancePerTick=.25;
    private sealed class Flame {public double Expires,Due;public int Version;public FireAttachment Attachment;}
    private readonly Dictionary<Point3,Flame> _flames=new();
    private readonly PriorityQueue<(Point3 Cell,int Version),double> _ticks=new();
    private readonly Queue<Point3> _lava=new();private readonly HashSet<Point3> _lavaQueued=new();
    private readonly Random _random=new(unchecked(world.Seed^0x46495245));
    private double _time;
    public int Count=>_flames.Count;
    internal bool CanAdd=>Count<Maximum;
    public int LastProcessed {get;private set;}
    private static readonly Point3[] Adjacent={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
    public static bool Flammable(BlockType b)=>b is BlockType.WoodLog or BlockType.Leaves or BlockType.WhiteWool or BlockType.WoodenPlanks or BlockType.CraftingTable
        or BlockType.Fence or BlockType.FenceGate or BlockType.Door or BlockType.DoorUpper or BlockType.Sign or BlockType.WoodenSlab or BlockType.DoubleWoodenSlab or BlockType.WoodenStairs or BlockType.Sapling or BlockType.TallGrass or BlockType.DeadBush or BlockType.Bed or BlockType.BedHead;
    public bool Ignite(Point3 cell,Point3? support=null)
    {
        if(!CanAdd || world.GetBlock(cell.X,cell.Y,cell.Z)!=BlockType.Air || !world.Contains(cell.X,cell.Y,cell.Z))return false;
        if(support is Point3 explicitFace && (explicitFace.Y>0 || Math.Abs(explicitFace.X)+Math.Abs(explicitFace.Y)+Math.Abs(explicitFace.Z)!=1))return false;
        foreach(Point3 d in Adjacent)if(Water.IsWater(world.GetBlock(cell.X+d.X,cell.Y+d.Y,cell.Z+d.Z)))return false;
        FireAttachment attachment=support.HasValue?Encode(support.Value):Infer(cell);
        if(!Valid(cell,attachment) || world.Weather.ExposedRain(new(cell.X+.5f,cell.Y+.5f,cell.Z+.5f)) || !world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Fire))return false;
        _flames[cell].Attachment=attachment;return true;
    }
    private static FireAttachment Encode(Point3 support)=>support.Y<0?FireAttachment.Floor:(FireAttachment)(2+(support.X>0?1:support.X<0?3:support.Z>0?2:0));
    public static Point3 Direction(FireAttachment attachment)=>attachment==FireAttachment.Floor?new(0,-1,0):World.FacingDirection((int)attachment-2);
    private bool Valid(Point3 cell,FireAttachment attachment)
    {
        if(attachment==FireAttachment.Automatic)return false;Point3 d=Direction(attachment),p=cell+d;
        return attachment==FireAttachment.Floor?world.SupportsVoxelTop(p):Blocks.IsSolid(world.GetBlock(p.X,p.Y,p.Z))||Flammable(world.GetBlock(p.X,p.Y,p.Z));
    }
    private FireAttachment Infer(Point3 cell)
    {
        // Pre-attachment saves used burning side faces in preference to a
        // nonflammable floor. Preserve that appearance when inferring old data.
        for(int i=0;i<4;i++){var a=(FireAttachment)(2+i);Point3 p=cell+Direction(a);if(Flammable(world.GetBlock(p.X,p.Y,p.Z)) && Valid(cell,a))return a;}
        if(Valid(cell,FireAttachment.Floor))return FireAttachment.Floor;
        for(int i=0;i<4;i++){var a=(FireAttachment)(2+i);if(Valid(cell,a))return a;}return FireAttachment.Automatic;
    }
    public FireAttachment Attachment(Point3 cell)=>_flames.TryGetValue(cell,out var flame) && Valid(cell,flame.Attachment)?flame.Attachment:Infer(cell);
    internal int SelectionBoxes(Point3 cell,Span<BoundingBox> boxes)
    {
        FireAttachment attachment=Attachment(cell);if(attachment==FireAttachment.Automatic)return 0;
        Vector3 origin=new(cell.X,cell.Y,cell.Z);
        if(attachment==FireAttachment.Floor){boxes[0]=World.Bounds(cell);return 1;}
        Point3 d=Direction(attachment);Vector3 min=new(.02f,.02f,.02f),max=new(.98f,.92f,.98f);
        if(d.X!=0){min.X=d.X>0?.665f:.015f;max.X=d.X>0?.985f:.335f;}
        else {min.Z=d.Z>0?.665f:.015f;max.Z=d.Z>0?.985f:.335f;}
        boxes[0]=new(origin+min,origin+max);return 1;
    }
    internal void Changed(Point3 cell,BlockType before,BlockType after)
    {
        if(before==BlockType.Fire)_flames.Remove(cell);
        if(after==BlockType.Fire && !_flames.ContainsKey(cell))
        {
            var f=new Flame{Expires=_time+9+_random.NextDouble()*9,Due=_time+1,Attachment=Infer(cell)};_flames.Add(cell,f);_ticks.Enqueue((cell,f.Version),f.Due);
        }
        if(Lava.IsLava(after) || Flammable(after))
        {
            if(_lavaQueued.Count<Maximum && _lavaQueued.Add(cell))_lava.Enqueue(cell);
        }
        if(Water.IsWater(after))foreach(var d in Adjacent)
        {Point3 p=cell+d;if(world.GetBlock(p.X,p.Y,p.Z)==BlockType.Fire)world.SetBlock(p.X,p.Y,p.Z,BlockType.Air);}
    }
    public bool Contact(BoundingBox bounds)
    {
        if(Count==0)return false;
        Span<BoundingBox> shapes=stackalloc BoundingBox[1];
        for(int y=Math.Max(0,(int)MathF.Floor(bounds.Min.Y));y<=Math.Min(World.Height-1,(int)bounds.Max.Y);y++)
        for(int z=Math.Max(0,(int)MathF.Floor(bounds.Min.Z));z<=Math.Min(world.Depth-1,(int)bounds.Max.Z);z++)
        for(int x=Math.Max(0,(int)MathF.Floor(bounds.Min.X));x<=Math.Min(world.Width-1,(int)bounds.Max.X);x++)
            if(world.GetBlock(x,y,z)==BlockType.Fire && SelectionBoxes(new(x,y,z),shapes)>0 && World.Overlaps(bounds,shapes[0]))return true;
        return false;
    }
    public void Update(double elapsed)
    {
        LastProcessed=0;if(!double.IsFinite(elapsed)||elapsed<=0)return;_time+=elapsed;
        for(int i=0;i<Math.Min(8,_lava.Count);i++)
        {
            Point3 p=_lava.Dequeue();_lavaQueued.Remove(p);BlockType b=world.GetBlock(p.X,p.Y,p.Z);
            foreach(Point3 d in Adjacent)
            {
                Point3 other=p+d;BlockType adjacent=world.GetBlock(other.X,other.Y,other.Z);
                if(!(Lava.IsLava(b)&&Flammable(adjacent)||Flammable(b)&&Lava.IsLava(adjacent)))continue;
                Point3 fuel=Flammable(b)?p:other;
                foreach(Point3 air in Adjacent)if(Ignite(fuel+air,new(-air.X,-air.Y,-air.Z)))break;
            }
        }
        while(LastProcessed<Budget && _ticks.TryPeek(out var next,out var due) && due<=_time)
        {
            _ticks.Dequeue();LastProcessed++;
            if(!_flames.TryGetValue(next.Cell,out var flame)||flame.Version!=next.Version)continue;
            Point3 cell=next.Cell;bool wet=world.Weather.ExposedRain(new(cell.X+.5f,cell.Y+.5f,cell.Z+.5f));Point3? fuel=null;
            flame.Attachment=Attachment(cell);
            foreach(Point3 d in Adjacent)
            {Point3 p=cell+d;BlockType b=world.GetBlock(p.X,p.Y,p.Z);wet|=Water.IsWater(b);if(Flammable(b))fuel=p;}
            if(wet || flame.Attachment==FireAttachment.Automatic){world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Air);continue;}
            if(flame.Expires<=_time)
            {
                if(fuel is Point3 f)world.SetBlock(f.X,f.Y,f.Z,BlockType.Air);
                world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Air);continue;
            }
            if(_random.NextDouble()<SpreadChancePerTick)
            {
                // Choose real adjacent fuel instead of wasting most spread rolls
                // on Air. At most one ignition per flame per second, still bounded.
                bool spread=false;int start=_random.Next(6),airStart=_random.Next(6);
                for(int i=0;i<6 && !spread;i++)
                {
                    Point3 p=cell+Adjacent[(start+i)%6];if(!Flammable(world.GetBlock(p.X,p.Y,p.Z)))continue;
                    for(int j=0;j<6;j++){Point3 air=Adjacent[(airStart+j)%6];if(Ignite(p+air,new(-air.X,-air.Y,-air.Z))){spread=true;break;}}
                }
            }
            flame.Version++;flame.Due=_time+1;_ticks.Enqueue((cell,flame.Version),flame.Due);
        }
    }
    public FireState[] Capture()
    {var result=new FireState[Count];int i=0;foreach(var p in _flames)result[i++]=new(p.Key,Math.Max(0,p.Value.Expires-_time),Math.Max(0,p.Value.Due-_time),Attachment(p.Key));return result;}
    public void Restore(FireState[] states)
    {
        if(states.Length>Maximum)throw new InvalidDataException("Too many fires.");
        foreach(var s in states)
        {
            if(world.GetBlock(s.Cell.X,s.Cell.Y,s.Cell.Z)!=BlockType.Fire||!double.IsFinite(s.Remaining)||s.Remaining<0||s.Remaining>18||!double.IsFinite(s.NextTick)||s.NextTick<0||s.NextTick>1||s.Attachment>FireAttachment.West||_flames.ContainsKey(s.Cell))throw new InvalidDataException("Invalid fire.");
            var f=new Flame{Expires=_time+s.Remaining,Due=_time+s.NextTick,Attachment=s.Attachment==FireAttachment.Automatic?Infer(s.Cell):s.Attachment};_flames.Add(s.Cell,f);_ticks.Enqueue((s.Cell,0),f.Due);
        }
    }
}
