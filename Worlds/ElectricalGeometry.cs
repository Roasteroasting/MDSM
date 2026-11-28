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
        =>Boxes(world.LocalPosition(cell),block,block==BlockType.CopperWire?Connections(world,cell):0,boxes);
    internal static int Boxes(Vector3 origin,BlockType block,int mask,Span<BoundingBox> boxes)
    {
        if(block==BlockType.SolarPanel)
        {boxes[0]=new(origin,origin+new Vector3(1,.1875f,1));return 1;}
        const float lo=.4375f,hi=.5625f;
        boxes[0]=new(origin+new Vector3(lo),origin+new Vector3(hi));int count=1;
        if((mask&1)!=0)boxes[count++]=new(origin+new Vector3(0,lo,lo),origin+new Vector3(lo,hi,hi));
        if((mask&2)!=0)boxes[count++]=new(origin+new Vector3(hi,lo,lo),origin+new Vector3(1,hi,hi));
        if((mask&4)!=0)boxes[count++]=new(origin+new Vector3(lo,0,lo),origin+new Vector3(hi,lo,hi));
        if((mask&8)!=0)boxes[count++]=new(origin+new Vector3(lo,hi,lo),origin+new Vector3(hi,1,hi));
        if((mask&16)!=0)boxes[count++]=new(origin+new Vector3(lo,lo,0),origin+new Vector3(hi,hi,lo));
        if((mask&32)!=0)boxes[count++]=new(origin+new Vector3(lo,lo,hi),origin+new Vector3(hi,hi,1));
        return count;
    }
}
