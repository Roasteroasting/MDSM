using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Worlds;

namespace MDSM.UI;

internal sealed class SignEditor:IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly string[] _lines={"","","",""};
    private World _world;private Point3 _cell;private int _line;
    public SignEditor(GraphicsDevice device){_sprites=new(device);_pixel=new(device,1,1);_pixel.SetData(new[]{Color.White});}
    public void Open(World world,Point3 cell)
    {_world=world;_cell=cell;_line=0;Array.Fill(_lines,"");if(world.Signs.TryGetValue(cell,out var text)){var lines=text.Split('\n');Array.Copy(lines,_lines,Math.Min(lines.Length,4));}}
    public void Close(){_world=null;Array.Fill(_lines,"");}
    public void TextInput(char c)
    {
        if(_world==null)return;
        if(c=='\b'){if(_lines[_line].Length>0)_lines[_line]=_lines[_line][..^1];}
        else if(c>=' ' && c<='~' && _lines[_line].Length<24)_lines[_line]+=c;
        else return;
        _world.EditSign(_cell,string.Join('\n',_lines));
    }
    private static Rectangle Done(Viewport v)=>new(v.Width/2-Math.Min(120,v.Width/3),v.Height/2+54,Math.Min(240,v.Width*2/3),30);
    public bool Update(KeyboardState keys,KeyboardState previous,MouseState mouse,MouseState old,Viewport v)
    {
        bool Pressed(Keys k)=>keys.IsKeyDown(k)&&previous.IsKeyUp(k);
        if(Pressed(Keys.Up))_line=(_line+3)%4;
        if(Pressed(Keys.Down)||Pressed(Keys.Enter))_line=(_line+1)%4;
        return Pressed(Keys.Escape)||mouse.LeftButton==ButtonState.Pressed && old.LeftButton==ButtonState.Released && Done(v).Contains(mouse.Position);
    }
    public void Draw(Viewport v)
    {
        int scale=v.Width>=640?2:1;
        _sprites.Begin(samplerState:SamplerState.PointClamp);
        _sprites.Draw(_pixel,new Rectangle(0,0,v.Width,v.Height),Color.Black*.65f);
        PixelFont.Draw(_sprites,_pixel,"EDIT SIGN",v.Width/2-27*scale,Math.Max(10,v.Height/2-100),scale,Color.White);
        for(int i=0;i<4;i++)
        {
            int y=v.Height/2-40+i*20;
            _sprites.Draw(_pixel,new Rectangle(v.Width/2-76*scale,y-3,152*scale,16),i==_line?new Color(106,84,55):new Color(72,55,36));
            PixelFont.Draw(_sprites,_pixel,_lines[i],v.Width/2-_lines[i].Length*3*scale,y,scale,Color.White);
        }
        Rectangle done=Done(v);_sprites.Draw(_pixel,done,new Color(125,119,107));_sprites.Draw(_pixel,new Rectangle(done.X+2,done.Y+2,done.Width-4,done.Height-4),new Color(65,64,59));
        PixelFont.Draw(_sprites,_pixel,"DONE",done.Center.X-12,done.Center.Y-3,1,Color.White);
        PixelFont.Draw(_sprites,_pixel,"ENTER / ARROWS: LINE   ESC: DONE",Math.Max(4,v.Width/2-96),Math.Min(v.Height-10,done.Bottom+10),1,Color.LightGray);
        _sprites.End();
    }
    public void Dispose(){_sprites.Dispose();_pixel.Dispose();}
}
