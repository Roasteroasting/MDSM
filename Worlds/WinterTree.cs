using System;

namespace MDSM.Worlds;

internal static class WinterTree
{
    internal const int Radius=3;

    internal static bool TryPlace(Point3 root,ulong shape,Func<long,int,long,BlockType> read,Action<long,int,long,BlockType> write)
    {
        int top=root.Y+6+(int)(shape%3);
        if (top+1>=World.Height || read(root.X,root.Y,root.Z)!=BlockType.Grass) return false;
        Span<Point3> wood=stackalloc Point3[24];
        Span<Point3> leaves=stackalloc Point3[4];
        int logs=0,foliage=0;
        for(int y=root.Y+1;y<=top;y++)wood[logs++]=new(root.X,y,root.Z);
        int branches=2+(int)(shape>>6&1),rotation=(int)(shape>>8&3);
        for(int branch=0;branch<branches;branch++)
        {
            var direction=World.FacingDirection((rotation+branch)%4);
            int y=top-2+(branch&1);
            int length=2+(int)(shape>>(12+branch)&1);
            for(int i=1;i<=length;i++)wood[logs++]=new(root.X+direction.X*i,y,root.Z+direction.Z*i);
            var tip=new Point3(root.X+direction.X*length,y+1,root.Z+direction.Z*length);
            wood[logs++]=tip;
            if(((shape>>(21+branch))&3)==0)leaves[foliage++]=tip+new Point3(0,1,0);
        }
        if((shape>>32&3)==0)leaves[foliage++]=new(root.X,top+1,root.Z);
        foreach(var p in wood[..logs])if(read(p.X,p.Y,p.Z)!=BlockType.Air)return false;
        foreach(var p in wood[..logs])write(p.X,p.Y,p.Z,BlockType.WoodLog);
        foreach(var p in leaves[..foliage])if(read(p.X,p.Y,p.Z)==BlockType.Air)write(p.X,p.Y,p.Z,BlockType.Leaves);
        return true;
    }
}
