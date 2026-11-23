using System;
using System.Collections.Generic;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

internal enum ShelterKind : byte { Outdoors, Enclosed, Glasshouse }

internal sealed class ShelterCache(World world)
{
    private const int HorizontalLimit=12, VerticalLimit=8, CellLimit=2048;
    private static readonly Point3[] Neighbors={new(1,0,0),new(-1,0,0),new(0,0,1),new(0,0,-1),new(0,-1,0),new(0,1,0)};
    private readonly Dictionary<ChunkAddress,Dictionary<Point3,ShelterKind>> _cache=new();
    private readonly HashSet<Point3> _visited=new();
    private readonly List<Point3> _open=new();

    internal bool ShelteredCrop(Point3 crop)=>At(crop)==ShelterKind.Glasshouse && (world.Lighting.Packed(crop)&15)>0;

    internal ShelterKind At(Point3 start)
    {
        var address=ChunkAddress.At(start.X,start.Z);
        if(_cache.TryGetValue(address,out var cells) && cells.TryGetValue(start,out var cached))return cached;
        var value=Inspect(start);
        if(cells==null)
        {
            if(_cache.Count>=128)_cache.Clear();
            _cache.Add(address,cells=new());
        }
        if(cells.Count<512)cells.Add(start,value);
        return value;
    }

    internal static bool AffectsEnclosure(BlockType before,BlockType after)
        => Boundary(before)!=Boundary(after);

    private static int Boundary(BlockType block)
    {
        if(Blocks.GlassCube(block))return 2;
        if(block is BlockType.Door or BlockType.DoorUpper)return 3;
        if(Blocks.GlassPane(block))return 4;
        return (Blocks.Get(block).Shape==BlockShape.Cube && Blocks.IsSolid(block)) || Blocks.Soil(block) ? 1 : 0;
    }

    internal void Invalidate(ChunkAddress changed)
    {
        if(_cache.Count==0)return;
        for(long z=changed.Z-1;z<=changed.Z+1;z++)for(long x=changed.X-1;x<=changed.X+1;x++)
            _cache.Remove(new(x,z));
    }

    private ShelterKind Inspect(Point3 start)
    {
        if(!world.IsLoaded(start) || start.Y>=world.SurfaceY(start.X,start.Z))return ShelterKind.Outdoors;
        _open.Clear();_visited.Clear();
        _open.Add(start);_visited.Add(start);
        int ceiling=0,glass=0;
        for(int i=0;i<_open.Count;i++)
        {
            Point3 cell=_open[i];
            if(cell.Y>=world.SurfaceY(cell.X,cell.Z))return ShelterKind.Outdoors;
            foreach(var delta in Neighbors)
            {
                var next=cell+delta;
                if(!world.IsLoaded(next))return ShelterKind.Outdoors;
                var block=world.GetBlock(next.X,next.Y,next.Z);
                int boundary=Boundary(block);
                if(boundary!=0 && (boundary!=3 || !world.Structure(next).Open) && (boundary!=4 || delta.Y==0))
                {
                    if(delta.Y>0){ceiling++;if(boundary==2)glass++;}
                    continue;
                }
                if(Math.Abs(next.X-start.X)>HorizontalLimit || Math.Abs(next.Z-start.Z)>HorizontalLimit
                    || Math.Abs(next.Y-start.Y)>VerticalLimit)return ShelterKind.Outdoors;
                if(!_visited.Add(next))continue;
                if(_open.Count>=CellLimit)return ShelterKind.Outdoors;
                _open.Add(next);
            }
        }
        return ceiling>0 && glass*2>=ceiling?ShelterKind.Glasshouse:ShelterKind.Enclosed;
    }
}
