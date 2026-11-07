using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;

namespace MDSM.UI;

internal static class SoundSettingsLayout
{
    public static Rectangle Slider(Viewport viewport,int index)
    {
        return SettingsLayout.Volume(viewport,index);
    }
    public static Rectangle Back(Viewport viewport)
    =>SettingsLayout.Back(viewport);
    public static float ValueAt(Rectangle slider,int x)=>Math.Clamp((float)Math.Round((x-slider.Left-8)*100d/Math.Max(1,slider.Width-17))/100,0,1);
    public static float Get(GameSettings settings,int index)=>index switch{0=>settings.MasterVolume,1=>settings.EffectsVolume,2=>settings.MobVolume,_=>settings.InterfaceVolume};
    public static void Set(GameSettings settings,int index,float value)
    {switch(index){case 0:settings.MasterVolume=value;break;case 1:settings.EffectsVolume=value;break;case 2:settings.MobVolume=value;break;default:settings.InterfaceVolume=value;break;}}
    public static readonly string[] Labels={"MASTER","EFFECTS","MOBS","INTERFACE"};
}
