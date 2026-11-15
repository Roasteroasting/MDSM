using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Gameplay;

internal static class ExplorationTools
{
    // Select/swap existing stacks first. A new Creative item never overwrites
    // an occupied slot without moving its previous contents somewhere safe.
    internal static bool Pick(GameSession session,BlockHit? hit)
    {
        if(!session.Player.Creative || hit is not BlockHit target)return false;
        BlockType block=session.World.GetBlock(target.Cell.X,target.Cell.Y,target.Cell.Z);
        block=block switch
        {
            BlockType.DoorUpper=>BlockType.Door,BlockType.BedHead=>BlockType.Bed,
            BlockType.DoubleWoodenSlab=>BlockType.WoodenSlab,BlockType.DoubleCobblestoneSlab=>BlockType.CobblestoneSlab,
            BlockType.DoubleTallGrassUpper=>BlockType.DoubleTallGrassLower,
            BlockType.FarmlandDry or BlockType.FarmlandWet=>BlockType.Dirt,_=>block
        };
        ItemId item;
        if(Water.IsWater(block))item=Items.WaterBucket;
        else if(Lava.IsLava(block))item=Items.LavaBucket;
        else if(Blocks.Crop(block))item=block>=BlockType.Potato1?Items.Potato:block>=BlockType.Carrot1?Items.Carrot:Items.WheatSeeds;
        else if(Blocks.IsPlaceable(block))item=Items.FromBlock(block);
        else return false;
        var inventory=session.Inventory;
        for(int i=0;i<Inventory.HotbarSlots;i++)if(!inventory[i].IsEmpty && inventory[i].Item==item){inventory.SelectedSlot=i;return true;}
        int selected=inventory.SelectedSlot;
        for(int i=Inventory.HotbarSlots;i<Inventory.SlotCount;i++)if(!inventory[i].IsEmpty && inventory[i].Item==item)
        {var old=inventory[selected];inventory.SetSlot(selected,inventory[i]);inventory.SetSlot(i,old);return true;}
        int empty=-1;for(int i=0;i<Inventory.SlotCount;i++)if(inventory[i].IsEmpty){empty=i;break;}
        if(empty<0)return false;
        if(empty<Inventory.HotbarSlots)inventory.SelectedSlot=empty;
        else {inventory.SetSlot(empty,inventory[selected]);}
        inventory.SetSlot(inventory.SelectedSlot,new(item,1));return true;
    }

    // Reconstruct from sparse persisted chests, so old saves work without a new
    // structure index or scanning hundreds of millions of world blocks.
    internal static Point3? NearestDungeon(World world,Vector3 player)
    {
        var tested=new HashSet<Point3>();Point3? nearest=null;float best=float.MaxValue;
        foreach(var pair in world.Containers.Entries)
        {
            if(pair.Value.Block!=BlockType.Chest)continue;
            for(int z=-2;z<=2;z+=4)for(int x=-2;x<=2;x+=4)
            {
                Point3 center=pair.Key+new Point3(x,0,z);
                if(!tested.Add(center))continue;
                float distance=Vector3.DistanceSquared(player,new(center.X+.5f,center.Y,center.Z+.5f));
                if(distance>=best || !Room(world,center))continue;
                best=distance;nearest=center;
            }
        }
        return nearest;
    }
    private static bool Room(World world,Point3 p)
    {
        if(!world.Contains(p.X-3,p.Y-1,p.Z-3)||!world.Contains(p.X+3,p.Y+5,p.Z+3))return false;
        for(int z=-3;z<=3;z++)for(int x=-3;x<=3;x++)
            if(!Masonry(world.GetBlock(p.X+x,p.Y-1,p.Z+z))||!Masonry(world.GetBlock(p.X+x,p.Y+5,p.Z+z)))return false;
        for(int y=0;y<5;y++)for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++)
            if(world.GetBlock(p.X+x,p.Y+y,p.Z+z) is not (BlockType.Air or BlockType.Chest))return false;
        return true;
    }
    private static bool Masonry(BlockType block)=>block is BlockType.Cobblestone or BlockType.MossyCobblestone;

    internal static bool Teleport(GameSession session,Vector3 destination)
    {
        var world=session.World;var player=session.Player;
        if(!float.IsFinite(destination.X)||!float.IsFinite(destination.Y)||!float.IsFinite(destination.Z)
            ||destination.X<Player.HalfWidth||destination.Z<Player.HalfWidth||destination.X>world.Width-Player.HalfWidth||destination.Z>world.Depth-Player.HalfWidth
            ||destination.Y<0||destination.Y+Player.BodyHeight>World.Height+(player.Creative?16:0))return false;
        var bounds=new BoundingBox(destination+new Vector3(-Player.HalfWidth,0,-Player.HalfWidth),destination+new Vector3(Player.HalfWidth,Player.BodyHeight,Player.HalfWidth));
        if(world.IntersectsSolid(bounds)||Water.Intersects(world,bounds)||Lava.Intersects(world,bounds)||world.Fire.Contact(bounds))return false;
        foreach(var mob in session.Mobs.Entities)if(World.Overlaps(bounds,mob.Bounds))return false;
        foreach(var mob in session.Hostiles.Entities)if(World.Overlaps(bounds,mob.Bounds))return false;
        player.Teleport(destination);session.Camera.Position=player.EyePosition;
        session.Mining.Reset();session.Bow.Cancel();session.Bobbing.Reset();session.Swing.Reset();
        return true;
    }
}
