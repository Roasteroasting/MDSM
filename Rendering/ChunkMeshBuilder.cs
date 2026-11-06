using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Entities;
using MDSM.Worlds;

namespace MDSM.Rendering;

// CPU-only geometry builder. One reusable scratch buffer serves all chunk uploads.
internal sealed class ChunkMeshBuilder
{
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[4096];
    public VertexPositionColorTexture[] Vertices => _vertices;
    public int VertexCount { get; private set; }
    public void Clear() => VertexCount = 0;

    public void AppendCube(BlockType block, Vector3 origin)
    {
        foreach (Face face in Faces)
            AddQuad(origin + face.A, origin + face.B, origin + face.C, origin + face.D,
                Blocks.Tile(block, face.Normal), new Color(new Vector3(face.Shade)), false);
    }

    public void AppendItem(BlockType block, Vector3 center)
    {
        float size = DroppedItem.Radius * 2;
        Vector3 origin = center - new Vector3(DroppedItem.Radius);
        BlockDefinition definition = Blocks.Get(block);
        if (definition.Shape is BlockShape.Cross or BlockShape.Torch)
        {
            AddQuad(origin, origin + new Vector3(size, 0, size), origin + new Vector3(size, size, size),
                origin + new Vector3(0, size, 0), definition.SideTile, Color.White, true);
            AddQuad(origin + new Vector3(size, 0, 0), origin + new Vector3(0, 0, size),
                origin + new Vector3(0, size, size), origin + new Vector3(size, size, 0), definition.SideTile, Color.White, true);
        }
        else if(definition.Shape is BlockShape.Fence or BlockShape.Gate or BlockShape.Door)
        {
            if(definition.Shape==BlockShape.Door)
            {DoorBox(origin,origin+new Vector3(1,1,.1875f)*size,definition.SideTile,Vector3.Right);DoorBox(origin+Vector3.Up*size,origin+new Vector3(1,2,.1875f)*size,42,Vector3.Right);}
            else
            {
                Box(new(.375f,0,.375f),new(.625f,1,.625f),7);
                Box(new(0,.3f,.4375f),new(1,.45f,.5625f),7);Box(new(0,.7f,.4375f),new(1,.85f,.5625f),7);
            }
        }
        else if(definition.Shape==BlockShape.Pane)
        {
            Vector3 min=new(0,0,7f/16),max=new(1,1,9f/16);
            foreach(Face face in Faces)PaneFace(origin+min*size,origin+max*size,min,max,face,definition.SideTile,face.Normal.X!=0);
        }
        else
            foreach (Face face in Faces)
                AddQuad(origin + face.A * size, origin + face.B * size, origin + face.C * size, origin + face.D * size,
                    definition.Tile(face.Normal), new Color(new Vector3(face.Shade)), false);
        void Box(Vector3 min,Vector3 max,int tile)
        {foreach(Face face in Faces)AddQuad(origin+(min+face.A*(max-min))*size,origin+(min+face.B*(max-min))*size,origin+(min+face.C*(max-min))*size,origin+(min+face.D*(max-min))*size,tile,new Color(new Vector3(face.Shade)),false);}
    }

    // Corners follow the geometric normal; emitted triangles use XNA's clockwise front faces.
    private static readonly Face[] Faces =
    {
        new(new Point3(1, 0, 0), Vector3.UnitX, 0.78f,
            new(1, 0, 1), new(1, 0, 0), new(1, 1, 0), new(1, 1, 1)),
        new(new Point3(-1, 0, 0), -Vector3.UnitX, 0.78f,
            new(0, 0, 0), new(0, 0, 1), new(0, 1, 1), new(0, 1, 0)),
        new(new Point3(0, 1, 0), Vector3.UnitY, 1f,
            new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0)),
        new(new Point3(0, -1, 0), -Vector3.UnitY, 0.5f,
            new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1)),
        new(new Point3(0, 0, 1), Vector3.UnitZ, 0.9f,
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)),
        new(new Point3(0, 0, -1), -Vector3.UnitZ, 0.9f,
            new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(1, 1, 0))
    };

    public void Build(World world, Chunk chunk)
    {
        VertexCount = 0;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for (int y = 0; y < World.Height; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            BlockType block = chunk.GetBlock(x, y, z);
            BlockDefinition definition = Blocks.Get(block);
            if(Lava.IsLava(block))
            {
                Vector3 at=new(chunk.OriginX+x,y,chunk.OriginZ+z);
                foreach(Face face in Faces)
                {
                    BlockType adjacent=world.GetBlock((int)at.X+face.Offset.X,y+face.Offset.Y,(int)at.Z+face.Offset.Z);
                    if(Lava.IsLava(adjacent)||Blocks.IsOpaque(adjacent))continue;
                    AddQuad(LavaVertex(world,at,face.A),LavaVertex(world,at,face.B),LavaVertex(world,at,face.C),LavaVertex(world,at,face.D),50,Color.White,false);
                }
                continue;
            }
            if (!definition.Renderable || definition.Shape == BlockShape.Fluid)
                continue;

            int worldX = chunk.OriginX + x, worldZ = chunk.OriginZ + z;
            Vector3 position = new(worldX, y, worldZ);
            if (definition.Shape == BlockShape.Torch)
            {
                AppendTorch(world, new(worldX,y,worldZ));
                continue;
            }
            if (definition.Shape == BlockShape.Pane)
            {
                int connections=world.PaneConnections(new(worldX,y,worldZ));
                int count=World.PaneBoxes(new(worldX,y,worldZ),connections,boxes),direction=-1;
                for(int box=0;box<count;box++)
                {
                    if(box>0){do{direction++;}while((connections&(1<<direction))==0);}
                    foreach(Face face in Faces)
                    {
                        int bit=face.Offset.X<0?1:face.Offset.X>0?2:face.Offset.Z<0?4:face.Offset.Z>0?8:0;
                        if(box==0 && (connections&bit)!=0 || box>0 && (bit==(1<<direction) || bit==(1<<(direction^1))))continue;
                        bool endCap=box==0 && bit!=0 && (bit<=2?(connections&12)==0:(connections&3)==0);
                        PaneFace(boxes[box].Min,boxes[box].Max,boxes[box].Min-position,boxes[box].Max-position,face,definition.SideTile,endCap);
                    }
                }
                continue;
            }
            if(definition.Shape is BlockShape.Farmland or BlockShape.Fence or BlockShape.Gate or BlockShape.Door)
            {
                int count=world.BlockBoxes(new(worldX,y,worldZ),boxes);
                if(definition.Shape==BlockShape.Door)
                {
                    var state=world.Structure(new(worldX,y,worldZ));
                    int facing=((state.Facing&3)+(state.Open?(state.Facing&4)!=0?3:1:0))%4;
                    Vector3 hinge=facing switch{0=>Vector3.Right,1=>Vector3.Backward,2=>Vector3.Left,_=>Vector3.Forward};
                    if((state.Facing&4)!=0)hinge=-hinge;if(state.Open)hinge=-hinge;
                    for(int i=0;i<count;i++)DoorBox(boxes[i].Min,boxes[i].Max,definition.SideTile,hinge);
                    continue;
                }
                for(int i=0;i<count;i++)foreach(Face face in Faces)
                {
                    Vector3 size=boxes[i].Max-boxes[i].Min;
                    AddQuad(boxes[i].Min+face.A*size,boxes[i].Min+face.B*size,boxes[i].Min+face.C*size,boxes[i].Min+face.D*size,definition.Tile(face.Normal),new Color(new Vector3(face.Shade)),false);
                }
                continue;
            }
            if (definition.Shape == BlockShape.Crop)
            {
                float height = Blocks.PlantHeight(block);
                for (int plane = 0; plane < 2; plane++)
                {
                    float offset = plane == 0 ? .25f : .75f;
                    AddQuad(position + new Vector3(offset,0,0), position + new Vector3(offset,0,1),
                        position + new Vector3(offset,height,1), position + new Vector3(offset,height,0), definition.SideTile, Color.White, true);
                    AddQuad(position + new Vector3(0,0,offset), position + new Vector3(1,0,offset),
                        position + new Vector3(1,height,offset), position + new Vector3(0,height,offset), definition.SideTile, Color.White, true);
                }
                continue;
            }
            if (definition.Shape == BlockShape.Cross)
            {
                AddQuad(position + new Vector3(0.1f, 0, 0.1f), position + new Vector3(0.9f, 0, 0.9f),
                    position + new Vector3(0.9f, 1, 0.9f), position + new Vector3(0.1f, 1, 0.1f), definition.SideTile, Color.White, true);
                AddQuad(position + new Vector3(0.9f, 0, 0.1f), position + new Vector3(0.1f, 0, 0.9f),
                    position + new Vector3(0.1f, 1, 0.9f), position + new Vector3(0.9f, 1, 0.1f), definition.SideTile, Color.White, true);
                continue;
            }
            foreach (Face face in Faces)
            {
                // World queries include adjacent chunks and return air beyond finite edges.
                BlockType neighbor=world.GetBlock(worldX + face.Offset.X,y + face.Offset.Y,worldZ + face.Offset.Z);
                if (Blocks.IsOpaque(neighbor) || block==BlockType.Glass && neighbor==BlockType.Glass)
                    continue;

                int tile = Blocks.Tile(block, face.Normal);
                // Shading changes RGB only: reducing vertex alpha would discard dark leaf faces.
                Color color = new(new Vector3(face.Shade));
                AddQuad(position + face.A, position + face.B, position + face.C, position + face.D, tile, color, false);
            }
        }
    }

    private void AppendTorch(World world, Point3 cell)
    {
        // Tile 43's existing sprite is two pixels wide and ten tall. The item keeps
        // using the full tile; only the placed cuboid samples these narrow regions.
        const float halfWidth = 1f / 16, height = 10f / 16;
        Point3 support = world.Structure(cell).Support;
        Vector3 pivot = new(.5f,0,.5f);
        Matrix rotation = Matrix.Identity;
        if (support.Y == 0)
        {
            const float angle = MathHelper.PiOver4 / 2; // 22.5 degrees from vertical.
            Vector3 towardWall = new(support.X,0,support.Z);
            rotation = Matrix.CreateFromAxisAngle(Vector3.Cross(Vector3.Up,-towardWall),angle);
            // Rotate about the lower end; its inner edge touches the supporting wall.
            pivot += towardWall * (.5f - halfWidth * MathF.Cos(angle));
            pivot.Y = .2f; // Preserve the existing wall attachment height.
        }
        Matrix transform = rotation * Matrix.CreateTranslation(new Vector3(cell.X,cell.Y,cell.Z)+pivot);
        Vector3 min = new(-halfWidth,0,-halfWidth), size = new(halfWidth*2,height,halfWidth*2);
        Rectangle tile = TextureAtlas.Source(Blocks.Get(BlockType.Torch).SideTile);
        foreach (Face face in Faces)
        {
            int firstRow = face.Normal.Y < 0 ? 14 : 6;
            int rows = face.Normal.Y == 0 ? 10 : 2;
            float left = (tile.X+7.5f)/TextureAtlas.AtlasSize, right = (tile.X+8.5f)/TextureAtlas.AtlasSize;
            float top = (tile.Y+firstRow+.5f)/TextureAtlas.AtlasSize, bottom = (tile.Y+firstRow+rows-.5f)/TextureAtlas.AtlasSize;
            Color color = new(new Vector3(face.Shade));
            Vector3 At(Vector3 corner) => Vector3.Transform(min+corner*size,transform);
            var a = new VertexPositionColorTexture(At(face.A),color,new(left,bottom));
            var b = new VertexPositionColorTexture(At(face.B),color,new(right,bottom));
            var c = new VertexPositionColorTexture(At(face.C),color,new(right,top));
            var d = new VertexPositionColorTexture(At(face.D),color,new(left,top));
            if (VertexCount+6>_vertices.Length) Array.Resize(ref _vertices,_vertices.Length*2);
            _vertices[VertexCount++]=a;_vertices[VertexCount++]=c;_vertices[VertexCount++]=b;
            _vertices[VertexCount++]=a;_vertices[VertexCount++]=d;_vertices[VertexCount++]=c;
        }
    }

    private void DoorBox(Vector3 min,Vector3 max,int tile,Vector3 hinge)
    {
        Vector3 size=max-min;bool thinX=size.X<size.Z;
        foreach(Face face in Faces)
        {
            Rectangle? region=null;bool transpose=false,flipU=false;
            if(face.Normal.Y!=0)
            {
                region=new(0,face.Normal.Y>0?0:13,16,3);transpose=thinX;
                flipU=thinX?(face.Normal.Y>0?hinge.Z>0:hinge.Z<0):hinge.X>0;
            }
            else if(thinX?face.Normal.Z!=0:face.Normal.X!=0)
                region=new(Vector3.Dot(face.Normal,hinge)>0?13:0,0,3,16);
            AddQuad(min+face.A*size,min+face.B*size,min+face.C*size,min+face.D*size,tile,new Color(new Vector3(face.Shade)),false,region,transpose,flipU);
        }
    }
    private void PaneFace(Vector3 min,Vector3 max,Vector3 localMin,Vector3 localMax,Face face,int tile,bool endCap=false)
    {
        Rectangle region;bool transpose=false;
        if(face.Normal.Y!=0)
        {
            transpose=localMax.Z-localMin.Z>localMax.X-localMin.X;
            float start=transpose?localMin.Z:localMin.X,end=transpose?localMax.Z:localMax.X;
            region=new((int)MathF.Round(start*16),face.Normal.Y>0?0:14,Math.Max(1,(int)MathF.Round((end-start)*16)),2);
        }
        else if(endCap)region=new(face.Normal.X+face.Normal.Z<0?0:14,0,2,16);
        else
        {
            float start,end;
            if(face.Normal.X>0){start=1-localMax.Z;end=1-localMin.Z;}
            else if(face.Normal.X<0){start=localMin.Z;end=localMax.Z;}
            else if(face.Normal.Z>0){start=localMin.X;end=localMax.X;}
            else{start=1-localMax.X;end=1-localMin.X;}
            region=new((int)MathF.Round(start*16),0,Math.Max(1,(int)MathF.Round((end-start)*16)),16);
        }
        Vector3 size=max-min;
        AddQuad(min+face.A*size,min+face.B*size,min+face.C*size,min+face.D*size,tile,new Color(new Vector3(face.Shade)),false,region,transpose);
    }

    private void AddQuad(Vector3 aPosition, Vector3 bPosition, Vector3 cPosition, Vector3 dPosition, int tile, Color color, bool twoSided,Rectangle? region=null,bool transpose=false,bool flipU=false)
    {
        int needed = twoSided ? 12 : 6;
        if (VertexCount + needed > _vertices.Length)
            Array.Resize(ref _vertices, _vertices.Length * 2);
        Vector2 Uv(float u,float v)
        {
            if(region is not Rectangle r)return TextureAtlas.Uv(tile,u,v);
            if(transpose)(u,v)=(v,u);if(flipU)u=1-u;
            Rectangle source=TextureAtlas.Source(tile);
            return new((source.X+r.X+.5f+u*(r.Width-1))/TextureAtlas.AtlasSize,(source.Y+r.Y+.5f+v*(r.Height-1))/TextureAtlas.AtlasSize);
        }
        var a = new VertexPositionColorTexture(aPosition, color, Uv(0,1));
        var b = new VertexPositionColorTexture(bPosition, color, Uv(1,1));
        var c = new VertexPositionColorTexture(cPosition, color, Uv(1,0));
        var d = new VertexPositionColorTexture(dPosition, color, Uv(0,0));
        _vertices[VertexCount++] = a; _vertices[VertexCount++] = c; _vertices[VertexCount++] = b;
        _vertices[VertexCount++] = a; _vertices[VertexCount++] = d; _vertices[VertexCount++] = c;
        if (twoSided)
        {
            _vertices[VertexCount++] = a; _vertices[VertexCount++] = b; _vertices[VertexCount++] = c;
            _vertices[VertexCount++] = a; _vertices[VertexCount++] = c; _vertices[VertexCount++] = d;
        }
    }
    private static Vector3 LavaVertex(World world,Vector3 at,Vector3 point)
        =>at+new Vector3(point.X,point.Y==0?0:Lava.CornerHeight(world,(int)(at.X+point.X),(int)at.Y,(int)(at.Z+point.Z)),point.Z);

    private readonly record struct Face(Point3 Offset, Vector3 Normal, float Shade, Vector3 A, Vector3 B, Vector3 C, Vector3 D);
}
