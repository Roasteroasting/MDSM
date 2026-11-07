using System;
using Microsoft.Xna.Framework;
using MDSM.Audio;

namespace MDSM.Worlds;

public sealed partial class World
{
    public static Point3 BedDirection(byte facing)=>(facing&3) switch {0=>new(0,0,-1),1=>new(1,0,0),2=>new(0,0,1),_=>new(-1,0,0)};
    internal Point3 BedPartner(Point3 cell,BlockType block)
    {Point3 d=BedDirection(Structure(cell).Facing);return cell+(block==BlockType.Bed?d:new(-d.X,0,-d.Z));}
    private bool _bedEdit;
    public bool PlaceBed(Point3 foot,byte facing,BoundingBox player)
    {
        Point3 direction=BedDirection(facing),head=foot+direction;
        if(Structures.Count>=65535)return false;
        foreach(Point3 p in new[]{foot,head})
        {
            if(!Contains(p.X,p.Y,p.Z) || GetBlock(p.X,p.Y,p.Z)!=BlockType.Air || !IsSolid(p.X,p.Y-1,p.Z)
                || Overlaps(new(new(p.X,p.Y,p.Z),new(p.X+1,p.Y+9f/16,p.Z+1)),player)
                || PlacementBlockedByMobs(p,BlockType.Bed) || Gravity.Intersects(Bounds(p)))return false;
        }
        _bedEdit=true;
        try
        {
            if(!SetBlock(foot.X,foot.Y,foot.Z,BlockType.Bed))return false;
            SetBlock(head.X,head.Y,head.Z,BlockType.BedHead);
            Structures[foot]=new(foot,(byte)(facing&3),false,new(0,-1,0));
            Structures[head]=new(head,(byte)(facing&3),false,new(0,-1,0));
        }
        finally{_bedEdit=false;}
        DirtyDetail(foot);DirtyDetail(head);Sounds?.Block(SoundKind.Place,BlockType.Bed,BlockSounds.Center(foot));return true;
    }
    private void BedChanged(Point3 cell,BlockType previous,BlockType next)
    {
        if(!_bedEdit && Blocks.Bed(previous))
        {
            Point3 partner=BedPartner(cell,previous);_bedEdit=true;
            try{if(Blocks.Bed(GetBlock(partner.X,partner.Y,partner.Z)))SetBlock(partner.X,partner.Y,partner.Z,BlockType.Air);}
            finally{_bedEdit=false;}
        }
        if(Blocks.Bed(next))Structures.TryAdd(cell,new(cell,0,false,new(0,-1,0)));
        if(Blocks.Bed(previous) && !Blocks.Bed(next))Structures.Remove(cell);
        if(!Blocks.IsSolid(next) && Blocks.Bed(GetBlock(cell.X,cell.Y+1,cell.Z)))SetBlock(cell.X,cell.Y+1,cell.Z,BlockType.Air);
    }
    internal bool UseBed(Point3 cell)=>Blocks.Bed(GetBlock(cell.X,cell.Y,cell.Z)) && Time.Sleep();
}
