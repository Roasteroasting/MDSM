using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Worlds;

namespace MDSM.Rendering;

// Original small sprite designs; all existing atlas texels retain their source artwork.
internal static class SeasonalArtwork
{
    internal static void Extend(Texture2D terrain)
    {
        var snow=new Color[256];var earth=new Color[256];
        terrain.GetData(0,TextureAtlas.Source(61),snow,0,256);
        terrain.GetData(0,TextureAtlas.Source(2),earth,0,256);
        int[] fringe={4,4,6,6,5,4,4,5,6,6,5,4,4,5,5,5};
        for(int z=0;z<16;z++)for(int x=0;x<16;x++)
            if(z<fringe[x])earth[z*16+x]=z==fringe[x]-1?new Color(191,207,223):snow[z*16+x];
        Put(terrain,Blocks.SnowyGrassTile,earth);
        Put(terrain,85,Plant(true));
        Put(terrain,86,Plant(false));
        static void Put(Texture2D atlas,int tile,Color[] pixels)=>atlas.SetData(0,TextureAtlas.Source(tile),pixels,0,256);
    }

    private static Color[] Plant(bool peony)
    {
        var image=new Color[256];
        Color stem=new(48,91,39),leaf=new(69,133,55),lightLeaf=new(103,159,68);
        for(int y=6;y<16;y++)image[y*16+7]=stem;
        for(int i=1;i<=4;i++)
        {
            image[(12-i/2)*16+7-i]=leaf;
            image[(14-i/2)*16+7+i]=lightLeaf;
        }
        var dark=peony?new Color(161,45,92):new Color(68,47,118);
        var mid=peony?new Color(220,87,147):new Color(106,74,191);
        var light=peony?new Color(252,150,190):new Color(167,129,231);
        for(int dy=-3;dy<=3;dy++)for(int dx=-4;dx<=4;dx++)
        {
            int rim=Math.Abs(dx)+Math.Abs(dy);
            if(rim>5 || !peony && Math.Abs(dx)>2 && Math.Abs(dy)>1)continue;
            image[(5+dy)*16+7+dx]=rim>=5 || dy==3?dark:dy<=0?light:mid;
        }
        image[5*16+7]=new Color(255,214,124);
        return image;
    }

    internal static Texture2D Variant(Texture2D source,Season season)
    {
        var pixels=new Color[TextureAtlas.AtlasSize*TextureAtlas.AtlasSize];
        source.GetData(pixels);
        foreach(int tile in new[]{0,1,6,12,51,52})
        {
            Rectangle region=TextureAtlas.Source(tile);
            for(int y=region.Top;y<region.Bottom;y++)for(int x=region.Left;x<region.Right;x++)
            {
                int index=y*TextureAtlas.AtlasSize+x;
                pixels[index]=Tint(pixels[index],season,tile==6);
            }
        }
        var texture=new Texture2D(source.GraphicsDevice,source.Width,source.Height);
        texture.SetData(pixels);
        return texture;
    }

    internal static Color Tint(Color original,Season season,bool foliage)
    {
        if(season==Season.Summer || original.A==0 || original.G<=original.R || original.G<=original.B)return original;
        Vector3 target=season switch
        {
            Season.Spring=>new(.66f,1.0f,.48f),
            Season.Autumn=>foliage?new(1.0f,.57f,.23f):new(1.0f,.75f,.30f),
            _=>foliage?new(1.0f,.76f,.53f):new(1.0f,.93f,.73f)
        };
        float value=Math.Clamp(.35f+original.G/255f*.70f,0,1);
        return new Color(new Vector4(target*value,original.A/255f));
    }
}
