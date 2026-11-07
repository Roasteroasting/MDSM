using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;

namespace MDSM.Worlds;

public sealed partial class World
{
    private int _blockChangeDepth;
    private Point3 _blockChangeRoot;
    private Random _supportLootRandom;
    private readonly record struct SupportChange(Point3 Cell,BlockType Previous,BlockType Next,bool Drops);
    private readonly record struct SupportDrop(Point3 Cell,BlockType Previous,ItemId Item);

    // Mining/decay already own the root's loot. Reserve those entities as well as
    // the dependent removals before making any edit; nested callbacks share one plan.
    internal bool RemoveHarvestedBlock(Point3 cell,DroppedItems items,int reservedDrops,bool creative=false)
        => ChangeWithSupportDrops(cell,BlockType.Air,items,reservedDrops,creative,true);

    private bool ChangeWithSupportDrops(Point3 cell,BlockType next,DroppedItems items,int reservedDrops,bool suppress,bool rootHarvested)
    {
        if(_blockChangeDepth!=0)return SetBlockCore(cell.X,cell.Y,cell.Z,next);
        if(!Contains(cell.X,cell.Y,cell.Z) || !Blocks.IsDefined(next))return false;
        BlockType previous=GetBlock(cell.X,cell.Y,cell.Z);
        if(previous==next)return false;
        List<SupportDrop> drops=items!=null && !suppress && !SuppressSupportDrops
            ? PlanSupportDrops(cell,previous,next,rootHarvested):null;
        if(items!=null && !items.CanSpawn(reservedDrops+(drops?.Count??0)))return false;
        bool changed;
        _blockChangeRoot=cell;_blockChangeDepth++;
        try { changed=SetBlockCore(cell.X,cell.Y,cell.Z,next); }
        finally { _blockChangeDepth--; }
        if(changed && drops!=null)
            foreach(var drop in drops)
                if(GetBlock(drop.Cell.X,drop.Cell.Y,drop.Cell.Z)!=drop.Previous)
                    items.Spawn(drop.Item,1,new Vector3(drop.Cell.X+.5f,drop.Cell.Y+.5f,drop.Cell.Z+.5f));
        return changed;
    }

    private List<SupportDrop> PlanSupportDrops(Point3 cell,BlockType previous,BlockType next,bool rootHarvested)
    {
        BlockType above=GetBlock(cell.X,cell.Y+1,cell.Z);
        bool cropRemoved=Blocks.Crop(previous) && !Blocks.Crop(next);
        // Most fluid-level/terrain changes cannot remove dependents. No allocation
        // or world scan is needed in that common path.
        if(!cropRemoved && previous is not (BlockType.Door or BlockType.DoorUpper) && !Blocks.DoubleGrass(previous)
            && !(Blocks.IsSolid(previous) && !Blocks.IsSolid(next))
            && !(Blocks.Get(above).NeedsGrass && !Blocks.SupportsPlant(next,above)))return null;
        var changes=new List<SupportChange>{new(cell,previous,next,cropRemoved && !rootHarvested)};
        var seen=new HashSet<Point3>{cell};
        List<SupportDrop> drops=null;
        Span<ItemId> loot=stackalloc ItemId[5];
        for(int index=0;index<changes.Count;index++)
        {
            var change=changes[index];Point3 p=change.Cell;
            if(change.Drops)
            {
                _supportLootRandom??=new Random(unchecked(Seed^0x53555050));
                int count=MiningRules.Drops(change.Previous,default,_supportLootRandom,loot);
                for(int i=0;i<count;i++)(drops??=new()).Add(new(p,change.Previous,loot[i]));
            }
            // The second half is structural cleanup, never another logical harvest.
            if(change.Previous is BlockType.Door or BlockType.DoorUpper)
            {
                Point3 other=p+new Point3(0,change.Previous==BlockType.Door?1:-1,0);
                if(GetBlock(other.X,other.Y,other.Z) is BlockType.Door or BlockType.DoorUpper)Remove(other,false);
            }
            if(Blocks.DoubleGrass(change.Previous))
            {
                Point3 other=p+new Point3(0,change.Previous==BlockType.DoubleTallGrassLower?1:-1,0);
                BlockType expected=change.Previous==BlockType.DoubleTallGrassLower?BlockType.DoubleTallGrassUpper:BlockType.DoubleTallGrassLower;
                if(GetBlock(other.X,other.Y,other.Z)==expected)Remove(other,false);
            }
            if(!Blocks.IsSolid(change.Next))foreach(Point3 direction in Adjacent)
            {
                Point3 neighbor=p+direction;BlockType block=GetBlock(neighbor.X,neighbor.Y,neighbor.Z);
                if(block==BlockType.Torch && neighbor+Structure(neighbor).Support==p
                    || direction.Y==1 && block==BlockType.Door)Remove(neighbor,true);
            }
            Point3 upper=p+new Point3(0,1,0);BlockType plant=GetBlock(upper.X,upper.Y,upper.Z);
            if(Blocks.Get(plant).NeedsGrass && !Blocks.SupportsPlant(change.Next,plant))Remove(upper,true);
        }
        return drops;

        void Remove(Point3 p,bool harvest)
        {
            if(seen.Add(p))changes.Add(new(p,GetBlock(p.X,p.Y,p.Z),BlockType.Air,harvest));
        }
    }
}
