using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;

namespace MDSM.Testing;

internal static class DropControlGraphicsChecks
{
    public static int Run()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".drop-control-input-"+Guid.NewGuid().ToString("N"));
        try{using var game=new CheckGame(root);game.Run();CommandChecks.Check(game.Verified,"Drop key input checks did not finish.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent))Directory.Delete(root,true);}
    }
    private sealed class CheckGame : Game1
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public bool Verified {get;private set;}
        public CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Private).GetValue(this);
        private object Call(string method,params object[] args)=>typeof(Game1).GetMethod(method,Private).Invoke(this,args);
        private GameSession Session=>Field<GameSession>("_session");
        private void Input(double seconds=0,Point mouse=default,params Keys[] keys)
            =>Call("ProcessInput",new KeyboardState(keys),new MouseState(mouse.X,mouse.Y,0,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(seconds)),Point.Zero);
        private static void Check(bool ok,string message)=>CommandChecks.Check(ok,message);
        protected override void Update(GameTime time) { }
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}Check(IsActive,"Drop control test window is not focused.");
            var fixture=CommandChecks.Session();var save=fixture.Capture();var store=Field<WorldSaveManager>("_saves");Call("BeginWorld",store.Create(save),save);
            foreach(int fps in new[]{30,60,144,360})
            {
                Input();Session.Items.RemoveAll();Session.Inventory.SetSlot(0,new(Items.Apple,20));
                Input(1d/fps,keys:new[]{Keys.Q});for(int i=0;i<3;i++)Input(1d/fps,keys:new[]{Keys.Q});
                Check(Session.Inventory.Selected.Count==19 && Session.Items.Entities.Sum(i=>i.Count)==1,"Held Q repeated at "+fps+" FPS.");
                Input();Input(keys:new[]{Keys.RightShift,Keys.Q});Check(Session.Inventory.Selected.IsEmpty && Session.Items.Entities.Sum(i=>i.Count)==20,"Right Shift+Q failed to drop selected stack.");
            }
            Input();Session.Items.RemoveAll();Session.Inventory.SetSlot(0,new(Items.Coal,5));Session.Inventory.SetSlot(9,new(Items.Stick,12));
            Input(keys:new[]{Keys.E});Check(State==GameState.Inventory,"Inventory did not open.");
            Point hovered=InventoryLayout.Slot(GraphicsDevice.Viewport,9).Center;
            Input(mouse:hovered,keys:new[]{Keys.Q});Input(mouse:hovered,keys:new[]{Keys.Q});
            Check(Session.Inventory[9].Count==11 && Session.Inventory.Selected.Count==5 && Session.Items.Entities.Sum(i=>i.Count)==1,"Inventory Q targeted wrong stack or repeated.");
            Input();Input(mouse:hovered,keys:new[]{Keys.LeftShift,Keys.Q});
            Check(Session.Inventory[9].IsEmpty && Session.Items.Entities.Sum(i=>i.Count)==12 && State==GameState.Inventory,"Left Shift+Q failed or closed inventory.");
            Session.Inventory.SetSlot(1,new(Items.Apple,4));Session.InventoryInteraction.Click(1,false);Input();
            Input(mouse:InventoryLayout.Slot(GraphicsDevice.Viewport,0).Center,keys:new[]{Keys.Q});
            Check(Session.InventoryInteraction.Cursor.Count==3 && Session.Inventory.Selected.Count==5 && Session.Items.Entities[^1].Item==Items.Apple,"Cursor did not take Q priority.");
            Input();Input(keys:new[]{Keys.RightShift,Keys.Q});Check(Session.InventoryInteraction.Cursor.IsEmpty && Session.Items.Entities[^1].Count==3,"Cursor Shift+Q failed.");
            base.Draw(new GameTime());
            Input();Input(keys:new[]{Keys.Escape});Check(State==GameState.Playing,"Closing inventory failed.");
            Input();Input(keys:new[]{Keys.T});Input(keys:new[]{Keys.Q});Call("TextInput",'q');
            Check(State==GameState.Command && Field<CommandBar>("_commandBar").Input.Text=="q" && Session.Inventory.Selected.Count==5,"Q in command text dropped a gameplay item.");
            Input();Input(keys:new[]{Keys.Escape});Call("SetState",GameState.Paused);Input(keys:new[]{Keys.Q});Check(Session.Inventory.Selected.Count==5,"Paused Q dropped an item.");
            Call("SetState",GameState.Playing);Input();Session.Inventory.SetSlot(0,new(Items.RawPorkchop,1));Input(keys:new[]{Keys.Q});Check(Session.Inventory.Selected.IsEmpty && Session.Items.Entities[^1].Count==1,"Q failed for a nonstackable item.");
            Call("SaveCurrentWorld");var loaded=store.Load(Session.Id);Check(loaded.Inventory.Selected.IsEmpty && loaded.DroppedItems.Sum(i=>i.Count)==Session.Items.Entities.Sum(i=>i.Count),"Actual Q save lost item quantities.");
            Call("HandleMenuAction",MenuAction.SaveAndReturn);Verified=true;Exit();
            Console.WriteLine("PASS: Real Game1 Q/Left+Right Shift+Q routes at 30/60/144/360 FPS, held-key suppression, hovered/cursor priorities, inventory staying open, paused/command isolation, nonstackable items, rendering and saved accounting (synthetic input).");
        }
    }
}
