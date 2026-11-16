using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;
namespace MDSM.Testing;
internal static class MenuUpdateChecks
{
    internal static void Cpu()
    {
        CraftingChecks.Layout();CraftingChecks.Transactions();ProgressionChecks.Containers();
        var world=PigChecks.Flat();var a=new Point3(15,1,12);var b=new Point3(16,1,12);var body=new Player(new(30,1,30)).Bounds;
        Check(world.TryPlace(a,BlockType.Chest,body)&&world.TryPlace(b,BlockType.Chest,body),"Chest pair placement failed.");
        var first=world.Containers.Get(a);var second=world.Containers.Get(b);first.SetSlot(0,new(Items.IronIngot,7));second.SetSlot(26,new(Items.Stick,19));
        var combined=world.Containers.View(a);Check(combined.SlotCount==54&&ReferenceEquals(combined,world.Containers.View(b))&&combined[53].Count==19,"Both halves do not expose the same 54-slot view.");
        var interaction=new InventoryInteraction(new Inventory());combined.Click(53,interaction,false);Check(second[26].IsEmpty&&interaction.Cursor.Count==19,"Combined transfers do not write through.");combined.Click(27,interaction,false);Check(second[0].Count==19,"Stack placement was lost in the second half.");
        Check(!world.TryPlace(new(17,1,12),BlockType.Chest,body),"Triple chest placement accepted.");
        using var data=new MemoryStream();WorldSerializer.Write(data,new WorldSave(world,"Double chest",new(30,1,30),0,0));data.Position=0;var restored=WorldSerializer.Read(data).World;
        Check(restored.Containers.View(a).SlotCount==54&&restored.Containers.View(b)[27].Count==19,"Double chest save/load lost independent halves.");
        var drops=new MDSM.Entities.DroppedItems(restored);Check(new MiningController().Update(restored,drops,new(a,default,1),true,100,new Inventory()),"Mining a double-chest half failed.");
        Check(drops.Entities.Where(i=>i.Item==Items.IronIngot).Sum(i=>i.Count)==7&&drops.Entities.Sum(i=>i.Count)==8&&restored.Containers.View(b).SlotCount==27&&restored.Containers.Get(b)[0].Count==19,"Breaking one half lost or copied the other's inventory.");
        Check(restored.TryPlace(a,BlockType.Chest,body)&&restored.Containers.View(a)[27].Count==19,"Rejoining an empty chest lost the surviving half's items.");
        foreach(var size in new[]{new Point(320,240),new(854,480),new(1280,720)})
        {
            var v=new Viewport(0,0,size.X,size.Y);foreach(int rows in new[]{3,6}){var panel=InventoryLayout.Panel(v,false,rows);Check(v.Bounds.Contains(panel),"Chest panel leaves viewport.");for(int i=0;i<rows*9;i++)Check(panel.Contains(InventoryLayout.ContainerSlot(v,BlockType.Chest,i,false,rows)),"Chest slot leaves panel.");for(int i=0;i<36;i++)Check(panel.Contains(InventoryLayout.Slot(v,i,false,rows)),"Player storage leaves double chest panel.");}
        }
    }
    private static void Check(bool b,string message){if(!b)throw new Exception(message);}
    internal static int Run(){try{Cpu();using var g=new Preview();g.Run();Check(g.Passed,"Menu previews did not complete.");Console.WriteLine("PASS: Reference menu input/layouts, 54-slot write-through chests and persistence, and single-application hand/item bob.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        internal bool Passed;private readonly GraphicsDeviceManager _graphics;
        internal Preview(){_graphics=new(this){PreferredBackBufferWidth=1280,PreferredBackBufferHeight=720};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            var atlas=new TextureAtlas(Content);using var screen=new InventoryScreen(GraphicsDevice,atlas);var world=PigChecks.Flat();var session=new GameSession("preview",new(world,"Menu preview",new(30,1,30),0,0));
            CreativeDeletionChecks.Graphics(GraphicsDevice,atlas);
            foreach(var size in new[]{new Point(320,240),new(1280,720)})foreach(string mode in new[]{"inventory","crafting","chest","double","furnace","creative-items","creative-inventory"})
            {
                session.CloseInterface();session.TryOpenCrafting(mode=="crafting"?3:2);session.ApplySettings(new GameSettings{GameMode=mode.StartsWith("creative")?GameMode.Creative:GameMode.Survival});screen.Open();
                if(mode is "chest" or "double" or "furnace")
                {var cell=new Point3(15,1,12);world.Containers.Detach(cell);world.SetBlock(cell.X,cell.Y,cell.Z,mode=="furnace"?BlockType.Furnace:BlockType.Chest);world.Containers.Detach(new(16,1,12));world.SetBlock(16,1,12,mode=="double"?BlockType.Chest:BlockType.Air);session.TryOpenContainer(new BlockHit(cell,cell,1));}
                using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);var v=GraphicsDevice.Viewport;
                if(mode=="creative-inventory"){var tab=InventoryLayout.CreativeTab(v,false);screen.Update(session,new Microsoft.Xna.Framework.Input.MouseState(tab.Center.X,tab.Center.Y,0,Microsoft.Xna.Framework.Input.ButtonState.Pressed,0,0,0,0),default,v);}
                void Click(Rectangle area)=>screen.Update(session,new Microsoft.Xna.Framework.Input.MouseState(area.Center.X,area.Center.Y,0,Microsoft.Xna.Framework.Input.ButtonState.Pressed,0,0,0,0),default,v);
                if(mode=="creative-items")
                {
                    int total=Enumerable.Range(0,36).Sum(i=>session.Inventory[i].Count);Click(InventoryLayout.BrowserSlot(v,0));Check(Enumerable.Range(0,36).Sum(i=>session.Inventory[i].Count)==total+1,"Creative tab catalog click did not grant exactly one real item.");
                }
                if(mode=="double")
                {
                    session.ActiveContainer.SetSlot(53,new(Items.Stick,17));Click(InventoryLayout.ContainerSlot(v,BlockType.Chest,53,false,6));Check(session.InventoryInteraction.Cursor.Count==17&&session.ActiveContainer[53].IsEmpty,"Double chest second-half mouse hitbox/transfer failed.");
                    session.TryReturnInterfaceItems();
                }
                screen.Draw(session,default,v);GraphicsDevice.SetRenderTarget(null);using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"menu-{mode}-{size.X}.png"));target.SaveAsPng(file,size.X,size.Y);
            }
            using var hand=new FirstPersonRenderer(GraphicsDevice,atlas);var inventory=new Inventory();var bob=Matrix.CreateRotationZ(.02f)*Matrix.CreateTranslation(.03f,-.04f,0);var swing=Matrix.CreateRotationX(.1f);var equip=Matrix.CreateTranslation(0,-.1f,0);
            foreach(bool occupied in new[]{false,true})
            {inventory.SetSlot(0,occupied?new(Items.FromBlock(BlockType.Stone),1):default);hand.Draw(GameState.Playing,inventory,1,swing,bob,equip);var effect=(AlphaTestEffect)typeof(FirstPersonRenderer).GetField("_effect",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(hand);Check(effect.View==bob,"Bob cancelled or applied to both model and view.");Matrix model=effect.World;hand.Draw(GameState.Playing,inventory,1,swing,Matrix.Identity,equip);Check(effect.View==Matrix.Identity&&effect.World==model,"Disabling bob lost independent swing/equip or accumulated transforms.");}
            Passed=true;Exit();
        }
    }
}
