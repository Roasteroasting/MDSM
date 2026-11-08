using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Entities;
using MDSM.Worlds;
using MDSM.UI;

namespace MDSM.Rendering;

// CPU-only geometry builder. One reusable scratch buffer serves all chunk uploads.
internal sealed class ChunkMeshBuilder
{
    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[4096];
    public VertexPositionColorTexture[] Vertices => _vertices;
    public int VertexCount { get; private set; }
    private VertexPositionColorTexture[] _textVertices=new VertexPositionColorTexture[256];
    public VertexPositionColorTexture[] TextVertices=>_textVertices;
    public int TextVertexCount {get;private set;}
    private World _world;
    private BlockLighting.Page _lightPage;
    private Chunk _buildingChunk;
    private int _buildY,_buildZ;
    public void Clear() { VertexCount = TextVertexCount = 0; _world=null; _lightPage=null; }

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
        if(definition.Shape==BlockShape.Cactus)AppendCactus(origin,size,null);
        else if (definition.Shape is BlockShape.Cross or BlockShape.Torch)
        {
            AddQuad(origin, origin + new Vector3(size, 0, size), origin + new Vector3(size, size, size),
                origin + new Vector3(0, size, 0), definition.SideTile, Color.White, true);
            AddQuad(origin + new Vector3(size, 0, 0), origin + new Vector3(0, 0, size),
                origin + new Vector3(0, size, size), origin + new Vector3(size, size, 0), definition.SideTile, Color.White, true);
        }
        else if(definition.Shape is BlockShape.Slab or BlockShape.Stairs or BlockShape.Sign)
        {
            if(definition.Shape==BlockShape.Sign){Box(new(0,.5f,.4375f),new(1,1,.5625f),7,true);Box(new(.4375f,0,.4375f),new(.5625f,.5f,.5625f),7,true);}
            else {Box(Vector3.Zero,new(1,.5f,1),definition.SideTile,true);if(definition.Shape==BlockShape.Stairs)Box(new(0,.5f,0),new(1,1,.5f),definition.SideTile,true);}
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
        void Box(Vector3 min,Vector3 max,int tile,bool cropped=false)
        {
            foreach(Face face in Faces)
            {
                Rectangle? region=null;
                if(cropped)
                {
                    Vector3 extent=max-min;
                    float u=face.Normal.X>0?1-max.Z:face.Normal.X<0?min.Z:face.Normal.Z<0?1-max.X:min.X;
                    float v=face.Normal.Y==0?1-max.Y:face.Normal.Y>0?min.Z:1-max.Z;
                    region=new((int)MathF.Round(u*16),(int)MathF.Round(v*16),Math.Max(1,(int)MathF.Round((face.Normal.X==0?extent.X:extent.Z)*16)),Math.Max(1,(int)MathF.Round((face.Normal.Y==0?extent.Y:extent.Z)*16)));
                }
                AddQuad(origin+(min+face.A*(max-min))*size,origin+(min+face.B*(max-min))*size,origin+(min+face.C*(max-min))*size,origin+(min+face.D*(max-min))*size,tile,new Color(new Vector3(face.Shade)),false,region);
            }
        }
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
    {BeginBuild(world,chunk);AdvanceBuild(long.MaxValue);}
    internal void BeginBuild(World world,Chunk chunk)
    {
        VertexCount = TextVertexCount = 0;
        _world=world;_lightPage=world.Lighting.GetPage(chunk.X,chunk.Z);
        _buildingChunk=chunk;_buildY=_buildZ=0;
    }
    internal bool AdvanceBuild(long deadline)
    {
        World world=_world;Chunk chunk=_buildingChunk;
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        while(_buildY<World.Height)
        {
        int y=_buildY,z=_buildZ;
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
            if(World.OrientedBuilding(block))
            {AppendBuilding(world,new(worldX,y,worldZ),block,boxes);continue;}
            if(definition.Shape==BlockShape.Cactus){AppendCactus(position,1,world);continue;}
            if(definition.Shape==BlockShape.Bed)
            {AppendBed(new(worldX,y,worldZ),world.Structure(new(worldX,y,worldZ)).Facing,block==BlockType.BedHead);continue;}
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
            if(definition.Shape is BlockShape.Farmland or BlockShape.Snow or BlockShape.Fence or BlockShape.Gate or BlockShape.Door)
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
                    if(definition.Shape==BlockShape.Snow)
                    {
                        BlockType adjacent=world.GetBlock(worldX+face.Offset.X,y+face.Offset.Y,worldZ+face.Offset.Z);
                        if(Blocks.IsOpaque(adjacent) || face.Normal.Y==0 && adjacent==BlockType.Snow)continue;
                    }
                    Vector3 size=boxes[i].Max-boxes[i].Min;
                    AddQuad(boxes[i].Min+face.A*size,boxes[i].Min+face.B*size,boxes[i].Min+face.C*size,boxes[i].Min+face.D*size,definition.Tile(face.Normal),new Color(new Vector3(face.Shade)),false,
                        definition.Shape==BlockShape.Snow && face.Normal.Y==0?new Rectangle(0,13,16,3):null);
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
            if(definition.Shape==BlockShape.Fire){AppendFire(world,new(worldX,y,worldZ));continue;}
            if (definition.Shape == BlockShape.Cross)
            {
                float height=block==BlockType.DeadBush?Blocks.PlantHeight(block):1;
                AddQuad(position + new Vector3(0.1f, 0, 0.1f), position + new Vector3(0.9f, 0, 0.9f),
                    position + new Vector3(0.9f, height, 0.9f), position + new Vector3(0.1f, height, 0.1f), definition.SideTile, Color.White, true);
                AddQuad(position + new Vector3(0.9f, 0, 0.1f), position + new Vector3(0.1f, 0, 0.9f),
                    position + new Vector3(0.1f, height, 0.9f), position + new Vector3(0.9f, height, 0.1f), definition.SideTile, Color.White, true);
                continue;
            }
            foreach (Face face in Faces)
            {
                // World queries include adjacent chunks and return air beyond finite edges.
                BlockType neighbor=world.GetBlock(worldX + face.Offset.X,y + face.Offset.Y,worldZ + face.Offset.Z);
                if (Blocks.IsOpaque(neighbor) || neighbor==BlockType.Snow && face.Normal.Y>0 || block==BlockType.Glass && neighbor==BlockType.Glass)
                    continue;

                int tile = Blocks.Tile(block, face.Normal);
                // Shading changes RGB only: reducing vertex alpha would discard dark leaf faces.
                Color color = new(new Vector3(face.Shade));
                AddQuad(position + face.A, position + face.B, position + face.C, position + face.D, tile, color, false);
            }
        }
        if(++_buildZ==Chunk.Size){_buildZ=0;_buildY++;}
        if(Stopwatch.GetTimestamp()>=deadline)return false;
        }
        return true;
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
            Color color = Lit(new Color(new Vector3(face.Shade)),new(cell.X+.5f,cell.Y+.5f,cell.Z+.5f));
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

    private void AppendCactus(Vector3 origin,float scale,World world)
    {
        Vector3 min=new(1f/16,0,1f/16),extent=new(14f/16,1,14f/16);
        foreach(var face in Faces)
        {
            if(world!=null && face.Offset.Y!=0)
            {BlockType neighbor=world.GetBlock((int)origin.X,(int)origin.Y+face.Offset.Y,(int)origin.Z);if(neighbor==BlockType.Cactus || Blocks.IsOpaque(neighbor))continue;}
            Rectangle region=face.Normal.Y==0?new(1,0,14,16):new(1,1,14,14);
            AddQuad(origin+(min+face.A*extent)*scale,origin+(min+face.B*extent)*scale,origin+(min+face.C*extent)*scale,origin+(min+face.D*extent)*scale,
                Blocks.Get(BlockType.Cactus).Tile(face.Normal),new Color(new Vector3(face.Shade)),false,region);
        }
    }
    private void AppendBuilding(World world,Point3 cell,BlockType block,Span<BoundingBox> boxes)
    {
        Vector3 origin=new(cell.X,cell.Y,cell.Z);int tile=Blocks.Get(block).SideTile;
        if(block==BlockType.Sign)
        {
            var state=world.Structure(cell);Matrix transform=World.SignTransform(cell,state);bool wall=state.Support.Y==0;
            SignBox(new(-.5f,wall?.25f:.5f,-.0625f),new(.5f,wall?.75f:1,.0625f));
            if(!wall)SignBox(new(-.0625f,0,-.0625f),new(.0625f,.5f,.0625f));
            if(world.Signs.TryGetValue(cell,out string text))
            {
                string[] lines=text.Split('\n');
                for(int line=0;line<lines.Length;line++)for(int ch=0;ch<lines[line].Length;ch++)
                {
                    if(lines[line][ch]==' ')continue;
                    float dot=SignTextPixelSize(lines[line].Length);
                    float left=(ch*6-lines[line].Length*3)*dot,top=(wall?.725f:.975f)-line*.12f;
                    for(int side=-1;side<=1;side+=2)
                    {
                        Vector3 a=Vector3.Transform(new(left*side,top-dot*7,.0635f*side),transform),b=Vector3.Transform(new((left+dot*5)*side,top-dot*7,.0635f*side),transform),
                            c=Vector3.Transform(new((left+dot*5)*side,top,.0635f*side),transform),d=Vector3.Transform(new(left*side,top,.0635f*side),transform);
                        AddQuad(a,b,c,d,PixelFont.SignTile(lines[line][ch]),Color.Black,false,new(0,0,5,7),text:true);
                    }
                }
            }
            void SignBox(Vector3 min,Vector3 max)
            {
                Vector3 size=max-min;
                foreach(var face in Faces)
                {
                    int width=Math.Max(1,(int)MathF.Round((face.Normal.X==0?size.X:size.Z)*16)),height=Math.Max(1,(int)MathF.Round((face.Normal.Y==0?size.Y:size.Z)*16));
                    AddQuad(Vector3.Transform(min+face.A*size,transform),Vector3.Transform(min+face.B*size,transform),Vector3.Transform(min+face.C*size,transform),Vector3.Transform(min+face.D*size,transform),tile,new Color(new Vector3(face.Shade)),false,new(0,0,width,height));
                }
            }
            return;
        }
        int count=world.BlockBoxes(cell,boxes);int mask=0;
        for(int y=0;y<2;y++)for(int z=0;z<2;z++)for(int x=0;x<2;x++)
        {
            Vector3 p=origin+new Vector3(x*.5f+.25f,y*.5f+.25f,z*.5f+.25f);
            for(int i=0;i<count;i++)if(boxes[i].Contains(p)!=ContainmentType.Disjoint){mask|=1<<(x+2*z+4*y);break;}
        }
        for(int y=0;y<2;y++)for(int z=0;z<2;z++)for(int x=0;x<2;x++)
        {
            if((mask&(1<<(x+2*z+4*y)))==0)continue;
            Vector3 min=new(x*.5f,y*.5f,z*.5f),max=min+new Vector3(.5f);
            foreach(var face in Faces)
            {
                int nx=x+face.Offset.X,ny=y+face.Offset.Y,nz=z+face.Offset.Z;
                if(nx>=0 && nx<2 && ny>=0 && ny<2 && nz>=0 && nz<2)
                {if((mask&(1<<(nx+2*nz+4*ny)))!=0)continue;}
                else if(Blocks.IsOpaque(world.GetBlock(cell.X+face.Offset.X,cell.Y+face.Offset.Y,cell.Z+face.Offset.Z)))continue;
                float u=face.Normal.X>0?1-max.Z:face.Normal.X<0?min.Z:face.Normal.Z<0?1-max.X:min.X;
                float v=face.Normal.Y==0?1-max.Y:face.Normal.Y>0?min.Z:1-max.Z;
                AddQuad(origin+min+face.A*.5f,origin+min+face.B*.5f,origin+min+face.C*.5f,origin+min+face.D*.5f,tile,new Color(new Vector3(face.Shade)),false,new((int)(u*16),(int)(v*16),8,8));
            }
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

    internal static float SignTextPixelSize(int characters)=>Math.Min(1f/64,.94f/Math.Max(1,characters*6));

    private void AppendFire(World world,Point3 cell)
    {
        Vector3 origin=new(cell.X,cell.Y,cell.Z);FireAttachment attachment=world.Fire.Attachment(cell);
        if(attachment==FireAttachment.Automatic)return;
        if(attachment!=FireAttachment.Floor){Side(FireSimulation.Direction(attachment));return;}
        for(int facing=0;facing<4;facing++)
        {
            Point3 d=World.FacingDirection(facing);Vector3 outward=new(d.X,0,d.Z),tangent=new(-d.Z,0,d.X);
            Vector3 a=origin+new Vector3(.5f,.02f,.5f)+outward*.22f-tangent*.45f;
            Vector3 b=a+tangent*.9f,c=b+Vector3.Up*.88f+outward*.25f,dv=c-tangent*.9f;
            AddQuad(a,b,c,dv,62,Color.White,true);
        }
        void Side(Point3 direction)
        {
            Vector3 toward=new(direction.X,0,direction.Z),tangent=new(-direction.Z,0,direction.X);
            Vector3 a=origin+new Vector3(.5f,.02f,.5f)+toward*.485f-tangent*.48f,b=a+tangent*.96f;
            Vector3 c=b+Vector3.Up*.9f-toward*.32f,d=c-tangent*.96f;
            AddQuad(a,b,c,d,62,Color.White,true);
        }
    }

    private void AddQuad(Vector3 aPosition, Vector3 bPosition, Vector3 cPosition, Vector3 dPosition, int tile, Color color, bool twoSided,Rectangle? region=null,bool transpose=false,bool flipU=false,bool text=false)
    {
        if(_world!=null)
        {
            Vector3 normal=Vector3.Cross(bPosition-aPosition,cPosition-aPosition);
            if(normal.LengthSquared()>0)normal.Normalize();
            color=Lit(color,(aPosition+bPosition+cPosition+dPosition)*.25f+normal*.02f);
        }
        int needed = twoSided ? 12 : 6;
        ref VertexPositionColorTexture[] vertices=ref (text?ref _textVertices:ref _vertices);
        int at=text?TextVertexCount:VertexCount;
        if (at + needed > vertices.Length)Array.Resize(ref vertices, vertices.Length * 2);
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
        vertices[at++] = a; vertices[at++] = c; vertices[at++] = b;
        vertices[at++] = a; vertices[at++] = d; vertices[at++] = c;
        if (twoSided)
        {
            vertices[at++] = a; vertices[at++] = b; vertices[at++] = c;
            vertices[at++] = a; vertices[at++] = c; vertices[at++] = d;
        }
        if(text)TextVertexCount=at;else VertexCount=at;
    }
    private Color Lit(Color color,Vector3 position)
    {
        if(_world==null)return color;
        int x=(int)MathF.Floor(position.X),y=(int)MathF.Floor(position.Y),z=(int)MathF.Floor(position.Z);
        byte packed=_lightPage!=null && x>=_lightPage.X-1 && x<=_lightPage.X+16 && z>=_lightPage.Z-1 && z<=_lightPage.Z+16
            ?_lightPage.At(x,y,z):_world.Lighting.Packed(new(x,y,z));
        return FlatLighting.Encode(color,packed);
    }
    private void AppendBed(Point3 cell,byte facing,bool head)
    {
        Matrix transform=Matrix.CreateTranslation(-.5f,0,-.5f)*Matrix.CreateRotationY(-(facing&3)*MathHelper.PiOver2)
            *Matrix.CreateTranslation(cell.X+.5f,cell.Y,cell.Z+.5f);
        Vector3 min=new(0,3f/16,0),size=new(1,6f/16,1);
        foreach(Face face in Faces)
        {
            if(face.Offset.Z==(head?1:-1))continue; // No internal seam between halves.
            int tile=face.Normal.Y>0?head?53:54:face.Normal.Y<0?59:face.Normal.Z!=0?head?57:58:head?55:56;
            Rectangle region=face.Normal.Y!=0?new(0,0,16,16):new(0,3,16,6);
            Vector3 At(Vector3 p)=>Vector3.Transform(min+p*size,transform);
            AddQuad(At(face.A),At(face.B),At(face.C),At(face.D),tile,new Color(new Vector3(face.Shade)),false,region,flipU:face.Normal.X>0);
        }
        // Two outer corner legs per half, original wood underside mapped in texel scale.
        for(int leg=0;leg<2;leg++)
        {
            min=new(leg==0?0:13f/16,0,head?0:13f/16);size=new(3f/16,3f/16,3f/16);
            foreach(Face face in Faces)
            {
                Vector3 At(Vector3 p)=>Vector3.Transform(min+p*size,transform);
                AddQuad(At(face.A),At(face.B),At(face.C),At(face.D),59,new Color(new Vector3(face.Shade)),false,new(0,0,3,3));
            }
        }
    }
    private static Vector3 LavaVertex(World world,Vector3 at,Vector3 point)
        =>at+new Vector3(point.X,point.Y==0?0:Lava.CornerHeight(world,(int)(at.X+point.X),(int)at.Y,(int)(at.Z+point.Z)),point.Z);

    private readonly record struct Face(Point3 Offset, Vector3 Normal, float Shade, Vector3 A, Vector3 B, Vector3 C, Vector3 D);
}
