using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM.UI;

internal static class ArmorHud
{
    private static readonly string[] Shape={"011111110","122222221","123222221","122222221","012222210","001222100","000121000","000010000"};
    public static int Icons(int points)=>points==0?0:points>20?10+(points-19)/2:10;
    public static int Fill(int points,int icon)=>Math.Clamp(points-icon*2,0,2);
    public static void Draw(SpriteBatch sprites,Texture2D pixel,Viewport viewport,int points)
    {
        if(points<=0)return;Rectangle area=ClassicHudLayout.Armor(viewport);int scale=ClassicHudLayout.Scale(viewport);
        for(int icon=0;icon<Icons(points);icon++)
        {
            int x=area.X+(9-icon%10)*8*scale,y=area.Y-(icon/10)*10*scale,fill=Fill(points,icon);
            for(int py=0;py<8;py++)for(int px=0;px<9;px++)
            {
                char part=Shape[py][px];if(part=='0')continue;
                Color color=part=='1'?MdsmUiStyle.Edge:fill==2||fill==1&&px>=4?new Color(174,202,194):new Color(62,77,81);
                sprites.Draw(pixel,new Rectangle(x+px*scale,y+py*scale,scale,scale),color);
            }
        }
    }
}
