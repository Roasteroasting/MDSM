using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Inventories;
namespace MDSM.Rendering;

// First-person only: preserve the painted front/back and extrude exposed pixel
// edges by one texel. Built once per selected sprite, never during ordinary draws.
internal sealed class HeldSpriteMeshes
{
    private readonly bool[] _opaque;
    private readonly bool[] _painted;
    private readonly Dictionary<ItemId,Vector3[]> _facingCache=new();
    private readonly Dictionary<ItemId,VertexPositionColorTexture[]> _cache=new();
    internal HeldSpriteMeshes(GraphicsDevice device,Texture2D atlas)
    {
        var previous=device.Textures[0];
        try
        {
            var pixels=new Color[atlas.Width*atlas.Height];atlas.GetData(pixels);
            _opaque=new bool[pixels.Length];_painted=new bool[pixels.Length];
            for(int i=0;i<pixels.Length;i++){_opaque[i]=pixels[i].A>=128;_painted[i]=_opaque[i]&&(pixels[i].R>0||pixels[i].G>0||pixels[i].B>0);}
        }
        finally {device.Textures[0]=null;device.Textures[0]=previous;}
    }
    // The reference's pivot is the painted face, not transparent padding or
    // the heavy black outline. These points are for framing only, never drawn.
    internal Vector3[] FacingVertices(ItemId item)
    {
        if(_facingCache.TryGetValue(item,out var cached))return cached;
        int tile=Items.Get(item).Sprite,tx=tile%16*16,ty=tile/16*16;
        var points=new List<Vector3>();
        void Fill(bool[] mask)
        {
            const float z=0; // Frame the painted sprite plane, independently of edge thickness.
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)if(mask[(ty+y)*256+tx+x])
            {
                Vector3 a=new((x/16f-.5f)*.875f,(.5f-y/16f)*.875f,z),b=a+Vector3.Right*(.875f/16),c=b-Vector3.Up*(.875f/16),d=a-Vector3.Up*(.875f/16);
                points.Add(a);points.Add(b);points.Add(c);points.Add(a);points.Add(c);points.Add(d);
            }
        }
        Fill(_painted);if(points.Count==0)Fill(_opaque);
        return _facingCache[item]=points.ToArray();
    }
    internal VertexPositionColorTexture[] Get(ItemId item)
    {
        if(_cache.TryGetValue(item,out var cached))return cached;
        var vertices=new List<VertexPositionColorTexture>(768);
        int tile=Items.Get(item).Sprite,tx=tile%16*16,ty=tile/16*16;
        const float size=.875f,depth=size/32;
        bool Solid(int x,int y)=>x>=0&&x<16&&y>=0&&y<16&&_opaque[(ty+y)*256+tx+x];
        Vector3 P(int x,int y,float z)=>new((x/16f-.5f)*size,(.5f-y/16f)*size,z);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color,Vector2 uvA,Vector2 uvB,Vector2 uvC,Vector2 uvD)
        {vertices.Add(new(a,color,uvA));vertices.Add(new(b,color,uvB));vertices.Add(new(c,color,uvC));vertices.Add(new(a,color,uvA));vertices.Add(new(c,color,uvC));vertices.Add(new(d,color,uvD));}
        // Face texels must align with the pixel-edge geometry, not a half-texel inset.
        const float inset=0.000001f;
        Vector2 lo=new(tx/256f+inset,ty/256f+inset),hi=new((tx+16)/256f-inset,(ty+16)/256f-inset);
        Quad(P(0,0,depth),P(0,16,depth),P(16,16,depth),P(16,0,depth),Color.White,lo,new(lo.X,hi.Y),hi,new(hi.X,lo.Y));
        Quad(P(16,0,-depth),P(16,16,-depth),P(0,16,-depth),P(0,0,-depth),new Color(220,220,220),new(hi.X,lo.Y),hi,new(lo.X,hi.Y),lo);
        for(int y=0;y<16;y++)for(int x=0;x<16;x++)if(Solid(x,y))
        {
            Vector2 uv=new((tx+x+.5f)/256,(ty+y+.5f)/256);
            void Edge(int ax,int ay,int bx,int by,byte shade)=>Quad(P(ax,ay,depth),P(bx,by,depth),P(bx,by,-depth),P(ax,ay,-depth),new Color(shade,shade,shade),uv,uv,uv,uv);
            if(!Solid(x-1,y))Edge(x,y+1,x,y,175);
            if(!Solid(x+1,y))Edge(x+1,y,x+1,y+1,205);
            if(!Solid(x,y-1))Edge(x,y,x+1,y,240);
            if(!Solid(x,y+1))Edge(x+1,y+1,x,y+1,150);
        }
        return _cache[item]=vertices.ToArray();
    }
}
