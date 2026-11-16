using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
namespace MDSM.UI;

// Menu controls share the inventory's teal metalwork and brass focus accents.
internal static class MenuChrome
{
    private static readonly string[] Letters={"10001110111010110101100011000110001", "11110100011000110001100011000111110", "01111100001000001110000010000111110", "10001110111010110101100011000110001"};
    internal static void Control(SpriteBatch batch,Texture2D pixel,Rectangle r,bool hovered=false,bool enabled=true,bool inset=false,bool keyboardFocus=false)
    {
        int s=Math.Clamp(r.Height/20,1,2);
        MdsmUiStyle.Frame(batch,pixel,r,s);
        if(inset)batch.Draw(pixel,new Rectangle(r.X+3*s,r.Y+3*s,Math.Max(1,r.Width-6*s),Math.Max(1,r.Height-6*s)),MdsmUiStyle.Inset);
        if(!enabled)batch.Draw(pixel,r,Color.Black*.35f);
        if(keyboardFocus && enabled)
            batch.Draw(pixel,new Rectangle(r.X+5*s,r.Bottom-4*s,r.Width-10*s,s),MdsmUiStyle.Light);
        if(hovered && enabled)
        {
            batch.Draw(pixel,new Rectangle(r.X+2*s,r.Y+2*s,2*s,r.Height-4*s),MdsmUiStyle.Accent);
            batch.Draw(pixel,new Rectangle(r.Right-4*s,r.Y+2*s,2*s,r.Height-4*s),MdsmUiStyle.Accent);
        }
    }
    internal static void Logo(SpriteBatch batch,Texture2D pixel,Viewport viewport,int y)
    {
        // Individually bevelled block letters, with a deep extruded silhouette.
        int buttonTop=viewport.Height<400?64:viewport.Height/2-50;
        int s=Math.Clamp(Math.Min(viewport.Width/42,(buttonTop-y-24)/7),2,12),width=23*s,x0=(viewport.Width-width)/2;
        for(int depth=3;depth>=0;depth--)
        for(int glyph=0;glyph<4;glyph++)for(int row=0;row<7;row++)for(int col=0;col<5;col++)
        {
            if(Letters[glyph][row*5+col]!='1')continue;
            var r=new Rectangle(x0+(glyph*6+col)*s+depth,y+row*s+depth*2,s,s);
            batch.Draw(pixel,r,depth>0?MdsmUiStyle.Edge:new Color(222-row*8,192-row*7,133-row*5));
            if(depth==0){batch.Draw(pixel,new Rectangle(r.X,r.Y,s,Math.Max(1,s/5)),new Color(247,227,176));batch.Draw(pixel,new Rectangle(r.X,r.Bottom-1,s,1),new Color(139,105,63));}
        }
    }
}

