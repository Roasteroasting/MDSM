using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed partial class World
{
    public Dictionary<Point3,string> Signs {get;}=new();
    public static bool OrientedBuilding(BlockType b)=>Blocks.Get(b).Shape is BlockShape.Slab or BlockShape.Stairs or BlockShape.Sign;
    // Grid-mounted blocks/falling cubes need a surface at the top of their
    // supporting voxel. A bottom slab is solid, but is not that surface.
    internal bool SupportsVoxelTop(Point3 cell)
    {
        BlockType block=GetBlock(cell.X,cell.Y,cell.Z);if(!Blocks.IsSolid(block))return false;
        if(Blocks.Get(block).Shape is not (BlockShape.Slab or BlockShape.Stairs))return true;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];int count=BlockBoxes(cell,boxes,true);
        for(int i=0;i<count;i++)if(boxes[i].Max.Y>=cell.Y+1-.001f && boxes[i].Min.X<=cell.X && boxes[i].Max.X>=cell.X+1 && boxes[i].Min.Z<=cell.Z && boxes[i].Max.Z>=cell.Z+1)return true;
        return false;
    }
    public static Point3 FacingDirection(int facing)=>(facing&3) switch{0=>new(0,0,-1),1=>new(1,0,0),2=>new(0,0,1),_=>new(-1,0,0)};
    public bool PlaceBuilding(Point3 cell,BlockType block,byte facing,bool upper,BoundingBox player,Point3 support)
    {
        if(!OrientedBuilding(block)||Structures.Count>=65536)return false;
        // Joining uses a full-size double-slab state so mining can return both
        // original slabs rather than losing one or converting them into planks.
        if(Blocks.Get(block).Shape==BlockShape.Slab && GetBlock(cell.X,cell.Y,cell.Z)==block
            && ((Structure(cell).Facing&4)!=0)!=upper)
        {
            BlockType full=block==BlockType.WoodenSlab?BlockType.DoubleWoodenSlab:BlockType.DoubleCobblestoneSlab;
            if(PlacementOverlaps(cell,full,player)||PlacementBlockedByMobs(cell,full,null))return false;
            return SetBlock(cell.X,cell.Y,cell.Z,full);
        }
        var state=new StructureState(cell,block==BlockType.Sign?(byte)(facing&11):(byte)((facing&3)|(upper?4:0)),false,support);
        if(block==BlockType.Sign && (support.Y>0 || !IsSolid(cell.X+support.X,cell.Y+support.Y,cell.Z+support.Z)))return false;
        if(block==BlockType.Sign && (support.Y<0?!SupportsVoxelTop(cell+support):Blocks.Get(GetBlock(cell.X+support.X,cell.Y,cell.Z+support.Z)).Shape is BlockShape.Slab or BlockShape.Stairs))return false;
        if(!TryPlaceBlock(cell,block,player,support,state))return false;
        Structures[cell]=state;if(block==BlockType.Sign)Signs[cell]="";DirtyDetail(cell);return true;
    }
    public bool EditSign(Point3 cell,string text)
    {
        if(GetBlock(cell.X,cell.Y,cell.Z)!=BlockType.Sign || !ValidSignText(text))return false;
        if(!Signs.TryGetValue(cell,out var prior)||prior!=text){Signs[cell]=text;MarkDirty(cell.X/16,cell.Z/16);Revision++;}return true;
    }
    public static bool ValidSignText(string text)
    {
        if(text==null || text.Length>99)return false;
        int lines=1;foreach(char c in text){if(c=='\n'){if(++lines>4)return false;}else if(c<' '||c>'~')return false;}
        foreach(string line in text.Split('\n'))if(line.Length>24)return false;
        return true;
    }
    private int BuildingBoxes(Point3 cell,BlockType block,Span<BoundingBox> boxes,StructureState? proposed,Point3? changedNeighbor=null,BlockType replacement=BlockType.Air,StructureState? neighborState=null)
    {
        Vector3 o=new(cell.X,cell.Y,cell.Z);var state=proposed??Structure(cell);bool upper=(state.Facing&4)!=0;
        if(block==BlockType.Sign)
        {
            Point3 support=state.Support;bool wall=support.Y==0;
            Vector3 min=new(-.5f,wall?.25f:.5f,-.0625f),max=new(.5f,wall?.75f:1,.0625f);
            Matrix transform=SignTransform(cell,state);
            boxes[0]=TransformBounds(min,max,transform);
            if(wall)return 1;
            boxes[1]=TransformBounds(new(-.0625f,0,-.0625f),new(.0625f,.5f,.0625f),transform);return 2;
        }
        boxes[0]=new(o+new Vector3(0,upper?.5f:0,0),o+new Vector3(1,upper?1:.5f,1));
        if(Blocks.Get(block).Shape==BlockShape.Slab)return 1;
        int facing=state.Facing&3,count=1,mask=0;
        for(int z=0;z<2;z++)for(int x=0;x<2;x++)if(High(facing,x,z))mask|=1<<(z*2+x);
        Point3 front=cell+FacingDirection(facing),back=cell+FacingDirection(facing+2);
        if(StairNeighbor(front,upper,out int turn) && (turn&1)!=(facing&1) && NoMatchingSide(turn+2))mask&=HalfMask(turn);
        else if(StairNeighbor(back,upper,out turn) && (turn&1)!=(facing&1) && NoMatchingSide(turn))mask|=HalfMask(turn);
        for(int z=0;z<2;z++)for(int x=0;x<2;x++)if((mask&(1<<(z*2+x)))!=0)
            boxes[count++]=new(o+new Vector3(x*.5f,upper?0:.5f,z*.5f),o+new Vector3((x+1)*.5f,upper?.5f:1,(z+1)*.5f));
        return count;
        bool StairNeighbor(Point3 p,bool top,out int dir)
        {var s=changedNeighbor==p && neighborState.HasValue?neighborState.Value:Structure(p);dir=s.Facing&3;return Blocks.Get(PlacementNeighbor(p,changedNeighbor,replacement)).Shape==BlockShape.Stairs && ((s.Facing&4)!=0)==top;}
        bool NoMatchingSide(int direction)=>!StairNeighbor(cell+FacingDirection(direction),upper,out int neighborFacing)||neighborFacing!=facing;
        static bool High(int dir,int x,int z)=>(dir&3) switch{0=>z==0,1=>x==1,2=>z==1,_=>x==0};
        static int HalfMask(int dir){int value=0;for(int z=0;z<2;z++)for(int x=0;x<2;x++)if(High(dir,x,z))value|=1<<(z*2+x);return value;}
    }
    public static Matrix SignTransform(Point3 cell,StructureState state)
    {
        int facing=state.Facing&3;Vector3 center=new(cell.X+.5f,cell.Y,cell.Z+.5f);
        if(state.Support.Y==0)center+=new Vector3(state.Support.X,0,state.Support.Z)*.43f;
        // Bit 3 adds a half-cardinal turn. Old signs' unused bit 2 remains ignored.
        float angle=facing*MathHelper.PiOver2+(state.Support.Y!=0 && (state.Facing&8)!=0?MathHelper.PiOver4:0);
        return Matrix.CreateRotationY(-angle)*Matrix.CreateTranslation(center);
    }
    private static BoundingBox TransformBounds(Vector3 min,Vector3 max,Matrix transform)
    {
        Vector3 low=new(float.PositiveInfinity),high=new(float.NegativeInfinity);
        for(int i=0;i<8;i++)
        {Vector3 p=Vector3.Transform(new((i&1)==0?min.X:max.X,(i&2)==0?min.Y:max.Y,(i&4)==0?min.Z:max.Z),transform);low=Vector3.Min(low,p);high=Vector3.Max(high,p);}
        return new(low,high);
    }
}
