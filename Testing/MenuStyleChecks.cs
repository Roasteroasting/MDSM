using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Rendering;
using MDSM.UI;
namespace MDSM.Testing;
internal static class MenuStyleChecks
{
    internal static int Run()
    {try{InterfaceChecks.InformationAndLayouts();using var game=new Preview();game.Run();Console.WriteLine("PASS: Code-drawn menu styles, panorama animation/time steps, transparent-free background, responsive menu previews and resource cleanup.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        internal Preview(){_ = new GraphicsDeviceManager(this){PreferredBackBufferWidth=1280,PreferredBackBufferHeight=720,SynchronizeWithVerticalRetrace=false};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        private static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
        protected override void Draw(GameTime time)
        {
            using var atlas=new TextureAtlas(Content);using var menu=new MenuController(GraphicsDevice,atlas,new GameSettings());
            // Exercise actual menu focus/hover state, rather than just the chrome helper.
            using(var target=new RenderTarget2D(GraphicsDevice,854,480))
            {
                var v=new Viewport(0,0,854,480);
                Rectangle Button(int index)=>(Rectangle)typeof(MenuController).GetMethod("ButtonBounds",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,new object[]{v,index});
                Color[] Render(){GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.Black);menu.Draw(v);GraphicsDevice.SetRenderTarget(null);var pixels=new Color[854*480];target.GetData(pixels);return pixels;}
                void Bars(Color[] pixels,int expected)
                {for(int i=0;i<4;i++){var r=Button(i);Check((pixels[(r.Y+10)*854+r.X+4]==MdsmUiStyle.Accent)==(i==expected),"Orange bars must follow hover, not retained focus.");}}
                menu.Open(GameState.MainMenu);Bars(Render(),-1);
                MouseState previous=default;
                for(int i=0;i<4;i++)
                {
                    var p=Button(i).Center;var mouse=new MouseState(p.X,p.Y,0,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
                    menu.Update(default,default,mouse,previous,v);previous=mouse;Bars(Render(),i);
                }
                menu.Update(new KeyboardState(Keys.Down),default,default,previous,v);Bars(Render(),-1);
                Check(menu.Update(new KeyboardState(Keys.Enter),default,default,default,v)==MenuAction.SelectWorld,"Keyboard navigation lost activation.");
                foreach(int scale in new[]{1,2,3})
                {
                    GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.Black);
                    using var batch=new SpriteBatch(GraphicsDevice);using var pixel=new Texture2D(GraphicsDevice,1,1);pixel.SetData(new[]{Color.White});
                    var r=new Rectangle(20,20,240,60*scale);batch.Begin(samplerState:SamplerState.PointClamp);
                    MdsmUiStyle.Frame(batch,pixel,r,scale);
                    var thumb=new Rectangle(300,20,12*scale,40*scale);MdsmUiStyle.ScrollThumb(batch,pixel,thumb,scale);batch.End();
                    GraphicsDevice.SetRenderTarget(null);var pixels=new Color[854*480];target.GetData(pixels);
                    for(int x=r.X+3*scale;x<r.Right-3*scale;x++)Check(pixels[(r.Y+4*scale)*854+x]==MdsmUiStyle.Panel,"Decorative frame dots returned.");
                    Check(pixels[(thumb.Y+3*scale)*854+thumb.X+2*scale+1]==new Color(193,144,85),"Scrollbar grip is missing.");
                    var track=InventoryLayout.BrowserTrack(v);
                    Check(Math.Abs(track.Center.X-(InventoryLayout.BrowserSlot(v,8).Right+InventoryLayout.Panel(v,true).Right)/2f)<=1,"Scrollbar is not centered in its gutter.");
                }
            }
            foreach(var size in new[]{new Point(320,240),new(854,480),new(1280,720)})
            foreach(var state in new[]{GameState.MainMenu,GameState.Settings,GameState.Controls,GameState.About,GameState.WorldCreation,GameState.WorldSelection,GameState.Paused})
            {
                using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);menu.Open(state);menu.Draw(GraphicsDevice.Viewport,state==GameState.Paused?"Landscape preview":null);
                GraphicsDevice.SetRenderTarget(null);
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"menu-style-{state}-{size.X}.png"));target.SaveAsPng(file,size.X,size.Y);
            }
            using(var panorama=new MenuPanorama(GraphicsDevice))
            using(var target=new RenderTarget2D(GraphicsDevice,512,288))
            {
                Color[] Capture(){GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.Magenta);panorama.Draw(GraphicsDevice.Viewport);GraphicsDevice.SetRenderTarget(null);var data=new Color[512*288];target.GetData(data);Check(data.All(c=>c.A==255&&c!=Color.Magenta),"Panorama has missing faces.");return data;}
                var first=Capture();for(int i=0;i<30*60;i++)panorama.Update(1.0/30);
                float angle=panorama.Angle;var second=Capture();Check(Math.Abs(angle-1.08f)<.0001f&&!first.SequenceEqual(second),"Panorama did not advance by gameplay-independent elapsed time.");
                panorama.Update(double.NaN);panorama.Update(-1);Check(panorama.Angle==angle,"Invalid elapsed time altered panorama.");
                for(int i=0;i<360*60;i++)panorama.Update(1.0/360);Check(Math.Abs(panorama.Angle-angle*2)<.0001f,"Panorama speed depends on frame rate.");
                for(int i=0;i<8;i++){Capture();panorama.Update(45);}
                Check(float.IsFinite(panorama.Angle)&&panorama.Angle<MathHelper.TwoPi,"Panorama phase is unbounded.");
            }
            GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            Exit();
        }
    }
}
