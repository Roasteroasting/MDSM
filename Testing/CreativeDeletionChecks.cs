using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
namespace MDSM.Testing;
internal static class CreativeDeletionChecks
{
    internal static void Graphics(GraphicsDevice device,TextureAtlas atlas)
    {
        using var screen=new InventoryScreen(device,atlas);
        foreach(var size in new[]{new Point(320,240),new(854,480),new(1280,720)})
        {
            var v=new Viewport(0,0,size.X,size.Y);var world=PigChecks.Flat();
            var session=new GameSession("delete-check",new(world,"Creative deletion",new(30,1,30),0,0));
            session.ApplySettings(new GameSettings{GameMode=GameMode.Creative});screen.Open();
            var interaction=session.InventoryInteraction;
            int Total()=>Enumerable.Range(0,36).Sum(i=>session.Inventory[i].Count);
            MouseState Mouse(Rectangle r,bool right=false)=>new(r.Center.X,r.Center.Y,0,right?ButtonState.Released:ButtonState.Pressed,ButtonState.Released,right?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released);
            void Click(Rectangle r,bool right=false,bool shift=false)=>screen.Update(session,Mouse(r,right),default,v,shift);
            for(int kind=0;kind<3;kind++)
            {
                session.Inventory.SetSlot(35,new(Items.Coal,13));interaction.Click(35,false);
                int before=Total();Click(InventoryLayout.BrowserSlot(v,0),kind==1,kind==2);
                Check(interaction.Cursor.IsEmpty&&Total()==before,"Catalog deletion granted or returned items.");
                Check(interaction.TryReturnCursor()&&session.Inventory[35].IsEmpty&&session.Items.Entities.Count==0,"Deleted cursor was resurrected or dropped.");
                var held=Mouse(InventoryLayout.BrowserSlot(v,0),kind==1);screen.Update(session,held,held,v);
                Check(Total()==before,"Held catalog mouse button granted an item after deletion.");
            }
            int total=Total();Click(InventoryLayout.BrowserSlot(v,0));Check(Total()==total+1,"Empty-cursor catalog grant changed.");
            Click(InventoryLayout.CreativeTab(v,false));var trash=InventoryLayout.CreativeDelete(v);
            Check(InventoryLayout.Panel(v,true).Contains(trash)&&trash.Left>InventoryLayout.Slot(v,8,true).Right&&trash.Height==InventoryLayout.Slot(v,8,true).Height,"Trash layout overlaps hotbar or leaves panel.");
            foreach(bool right in new[]{false,true})
            {
                Check(interaction.TryReceive(new(Items.Stick,64)),"Cursor fixture failed.");total=Total();Click(trash,right);
                Check(interaction.Cursor.IsEmpty&&Total()==total&&session.Items.Entities.Count==0,"Trash did not delete the entire cursor stack only.");
            }
            var tool=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),1);
            Check(interaction.TryReceive(tool),"Tool cursor fixture failed.");Click(trash);
            Check(interaction.Cursor.IsEmpty&&interaction.TryReturnCursor(),"Tool metadata survived deletion.");
            total=Total();Click(trash);Check(Total()==total,"Empty trash click changed inventory.");
            Click(InventoryLayout.CreativeTab(v,true));
            for(int slot=0;slot<36;slot++)session.Inventory.SetSlot(slot,new(Items.Coal,64));
            Check(interaction.TryReceive(new(Items.Stick,32)),"Full-inventory cursor fixture failed.");
            total=Total();Click(InventoryLayout.BrowserSlot(v,0));
            Check(interaction.Cursor.IsEmpty&&Total()==total&&session.Items.Entities.Count==0,"Catalog deletion failed with a full inventory.");
            Click(InventoryLayout.CreativeTab(v,false));
            Check(interaction.TryReceive(new(Items.Coal,7)),"Survival fixture failed.");session.ApplySettings(new GameSettings{GameMode=GameMode.Survival});
            Click(trash);Check(Total()+interaction.Cursor.Count+session.Items.Entities.Sum(i=>i.Count)+Enumerable.Range(0,session.Crafting.SlotCount).Sum(i=>session.Crafting[i].Count)==total+7,"Creative trash deleted items in Survival.");
        }
        Console.WriteLine("PASS: Creative catalog/trash deletion, whole stacks/tools, no grants/drops/resurrection, empty cursor, held click, survival protection and responsive hitboxes.");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
