using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Worlds;

namespace MDSM.Rendering;

// Seasonal color variants of authored atlas pixels. Base artwork lives in terrain.png.
internal static class SeasonalArtwork
{
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
