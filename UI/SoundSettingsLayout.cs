using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;

namespace MDSM.UI;

internal static class SoundSettingsLayout
{
    public static Rectangle Slider(Viewport viewport,int index)
    {
        int width=Math.Min(520,Math.Max(32,viewport.Width-32));bool small=viewport.Height<400;
        return new((viewport.Width-width)/2,(small?54:Math.Max(100,viewport.Height/2-110))+index*(small?36:62),width,small?16:32);
    }
    public static Rectangle Back(Viewport viewport)
    {var last=Slider(viewport,3);return new((viewport.Width-Math.Min(300,last.Width))/2,last.Bottom+16,Math.Min(300,last.Width),viewport.Height<400?28:42);}
    public static float ValueAt(Rectangle slider,int x)=>Math.Clamp((float)Math.Round((x-slider.Left-8)*100d/Math.Max(1,slider.Width-17))/100,0,1);
    public static float Get(GameSettings settings,int index)=>index switch{0=>settings.MasterVolume,1=>settings.EffectsVolume,2=>settings.MobVolume,_=>settings.InterfaceVolume};
    public static void Set(GameSettings settings,int index,float value)
    {switch(index){case 0:settings.MasterVolume=value;break;case 1:settings.EffectsVolume=value;break;case 2:settings.MobVolume=value;break;default:settings.InterfaceVolume=value;break;}}
    public static readonly string[] Labels={"MASTER","EFFECTS","MOBS","INTERFACE"};
}
