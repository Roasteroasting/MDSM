using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

internal static class ElectricalGeometry
{
    internal static int Connections(World world,Point3 cell)
    {
        int mask=0;
        for(int i=0;i<ElectricalNetwork.Directions.Length;i++)
        {
            var d=ElectricalNetwork.Directions[i];var n=cell+d;
            if(world.MeshSampleKnown(n)&&world.Electricity.Connects(n,new(-d.X,-d.Y,-d.Z)))mask|=1<<i;
        }
        return mask;
    }
    internal static int Boxes(World world,Point3 cell,BlockType block,Span<BoundingBox> boxes)
        =>Boxes(world.LocalPosition(cell),block,Connections(world,cell),boxes,RaisedConnections(world,cell));
    internal static int RaisedConnections(World world,Point3 cell)
    {
        int mask=0;
        for(int i=0;i<6;i++)
        {
            var d=ElectricalNetwork.Directions[i];if(d.Y!=0)continue;
            var n=cell+d;
            if(world.MeshSampleKnown(n)&&world.Electricity.Connects(n,new(-d.X,0,-d.Z))
                &&Blocks.Get(world.GetBlock(n.X,n.Y,n.Z)).Shape==BlockShape.Cube)mask|=1<<i;
        }
        return mask;
    }
    internal static int Boxes(Vector3 origin,BlockType block,int mask,Span<BoundingBox> boxes,int raised=0)
    {
        if(block==BlockType.SolarPanel)
        {
            boxes[0]=new(origin,origin+new Vector3(1,.1875f,1));
            // Bridge the gap between the low plate and a top-connected cable.
            if((mask&8)==0)return 1;
            boxes[1]=new(origin+new Vector3(.4375f,.1875f,.4375f),origin+new Vector3(.5625f,1,.5625f));return 2;
        }
        const float lo=.4375f,hi=.5625f;
        const float thickness=1f/8;
        // Horizontal runs rest at floor level; an up connection extends the trunk.
        boxes[0]=new(origin+new Vector3(lo,0,lo),origin+new Vector3(hi,(mask&8)!=0?1:thickness,hi));int count=1;
        if((mask&1)!=0)boxes[count++]=new(origin+new Vector3(0,0,lo),origin+new Vector3(lo,thickness,hi));
        if((mask&2)!=0)boxes[count++]=new(origin+new Vector3(hi,0,lo),origin+new Vector3(1,thickness,hi));
        if((mask&16)!=0)boxes[count++]=new(origin+new Vector3(lo,0,0),origin+new Vector3(hi,thickness,lo));
        if((mask&32)!=0)boxes[count++]=new(origin+new Vector3(lo,0,hi),origin+new Vector3(hi,thickness,1));
        // Rise against full-block terminals, while solar connections remain low.
        // All four terminal risers still fit the shared nine-box shape budget.
        if((mask&raised&1)!=0)boxes[count++]=new(origin+new Vector3(0,thickness,lo),origin+new Vector3(thickness,hi,hi));
        if((mask&raised&2)!=0)boxes[count++]=new(origin+new Vector3(1-thickness,thickness,lo),origin+new Vector3(1,hi,hi));
        if((mask&raised&16)!=0)boxes[count++]=new(origin+new Vector3(lo,thickness,0),origin+new Vector3(hi,hi,thickness));
        if((mask&raised&32)!=0)boxes[count++]=new(origin+new Vector3(lo,thickness,1-thickness),origin+new Vector3(hi,hi,1));
        return count;
    }
}
