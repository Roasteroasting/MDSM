using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Inventories;
using MDSM.Rendering;
using MDSM.Worlds;
namespace MDSM.Testing;
internal static class HeldItemChecks
{
    internal static int Run(){try{using var game=new Preview();game.Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        internal Preview(){_ = new GraphicsDeviceManager(this){PreferredBackBufferWidth=1280,PreferredBackBufferHeight=720};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            PresentationChecks.ViewModels();
            using var atlas=new TextureAtlas(Content);using var renderer=new FirstPersonRenderer(GraphicsDevice,atlas);var inventory=new Inventory();
            var sprites=new HeldSpriteMeshes(GraphicsDevice,atlas.ItemAtlas);
            foreach(var item in Items.Obtainable)
            {
                inventory.SetSlot(0,new(item,1));var selection=ViewModelSelection.Select(GameState.Playing,inventory);
                var pose=HeldItemPose.World(selection,Matrix.Identity,Matrix.Identity);
                // Reflection changes handedness around the same camera-space
                // anchor, rather than mirroring the final screen placement.
                Vector3 anchor=pose.Translation;
                bool tool=selection.Kind==ViewModelKind.Sprite&&Items.Get(item).Tool!=ToolKind.None;
                if((pose.Determinant()>0)!=tool||anchor.X<=0)throw new Exception("Held representation has incorrect handedness or anchor.");
                if(item==Items.Coal&&Vector3.Distance(anchor,new(1.04f,-.55f,-1))>.00001f)
                    throw new Exception("Mirroring moved the loose-item screen anchor.");
                if(selection.Kind==ViewModelKind.Sprite&&!Items.TryGetBlock(item,out _))
                {
                    var mesh=sprites.Get(item);var facing=HeldItemPose.SpriteFacing(sprites.FacingVertices(item),pose);
                    var corrected=HeldItemPose.World(selection,Matrix.Identity,Matrix.Identity,facing);
                    foreach(var size in new[]{new Point(320,240),new(1280,720),new(3440,1440),new(3440,1369)})
                    {
                        var v=new Viewport(0,0,size.X,size.Y);
                        var projection=Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(70),v.AspectRatio,.05f,10);
                        float projectedSum=v.Width-facing.M31*projection.M11*v.Width*.5f;

                        for(int i=12;i<mesh.Length;i++)
                        {
                            var a=v.Project(mesh[i].Position,projection,Matrix.Identity,pose);var b=v.Project(mesh[i].Position,projection,Matrix.Identity,corrected);
                            if(Math.Abs(a.X+b.X-projectedSum)>.02f||Math.Abs(a.Y-b.Y)>.02f||Math.Abs(a.Z-b.Z)>.00001f)
                                throw new Exception("Item was not mirrored around its visible center with height/depth preserved.");
                        }
                    }
                }
                if(selection.Kind==ViewModelKind.Sprite)
                {
                    var mesh=sprites.Get(item);
                    if(!ReferenceEquals(mesh,sprites.Get(item))||mesh.Length<=12||mesh.Length%6!=0||mesh.Length>6168
                        ||mesh.Any(v=>!float.IsFinite(v.Position.LengthSquared())||v.TextureCoordinate.X<0||v.TextureCoordinate.X>1||v.TextureCoordinate.Y<0||v.TextureCoordinate.Y>1)
                        ||mesh.Max(v=>v.Position.Z)-mesh.Min(v=>v.Position.Z)<.05f)throw new Exception("Sprite lost cached pixel extrusion/valid UVs.");
                }
                GraphicsDevice.Clear(Color.CornflowerBlue);renderer.Draw(GameState.Playing,inventory);
                if(Items.TryGetBlock(item,out _))
                {
                    var effect=(AlphaTestEffect)typeof(FirstPersonRenderer).GetField("_effect",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
                    if(effect.World!=pose)throw new Exception("Held-block pose was changed by the item facing correction.");
                }
            }
            foreach(var item in new[]{Items.FromBlock(BlockType.Dirt),Items.FromBlock(BlockType.Cobblestone),Items.FromBlock(BlockType.WoodLog),Items.Tool(ToolTier.Stone,ToolKind.Sword),Items.FromBlock(BlockType.Torch),Items.Coal})
            {
                inventory.SetSlot(0,new(item,1));GraphicsDevice.Clear(Color.CornflowerBlue);renderer.Draw(GameState.Playing,inventory);
                var pixels=new Color[1280*720];GraphicsDevice.GetBackBufferData(pixels);int count=0,minX=1280,minY=720;
                for(int y=0;y<720;y++)for(int x=0;x<1280;x++)if(pixels[y*1280+x]!=Color.CornflowerBlue){count++;minX=Math.Min(minX,x);minY=Math.Min(minY,y);}
                Console.WriteLine($"{Items.Name(item)}: pixels {count}; left/top {minX},{minY}");
                if(count<200||minX<640||minY<180)throw new Exception("Held model missing or obstructing central view.");
                if(Items.Get(item).Tool==ToolKind.Sword && (minX<1280*.70f||minX>1280*.82f||minY<720*.28f||minY>720*.40f))
                    throw new Exception("Sword footprint no longer matches the reference's right-edge grip and blade height.");
                using var image=new Texture2D(GraphicsDevice,1280,720);image.SetData(pixels);using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"held-reference-{item.Value}.png"));image.SaveAsPng(file,1280,720);
                GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=atlas.Texture;
            }
            foreach(var size in new[]{new Point(320,240),new(1280,720),new(2560,1080),new(3440,1440),new(3440,1369)})
            foreach(var resizedItem in new[]{Items.FromBlock(BlockType.Dirt),Items.Tool(ToolTier.Stone,ToolKind.Sword),Items.Coal})
            using(var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24))
            {
                GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);
                inventory.SetSlot(0,new(resizedItem,1));
                Matrix bob=Matrix.CreateRotationZ(.015f)*Matrix.CreateTranslation(.025f,-.04f,0),swing=Matrix.CreateRotationX(-.15f),equip=Matrix.CreateTranslation(0,-.05f,0);
                var viewport=GraphicsDevice.Viewport;var sampler=GraphicsDevice.SamplerStates[0];var blend=GraphicsDevice.BlendState;var depth=GraphicsDevice.DepthStencilState;
                renderer.Draw(GameState.Playing,inventory,walkingBob:bob,swing:swing,equip:equip);
                var effect=(AlphaTestEffect)typeof(FirstPersonRenderer).GetField("_effect",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
                                var selected=ViewModelSelection.Select(GameState.Playing,inventory);
                Matrix? facing=selected.Kind==ViewModelKind.Sprite&&!Items.TryGetBlock(resizedItem,out _)?HeldItemPose.SpriteFacing(sprites.FacingVertices(resizedItem),HeldItemPose.World(selected,Matrix.Identity,Matrix.Identity)):null;
                var expected=HeldItemPose.World(selected,swing,equip,facing);
                if(effect.View!=bob||effect.World!=expected||!GraphicsDevice.Viewport.Equals(viewport)||GraphicsDevice.SamplerStates[0]!=sampler||GraphicsDevice.BlendState!=blend||GraphicsDevice.DepthStencilState!=depth)
                    throw new Exception("Walking bob was cancelled/doubled, local animation lost, or device state changed.");
                                if(size.X==3440)
                {
                    GraphicsDevice.Clear(Color.Black);renderer.Draw(GameState.Playing,inventory);
                    GraphicsDevice.SetRenderTarget(null);
                    using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"held-facing-{resizedItem.Value}-{size.X}x{size.Y}.png"));target.SaveAsPng(file,size.X,size.Y);
                    GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(Color.CornflowerBlue);renderer.Draw(GameState.Playing,inventory);
                }
                GraphicsDevice.SetRenderTarget(null);var pixels=new Color[size.X*size.Y];target.GetData(pixels);
                if(pixels[size.Y/2*size.X+size.X/2]!=Color.CornflowerBlue||pixels.Count(c=>c!=Color.CornflowerBlue)<200)throw new Exception("Resized held model disappeared or covers crosshair.");
            }
            Console.WriteLine("PASS: Full held registry, cached sprite extrusion, six reference previews, resized projection, bob/swing/equip composition and device state.");
            Exit();
        }
    }
}

