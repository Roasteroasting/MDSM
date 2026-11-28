using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Inventories;

namespace MDSM.Rendering;

internal sealed partial class ChunkMeshBuilder
{
    private static bool GlassMaterial(BlockType b)=>Blocks.GlassCube(b)||GlassProcessing.IsGlass(b)&&!GlassProcessing.IsVase(b);
    private int GlassMask(World world,Point3 cell,BlockType b)
    {
        if(Blocks.Get(b).Shape==BlockShape.Cube)return 255;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];int count=world.BlockBoxes(cell,boxes);int mask=0;
        Vector3 origin=world.LocalPosition(cell);
        for(int y=0;y<2;y++)for(int z=0;z<2;z++)for(int x=0;x<2;x++)
        {var p=origin+new Vector3(x*.5f+.25f,y*.5f+.25f,z*.5f+.25f);for(int i=0;i<count;i++)if(boxes[i].Contains(p)!=ContainmentType.Disjoint){mask|=1<<(x+2*z+4*y);break;}}
        return mask;
    }
    private void AppendGlass(World world,Point3 cell,BlockType block)
        =>AppendGlassMask(GlassMask(world,cell,block),GeometryOrigin(cell),block,1,world,cell);
    private void AppendGlassMask(int mask,Vector3 origin,BlockType block,float scale,World world=null,Point3 cell=default)
    {
        int tile=Blocks.Get(block).SideTile;
        Span<int> planes=stackalloc int[3];
        foreach(var face in Faces)
        {
            planes.Clear();int axis=face.Offset.X!=0?0:face.Offset.Y!=0?1:2;
            bool positive=face.Offset.X+face.Offset.Y+face.Offset.Z>0;
            var neighbor=world?.GetBlock(cell.X+face.Offset.X,cell.Y+face.Offset.Y,cell.Z+face.Offset.Z)??BlockType.Air;
            bool opaque=Blocks.IsOpaque(neighbor);
            bool compatible=GlassMaterial(neighbor)&&GlassProcessing.ColorIndex(block)==GlassProcessing.ColorIndex(neighbor);
            bool partialSolid=!GlassMaterial(neighbor)&&Blocks.IsSolid(neighbor)&&Blocks.Get(neighbor).Shape is BlockShape.Slab or BlockShape.Stairs;
            int other=opaque?255:compatible||partialSolid?GlassMask(world,cell+face.Offset,neighbor):0;
            if(mask==255&&other==255)continue;
            for(int y=0;y<2;y++)for(int z=0;z<2;z++)for(int x=0;x<2;x++)
            {
                if((mask&(1<<(x+2*z+4*y)))==0)continue;
                int nx=x+(int)face.Offset.X,ny=y+face.Offset.Y,nz=z+(int)face.Offset.Z;
                bool inside=nx>=0&&nx<2&&ny>=0&&ny<2&&nz>=0&&nz<2;
                if(inside?(mask&(1<<(nx+2*nz+4*ny)))!=0:(other&(1<<(((nx+2)%2)+2*((nz+2)%2)+4*((ny+2)%2))))!=0)continue;
                int plane=(axis==0?x:axis==1?y:z)+(positive?1:0),u=axis==0?z:x,v=axis==1?z:y;
                planes[plane]|=1<<(u+2*v);
            }
            for(int plane=0;plane<3;plane++)for(int v=0;v<2;v++)for(int u=0;u<2;u++)
            {
                if((planes[plane]&(1<<(u+2*v)))==0)continue;
                int width=u==0&&(planes[plane]&(1<<(1+2*v)))!=0?2:1,height=1;
                int row=((1<<width)-1)<<u;
                if(v==0&&(planes[plane]&(row<<2))==(row<<2))height=2;
                for(int j=0;j<height;j++)planes[plane]&=~(row<<(2*(v+j)));
                Vector3 min=axis==0?new(plane*.5f,v*.5f,u*.5f):axis==1?new(u*.5f,plane*.5f,v*.5f):new(u*.5f,v*.5f,plane*.5f);
                Vector3 max=axis==0?new(plane*.5f,(v+height)*.5f,(u+width)*.5f):axis==1?new((u+width)*.5f,plane*.5f,(v+height)*.5f):new((u+width)*.5f,(v+height)*.5f,plane*.5f);
                float left=face.Normal.X>0?1-max.Z:face.Normal.X<0?min.Z:face.Normal.Z<0?1-max.X:min.X;
                float top=face.Normal.Y==0?1-max.Y:face.Normal.Y>0?min.Z:1-max.Z;
                Vector3 extent=max-min;
                var region=new Rectangle((int)MathF.Round(left*16),(int)MathF.Round(top*16),width*8,height*8);
                Color shade=new((world==null||_singleBlock.HasValue?DyePalette.Tint(block).ToVector3():Vector3.One)*face.Shade);
                AddQuad(origin+(min+face.A*extent)*scale,origin+(min+face.B*extent)*scale,origin+(min+face.C*extent)*scale,origin+(min+face.D*extent)*scale,tile,shade,false,region);
            }
        }
    }
    private void AppendVase(BlockType block,Vector3 origin,float scale=1,Point3? cell=null)
    {
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];int count=GlassProcessing.VaseBoxes(Vector3.Zero,boxes);
        foreach(var face in Faces)for(int i=0;i<count;i++)
        {
            var min=boxes[i].Min;var extent=boxes[i].Max-min;
            Color shade=new((_world==null||_singleBlock.HasValue?DyePalette.Tint(block).ToVector3():Vector3.One)*face.Shade);
            AddQuad(origin+(min+face.A*extent)*scale,origin+(min+face.B*extent)*scale,origin+(min+face.C*extent)*scale,origin+(min+face.D*extent)*scale,GlassProcessing.VaseTile,shade,false);
        }
        if(_world==null||cell==null||!_world.Containers.Entries.TryGetValue(cell.Value,out var vase)||vase[0].IsEmpty||!Items.TryGetBlock(vase[0].Item,out var flower))return;
        int tile=Blocks.Get(flower).SideTile;
        AddQuad(origin+new Vector3(.25f,.35f,.25f),origin+new Vector3(.75f,.35f,.75f),origin+new Vector3(.75f,.98f,.75f),origin+new Vector3(.25f,.98f,.25f),tile,Color.White,true);
        AddQuad(origin+new Vector3(.75f,.35f,.25f),origin+new Vector3(.25f,.35f,.75f),origin+new Vector3(.25f,.98f,.75f),origin+new Vector3(.75f,.98f,.25f),tile,Color.White,true);
    }
}
