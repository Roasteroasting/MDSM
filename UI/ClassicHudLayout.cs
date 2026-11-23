using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
namespace MDSM.UI;
internal static class ClassicHudLayout
{
    internal static int Scale(Viewport v)=>Math.Max(1,Math.Min(v.Width/320,v.Height/240));
    internal static Rectangle Bar(Viewport v){int s=Scale(v);return new(v.X+(v.Width-182*s)/2,v.Y+v.Height-22*s,182*s,22*s);}
    internal static Rectangle Slot(Viewport v,int slot){var b=Bar(v);int s=Scale(v);return new(b.X+(1+slot*20)*s,b.Y+s,20*s,20*s);}
    internal static Rectangle Selection(Viewport v,int slot){var b=Bar(v);int s=Scale(v);return new(b.X+(slot*20-1)*s,b.Y-s,24*s,24*s);}
    internal static Rectangle Armor(Viewport v){var b=Bar(v);int s=Scale(v);return new(b.Right-82*s,b.Y-10*s,81*s,9*s);}
    internal static Rectangle BootsIndicator(Viewport v)
    {
        Rectangle bar=Bar(v);
        int scale=Scale(v);
        return new(bar.Center.X-4*scale,bar.Y-10*scale,8*scale,8*scale);
    }
}
// Original procedural hotbar and corner-bracket selection artwork.
internal sealed class ClassicHudGraphics:IDisposable
{
    internal readonly Texture2D Bar,Selection;
    internal readonly BlendState CrosshairBlend=new(){ColorSourceBlend=Blend.InverseDestinationColor,ColorDestinationBlend=Blend.InverseSourceColor,AlphaSourceBlend=Blend.Zero,AlphaDestinationBlend=Blend.One};
    internal ClassicHudGraphics(GraphicsDevice device)
    {
        Bar=MdsmUiStyle.Hotbar(device);
        var frame=new Color[24*24];for(int y=0;y<24;y++)for(int x=0;x<24;x++)
        {int edge=Math.Min(Math.Min(x,23-x),Math.Min(y,23-y));bool corner=(x<6||x>17)&&(y<6||y>17);frame[y*24+x]=corner&&edge<2?MdsmUiStyle.Accent:Color.Transparent;}
        Selection=new(device,24,24);Selection.SetData(frame);
    }
    public void Dispose(){Bar.Dispose();Selection.Dispose();CrosshairBlend.Dispose();}
}
