using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;

namespace MDSM.Rendering;

// Remap the original pixels at load time. Keeping coordinates and alpha intact
// preserves face details, silhouettes and UVs while removing noisy shading.
internal static class HostilePalettes
{
    private static readonly Color[] Zombie =
    {
        new(20,23,19),new(116,45,45),
        new(49,75,35),new(89,130,64),
        new(12,61,65),new(19,137,140),
        new(51,28,75),new(94,50,125)
    };
    private static readonly Color[] Skeleton =
    {
        new(23,22,20),new(66,61,53),new(141,129,105),new(228,220,190)
    };
    private static readonly Color[] Spider =
    {
        new(21,15,13),new(51,35,28),new(94,67,51),
        new(142,30,21),new(240,61,42)
    };
    internal static Texture2D Create(GraphicsDevice device,Texture2D source,MobSpecies species)
    {
        var pixels=new Color[source.Width*source.Height];
        source.GetData(pixels);Remap(pixels,species);
        var texture=new Texture2D(device,source.Width,source.Height);
        texture.SetData(pixels);
        return texture;
    }
    internal static void Remap(Span<Color> pixels,MobSpecies species)
    {
        var palette=species switch {MobSpecies.Zombie=>Zombie,MobSpecies.Skeleton=>Skeleton,MobSpecies.Spider=>Spider,_=>throw new ArgumentOutOfRangeException(nameof(species))};
        for(int i=0;i<pixels.Length;i++)
        {
            var original=pixels[i];if(original.A==0)continue;
            int nearest=int.MaxValue;Color replacement=original;
            foreach(var candidate in palette)
            {
                int r=original.R-candidate.R,g=original.G-candidate.G,b=original.B-candidate.B;
                int distance=r*r+g*g+b*b;
                if(distance<nearest){nearest=distance;replacement=candidate;}
            }
            pixels[i]=new Color(replacement.R,replacement.G,replacement.B,original.A);
        }
    }
}
