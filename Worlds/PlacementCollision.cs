using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed partial class World
{
    private BlockType PlacementNeighbor(Point3 cell,Point3? changed,BlockType replacement)
        => changed==cell?replacement:GetBlock(cell.X,cell.Y,cell.Z);

    // Preview the same boxes used by movement, without mutating blocks, metadata,
    // revisions or fluid queues. Populations are bound by the active GameSession.
    private bool PlacementBlockedByMobs(Point3 cell,BlockType block,StructureState? proposed=null)
    {
        if (!Blocks.IsSolid(block) || LiveMobs==null && LiveHostiles==null) return false;
        System.Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        int count=ShapeBoxes(cell,block,boxes,true,proposed);
        for(int i=0;i<count;i++)if(MobOverlaps(boxes[i]))return true;
        // Adding a solid neighbor can extend an existing Pane or Fence into an
        // animal standing in its previously open space, even outside the new cell.
        foreach(Point3 direction in Adjacent)
        {
            if(direction.Y!=0)continue;
            Point3 neighbor=cell+direction;
            BlockType existing=GetBlock(neighbor.X,neighbor.Y,neighbor.Z);
            if(existing is not (BlockType.GlassPane or BlockType.Fence) && Blocks.Get(existing).Shape!=BlockShape.Stairs)continue;
            count=ShapeBoxes(neighbor,existing,boxes,true,changedNeighbor:cell,replacement:block,neighborState:proposed);
            for(int i=0;i<count;i++)if(MobOverlaps(boxes[i]))return true;
        }
        return false;
    }

    private bool MobOverlaps(BoundingBox bounds)
    {
        if(LiveMobs!=null)
            for(int i=0;i<LiveMobs.Entities.Count;i++)
            {var mob=LiveMobs.Entities[i];if(!mob.IsDead && Overlaps(bounds,mob.Bounds))return true;}
        if(LiveHostiles!=null)
            for(int i=0;i<LiveHostiles.Entities.Count;i++)
            {var mob=LiveHostiles.Entities[i];if(!mob.IsDead && Overlaps(bounds,mob.Bounds))return true;}
        return false;
    }
}
