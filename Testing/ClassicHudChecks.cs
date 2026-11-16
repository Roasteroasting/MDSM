using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.UI;
using MDSM.Core;
using MDSM.Inventories;
using MDSM.Rendering;
using MDSM.Worlds;
namespace MDSM.Testing;
internal static class ClassicHudChecks
{
    internal static void Layout()
    {
        SurvivalChecks.HealthAndHearts();
        foreach(var size in new[]{new Point(320,240),new(854,480),new(1280,720),new(1920,1080)})
        {
            var v=new Viewport(0,0,size.X,size.Y);int s=ClassicHudLayout.Scale(v);var b=ClassicHudLayout.Bar(v);var hearts=HealthHud.Bounds(v);var armor=ClassicHudLayout.Armor(v);
            Check(b.Width==182*s&&b.Height==22*s&&b.Bottom==v.Height&&Math.Abs(b.Center.X-v.Width/2)<=1,"Hotbar proportions/anchor differ from Beta.");
            Check(hearts.Y==armor.Y&&hearts.Right<armor.Left&&armor.Right<b.Right,"Health/armor do not share separate left/right halves.");
            for(int i=0;i<9;i++)Check(b.Contains(InventoryLayout.HotbarSlot(v,i))&&ClassicHudLayout.Selection(v,i).Width==24*s,"Slot or selection layout escaped the strip.");
            Check(BreathHud.Bounds(v,30).Bottom<armor.Y-10*s,"Breath overlaps the additional armor row.");
        }
    }
    private static void Check(bool b,string m){if(!b)throw new Exception(m);}
    internal static int Run(){try{Layout();PresentationChecks.ViewModels();using var game=new Preview();game.Run();Check(game.Passed,"HUD preview did not finish.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        private readonly GraphicsDeviceManager _graphics;internal bool Passed;
        internal Preview(){_graphics=new(this){PreferredBackBufferWidth=854,PreferredBackBufferHeight=480};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            var atlas=new TextureAtlas(Content);using var hud=new Hud(GraphicsDevice,atlas);using var hand=new FirstPersonRenderer(GraphicsDevice,atlas);var inventory=new Inventory();
            for(int i=0;i<4;i++)inventory.SetEquipment(i,new ItemStack(Items.Armor(ArmorMaterial.Iron,(ArmorSlot)i),1));
            var world=new World(173,WorldSize.Small,false);Color sky=new(130,160,220);
            foreach(var size in new[]{new Point(320,240),new(854,480),new(1280,720)})
            {
                using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(sky);var v=GraphicsDevice.Viewport;
                hand.Draw(GameState.Playing,inventory);
                hud.Draw(v,inventory,true,false,Vector3.Zero,world,new GameTime(),health:19);
                GraphicsDevice.SetRenderTarget(null);var pixels=new Color[size.X*size.Y];target.GetData(pixels);Color center=pixels[size.Y/2*size.X+size.X/2];
                Check(Math.Abs(center.R-(255-sky.R))<=1&&Math.Abs(center.G-(255-sky.G))<=1,"Crosshair does not invert background colors.");
                var slot=InventoryLayout.HotbarSlot(v,4);Color inner=pixels[(slot.Y+slot.Height/2)*size.X+slot.X+slot.Width/2];Check(inner!=sky&&inner.B>10,"Hotbar interior lost alpha/background tint.");
                using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"classic-hud-{size.X}.png"));target.SaveAsPng(file,size.X,size.Y);
            }
            Passed=true;Console.WriteLine("PASS: Classic HUD sizes, hearts/armor/breath, selection, inverted crosshair, translucent slots and real hand previews.");Exit();
        }
    }
}
