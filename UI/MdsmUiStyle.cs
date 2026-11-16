using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
namespace MDSM.UI;

// MDSM's code-native UI language. Reference art informs organization only.
internal static class MdsmUiStyle
{
    internal static readonly Color Panel=new(42,57,62),Inset=new(25,36,41),Edge=new(13,24,28),Light=new(91,119,122),Accent=new(217,163,94),Ink=new(230,228,210);
    internal static void Frame(SpriteBatch batch,Texture2D pixel,Rectangle r,int scale=1)
    {
        void Fill(Rectangle a,Color c)=>batch.Draw(pixel,a,c);
        Fill(new(r.X+scale,r.Y+scale,r.Width,r.Height),Color.Black*.55f);
        Fill(r,Edge);Fill(new(r.X+scale,r.Y+scale,r.Width-2*scale,r.Height-2*scale),Panel);
        Fill(new(r.X+2*scale,r.Y+2*scale,r.Width-4*scale,scale),Light);
        Fill(new(r.X+2*scale,r.Bottom-3*scale,r.Width-4*scale,scale),Inset);
        Fill(new(r.X,r.Y,scale,scale),Color.Transparent);
    }
    internal static void ScrollThumb(SpriteBatch batch,Texture2D pixel,Rectangle r,int scale)
    {
        batch.Draw(pixel,r,Accent);
        // Recessed grip ridges retain an even margin at every UI scale.
        int inset=System.Math.Max(1,2*scale),gap=3*scale;
        for(int y=r.Y+gap;y+2*scale<=r.Bottom-gap;y+=gap)
        {
            int width=r.Width-2*inset;
            if(width<=2)break;
            batch.Draw(pixel,new Rectangle(r.X+inset+1,y,width-2,2*scale),new Color(193,144,85));
            batch.Draw(pixel,new Rectangle(r.X+inset,y+1,width,System.Math.Max(1,2*scale-2)),new Color(193,144,85));
        }
    }
    internal static Texture2D Hotbar(GraphicsDevice device)
    {
        var data=new Color[182*22];
        void Rect(int x,int y,int w,int h,Color c){for(int j=y;j<y+h;j++)for(int i=x;i<x+w;i++)data[j*182+i]=c;}
        Rect(0,0,182,22,Edge);Rect(1,1,180,20,Panel);Rect(2,1,178,1,Light);
        for(int n=0;n<9;n++){int x=2+n*20;Rect(x,3,18,17,Inset);Rect(x,3,18,1,Edge);Rect(x,19,18,1,Light);Rect(x+2,5,14,12,new Color(24,35,39,180));}
        data[0]=data[181]=data[21*182]=data[^1]=Color.Transparent;
        for(int i=0;i<data.Length;i++)data[i]=Color.FromNonPremultiplied(data[i].R,data[i].G,data[i].B,data[i].A);
        var texture=new Texture2D(device,182,22);texture.SetData(data);return texture;
    }
    internal static void Arrow(SpriteBatch batch,Texture2D pixel,Rectangle bounds,Color color)
    {
        int unit=System.Math.Max(1,bounds.Height/7);
        batch.Draw(pixel,new Rectangle(bounds.X,bounds.Center.Y-unit,bounds.Width-3*unit,2*unit),color);
        for(int n=0;n<4;n++)batch.Draw(pixel,new Rectangle(bounds.Right-(4-n)*unit,bounds.Center.Y-(4-n)*unit,unit,(8-n*2)*unit),color);
    }
}
