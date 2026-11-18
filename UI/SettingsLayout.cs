using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;

namespace MDSM.UI;

internal static class SettingsLayout
{
    private const int RowCount = 8;

    private static Rectangle Row(Viewport v,int row,int column=-1)
    {
        int gap=v.Height<400?4:10,height=v.Height<300?18:v.Height<400?28:36;
        int margin=v.Height<400?38:52;
        height=Math.Clamp((v.Height-margin-(RowCount-1)*gap-6)/RowCount,12,height);
        int width=Width(v),x=(v.Width-width)/2,top=Math.Max(margin,(v.Height-RowCount*(height+gap))/2);
        if(column>=0){width=(width-gap)/2;x+=column*(width+gap);}
        return new(x,top+row*(height+gap),width,height);
    }
    public static Rectangle Slider(Viewport viewport)=>Row(viewport,3,0);
    public static Rectangle Brightness(Viewport viewport)=>Row(viewport,0,0);
    public static Rectangle SmoothLighting(Viewport viewport)=>Row(viewport,3,1);
    public static Rectangle Volume(Viewport viewport,int index)=>index==4?Row(viewport,0,1):Row(viewport,1+index/2,index%2);
    public static Rectangle Toggle(Viewport viewport)=>Row(viewport,4,0);
    public static Rectangle Bobbing(Viewport viewport)=>Row(viewport,4,1);
    public static Rectangle Choice(Viewport viewport,int index)=>Row(viewport,5,index);
    public static Rectangle Information(Viewport viewport,int index)=>Row(viewport,6,index);
    public static Rectangle Back(Viewport viewport)=>Row(viewport,7);
    // Leave one pixel per FPS increment on wide windows, even in a half-width row.
    private static int Width(Viewport viewport) => Math.Min(754, Math.Max(32, viewport.Width - 32));
    public static int ValueAt(Rectangle slider, int x) => Math.Clamp(GameSettings.MinimumFps + (int)Math.Round(
        (x - slider.Left - 8) / (double)Math.Max(1, slider.Width - 17) * (GameSettings.MaximumFps - GameSettings.MinimumFps)),
        GameSettings.MinimumFps, GameSettings.MaximumFps);
    public static int ThumbX(Rectangle slider, int value) => slider.Left + 8 + (int)Math.Round(
        (value - GameSettings.MinimumFps) / (double)(GameSettings.MaximumFps - GameSettings.MinimumFps) * (slider.Width - 17));
}
