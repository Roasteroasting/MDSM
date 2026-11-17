using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Rendering;

// Four light samples on the outside of a face, with bounded voxel AO.
// Encoded quarter-level samples fit the existing 24-byte terrain vertex. The
// block-light byte's unused range marks smooth vertices; flat bytes are unchanged.
internal readonly record struct CornerLight(float Sky,float Block,float Ambient)
{
    internal Color Encode(Color shade)=>new((byte)Math.Clamp((int)MathF.Round(shade.R*Ambient),0,255),
        (byte)Math.Clamp((int)MathF.Round(Sky*4),0,56),(byte)(64+Math.Clamp((int)MathF.Round(Block*4),0,56)),shade.A);
    internal static float Occlusion(BlockType block)=>Blocks.IsOpaque(block)&&Blocks.Get(block).Shape==BlockShape.Cube?1:block==BlockType.Leaves?.35f:0;
    internal static float Weight(Color encoded)=>encoded.R*Math.Max(encoded.G,encoded.B-64);
}

internal sealed partial class ChunkMeshBuilder
{
    internal bool SmoothLighting {get;set;}
    private byte SampleLight(Point3 p)=>_lightPage!=null&&p.X>=_lightPage.X-1&&p.X<=_lightPage.X+16&&p.Z>=_lightPage.Z-1&&p.Z<=_lightPage.Z+16
        ?_lightPage.At(p.X,p.Y,p.Z):_world.Lighting.Packed(p);
    internal CornerLight Corner(Point3 face,Point3 u,Point3 v)
    {
        byte light=SampleLight(face);
        var p=face+u;var q=face+v;var r=p+v;
        bool knownP=_world.TryMeshBlock(p,out var bp),knownQ=_world.TryMeshBlock(q,out var bq),knownR=_world.TryMeshBlock(r,out var br);
        float a=knownP?CornerLight.Occlusion(bp):0;
        float b=knownQ?CornerLight.Occlusion(bq):0;
        float c=knownR?CornerLight.Occlusion(br):0;
        // A diagonal behind two touching solids cannot illuminate the corner.
        bool sealedCorner=a>=1&&b>=1;if(sealedCorner)c=1;
        byte lp=knownP&&a<1?SampleLight(p):light,lq=knownQ&&b<1?SampleLight(q):light;
        byte lr=knownR&&c<1&&!sealedCorner?SampleLight(r):light;
        // Use the same available maximum for opaque/unknown samples on either
        // side of a shared vertex, avoiding a base-cell-dependent seam.
        byte fallback=(byte)((Math.Max(Math.Max(light>>4,lp>>4),Math.Max(lq>>4,lr>>4))<<4)|Math.Max(Math.Max(light&15,lp&15),Math.Max(lq&15,lr&15)));
        if(!knownP||a>=1)lp=fallback;if(!knownQ||b>=1)lq=fallback;if(!knownR||c>=1||sealedCorner)lr=fallback;
        return new(((light>>4)+(lp>>4)+(lq>>4)+(lr>>4))*.25f,
            ((light&15)+(lp&15)+(lq&15)+(lr&15))*.25f,1-.2f*(a+b+c));
    }
    private bool SmoothFace(Vector3 center,Vector3 normal,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color shade,out Color ca,out Color cb,out Color cc,out Color cd)
    {
        ca=cb=cc=cd=shade;
        int axis=Math.Abs(normal.X)>.9999f?0:Math.Abs(normal.Y)>.9999f?1:Math.Abs(normal.Z)>.9999f?2:-1;
        if(axis<0)return false;
        var owner=center-normal*.02f;var cell=new Point3((long)MathF.Floor(owner.X),(int)MathF.Floor(owner.Y),(long)MathF.Floor(owner.Z));
        if(_world.Lighting.Emission(cell,_world.GetBlock(cell.X,cell.Y,cell.Z))>0)return false;
        var at=center+normal*.02f;var face=new Point3((long)MathF.Floor(at.X),(int)MathF.Floor(at.Y),(long)MathF.Floor(at.Z));
        int ux=axis==0?2:0,vx=axis==1?2:1;
        Point3 Offset(int dimension,int sign)=>dimension==0?new(sign,0,0):dimension==1?new(0,sign,0):new(0,0,sign);
        var ll=Corner(face,Offset(ux,-1),Offset(vx,-1));var hl=Corner(face,Offset(ux,1),Offset(vx,-1));
        var lh=Corner(face,Offset(ux,-1),Offset(vx,1));var hh=Corner(face,Offset(ux,1),Offset(vx,1));
        float Component(Vector3 p,int dimension)=>dimension==0?p.X:dimension==1?p.Y:p.Z;
        float baseU=MathF.Floor(Component(center,ux)),baseV=MathF.Floor(Component(center,vx));
        Color ColorAt(Vector3 p)
        {
            float u=Math.Clamp(Component(p,ux)-baseU,0,1),v=Math.Clamp(Component(p,vx)-baseV,0,1);
            float Blend(float l,float h,float l2,float h2)=>MathHelper.Lerp(MathHelper.Lerp(l,h,u),MathHelper.Lerp(l2,h2,u),v);
            return new CornerLight(Blend(ll.Sky,hl.Sky,lh.Sky,hh.Sky),Blend(ll.Block,hl.Block,lh.Block,hh.Block),Blend(ll.Ambient,hl.Ambient,lh.Ambient,hh.Ambient)).Encode(shade);
        }
        ca=ColorAt(a);cb=ColorAt(b);cc=ColorAt(c);cd=ColorAt(d);return true;
    }
}
