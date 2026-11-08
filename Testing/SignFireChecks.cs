using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class SignFireChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Separate text-atlas mesh, larger fitted glyphs and clearing text",TextGeometry),
        ("Eight sign directions, old cardinal metadata, selection and persistence",Rotations),
        ("Fire side attachment, outward tilt, floor perimeter and bounded stronger spread",Fire)
    };
    public static int Run()
    {int failed=0;foreach(var check in All)try{check.Check();Console.WriteLine("PASS: "+check.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+check.Name+": "+e);}return failed==0?0:1;}
    private static void C(bool ok,string message)=>OvernightChecks.Check(ok,message);
    internal static int Tile(Microsoft.Xna.Framework.Graphics.VertexPositionColorTexture vertex)
        =>(int)(vertex.TextureCoordinate.Y*16)*16+(int)(vertex.TextureCoordinate.X*16);
    private static void TextGeometry()
    {
        var w=PigChecks.Flat();Point3 p=new(20,1,20);var player=new Player(new(50,1,50));
        C(w.PlaceBuilding(p,BlockType.Sign,0,false,player.Bounds,new(0,-1,0)),"Sign fixture failed.");w.EditSign(p,"TEXT");
        var mesh=new ChunkMeshBuilder();mesh.Build(w,w.GetChunk(1,1));
        C(mesh.TextVertexCount==48,"Text did not use one quad per glyph per board side.");
        C(mesh.Vertices.Take(mesh.VertexCount).All(v=>Tile(v)<80 || Tile(v)>174),"Terrain mesh still samples glyph tiles.");
        C(mesh.TextVertices.Take(mesh.TextVertexCount).All(v=>Tile(v)>=80 && Tile(v)<=174),"Text batch has terrain UVs.");
        float height=mesh.TextVertices.Take(mesh.TextVertexCount).Max(v=>v.Position.Y)-mesh.TextVertices.Take(mesh.TextVertexCount).Min(v=>v.Position.Y);
        C(Math.Abs(height-7f/64)<.0001f && height>2*7f/160,"Normal sign text is not substantially larger.");
        foreach(int length in new[]{1,10,16,24})C(ChunkMeshBuilder.SignTextPixelSize(length)*length*6<=.9401f,"Text exceeds the board width.");
        w.EditSign(p,"");mesh.Build(w,w.GetChunk(1,1));C(mesh.TextVertexCount==0,"Cleared sign text retained geometry.");
    }
    private static void Rotations()
    {
        var w=PigChecks.Flat();var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.Sign),16));var player=new Player(new(50,1,50));Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(int i=0;i<8;i++)
        {
            Point3 ground=new(20+i*3,0,20);float angle=(i-4)*MathHelper.PiOver4;
            C(BlockPlacement.TryPlace(w,inventory,new(ground,new(0,1,0),1),player.Bounds,false,angle),"Angle-based sign placement failed.");
            Point3 cell=ground+new Point3(0,1,0);Matrix transform=World.SignTransform(cell,w.Structure(cell));
            C(Vector3.Distance(Vector3.TransformNormal(Vector3.Forward,transform),Vector3.TransformNormal(Vector3.Forward,Matrix.CreateRotationY(-angle)))<.0001f,"Sign did not snap to its 45-degree direction.");
            int count=w.BlockBoxes(cell,boxes);C(count==2 && w.BlockBoxes(cell,boxes,true)==0,"Sign collision changed.");w.BlockBoxes(cell,boxes);
            for(int corner=0;corner<8;corner++)
            {Vector3 local=new((corner&1)==0?-.5f:.5f,(corner&2)==0?.5f:1,(corner&4)==0?-.0625f:.0625f);Vector3 v=Vector3.Transform(local,transform);C(v.X>=boxes[0].Min.X-.0001f && v.X<=boxes[0].Max.X+.0001f && v.Z>=boxes[0].Min.Z-.0001f && v.Z<=boxes[0].Max.Z+.0001f,"Diagonal sign selection clips a corner.");}
            var old=new StructureState(cell,(byte)((i&3)|4),false,new(0,-1,0));
            C(World.SignTransform(cell,old)==World.SignTransform(cell,old with {Facing=(byte)(i&3)}),"Old unused sign bit changed existing orientation.");
        }
        C(inventory[0].Count==8,"Sign placement consumed an incorrect quantity.");
        using var data=new MemoryStream();WorldSerializer.Write(data,new(w,"Angles",player.Position,0,0,inventory));data.Position=0;var loaded=WorldSerializer.Read(data);
        foreach(var state in w.Structures.Values)C(loaded.World.Structure(state.Cell)==state && World.SignTransform(state.Cell,state)==World.SignTransform(state.Cell,loaded.World.Structure(state.Cell)),"Diagonal orientation changed after loading.");
    }
    private static void Fire()
    {
        var w=PigChecks.Flat();var mesh=new ChunkMeshBuilder();Point3 cell=new(20,1,20);
        for(int facing=0;facing<4;facing++)
        {
            Point3 d=World.FacingDirection(facing),fuel=cell+d;w.SetBlock(fuel.X,fuel.Y,fuel.Z,BlockType.WoodLog);C(w.Fire.Ignite(cell,d),"Wall fire fixture failed.");mesh.Build(w,w.GetChunk(1,1));
            var vertices=mesh.Vertices.Take(mesh.VertexCount).Where(v=>Tile(v)==62).ToArray();C(vertices.Length==12,"Side fire remains a cross or full block.");
            float bottom=vertices.Min(v=>v.Position.Y),top=vertices.Max(v=>v.Position.Y);var low=vertices.Where(v=>v.Position.Y==bottom).Select(v=>v.Position).ToArray();var high=vertices.Where(v=>v.Position.Y==top).Select(v=>v.Position).ToArray();
            float lowDistance=low.Average(p=>p.X*d.X+p.Z*d.Z),highDistance=high.Average(p=>p.X*d.X+p.Z*d.Z);
            C(Math.Abs(lowDistance-highDistance-.32f)<.0001f,"Fire tilts into its burning wall.");
            w.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Air);w.SetBlock(fuel.X,fuel.Y,fuel.Z,BlockType.Air);
        }
        w.Fire.Ignite(cell);mesh.Build(w,w.GetChunk(1,1));C(mesh.Vertices.Take(mesh.VertexCount).Count(v=>Tile(v)==62)==48,"Floor fire is not an outward perimeter.");
        int spread=0;
        for(int seed=0;seed<48;seed++)
        {
            var sample=new World(seed,WorldSize.Small,false);sample.SetGeneratedBlock(20,0,20,BlockType.Stone);sample.SetGeneratedBlock(20,1,20,BlockType.WoodLog);sample.Fire.Ignite(new(20,2,20));
            for(int second=0;second<4;second++){sample.Fire.Update(1);C(sample.Fire.LastProcessed<=FireSimulation.Budget && sample.Fire.Count<=FireSimulation.Maximum,"Stronger spread exceeded its budget.");}
            if(sample.Fire.Count>1)spread++;
        }
        C(spread>=24,"Fire still rarely spreads even beside valid fuel.");Console.WriteLine($"  Fire: {spread}/48 sources spread within four active seconds; chance per tick {FireSimulation.SpreadChancePerTick:P0}.");
    }
}
