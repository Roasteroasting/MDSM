using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed partial class World
{
    // Bits and box order are west, east, north, south. Only local neighbor queries;
    // SetBlock already invalidates both sides of every horizontal chunk boundary.
    public int PaneConnections(Point3 cell)
    {
        int mask=0;
        if(Blocks.ConnectsPane(GetBlock(cell.X-1,cell.Y,cell.Z)))mask|=1;
        if(Blocks.ConnectsPane(GetBlock(cell.X+1,cell.Y,cell.Z)))mask|=2;
        if(Blocks.ConnectsPane(GetBlock(cell.X,cell.Y,cell.Z-1)))mask|=4;
        if(Blocks.ConnectsPane(GetBlock(cell.X,cell.Y,cell.Z+1)))mask|=8;
        return mask;
    }
    internal int PaneBoxes(Point3 cell,Span<BoundingBox> boxes)=>PaneBoxes(cell,PaneConnections(cell),boxes);
    internal static int PaneBoxes(Point3 cell,int connections,Span<BoundingBox> boxes)
    {
        const float lo=7f/16,hi=9f/16;Vector3 at=new(cell.X,cell.Y,cell.Z);int count=1;
        boxes[0]=new(at+new Vector3(lo,0,lo),at+new Vector3(hi,1,hi));
        if((connections&1)!=0)boxes[count++]=new(at+new Vector3(0,0,lo),at+new Vector3(lo,1,hi));
        if((connections&2)!=0)boxes[count++]=new(at+new Vector3(hi,0,lo),at+new Vector3(1,1,hi));
        if((connections&4)!=0)boxes[count++]=new(at+new Vector3(lo,0,0),at+new Vector3(hi,1,lo));
        if((connections&8)!=0)boxes[count++]=new(at+new Vector3(lo,0,hi),at+new Vector3(hi,1,1));
        return count;
    }
    private bool PlacementOverlaps(Point3 cell,BlockType block,BoundingBox player)
    {
        if(block!=BlockType.GlassPane)return Overlaps(Bounds(cell),player);
        Span<BoundingBox> boxes=stackalloc BoundingBox[5];int count=PaneBoxes(cell,boxes);
        for(int i=0;i<count;i++)if(Overlaps(boxes[i],player))return true;
        return false;
    }
}
