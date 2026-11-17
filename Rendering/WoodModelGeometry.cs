using System;
using Microsoft.Xna.Framework;
namespace MDSM.Rendering;
internal sealed partial class ChunkMeshBuilder
{
    // Emit only the boundary of a small union of touching wooden components.
    // Rails terminate at posts; joint caps and covered post patches are omitted.
    private void AppendWoodModel(ReadOnlySpan<BoundingBox> boxes,Vector3 origin)
    {
        Span<Vector4> a=stackalloc Vector4[128],b=stackalloc Vector4[128];
        for(int i=0;i<boxes.Length;i++)foreach(var face in Faces)
        {
            int axis=face.Normal.X!=0?0:face.Normal.Y!=0?1:2;bool positive=face.Normal.X+face.Normal.Y+face.Normal.Z>0;
            float At(Vector3 p,int dim)=>dim==0?p.X:dim==1?p.Y:p.Z;
            int u=axis==0?2:0,v=axis==1?2:1;
            float plane=At(positive?boxes[i].Max:boxes[i].Min,axis);
            if(_world!=null&&axis!=1&&MathF.Abs(plane-(At(origin,axis)+(positive?1:0)))<.00001f)
            {
                var cell=new MDSM.Worlds.Point3((long)origin.X+(_singleBlock.HasValue?_world.OriginX:0), (int)origin.Y,(long)origin.Z+(_singleBlock.HasValue?_world.OriginZ:0));
                var neighbor=_world.GetBlock(cell.X+face.Offset.X,cell.Y,cell.Z+face.Offset.Z);
                if(neighbor==MDSM.Worlds.BlockType.Fence||MDSM.Worlds.Blocks.IsOpaque(neighbor))continue;
            }
            a[0]=new(At(boxes[i].Min,u),At(boxes[i].Min,v),At(boxes[i].Max,u),At(boxes[i].Max,v));int count=1;
            for(int j=0;j<boxes.Length&&count>0;j++)
            {
                if(i==j)continue;
                float lo=At(boxes[j].Min,axis),hi=At(boxes[j].Max,axis);
                bool covered=positive?lo<=plane+.00001f&&hi>plane+.00001f:lo<plane-.00001f&&hi>=plane-.00001f;
                bool duplicate=j<i&&MathF.Abs((positive?hi:lo)-plane)<.00001f;
                if(!covered&&!duplicate)continue;
                Vector4 cut=new(At(boxes[j].Min,u),At(boxes[j].Min,v),At(boxes[j].Max,u),At(boxes[j].Max,v));int next=0;
                for(int n=0;n<count;n++)
                {
                    Vector4 r=a[n];float left=MathF.Max(r.X,cut.X),bottom=MathF.Max(r.Y,cut.Y),right=MathF.Min(r.Z,cut.Z),top=MathF.Min(r.W,cut.W);
                    if(right<=left||top<=bottom){b[next++]=r;continue;}
                    if(r.X<left)b[next++]=new(r.X,r.Y,left,r.W);
                    if(right<r.Z)b[next++]=new(right,r.Y,r.Z,r.W);
                    if(r.Y<bottom)b[next++]=new(left,r.Y,right,bottom);
                    if(top<r.W)b[next++]=new(left,top,right,r.W);
                }
                b[..next].CopyTo(a);count=next;
            }
            for(int n=0;n<count;n++)
            {
                var r=a[n];Vector3 min=axis==0?new(plane,r.Y,r.X):axis==1?new(r.X,plane,r.Y):new(r.X,r.Y,plane);
                Vector3 max=axis==0?new(plane,r.W,r.Z):axis==1?new(r.Z,plane,r.W):new(r.Z,r.W,plane);
                AppendWoodFace(min,max,origin,face);
            }
        }
    }
}
