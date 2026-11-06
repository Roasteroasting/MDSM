using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.UI;

namespace MDSM.Testing;

internal static class TorchChecks
{
    private static readonly Point3[] Supports={new(0,-1,0),new(1,0,0),new(-1,0,0),new(0,0,1),new(0,0,-1)};
    private static readonly Point3 Cell=new(15,4,15);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static World Fixture(Point3 support)
    {
        var world=new World(173,WorldSize.Small,false);Point3 backing=Cell+support;
        world.SetBlock(backing.X,backing.Y,backing.Z,BlockType.Stone);
        Check(world.PlaceStructure(Cell,BlockType.Torch,3,support,new Player(new(40,4,40)).Bounds),"Torch attachment fixture failed.");return world;
    }
    private static VertexPositionColorTexture[] Mesh(World world)
    {
        var mesh=new ChunkMeshBuilder();mesh.Build(world,world.GetChunk(Cell.X/16,Cell.Z/16));Rectangle tile=TextureAtlas.Source(43);
        return mesh.Vertices.Take(mesh.VertexCount).Where(v=>v.TextureCoordinate.X>tile.Left/256f && v.TextureCoordinate.X<tile.Right/256f
            && v.TextureCoordinate.Y>tile.Top/256f && v.TextureCoordinate.Y<tile.Bottom/256f).ToArray();
    }
    private static Vector3 Center(VertexPositionColorTexture[] vertices,int face)
    {Vector3 sum=Vector3.Zero;for(int i=0;i<6;i++)sum+=vertices[face*6+i].Position;return sum/6;}
    public static void Uvs()
    {
        var vertices=Mesh(Fixture(Supports[0]));Check(vertices.Length==36,"Torch must remain a six-face cuboid.");Rectangle tile=TextureAtlas.Source(43);
        Check(Math.Abs(vertices.Max(v=>v.Position.X)-vertices.Min(v=>v.Position.X)-.125f)<.0001f
            && Math.Abs(vertices.Max(v=>v.Position.Z)-vertices.Min(v=>v.Position.Z)-.125f)<.0001f,"Torch width no longer matches two texture pixels.");
        for(int face=0;face<6;face++)
        {
            var quad=vertices.Skip(face*6).Take(6).ToArray();
            float minU=quad.Min(v=>v.TextureCoordinate.X)*256-tile.X,maxU=quad.Max(v=>v.TextureCoordinate.X)*256-tile.X;
            float minV=quad.Min(v=>v.TextureCoordinate.Y)*256-tile.Y,maxV=quad.Max(v=>v.TextureCoordinate.Y)*256-tile.Y;
            Check(minU==7.5f && maxU==8.5f,"Torch side/top maps the full icon instead of its narrow central strip.");
            Check(minV==(face==3?14.5f:6.5f) && maxV==(face==2?7.5f:15.5f),"Torch cap/side UV rows do not match the intended atlas region.");
            Check(quad.All(v=>v.Color.A==255),"Torch face alpha was reduced by shading.");
        }
        var builder=new ChunkMeshBuilder();builder.AppendItem(BlockType.Torch,Vector3.Zero);
        Check(builder.VertexCount==24,"Inventory/held Torch representation changed.");
        var uv=builder.Vertices.Take(24).Select(v=>v.TextureCoordinate).ToArray();
        Check(uv.Min(v=>v.X)==TextureAtlas.Uv(43,0,0).X && uv.Max(v=>v.X)==TextureAtlas.Uv(43,1,0).X
            && uv.Min(v=>v.Y)==TextureAtlas.Uv(43,0,0).Y && uv.Max(v=>v.Y)==TextureAtlas.Uv(43,0,1).Y,"Torch item no longer uses its original complete sprite.");
    }
    public static void PlacementAndPersistence()
    {
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        foreach(Point3 support in Supports.OrderBy(s=>s.Y!=0))
        {
            World world=Fixture(support);var vertices=Mesh(world);Vector3 bottom=Center(vertices,3),top=Center(vertices,2),up=top-bottom;
            bool wall=support.Y==0;Vector3 origin=new(Cell.X,Cell.Y,Cell.Z),toward=new(support.X,0,support.Z);
            if(wall)
            {
                float angle=MathF.Acos(up.Y/up.Length())*180/MathF.PI;
                Check(angle>=20 && angle<=25 && Vector3.Dot(up,-toward)>0,"Wall Torch does not lean outward 20-25 degrees.");
                Check(Math.Abs(Vector3.Dot(up,Vector3.Cross(Vector3.Up,toward)))<.0001f,"Torch leans sideways instead of away from its supporting wall.");
                Check(Math.Abs(bottom.Y-Cell.Y-.2f)<.0001f,"Wall Torch lost its existing raised attachment height.");
                float nearest=vertices.Max(v=>Vector3.Dot(v.Position-origin-new Vector3(.5f,0,.5f),toward));
                Check(nearest<=.5001f && nearest>=.49f,"Torch base detaches from or intersects the supporting wall.");
            }
            else Check(Vector3.Distance(bottom,origin+new Vector3(.5f,0,.5f))<.0001f && Math.Abs(up.X)+Math.Abs(up.Z)<.0001f,"Floor Torch is tilted or off center.");
            Check(Math.Abs(up.Length()-.625f)<.0001f,"Torch height does not match ten texture pixels.");
            Vector3[] normals={Vector3.Right,Vector3.Left,Vector3.Up,Vector3.Down,Vector3.Backward,Vector3.Forward};
            Matrix rotation=wall?Matrix.CreateFromAxisAngle(Vector3.Cross(Vector3.Up,-toward),MathHelper.PiOver4/2):Matrix.Identity;
            for(int face=0;face<6;face++)
            {
                Vector3 a=vertices[face*6].Position,b=vertices[face*6+1].Position,c=vertices[face*6+2].Position;
                Check(Vector3.Dot(Vector3.Cross(b-a,c-a),Vector3.TransformNormal(normals[face],rotation))<0,"Torch face winding changed after rotation.");
            }
            Check(world.BlockBoxes(Cell,boxes,true)==0 && world.BlockBoxes(Cell,boxes)==1,"Torch became solid or lost targeting geometry.");
            Vector3 shift=new(support.X*.28f,wall?.2f:0,support.Z*.28f);
            Check(boxes[0].Min==origin+shift+new Vector3(.4f,0,.4f) && boxes[0].Max==origin+shift+new Vector3(.6f,.7f,.6f),"Existing Torch targeting bounds changed.");
            Vector3 away=wall?-toward:Vector3.Forward,selectionCenter=(boxes[0].Min+boxes[0].Max)*.5f;
            Check(BlockRaycaster.Cast(world,selectionCenter+away*2,-away)?.Cell==Cell,"Torch targeting no longer finds its existing selection box.");
            Check(world.Torches.Protected(origin+new Vector3(.5f,.5f,.5f)),"Torch spawn suppression changed.");
            using var stream=new MemoryStream();WorldSerializer.Write(stream,new(world,"Torch geometry",new(40,4,40),0,0));stream.Position=0;
            World loaded=WorldSerializer.Read(stream).World;
            Check(loaded.Structure(Cell)==world.Structure(Cell) && Mesh(loaded).AsSpan().SequenceEqual(vertices),"Torch orientation/UVs changed on save/load.");
            loaded.DirtyDetail(Cell);Check(Mesh(loaded).AsSpan().SequenceEqual(vertices),"Torch changed after a chunk rebuild.");
        }
    }
    public static void UnrelatedFingerprint()
    {
        var builder=new ChunkMeshBuilder();using var data=new MemoryStream();using var writer=new BinaryWriter(data);
        var world=new World(173,WorldSize.Small,false);int index=0;
        foreach(BlockType block in Blocks.Selection)
        {
            builder.Clear();builder.AppendItem(block,Vector3.Zero);Write();
            if(block==BlockType.Torch)continue;
            world.SetGeneratedBlock(index%8*2,3,index/8*2,BlockType.Stone);
            world.SetGeneratedBlock(index%8*2,4,index/8*2,block);index++;
        }
        world.SetGeneratedBlock(0,3,0,BlockType.FarmlandWet);world.SetGeneratedBlock(0,4,0,BlockType.Wheat4);
        builder.Build(world,world.GetChunk(0,0));Write();
        writer.Flush();Console.WriteLine("  Unrelated block and all item geometry/UV SHA-256: "+Convert.ToHexString(SHA256.HashData(data.ToArray())));
        void Write(){foreach(var v in builder.Vertices.AsSpan(0,builder.VertexCount)){writer.Write(v.Position.X);writer.Write(v.Position.Y);writer.Write(v.Position.Z);writer.Write(v.Color.PackedValue);writer.Write(v.TextureCoordinate.X);writer.Write(v.TextureCoordinate.Y);}}
    }
    public static int Run()
    {
        int failures=0;foreach(var test in new (string Name,Action Check)[]{("Torch strip/cap UVs and unchanged item sprite",Uvs),("Floor/wall Torch transforms, hitboxes, suppression and persisted orientation",PlacementAndPersistence),("Unrelated geometry fingerprint",UnrelatedFingerprint)})
            try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        return failures==0?0:1;
    }
    public static void VerifyGraphics(Game1 game)
    {
        GraphicsDevice device=game.GraphicsDevice;var atlas=new TextureAtlas(game.Content);
        var originalTargets=device.GetRenderTargets();Viewport originalViewport=device.Viewport;Texture originalTexture=device.Textures[0];
        using var target=new RenderTarget2D(device,768,1200,false,SurfaceFormat.Color,DepthFormat.Depth24);
        using var pixel=new Texture2D(device,1,1);pixel.SetData(new[]{Color.White});using var sprites=new SpriteBatch(device);
        try
        {
            // The actual content asset already contains opaque 2x10 artwork and a hot 2x2 cap.
            var artwork=new Color[256*256];atlas.Texture.GetData(artwork);Rectangle tile=TextureAtlas.Source(43);
            for(int y=6;y<16;y++)for(int x=7;x<9;x++)Check(artwork[(tile.Y+y)*256+tile.X+x].A==255,"Torch's sampled strip contains transparent gaps.");
            for(int y=6;y<8;y++)for(int x=7;x<9;x++)Check(artwork[(tile.Y+y)*256+tile.X+x].R>=180,"Torch cap does not sample the existing bright artwork.");
            device.Textures[0]=null;device.Textures[0]=originalTexture;
            device.SetRenderTarget(target);device.Clear(new Color(45,55,65));
            for(int row=0;row<Supports.Length;row++)
            {
                Point3 support=Supports[row];World world=Fixture(support);var vertices=Mesh(world);
                Vector3 center=(Center(vertices,2)+Center(vertices,3))*.5f;
                Vector3 away=support.Y==0?new(-support.X,0,-support.Z):Vector3.Forward;
                using var renderer=new WorldRenderer(device,atlas,world);
                for(int column=0;column<3;column++)
                {
                    device.Viewport=new Viewport(column*256,row*240,256,240);
                    Vector3 offset=column==0?away*1.15f+Vector3.Up*.18f:column==1?Vector3.Cross(Vector3.Up,away)*1.15f+Vector3.Up*.15f:away*.4f+Vector3.Up*1.2f;
                    var camera=new Camera{Position=center+offset};Vector3 forward=-offset;
                    camera.RestoreOrientation(MathF.Atan2(forward.X,-forward.Z),MathF.Atan2(forward.Y,new Vector2(forward.X,forward.Z).Length()));
                    RenderingPerformanceChecks.Warm(renderer,camera);renderer.Draw(camera,null);
                }
            }
            device.Viewport=new Viewport(0,0,768,1200);
            sprites.Begin(samplerState:SamplerState.PointClamp);
            string[] names={"FLOOR","EAST WALL","WEST WALL","SOUTH WALL","NORTH WALL"},views={"FRONT","SIDE","TOP"};
            for(int row=0;row<5;row++)for(int column=0;column<3;column++)PixelFont.Draw(sprites,pixel,names[row]+" / "+views[column],column*256+8,row*240+8,1,Color.White);
            sprites.End();device.SetRenderTarget(null);
            var pixels=new Color[target.Width*target.Height];target.GetData(pixels);
            using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,"torch-rendering-regression.png")))target.SaveAsPng(file,target.Width,target.Height);
            for(int row=0;row<5;row++)for(int column=0;column<3;column++)
            {
                int bright=0;for(int y=row*240+30;y<(row+1)*240;y++)for(int x=column*256;x<(column+1)*256;x++)
                {Color c=pixels[y*target.Width+x];if(c.R>170 && c.G>100 && c.B<240)bright++;}
                Check(bright>5,$"Torch strip/cap missing from {names[row]} / {views[column]} GPU view ({bright} bright pixels).");
            }
        }
        finally
        {
            device.SetRenderTargets(originalTargets);device.Viewport=originalViewport;
            device.Textures[0]=null;device.Textures[0]=originalTexture;
        }
        Console.WriteLine("PASS: Existing Torch strip/cap artwork, floor plus four wall orientations, GPU front/side/top views and point sampling.");
    }
}
