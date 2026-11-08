using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class DoorChecks
{
    private static ItemId Door=>Items.FromBlock(BlockType.Door);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void InventoryAndWorld()
    {
        Check(Door.Value==44 && Items.StackLimit(Door)==64 && Items.Get(Door).Durability==0,"Door registration changed.");
        Check(Blocks.Get(BlockType.Door).SideTile==41 && Blocks.Get(BlockType.DoorUpper).SideTile==42,"Door atlas mapping changed.");
        var inventory=new Inventory();Check(CreativeCatalog.Grant(inventory,Door,false)==1 && CreativeCatalog.Grant(inventory,Door,true)==64,"Creative Door grant failed.");
        Check(inventory[0].Count==64 && inventory[1].Count==1 && inventory[0].InstanceId==Guid.Empty,"Door stacking/metadata incorrect.");
        var mouse=new InventoryInteraction(inventory);mouse.Click(0,false);mouse.Click(9,false);mouse.Click(9,true);mouse.Click(0,false);
        Check(inventory[0].Count==32 && inventory[9].Count==32 && mouse.Cursor.IsEmpty,"Door move/split lost items.");
        inventory.SelectedSlot=0;inventory.ConsumeSelected();Check(inventory.Selected.Count==31,"Door removal failed.");
        var grid=new CraftingGrid(3);
        for(int y=0;y<3;y++)for(int x=0;x<2;x++)grid.SetSlot(y*3+x,new(Items.FromBlock(BlockType.WoodenPlanks),1));
        Check(grid.Preview.Item==Door && grid.Preview.Count==3 && grid.TryCraft(mouse) && mouse.Cursor.Count==3,"Door recipe failed.");
        Check(mouse.TryReturnCursor(),"Crafted Doors could not return to inventory.");
        var world=PigChecks.Flat();var player=new Player(new(40,1,40));Point3 cell=new(25,1,25);
        int count=inventory.Selected.Count;
        Check(BlockPlacement.TryPlace(world,inventory,cell,player.Bounds) && inventory.Selected.Count==count-1
            && world.GetBlock(25,2,25)==BlockType.DoorUpper,"Door placement/consumption failed.");
        Check(world.Toggle(cell) && world.Structure(cell).Open && world.Toggle(new(25,2,25)) && !world.Structure(cell).Open,"Door open/close failed.");
        world.Toggle(cell);
        using var stream=new MemoryStream();WorldSerializer.Write(stream,new(world,"Door regression",player.Position,0,0,inventory));stream.Position=0;
        WorldSave restored=WorldSerializer.Read(stream);
        Check(restored.World.Structure(cell).Open && restored.World.GetBlock(25,2,25)==BlockType.DoorUpper,"Placed Door state did not persist.");
        for(int i=0;i<36;i++)Check(restored.Inventory[i]==inventory[i],"Door inventory save changed contents.");
        var drops=new DroppedItems(restored.World);var mining=new MiningController();
        Check(mining.Update(restored.World,drops,new(new(25,2,25),default,1),true,4)
            && restored.World.GetBlock(25,1,25)==BlockType.Air && restored.World.GetBlock(25,2,25)==BlockType.Air
            && drops.Entities.Count==1 && drops.Entities[0].Item==Door && drops.Entities[0].Count==1,"Door breaking duplicated a half or lost its drop.");
        var geometry=new ViewModelGeometry();var temporary=new Inventory();int maximum=0;
        foreach(ItemId item in Items.Obtainable)
        {
            temporary.SetSlot(0,new(item,1));geometry.Build(ViewModelSelection.Select(GameState.Playing,temporary));maximum=Math.Max(maximum,geometry.VertexCount);
            Check(geometry.VertexCount>0 && geometry.VertexCount%3==0,"Registered item has invalid held geometry.");
        }
        temporary.SetSlot(0,new(Door,1));geometry.Build(ViewModelSelection.Select(GameState.Playing,temporary));
        Check(geometry.VertexCount==72,"Door model lost a cuboid.");Console.WriteLine($"  Door held model: {geometry.VertexCount} vertices; largest registered model: {maximum} vertices.");
    }
    public static int Run()
    {try{InventoryAndWorld();Console.WriteLine("PASS: Creative Door acquisition, stacking/movement, crafting, placement, interaction, mining and persistence.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}

    public static int RunGraphics()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".door-graphics-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var game=new DoorGame(root);game.Run();
            if(game.Failure!=null)throw new InvalidOperationException("Door acquisition/draw failed through the real game loop.",game.Failure);
            Check(game.Verified,"Door runtime regression did not finish.");
            Console.WriteLine("PASS: Actual E/browser-click/E-close Door acquisition, hotbar/held rendering, full-stack grant, Fence/Gate growth, buffer reuse and all obtainable held models.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally
        {
            Check(Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent),"Test cleanup escaped its directory.");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private sealed class DoorGame : Game1
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private readonly string _root;private int _frame;private DynamicVertexBuffer _doorBuffer,_largeBuffer;
        public Exception Failure { get; private set; }
        public bool Verified { get; private set; }
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Private).GetValue(this);
        private object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,Private).Invoke(this,args);
        private GameSession Session=>Field<GameSession>("_session");
        private FirstPersonRenderer Held=>Field<FirstPersonRenderer>("_firstPerson");
        private DynamicVertexBuffer Buffer=>(DynamicVertexBuffer)typeof(FirstPersonRenderer).GetField("_buffer",Private).GetValue(Held);
        public DoorGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {_root=root;Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Update(GameTime time) { }
        private void Input(MouseState mouse=default,params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(),Point.Zero);
        private void Press(Keys key){Input();Input(default,key);}
        private void Click(Rectangle slot,bool right=false,bool shift=false)
        {
            Input();Input(new MouseState(slot.Center.X,slot.Center.Y,0,right?ButtonState.Released:ButtonState.Pressed,ButtonState.Released,
                right?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released),shift?new[]{Keys.LeftShift}:Array.Empty<Keys>());
        }
        private void Obtain(BlockType block,bool full=false)
        {
            ItemId item=Items.FromBlock(block);int index=Array.IndexOf(CreativeCatalog.All.ToArray(),item);
            Check(index>=0 && index<InventoryLayout.BrowserRows(GraphicsDevice.Viewport)*5,"Regression item is not visible in browser.");
            Click(InventoryLayout.BrowserSlot(GraphicsDevice.Viewport,index),shift:full);
        }
        private void Capture(string name)
        {
            string path=new ScreenshotCapture(Path.Combine(_root,"screenshots")).Capture(GraphicsDevice);
            File.Copy(path,Path.Combine(AppContext.BaseDirectory,name),true);
        }
        protected override void Draw(GameTime time)
        {
            if(Failure!=null)return;
            try
            {
                Check(IsActive,"Door graphics window lost focus.");_frame++;
                switch(_frame)
                {
                    case 1:
                        Field<GameSettings>("_settings").GameMode=GameMode.Creative;
                        var world=PigChecks.Flat();world.SavedGameMode=GameMode.Creative;var save=new WorldSave(world,"Door acquisition",new(40,1,40),0,0,new Inventory());
                        string id=Field<WorldSaveManager>("_saves").Create(save);Call("BeginWorld",id,save);
                        RenderingPerformanceChecks.Warm(Field<WorldRenderer>("_renderer"),Session.Camera);Press(Keys.E);break;
                    case 2:
                        Obtain(BlockType.Door);Check(Session.Inventory.Selected.Item==Door && Session.Inventory.Selected.Count==1,"Actual Creative click did not grant Door.");
                        Click(InventoryLayout.Slot(GraphicsDevice.Viewport,0,true));Click(InventoryLayout.Slot(GraphicsDevice.Viewport,9,true));
                        Click(InventoryLayout.Slot(GraphicsDevice.Viewport,9,true),right:true);Click(InventoryLayout.Slot(GraphicsDevice.Viewport,0,true));
                        Check(Session.Inventory.Selected.Item==Door && Session.InventoryInteraction.Cursor.IsEmpty,"Door UI movement failed.");
                        Console.WriteLine("  Door acquired by Creative browser; inventory remains open and renders normally.");break;
                    case 3:
                        Press(Keys.E);Check(State==GameState.Playing,"Inventory did not close.");
                        Console.WriteLine("  Drawing the selected Door through Game1.Draw after closing inventory.");break;
                    case 4: Press(Keys.E);Obtain(BlockType.Door,true);Check(Session.Inventory[0].Count==64 && Session.Inventory[1].Count==1,"Shift grant lost Door quantities.");break;
                    case 5: Press(Keys.E);break;
                    case 6: Press(Keys.D3);Press(Keys.E);Obtain(BlockType.Fence,true);Press(Keys.E);break;
                    case 7: Press(Keys.D1);break;
                    case 8:
                        Check(ReferenceEquals(Buffer,_largeBuffer),"Switching back to Door unnecessarily recreated the larger GPU buffer.");
                        var inventory=new Inventory();
                        foreach(ItemId item in Items.Obtainable){inventory.SetSlot(0,new(item,1));Held.Draw(GameState.Playing,inventory);}
                        Check(ReferenceEquals(Buffer,_largeBuffer),"Repeated registered items unexpectedly recreated the held buffer.");
                        Press(Keys.E);Call("SetState",GameState.Paused);Call("HandleMenuAction",MenuAction.SaveAndReturn);Verified=true;Exit();return;
                }
                base.Draw(time);
                if(_frame==2)Capture("door-inventory-regression.png");
                if(_frame==3){_doorBuffer=Buffer;Check(Buffer.VertexCount>=72,"Door upload exceeds its GPU buffer.");Capture("door-held-regression.png");}
                if(_frame==6){_largeBuffer=Buffer;Check(Buffer.VertexCount>=108,"Fence upload exceeds its GPU buffer.");Check(_doorBuffer.IsDisposed,"Replaced held GPU buffer was not disposed.");}
            }
            catch(Exception e){Failure=e;Exit();}
        }
    }
}
