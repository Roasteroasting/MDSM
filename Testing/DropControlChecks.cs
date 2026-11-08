using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class DropControlChecks
{
    internal static readonly (string Name,Action Check)[] All =
    {
        ("Q selected/cursor one-item and full-stack drops preserve quantities and equipment identity", Quantities),
        ("Inventory Q current-layout targets, equipment, crafting and container stacks", Hovered),
        ("Q cannot drop virtual recipe/browser items or consume blocked/empty/dead sources", Safety),
        ("Q item transfers survive save/load with exact metadata and pickup delays", Persistence)
    };
    private static void Check(bool ok,string message)=>CommandChecks.Check(ok,message);
    public static void Quantities()
    {
        foreach(GameMode mode in Enum.GetValues<GameMode>())
        {
            var session=CommandChecks.Session();session.ApplySettings(new(){GameMode=mode});
            session.Inventory.SetSlot(0,new(Items.Stick,27));
            Check(session.DropSelected() && session.Inventory.Selected.Count==26 && session.Items.Entities.Single().Count==1,"Q did not drop exactly one selected item.");
            Check(session.DropSelected(true) && session.Inventory.Selected.IsEmpty && session.Items.Entities.Sum(i=>i.Count)==27,"Shift+Q did not drop the remaining full stack.");
            var tool=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),1,37);
            session.Inventory.SetSlot(0,tool);Check(session.DropSelected(true) && session.Items.Entities[^1].Stack==tool,"Dropping a tool reset its identity/durability.");
            session.Inventory.SetSlot(1,new(Items.Apple,7));session.InventoryInteraction.Click(1,false);
            Check(session.DropCursor(false) && session.InventoryInteraction.Cursor.Count==6 && session.Items.Entities[^1].Count==1,"Cursor Q did not drop exactly one.");
            Check(session.InventoryInteraction.TryReturnCursor() && session.Inventory[1].Count==6,"Partial cursor drop lost the original return slot.");
            session.InventoryInteraction.Click(1,false);Check(session.DropCursor(true) && session.InventoryInteraction.Cursor.IsEmpty && session.Items.Entities[^1].Count==6,"Cursor Shift+Q did not drop the full remainder.");
            Check(session.Items.Entities.All(i=>i.PickupDelay==1),"Keyboard drops omitted the deliberate pickup delay.");
        }
        SurvivalChecks.ManualDropAndDelay(); // Existing outside-click full-stack drop behavior stays intact.
    }
    public static void Hovered()
    {
        foreach(GameMode mode in Enum.GetValues<GameMode>())foreach(Point size in new[]{new Point(320,240),new(800,600),new(1280,720)})
        {
            var session=CommandChecks.Session();session.ApplySettings(new(){GameMode=mode});var viewport=new Viewport(0,0,size.X,size.Y);bool creative=mode==GameMode.Creative;
            session.Inventory.SetSlot(0,new(Items.Coal,10));session.Inventory.SetSlot(12,new(Items.Apple,9));
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.Slot(viewport,12,creative).Center,viewport,false)==true
                && session.Inventory[12].Count==8 && session.Inventory.Selected.Count==10 && session.Items.Entities[^1].Item==Items.Apple,"Q used the selected/old slot instead of hovered slot.");
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.Slot(viewport,12,creative).Center,viewport,true)==true && session.Inventory[12].IsEmpty,"Hovered Shift+Q did not empty its slot.");
            var armor=new ItemStack(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Helmet),1,12);session.Inventory.SetEquipment(0,armor);
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.Equipment(viewport,0,creative).Center,viewport,false)==true && session.Inventory.Equipped(0).IsEmpty && session.Items.Entities[^1].Stack==armor,"Armor drop lost metadata or equipment accounting.");
            session.Crafting.SetSlot(0,new(Items.Stick,3));
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.CraftingSlot(viewport,2,0,creative).Center,viewport,false)==true && session.Crafting[0].Count==2,"Crafting input Q failed.");
            session.Inventory.SetSlot(1,new(Items.Leather,5));session.InventoryInteraction.Click(1,false);
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.Slot(viewport,0,creative).Center,viewport,false)==true && session.InventoryInteraction.Cursor.Count==4 && session.Inventory[0].Count==10,"Cursor did not take precedence over hovered slot.");
            Check(session.InventoryInteraction.TryReturnCursor(),"Cursor remainder could not return.");
            Point3 cell=new(20,1,20);session.World.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Chest);Check(session.TryOpenContainer(new(cell,default,1)),"Chest fixture failed.");
            session.ActiveContainer.SetSlot(2,new(Items.Leather,14));
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.ContainerSlot(viewport,BlockType.Chest,2,creative).Center,viewport,true)==true && session.ActiveContainer[2].IsEmpty,"Chest stack Q failed.");
            cell=new(22,1,20);session.World.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Furnace);Check(session.TryOpenContainer(new(cell,default,1)),"Furnace fixture failed.");
            session.ActiveContainer.SetSlot(2,new(Items.IronIngot,7));
            Check(InventoryScreen.TryDropHovered(session,InventoryLayout.ContainerSlot(viewport,BlockType.Furnace,2,creative).Center,viewport,false)==true && session.ActiveContainer[2].Count==6,"Real furnace output Q failed.");
        }
    }
    public static void Safety()
    {
        var session=CommandChecks.Session();var viewport=new Viewport(0,0,1280,720);
        for(int i=0;i<4;i++)session.Crafting.SetSlot(i,new(Items.FromBlock(BlockType.WoodenPlanks),2));
        var preview=session.Crafting.Preview;
        Check(!preview.IsEmpty && InventoryScreen.TryDropHovered(session,InventoryLayout.Output(viewport,2).Center,viewport,false)==null
            && session.Crafting.Preview==preview && session.Crafting[0].Count==2 && session.Items.Entities.Count==0,"Q created a preview item without crafting.");
        session.ApplySettings(new(){GameMode=GameMode.Creative});
        Check(InventoryScreen.TryDropHovered(session,InventoryLayout.BrowserSlot(viewport,0).Center,viewport,true)==null && session.Items.Entities.Count==0,"Q spawned a virtual Creative catalog item.");
        Check(InventoryScreen.TryDropHovered(session,new Point(0,0),viewport,true)==null && !session.DropSelected(),"Empty/outside drop generated an item.");
        session.Inventory.SetSlot(0,new(Items.Stick,8));
        for(int x=37;x<=44;x++)for(int y=1;y<=5;y++)for(int z=37;z<=44;z++)session.World.SetBlock(x,y,z,BlockType.Stone);
        Check(!session.DropSelected() && session.Inventory.Selected.Count==8 && session.Items.Entities.Count==0,"Failed drop consumed inventory.");
        session.InventoryInteraction.Click(0,false);Check(!session.DropCursor(false) && session.InventoryInteraction.Cursor.Count==8,"Failed cursor Q lost items.");
        session.Player.Kill();Check(!session.DropCursor() && session.InventoryInteraction.Cursor.Count==8,"Dead player keyboard drop mutated held items.");
    }
    public static void Persistence()
    {
        var session=CommandChecks.Session();var tool=new ItemStack(Items.Tool(ToolTier.Diamond,ToolKind.Axe),1,123);
        session.Inventory.SetSlot(0,tool);session.Inventory.SetSlot(9,new(Items.Apple,17));Check(session.DropSelected(),"Tool persistence drop failed.");
        var viewport=new Viewport(0,0,1280,720);Check(InventoryScreen.TryDropHovered(session,InventoryLayout.Slot(viewport,9).Center,viewport,false)==true,"Hovered persistence drop failed.");
        using var data=new MemoryStream();WorldSerializer.Write(data,session.Capture());data.Position=0;var loaded=new GameSession("loaded",WorldSerializer.Read(data));
        Check(loaded.Inventory.Selected.IsEmpty && loaded.Inventory[9].Count==16 && loaded.Items.Entities.Count==2 && loaded.Items.Entities[0].Stack==tool
            && loaded.Items.Entities[1].Item==Items.Apple && loaded.Items.Entities[1].Count==1 && loaded.Items.Entities.All(i=>i.PickupDelay==1),"Keyboard drop save/load changed quantities, durability, identity or pickup delay.");
    }
    public static int Run()
    {
        int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failures}/{All.Length} drop-control checks passed.");return failures==0?0:1;
    }
}
