using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using MDSM.Inventories;
using MDSM.Rendering;

namespace MDSM.Testing;

internal static class ItemAtlasChecks
{
    public static void LayoutAndTiles()
    {
        string root=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(root,"MDSM.csproj")))root=Directory.GetParent(root)?.FullName??throw new Exception("Run source-asset checks from this repository.");
        var atlas=Read(Path.Combine(root,"Content/Items/items.png"));
        if(atlas.Width!=256 || atlas.Height!=256)throw new Exception("Item atlas dimensions changed.");
        foreach(ItemId item in Items.Sprites)
        {
            int tile=Items.Get(item).Sprite;var bounds=TextureAtlas.Source(tile);bool opaque=false,clear=false;
            for(int y=bounds.Top;y<bounds.Bottom;y++)for(int x=bounds.Left;x<bounds.Right;x++)
            {byte alpha=atlas.Pixels[(y*256+x)*4+3];opaque|=alpha>=128;clear|=alpha==0;}
            if(!opaque || !clear)throw new Exception("Item atlas tile missing silhouette/transparency: "+Items.Name(item));
        }
        Console.WriteLine($"  Item atlas dimensions and {Items.Sprites.Length} registered sprite tiles verified.");
    }
    // Small test-only RGBA8 PNG reader. Independent of MGCB/GPU premultiplication;
    // validates source atlas pixels without relying on runtime texture loading.
    private static (int Width,int Height,byte[] Pixels) Read(string path)
    {
        byte[] png=File.ReadAllBytes(path);int width=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)),height=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        if(width<1 || height<1 || width*height>1024*1024 || png[24]!=8 || png[25]!=6 || png[28]!=0)throw new Exception("Expected bounded non-interlaced RGBA8 PNG.");
        using var compressed=new MemoryStream();int offset=8;
        while(offset<png.Length)
        {
            int length=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));offset+=4;
            bool data=png[offset]==73 && png[offset+1]==68 && png[offset+2]==65 && png[offset+3]==84;
            if(data)compressed.Write(png,offset+4,length);offset+=length+8;
        }
        compressed.Position=0;using var zlib=new ZLibStream(compressed,CompressionMode.Decompress);
        var pixels=new byte[width*height*4];var row=new byte[width*4+1];int stride=width*4;
        for(int y=0;y<height;y++)
        {
            zlib.ReadExactly(row);int filter=row[0];
            for(int x=0;x<stride;x++)
            {
                int left=x>=4?pixels[y*stride+x-4]:0,up=y>0?pixels[(y-1)*stride+x]:0,diagonal=x>=4 && y>0?pixels[(y-1)*stride+x-4]:0;
                int prediction=filter switch{0=>0,1=>left,2=>up,3=>(left+up)/2,4=>Paeth(left,up,diagonal),_=>throw new Exception("Invalid PNG filter.")};
                pixels[y*stride+x]=unchecked((byte)(row[x+1]+prediction));
            }
        }
        return(width,height,pixels);
    }
    private static int Paeth(int a,int b,int c)
    {int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb && pa<=pc?a:pb<=pc?b:c;}
}
