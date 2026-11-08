using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Audio;

namespace MDSM.Worlds;

public readonly record struct StructureState(Point3 Cell,byte Facing,bool Open,Point3 Support);
public sealed partial class World
{
    public FarmingSimulation Farming { get; private set; }
    public Dictionary<Point3,StructureState> Structures { get; }=new();
    public TorchIndex Torches { get; }=new();
    internal DroppedItems LiveDrops { get; set; }
    internal PassiveMobPopulation LiveMobs { get; set; }
    internal HostilePopulation LiveHostiles { get; set; }
    internal bool SuppressSupportDrops { get; set; }
    private bool _structureEdit;
    private Point3? _doorPartnerEdit;
    public CaveStats GeneratedCaves { get; internal set; }
    internal List<Point3> CaveOreSites { get; set; }=new();
    internal void NoteCaveOres(int coal,int iron,int diamond,int veins)=>GeneratedOres=GeneratedOres with {Coal=GeneratedOres.Coal+coal,Iron=GeneratedOres.Iron+iron,Diamond=GeneratedOres.Diamond+diamond,Veins=GeneratedOres.Veins+veins};
    private static readonly Point3[] Adjacent={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
    private void ResolveFluidContact(Point3 cell)
    {
        bool water=Water.IsWater(GetBlock(cell.X,cell.Y,cell.Z));
        if(water)
        {foreach(Point3 direction in Adjacent){Point3 p=cell+direction;if(Lava.IsLava(GetBlock(p.X,p.Y,p.Z)) && SetBlock(p.X,p.Y,p.Z,BlockType.Obsidian))Sounds?.At(SoundKind.Fizz,BlockSounds.Center(p));}}
        else if(Lava.IsLava(GetBlock(cell.X,cell.Y,cell.Z)))
            foreach(Point3 direction in Adjacent){Point3 p=cell+direction;if(Water.IsWater(GetBlock(p.X,p.Y,p.Z))){if(SetBlock(cell.X,cell.Y,cell.Z,BlockType.Obsidian))Sounds?.At(SoundKind.Fizz,BlockSounds.Center(cell));break;}}
    }
    public StructureState Structure(Point3 cell)
    {
        if(GetBlock(cell.X,cell.Y,cell.Z)==BlockType.DoorUpper)cell+=new Point3(0,-1,0);
        return Structures.TryGetValue(cell,out var state)?state:new(cell,0,false,new(0,-1,0));
    }
    public bool Toggle(Point3 cell)
    {
        BlockType block=GetBlock(cell.X,cell.Y,cell.Z);if(block is not (BlockType.FenceGate or BlockType.Door or BlockType.DoorUpper))return false;
        var state=Structure(cell);Structures[state.Cell]=state with {Open=!state.Open};
        Sounds?.At(block==BlockType.FenceGate?(state.Open?SoundKind.GateClose:SoundKind.GateOpen):(state.Open?SoundKind.DoorClose:SoundKind.DoorOpen),BlockSounds.Center(state.Cell));
        DirtyDetail(state.Cell);return true;
    }
    internal void DirtyDetail(Point3 cell)
    {
        Revision++;for(int z=Math.Max(0,cell.Z/16-1);z<=Math.Min(ChunksZ-1,cell.Z/16+1);z++)for(int x=Math.Max(0,cell.X/16-1);x<=Math.Min(ChunksX-1,cell.X/16+1);x++)GetChunk(x,z).MarkDirty();
    }
    public bool PlaceStructure(Point3 cell,BlockType block,byte facing,Point3 support,BoundingBox player)
    {
        if (support.Y>0 && Blocks.RejectsUnderside(block)) return false;
        if(Structures.Count>=65536)return false;
        byte orientation=(byte)(facing%4);
        if(block==BlockType.Door)
        {Vector3 center=(player.Min+player.Max)*.5f;if((orientation%2==0?center.X-cell.X:center.Z-cell.Z)<.5f)orientation|=4;}
        var proposed = new StructureState(cell,orientation,false,support);
        if(block==BlockType.Door)
        {
            if(!Contains(cell.X,cell.Y+1,cell.Z) || GetBlock(cell.X,cell.Y,cell.Z)!=BlockType.Air || GetBlock(cell.X,cell.Y+1,cell.Z)!=BlockType.Air
                || !SupportsVoxelTop(new(cell.X,cell.Y-1,cell.Z)) || Overlaps(new(new(cell.X,cell.Y,cell.Z),new(cell.X+1,cell.Y+2,cell.Z+1)),player)
                || PlacementBlockedByMobs(cell,block,proposed)
                || PlacementBlockedByMobs(cell+new Point3(0,1,0),BlockType.DoorUpper,proposed))return false;
            _structureEdit=true;SetBlock(cell.X,cell.Y,cell.Z,block);SetBlock(cell.X,cell.Y+1,cell.Z,BlockType.DoorUpper);_structureEdit=false;
        }
        else
        {
            if(block==BlockType.Torch && !IsSolid(cell.X+support.X,cell.Y+support.Y,cell.Z+support.Z))return false;
            if(block==BlockType.Torch && (support.Y<0?!SupportsVoxelTop(cell+support):Blocks.Get(GetBlock(cell.X+support.X,cell.Y,cell.Z+support.Z)).Shape is BlockShape.Slab or BlockShape.Stairs))return false;
            if(!TryPlaceBlock(cell,block,player,support,proposed))return false;
        }
        Structures[cell]=proposed;if(block==BlockType.Torch)Torches.Add(cell);DirtyDetail(cell);
        if(block==BlockType.Door)Sounds?.Block(SoundKind.Place,block,BlockSounds.Center(cell));return true;
    }
    private bool CanDetailChange(Point3 cell,BlockType previous,BlockType next)
    {
        if(Blocks.Bed(next) && !_bedEdit)return false;
        if(Blocks.Crop(next) && !Blocks.Crop(previous) && !Farming.CanPlant)return false;
        if((next is BlockType.Torch or BlockType.Door or BlockType.FenceGate || OrientedBuilding(next)) && !Structures.ContainsKey(cell) && Structures.Count>=65536)return false;
        return true; // The outer block-change context reserves the complete cascade's loot.
    }
    private void DetailChanged(Point3 cell,BlockType previous,BlockType next)
    {
        BedChanged(cell,previous,next);
        Farming.Changed(cell,previous,next);
        if(previous==BlockType.Torch)Torches.Remove(cell);
        if(next==BlockType.Torch){Torches.Add(cell);Structures.TryAdd(cell,new(cell,0,false,new(0,-1,0)));}
        if(next is BlockType.FenceGate or BlockType.Door)Structures.TryAdd(cell,new(cell,0,false,new(0,-1,0)));
        if(OrientedBuilding(next))Structures.TryAdd(cell,new(cell,0,false,new(0,-1,0)));
        if(previous==BlockType.Sign && next!=previous)Signs.Remove(cell);
        if(next==BlockType.Sign)Signs.TryAdd(cell,"");
        if(previous!=next && next is not (BlockType.Torch or BlockType.FenceGate or BlockType.Door) && !Blocks.Bed(next) && !OrientedBuilding(next))Structures.Remove(cell);
        if(!_structureEdit && _doorPartnerEdit!=cell && previous is BlockType.Door or BlockType.DoorUpper)
        {
            Point3 other=cell+new Point3(0,previous==BlockType.Door?1:-1,0);
            Point3? prior=_doorPartnerEdit;_doorPartnerEdit=other;
            try
            {
                if(GetBlock(other.X,other.Y,other.Z) is BlockType.Door or BlockType.DoorUpper)SetBlock(other.X,other.Y,other.Z,BlockType.Air);
                Structures.Remove(other);
            }
            finally { _doorPartnerEdit=prior; }
        }
        // Only adjacent special placements need support invalidation.
        foreach(Point3 direction in Adjacent)
        {
            Point3 p=cell+direction;BlockType b=GetBlock(p.X,p.Y,p.Z);
            if(b is BlockType.Torch or BlockType.Sign && !IsSolid(cell.X,cell.Y,cell.Z) && p+Structure(p).Support==cell)
                SetBlock(p.X,p.Y,p.Z,BlockType.Air);
            if(direction.Y==1 && b==BlockType.Door && !IsSolid(cell.X,cell.Y,cell.Z))
                SetBlock(p.X,p.Y,p.Z,BlockType.Air);
            if(direction.Y==1 && b==BlockType.Snow && !IsSolid(cell.X,cell.Y,cell.Z))
                SetBlock(p.X,p.Y,p.Z,BlockType.Air);
            if(b==BlockType.Cactus && !CanCactus(p))SetBlock(p.X,p.Y,p.Z,BlockType.Air);
        }
    }
    // Shared geometry for voxel meshes, collision and accurate ray hitboxes. World-space boxes.
    public int BlockBoxes(Point3 cell,Span<BoundingBox> boxes,bool collision=false)
        => ShapeBoxes(cell,GetBlock(cell.X,cell.Y,cell.Z),boxes,collision);

    private int ShapeBoxes(Point3 cell,BlockType block,Span<BoundingBox> boxes,bool collision,
        StructureState? proposed=null,Point3? changedNeighbor=null,BlockType replacement=BlockType.Air,StructureState? neighborState=null)
    {
        BlockShape shape=Blocks.Get(block).Shape;
        if(collision && !Blocks.IsSolid(block))return 0;
        if(shape==BlockShape.Fire)return Fire.SelectionBoxes(cell,boxes);
        if(OrientedBuilding(block))return BuildingBoxes(cell,block,boxes,proposed,changedNeighbor,replacement,neighborState);
        if(shape==BlockShape.Pane)return PaneBoxes(cell,PaneConnections(cell,changedNeighbor,replacement),boxes);
        int count=0;Vector3 origin=new(cell.X,cell.Y,cell.Z);
        count=1;boxes[0]=new(origin,origin+Vector3.One);
        if(shape==BlockShape.Cactus){boxes[0]=new(origin+new Vector3(1f/16,0,1f/16),origin+new Vector3(15f/16,1,15f/16));return 1;}
        if(shape==BlockShape.Fluid)
        {float height=Lava.IsLava(block)?Lava.Height(block):Water.Height(block);boxes[0]=new(origin,origin+new Vector3(1,height,1));return 1;}
        if(shape is BlockShape.Farmland){boxes[0]=new(origin,origin+new Vector3(1,15f/16,1));return 1;}
        if(shape==BlockShape.Snow){boxes[0]=new(origin,origin+new Vector3(1,3f/16,1));return 1;}
        if(shape==BlockShape.Bed){boxes[0]=new(origin,origin+new Vector3(1,9f/16,1));return 1;}
        if (shape is BlockShape.Cross or BlockShape.Crop)
        {
            float inset = shape == BlockShape.Crop || Blocks.DoubleGrass(block) ? .1f : block == BlockType.TallGrass ? .15f : .25f;
            boxes[0] = new(origin + new Vector3(inset, 0, inset), origin + new Vector3(1-inset, Blocks.DoubleGrass(block)?1:Blocks.PlantHeight(block), 1-inset));
            return 1;
        }
        if(shape==BlockShape.Torch)
        {Point3 support=Structure(cell).Support;Vector3 shift=new(support.X*.28f,support.Y==0?.2f:0,support.Z*.28f);boxes[0]=new(origin+shift+new Vector3(.4f,0,.4f),origin+shift+new Vector3(.6f,.7f,.6f));return 1;}
        if(shape is BlockShape.Gate or BlockShape.Door)
        {
            var state=proposed??Structure(cell);if(collision && shape==BlockShape.Gate && state.Open)return 0;
            int facing=((state.Facing&3)+(state.Open?(state.Facing&4)!=0?3:1:0))%4;float height=collision && shape==BlockShape.Gate?1.5f:1;
            if(shape==BlockShape.Gate && !state.Open)
                boxes[0]=facing%2==0?new(origin+new Vector3(0,0,.40625f),origin+new Vector3(1,height,.59375f)):new(origin+new Vector3(.40625f,0,0),origin+new Vector3(.59375f,height,1));
            else boxes[0]=facing switch
            {
                0=>new(origin,origin+new Vector3(1,height,.1875f)),
                1=>new(origin+new Vector3(.8125f,0,0),origin+new Vector3(1,height,1)),
                2=>new(origin+new Vector3(0,0,.8125f),origin+new Vector3(1,height,1)),
                _=>new(origin,origin+new Vector3(.1875f,height,1))
            };
            return 1;
        }
        if(shape==BlockShape.Fence)
        {
            float height=collision?1.5f:1;count=1;boxes[0]=new(origin+new Vector3(.375f,0,.375f),origin+new Vector3(.625f,height,.625f));
            for(int axis=0;axis<4;axis++)
            {
                int dx=axis<2?axis==0?-1:1:0,dz=axis>=2?axis==2?-1:1:0;
                BlockType neighbor=PlacementNeighbor(new(cell.X+dx,cell.Y,cell.Z+dz),changedNeighbor,replacement);if(neighbor is not (BlockType.Fence or BlockType.FenceGate) && !Blocks.IsOpaque(neighbor))continue;
                for(int rail=0;rail<(collision?1:2);rail++)
                {
                    float low=collision?0:rail==0?.3f:.7f,high=collision?1.5f:low+.15f;
                    boxes[count++]=dx!=0?new(origin+new Vector3(dx<0?0:.5f,low,.4375f),origin+new Vector3(dx<0?.5f:1,high,.5625f))
                        :new(origin+new Vector3(.4375f,low,dz<0?0:.5f),origin+new Vector3(.5625f,high,dz<0?.5f:1));
                }
            }
            return count;
        }
        return count;
    }
}

public sealed class TorchIndex
{
    private readonly Dictionary<Point3,HashSet<Point3>> _cells=new();
    private static Point3 Bucket(Point3 p)=>new(p.X/16,p.Y/16,p.Z/16);
    public void Add(Point3 p){Point3 key=Bucket(p);if(!_cells.TryGetValue(key,out var set))_cells[key]=set=new();set.Add(p);}
    public void Remove(Point3 p){Point3 key=Bucket(p);if(_cells.TryGetValue(key,out var set)){set.Remove(p);if(set.Count==0)_cells.Remove(key);}}
    public bool Protected(Vector3 position)
    {
        Point3 key=Bucket(new((int)position.X,(int)position.Y,(int)position.Z));
        for(int y=key.Y-1;y<=key.Y+1;y++)for(int z=key.Z-1;z<=key.Z+1;z++)for(int x=key.X-1;x<=key.X+1;x++)
            if(_cells.TryGetValue(new(x,y,z),out var set))foreach(Point3 p in set)if(Vector3.DistanceSquared(position,new(p.X+.5f,p.Y+.5f,p.Z+.5f))<=225)return true;
        return false;
    }
}
