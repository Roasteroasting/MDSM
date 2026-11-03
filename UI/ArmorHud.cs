using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM.UI;

internal static class ArmorHud
{
    public static int Icons(int points)=>points==0?0:points>20?10+(points-19)/2:10;
    public static int Fill(int points,int icon)=>Math.Clamp(points-icon*2,0,2);
    public static void Draw(SpriteBatch sprites,Texture2D pixel,Viewport viewport,int points)
    {
        if(points<=0)return;Rectangle hearts=HealthHud.Bounds(viewport);int scale=hearts.Height/8;
        for(int icon=0;icon<Icons(points);icon++)
        {
            int x=hearts.X+(icon%10)*10*scale,y=hearts.Y-(icon>=10?20:10)*scale-4,fill=Fill(points,icon);
            for(int py=0;py<7;py++)for(int px=0;px<8;px++)
            {
                bool outline=py==0||py==6||px==0||px==7;bool body=py>=2||px<2||px>5;if(!body)continue;
                Color color=outline?new Color(25,28,32):fill==2||fill==1&&px<4?Color.LightGray:new Color(70,75,82);
                sprites.Draw(pixel,new Rectangle(x+px*scale,y+py*scale,scale,scale),color);
            }
        }
    }
}
