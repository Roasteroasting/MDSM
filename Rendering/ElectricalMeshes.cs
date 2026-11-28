using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed partial class ChunkMeshBuilder
{
    private void AppendElectrical(BlockType block,Vector3 origin,int connections,float scale=1)
    {
        Span<BoundingBox> parts=stackalloc BoundingBox[7];
        int count=ElectricalGeometry.Boxes(Vector3.Zero,block,connections,parts);
        for(int i=0;i<count;i++)foreach(var face in Faces)
        {
            var box=parts[i];var extent=box.Max-box.Min;
            // The central joint and arm caps meet exactly; omit buried caps.
            if(block==BlockType.CopperWire)
            {
                int bit=face.Offset.X<0?1:face.Offset.X>0?2:face.Offset.Y<0?4:face.Offset.Y>0?8:face.Offset.Z<0?16:32;
                if(i==0&&(connections&bit)!=0)continue;
                if(i>0)
                {
                    int arm=0,n=0;for(int k=0;k<6;k++)if((connections&(1<<k))!=0&&++n==i){arm=k;break;}
                    if(bit==(1<<(arm^1)))continue;
                }
            }
            int tile=Blocks.Tile(block,face.Normal);
            AddQuad(origin+(box.Min+face.A*extent)*scale,origin+(box.Min+face.B*extent)*scale,
                origin+(box.Min+face.C*extent)*scale,origin+(box.Min+face.D*extent)*scale,
                tile,new Color(new Vector3(face.Shade)),false);
        }
    }
}
