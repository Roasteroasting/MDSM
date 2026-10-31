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

internal static class ProgressionGraphicsChecks
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Verify(Game1 game)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(Game1).GetField(name,flags).GetValue(game);
        object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,flags).Invoke(game,args);
        GameSession Session()=>Field<GameSession>("_session");
        GraphicsDevice device=game.GraphicsDevice;TextureAtlas atlas=Field<TextureAtlas>("_atlas");
        void RestoreTexture(){device.Textures[0]=null;device.Textures[0]=atlas.Texture;}
        Color[] Read(RenderTarget2D target,string file=null)
        {device.SetRenderTarget(null);var pixels=new Color[target.Width*target.Height];target.GetData(pixels);if(file!=null){using var png=File.Create(Path.Combine(AppContext.BaseDirectory,file));target.SaveAsPng(png,target.Width,target.Height);}RestoreTexture();return pixels;}
        using(var target=new RenderTarget2D(device,640,360,false,SurfaceFormat.Color,DepthFormat.Depth24))
        using(var sprites=new SpriteBatch(device))
        using(var pixel=new Texture2D(device,1,1))
        {
            pixel.SetData(new[]{Color.White});device.SetRenderTarget(target);device.Clear(new Color(35,38,44));
            sprites.Begin(samplerState:SamplerState.PointClamp);
            for(int i=0;i<25;i++)
            {
                ItemId item=new((ushort)(259+i));int durability=Items.Get(item).Durability;
                ItemStack stack=durability>0?new(item,1,Math.Max(1,durability/2)):new(item,12);
                ItemIcons.Draw(sprites,atlas,pixel,stack,new Rectangle(8+i%8*76,8+i/8*80,60,60));
            }
            sprites.End();Color[] icons=Read(target,"survival-icons.png");
            Require(icons.Count(c=>c!=new Color(35,38,44))>4000,"New icons/durability bars missing.");
        }
        using(var target=new RenderTarget2D(device,320,240,false,SurfaceFormat.Color,DepthFormat.Depth24))
        {
            Color[] previous=null;
            for(int i=0;i<25;i++)
            {
                var inventory=new Inventory();ItemId item=new((ushort)(259+i));inventory.SetSlot(0,new(item,1));
                device.SetRenderTarget(target);device.Clear(Color.CornflowerBlue);Field<FirstPersonRenderer>("_firstPerson").Draw(GameState.Playing,inventory);
                Color[] pixels=Read(target,item==Items.Bow || item==Items.Tool(ToolTier.Diamond,ToolKind.Pickaxe)?$"held-survival-{item.Value}.png":null);
                Require(pixels.Count(c=>c!=Color.CornflowerBlue)>10 && (previous==null || !pixels.AsSpan().SequenceEqual(previous)),"New held item missing or atlas UV unchanged.");previous=pixels;
            }
        }
        var modelWorld=new World(173,WorldSize.Small,false);
        BlockType[] blocks={BlockType.Glass,BlockType.IronOre,BlockType.CoalOre,BlockType.DiamondOre,BlockType.Furnace,BlockType.Chest};
        for(int i=0;i<blocks.Length;i++)modelWorld.SetBlock(4+i*2,1,8,blocks[i]);
        var camera=new Camera{Position=new Vector3(9,3,2)};camera.RestoreOrientation(MathHelper.Pi,-.2f);
        using(var target=new RenderTarget2D(device,640,360,false,SurfaceFormat.Color,DepthFormat.Depth24))
        using(var renderer=new WorldRenderer(device,atlas,modelWorld))
        {
            device.SetRenderTarget(target);RenderingPerformanceChecks.Warm(renderer,camera);device.Clear(WorldRenderer.SkyColor);renderer.Draw(camera,null);
            Vector3 hole=device.Viewport.Project(new Vector3(4.5f,1.5f,8),camera.Projection(640f/360),camera.View,Matrix.Identity);
            Color[] models=Read(target,"survival-blocks.png");
            Require(models.Count(c=>c!=WorldRenderer.SkyColor)>1000 && models[(int)hole.Y*640+(int)hole.X]==WorldRenderer.SkyColor,"Block textures or transparent glass center failed.");
            var arrows=new Projectiles(modelWorld,new[]{new ArrowState(new Vector3(9,2,6),Vector3.Right*10,.2f,3,Guid.NewGuid())});
            device.SetRenderTarget(target);device.Clear(WorldRenderer.SkyColor);renderer.Draw(camera,null);
            Texture before=device.Textures[0];BlendState blend=device.BlendState;DepthStencilState depth=device.DepthStencilState;
            Field<ProjectileRenderer>("_projectileRenderer").Draw(camera,arrows);
            Require(device.Textures[0]==before && device.BlendState==blend && device.DepthStencilState==depth,"Projectile render leaked graphics state.");
            Color[] arrowPixels=Read(target,"survival-arrow.png");Require(!arrowPixels.AsSpan().SequenceEqual(models),"Arrow projectile did not render.");
        }
        void Input(double seconds=0,MouseState mouse=default,params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(seconds)),Point.Zero);
        void Press(Keys key){Input();Input(keys:new[]{key});}
        void Click(Rectangle bounds,bool right=false)
        {
            Input();Input(mouse:new MouseState(bounds.Center.X,bounds.Center.Y,0,right?ButtonState.Released:ButtonState.Pressed,
                ButtonState.Released,right?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released));
        }
        World world=PigChecks.Flat();var chestCell=new Point3(40,1,38);var furnaceCell=new Point3(42,1,38);
        world.SetBlock(40,1,38,BlockType.Chest);world.SetBlock(42,1,38,BlockType.Furnace);
        var inv=new Inventory();var tool=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Sword),1,47);inv.SetSlot(0,tool);
        inv.SetSlot(1,new(Items.FromBlock(BlockType.IronOre),3));inv.SetSlot(2,new(Items.Coal,2));
        var save=new WorldSave(world,"Survival UI",new Vector3(40.5f,1,40.5f),0,-.5f,inv);
        var store=Field<WorldSaveManager>("_saves");string id=store.Create(save);Call("BeginWorld",id,save);RenderingPerformanceChecks.Warm(Field<WorldRenderer>("_renderer"),Session().Camera);
        Click(new Rectangle(0,0,1,1),true);Require(game.State==GameState.Inventory && Session().ActiveContainer?.Block==BlockType.Chest,"RMB did not open chest.");
        Click(InventoryLayout.Slot(device.Viewport,0));Click(InventoryLayout.ContainerSlot(device.Viewport,BlockType.Chest,26));
        Require(world.Containers.Get(chestCell)[26]==tool,"Chest UI lost damaged equipment.");Press(Keys.Escape);
        Vector3 delta=new Vector3(42.5f,1.5f,38.5f)-Session().Camera.Position;
        Session().Camera.RestoreOrientation(MathF.Atan2(delta.X,-delta.Z),MathF.Atan2(delta.Y,new Vector2(delta.X,delta.Z).Length()));
        Click(new Rectangle(0,0,1,1),true);Require(Session().ActiveContainer?.Block==BlockType.Furnace,"RMB did not open furnace.");
        Click(InventoryLayout.Slot(device.Viewport,1));Click(InventoryLayout.ContainerSlot(device.Viewport,BlockType.Furnace,0));
        Click(InventoryLayout.Slot(device.Viewport,2));Click(InventoryLayout.ContainerSlot(device.Viewport,BlockType.Furnace,1));
        Input(10);var furnace=world.Containers.Get(furnaceCell);
        Require(furnace[2].Item==Items.IronIngot && furnace[2].Count==1 && furnace[0].Count==2,"Observed furnace did not process while UI was open.");
        using(var target=new RenderTarget2D(device,320,240,false,SurfaceFormat.Color,DepthFormat.Depth24))
        {
            device.SetRenderTarget(target);Field<InventoryScreen>("_inventoryScreen").Draw(Session(),default,device.Viewport);Read(target,"furnace-interface.png");
            Require(Session().TryReturnInterfaceItems(),"Could not return furnace cursor.");Session().TryOpenContainer(new BlockHit(chestCell,default,3));
            device.SetRenderTarget(target);Field<InventoryScreen>("_inventoryScreen").Draw(Session(),default,device.Viewport);Read(target,"chest-interface.png");
        }
        Call("SetState",GameState.Paused);double burn=furnace.Burn;Input(30);Require(furnace.Burn==burn,"Real pause advanced furnace.");
        Call("HandleMenuAction",MenuAction.SaveAndReturn);Require(game.State==GameState.MainMenu,"Survival save/return failed.");
        WorldSave loaded=store.Load(id);Require(loaded.World.Containers.Get(chestCell)[26]==tool && loaded.World.Containers.Get(furnaceCell)[2].Count==1,"Container UI save/load changed contents.");
        Console.WriteLine("PASS: Survival GPU icons/held sprites/bars, ore/container textures, clear glass, projectile/state, real chest/furnace input, observed smelting, pause and persistence.");
    }
}
