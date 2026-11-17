using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Inventories;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;
namespace MDSM.Testing;
internal static class InventoryIconChecks
{
    internal static readonly (string Name,Action Check)[] All={ ("Inventory model geometry, face tiles, proportions and finite atlas UVs",Geometry) };
    internal static int Run(){try{Geometry();using var game=new Preview();game.Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static void Geometry()
    {
        var builder=new ChunkMeshBuilder();
        foreach(var item in Items.Obtainable)
        {
            if(!Items.TryGetBlock(item,out var block)||Blocks.Get(block).Shape is BlockShape.Cross or BlockShape.Crop or BlockShape.Fire)continue;
            builder.AppendInventoryItem(block);C(builder.VertexCount>0&&builder.VertexCount%3==0,"Invalid inventory triangles.");
            for(int i=0;i<builder.VertexCount;i++)
            {var v=builder.Vertices[i];C(float.IsFinite(v.Position.LengthSquared())&&v.TextureCoordinate.X is >=0 and <=1&&v.TextureCoordinate.Y is >=0 and <=1&&v.Color.A==255,"Invalid position/UV/shading alpha.");}
            if(Blocks.Get(block).Shape!=BlockShape.Cube)continue;
            var definition=Blocks.Get(block);
            foreach(var normal in new[]{Vector3.Up,Vector3.Down,Vector3.Forward,Vector3.Right})
            {
                int tile=definition.Tile(normal);
                C(builder.Vertices.Take(builder.VertexCount).Any(v=>(int)(v.TextureCoordinate.Y*16)*16+(int)(v.TextureCoordinate.X*16)==tile),"Missing per-face texture in inventory cube.");
            }
        }
        builder.AppendInventoryItem(BlockType.WoodenSlab);float slab=builder.Vertices.Take(builder.VertexCount).Max(v=>v.Position.Y)-builder.Vertices.Take(builder.VertexCount).Min(v=>v.Position.Y);
        C(Math.Abs(slab-.5f)<.00001,"Slab icon lost half-height geometry.");
        builder.AppendInventoryItem(BlockType.Door);C(builder.Vertices.Take(builder.VertexCount).Max(v=>v.Position.Y)-builder.Vertices.Take(builder.VertexCount).Min(v=>v.Position.Y)>1.9f,"Door lost a half.");
        builder.AppendInventoryItem(BlockType.Torch);C(builder.VertexCount==36,"Torch is not a six-face cuboid.");
        foreach(var block in new[]{BlockType.Fence,BlockType.FenceGate})
        {
            builder.AppendInventoryItem(block);var canonical=builder.Vertices.Take(builder.VertexCount).ToArray();
            var held=new ViewModelGeometry();held.Build(new(ViewModelKind.Cube,Items.FromBlock(block)));
            C(held.VertexCount==canonical.Length&&canonical.Length==(block==BlockType.Fence?204:180),$"Held fence/gate uses obsolete geometry: {block} {canonical.Length}/{held.VertexCount}.");
            for(int i=0;i<canonical.Length;i++)
            {
                Vector3 expected=(canonical[i].Position-Vector3.One*.5f)*(MDSM.Entities.DroppedItem.Radius*2)*3.5f;
                C(Vector3.Distance(held.Vertices[i].Position,expected)<.00001f&&held.Vertices[i].TextureCoordinate==canonical[i].TextureCoordinate&&held.Vertices[i].Color==canonical[i].Color,"Held geometry/UVs differ from corrected inventory model.");
            }
            var drops=new ChunkMeshBuilder();drops.AppendItem(BlockType.Stone,Vector3.Zero);
            var prefix=drops.Vertices.Take(drops.VertexCount).ToArray();int first=drops.VertexCount;
            drops.AppendItem(block,new Vector3(5,6,7));
            C(drops.VertexCount==first+canonical.Length&&drops.Vertices.Take(first).SequenceEqual(prefix),"Appending fence/gate destroyed earlier dropped-item geometry.");
            for(int i=0;i<canonical.Length;i++)C(Vector3.Distance(drops.Vertices[first+i].Position,new Vector3(5,6,7)+(canonical[i].Position-Vector3.One*.5f)*(MDSM.Entities.DroppedItem.Radius*2))<.00001f,"Dropped fence/gate scale or anchor changed.");
        }
    }
    private static void C(bool v,string message){if(!v)throw new InvalidOperationException(message);}
    private sealed class Preview:Game
    {
        private TextureAtlas _atlas;private SpriteBatch _batch;private Texture2D _pixel;
        internal Preview(){var graphics=new GraphicsDeviceManager(this){PreferredBackBufferWidth=960,PreferredBackBufferHeight=900,SynchronizeWithVerticalRetrace=false};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void LoadContent()
        {
            _batch=new(GraphicsDevice);_pixel=new(GraphicsDevice,1,1);_pixel.SetData(new[]{Color.White});
            using var original=new RenderTarget2D(GraphicsDevice,64,64,false,SurfaceFormat.Color,DepthFormat.Depth24,0,RenderTargetUsage.PreserveContents);
            GraphicsDevice.SetRenderTarget(original);GraphicsDevice.Clear(Color.Magenta);
            var viewport=GraphicsDevice.Viewport;var blend=GraphicsDevice.BlendState;var depth=GraphicsDevice.DepthStencilState;var sampler=GraphicsDevice.SamplerStates[0];
            GraphicsDevice.Textures[0]=_pixel;
            _atlas=new TextureAtlas(Content);
            C(GraphicsDevice.GetRenderTargets().Single().RenderTarget==original&&GraphicsDevice.Viewport.Equals(viewport)
                &&GraphicsDevice.BlendState==blend&&GraphicsDevice.DepthStencilState==depth&&GraphicsDevice.SamplerStates[0]==sampler&&GraphicsDevice.Textures[0]==_pixel,"Icon baking leaked graphics state.");
            GraphicsDevice.SetRenderTarget(null);var preserved=new Color[4096];original.GetData(preserved);C(preserved.All(p=>p==Color.Magenta),"Icon bake damaged the previous render target.");
            var data=new Color[512*512];_atlas.Icons.Texture.GetData(data);
            foreach(var item in Items.Obtainable)
            {
                if(!_atlas.Icons.IsModel(item))continue;
                var r=InventoryIconAtlas.ModelSource(item);int pixels=0;
                for(int y=0;y<32;y++)for(int x=0;x<32;x++)
                {
                    byte alpha=data[(r.Y+y)*512+r.X+x].A;if(alpha==0)continue;pixels++;
                    C(x>0&&x<31&&y>0&&y<31,"Icon reaches atlas edge/clips: "+Items.Name(item));
                }
                C(pixels>5&&pixels<900,"Empty/opaque inventory icon: "+Items.Name(item));
            }
            C(!_atlas.Icons.IsModel(Items.Apple)&&!_atlas.Icons.IsModel(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe))&&!_atlas.Icons.IsModel(Items.FromBlock(BlockType.RedFlower)),"Ordinary item/plant became a 3D block.");
            var sprites=new Color[256*256];_atlas.ItemAtlas.GetData(sprites);
            foreach(var item in Items.Obtainable)
            {
                if(_atlas.Icons.IsModel(item)||!Items.IsSprite(item))continue;
                Rectangle tile=_atlas.ItemSource(item),source=_atlas.Icons.SpriteSource(item,tile);
                C(tile.Contains(source),"2D icon crop crossed an atlas tile boundary.");
                for(int y=tile.Top;y<tile.Bottom;y++)for(int x=tile.Left;x<tile.Right;x++)
                    if(sprites[y*256+x].A>0)C(source.Contains(x,y),"2D icon crop lost visible pixels.");
            }
            foreach(var block in new[]{BlockType.Grass,BlockType.WoodenSlab,BlockType.CobblestoneStairs,BlockType.Fence,BlockType.FenceGate,BlockType.Door,BlockType.Torch,BlockType.Bed})C(_atlas.Icons.IsModel(Items.FromBlock(block)),"Missing special block icon.");
            using var file=File.Create(Path.Combine(AppContext.BaseDirectory,"inventory-icon-atlas.png"));_atlas.Icons.Texture.SaveAsPng(file,512,512);
        }
        protected override void Update(GameTime time){}
        protected override void Draw(GameTime time)
        {
            GraphicsDevice.Clear(new Color(37,50,56));_batch.Begin(samplerState:SamplerState.PointClamp);
            int i=0;foreach(var item in Items.Obtainable)
            {
                int x=8+i%12*79,y=8+i/12*80;
                _batch.Draw(_pixel,new Rectangle(x,y,72,72),new Color(22,32,37));
                ItemIcons.Draw(_batch,_atlas,_pixel,new(item,1),new(x,y,72,64),false,2);
                string name=Items.Name(item);PixelFont.Draw(_batch,_pixel,name[..Math.Min(11,name.Length)],x+2,y+65,1,Color.White);i++;
            }
            _batch.End();
            using var shot=File.Create(Path.Combine(AppContext.BaseDirectory,"inventory-icons-preview.png"));
            var pixels=new Color[960*900];GraphicsDevice.GetBackBufferData(pixels);using var texture=new Texture2D(GraphicsDevice,960,900);texture.SetData(pixels);texture.SaveAsPng(shot,960,900);
            using(var held=new FirstPersonRenderer(GraphicsDevice,_atlas))
            foreach(var block in new[]{BlockType.Fence,BlockType.FenceGate})
            {
                GraphicsDevice.Clear(Color.CornflowerBlue);var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(block),1));
                // The preview's explicit readback/SetData calls can disturb the
                // native DesktopGL binding; restore it before testing rendering.
                GraphicsDevice.Textures[0]=null;GraphicsDevice.Textures[0]=_atlas.Texture;
                held.Draw(MDSM.Core.GameState.Playing,inventory);GraphicsDevice.GetBackBufferData(pixels);
                C(pixels.Count(p=>p!=Color.CornflowerBlue)>100,"Held fence/gate was not visible in the real first-person pass.");
                C(pixels.Count(p=>p.R>70&&p.R>p.B*1.2f&&p.G>p.B*1.1f)>100,"Held fence/gate lost its wood texture.");
                texture.SetData(pixels);using var file=File.Create(Path.Combine(AppContext.BaseDirectory,block==BlockType.Fence?"held-fence.png":"held-gate.png"));texture.SaveAsPng(file,960,900);
            }
            Console.WriteLine("PASS: Cached block/special geometry, alpha borders, 2D classification and render-target/state restoration; preview saved.");Exit();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(_atlas!=null){_atlas.Dispose();_atlas.Dispose();C(_atlas.Icons.Texture.IsDisposed&&!_atlas.Texture.IsDisposed,"Icon disposal leaked its target or disposed Content-owned textures.");}
                _batch?.Dispose();_pixel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
