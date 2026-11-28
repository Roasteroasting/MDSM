using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed partial class ChunkMeshBuilder
{
    private void AppendElectrical(BlockType block,Vector3 origin,int connections,float scale=1,int raised=0)
    {
        Span<BoundingBox> parts=stackalloc BoundingBox[9];
        int count=ElectricalGeometry.Boxes(Vector3.Zero,block,connections,parts,raised);
        Span<Vector4> patches=stackalloc Vector4[64],scratch=stackalloc Vector4[64];
        for(int i=0;i<count;i++)foreach(var face in Faces)
        {
            var box=parts[i];
            int axis=face.Normal.X!=0?0:face.Normal.Y!=0?1:2;
            int u=axis==0?2:0,v=axis==1?2:1;
            float At(Vector3 p,int dim)=>dim==0?p.X:dim==1?p.Y:p.Z;
            bool positive=face.Normal.X+face.Normal.Y+face.Normal.Z>0;
            float plane=At(positive?box.Max:box.Min,axis);
            int bit=face.Offset.X<0?1:face.Offset.X>0?2:face.Offset.Y<0?4:face.Offset.Y>0?8:face.Offset.Z<0?16:32;
            if(_world!=null&&(block==BlockType.CopperWire||i>0)&&(connections&bit)!=0
                &&MathF.Abs(plane-(positive?1:0))<.00001f)continue;
            patches[0]=new(At(box.Min,u),At(box.Min,v),At(box.Max,u),At(box.Max,v));int n=1;
            // Clip joint faces instead of overlapping caps or leaving holes at
            // cable trunks/terminal risers. Uses the existing wood-model convention.
            for(int j=0;j<count&&n>0;j++)
            {
                if(i==j)continue;
                float lo=At(parts[j].Min,axis),hi=At(parts[j].Max,axis);
                bool covered=positive?lo<=plane+.00001f&&hi>plane+.00001f:lo<plane-.00001f&&hi>=plane-.00001f;
                if(!covered)continue;
                var cut=new Vector4(At(parts[j].Min,u),At(parts[j].Min,v),At(parts[j].Max,u),At(parts[j].Max,v));int next=0;
                for(int k=0;k<n;k++)
                {
                    var r=patches[k];float left=MathF.Max(r.X,cut.X),bottom=MathF.Max(r.Y,cut.Y),right=MathF.Min(r.Z,cut.Z),top=MathF.Min(r.W,cut.W);
                    if(right<=left||top<=bottom){scratch[next++]=r;continue;}
                    if(r.X<left)scratch[next++]=new(r.X,r.Y,left,r.W);
                    if(right<r.Z)scratch[next++]=new(right,r.Y,r.Z,r.W);
                    if(r.Y<bottom)scratch[next++]=new(left,r.Y,right,bottom);
                    if(top<r.W)scratch[next++]=new(left,top,right,r.W);
                }
                scratch[..next].CopyTo(patches);n=next;
            }
            for(int k=0;k<n;k++)
            {
                var r=patches[k];
                Vector3 min=axis==0?new(plane,r.Y,r.X):axis==1?new(r.X,plane,r.Y):new(r.X,r.Y,plane);
                Vector3 max=axis==0?new(plane,r.W,r.Z):axis==1?new(r.Z,plane,r.W):new(r.Z,r.W,plane);
                var extent=max-min;
                bool wire=block==BlockType.CopperWire||i>0;
                Rectangle? region=null;bool transpose=false;
                int tile=wire?Blocks.Get(BlockType.CopperWire).SideTile:Blocks.Tile(block,face.Normal);
                int width=Math.Max(1,(int)MathF.Round((r.Z-r.X)*16)),height=Math.Max(1,(int)MathF.Round((r.W-r.Y)*16));
                if(wire)
                {
                    // A two-texel copper band at the same density as block faces,
                    // rotated along vertical/Z runs; never squash the whole tile.
                    transpose=height>width;
                    int length=Math.Max(width,height),thickness=Math.Min(width,height);
                    region=new((16-length)/2,4,length,thickness);
                }
                else
                {
                    float left=face.Normal.X>0?1-max.Z:face.Normal.X<0?min.Z:face.Normal.Z<0?1-max.X:min.X;
                    float top=face.Normal.Y==0?1-max.Y:face.Normal.Y>0?min.Z:1-max.Z;
                    region=new((int)MathF.Round(left*16),(int)MathF.Round(top*16),width,height);
                }
                AddQuad(origin+(min+face.A*extent)*scale,origin+(min+face.B*extent)*scale,
                    origin+(min+face.C*extent)*scale,origin+(min+face.D*extent)*scale,
                    tile,new Color(new Vector3(face.Shade)),false,region,transpose);
            }
        }
    }
}
