using System;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public static class SurvivalInteractions
{
    public static bool BeforeBlocks(GameSession session)
    {
        ItemId item=session.Inventory.Selected.Item;
        if(item==Items.Wheat)
        {
            PassiveMob mob=EntityRaycaster.Cast(session.World,session.Mobs,session.Camera.Position,session.Camera.Forward);
            if(mob!=null){session.Mobs.Feed(mob,session.Inventory);return true;}
        }
        if(item==Items.Bucket)
        {
            var hit=BlockRaycaster.Cast(session.World,session.Camera.Position,session.Camera.Forward,includeFluids:true);
            if(hit is BlockHit fluid && session.World.GetBlock(fluid.Cell.X,fluid.Cell.Y,fluid.Cell.Z) is BlockType.WaterSource or BlockType.LavaSource)
            {UseBucket(session,null);return true;}
        }
        return false;
    }
    public static bool Use(GameSession session,BlockHit? hit)
    {
        ItemStack held=session.Inventory.Selected;World world=session.World;
        if(hit is BlockHit structure && world.Toggle(structure.Cell))return true;
        if(held.Item==Items.Wheat)
        {session.Mobs.Feed(EntityRaycaster.Cast(world,session.Mobs,session.Camera.Position,session.Camera.Forward),session.Inventory);return true;}
        if(held.Item==Items.Bucket || held.Item==Items.WaterBucket || held.Item==Items.LavaBucket)
        {UseBucket(session,hit);return true;}
        if(hit is not BlockHit block)return false;
        if(held.Item==Items.WheatSeeds){world.Farming.Plant(block.Cell,session.Inventory);return true;}
        if(Items.Get(held.Item).Tool==ToolKind.Hoe){world.Farming.Till(block.Cell,session.Inventory);return true;}
        if(Items.TryGetBlock(held.Item,out BlockType type) && type is BlockType.Door or BlockType.FenceGate or BlockType.Torch)
        {
            if(block.HasPlacementFace && world.PlaceStructure(block.PlacementCell,type,(byte)(((int)MathF.Round(session.Camera.Yaw/MathHelper.PiOver2)+4)%4),new(-block.Normal.X,-block.Normal.Y,-block.Normal.Z),session.Player.Bounds))session.Inventory.ConsumeSelected();
            return true;
        }
        return false;
    }
    public static bool UseBucket(GameSession session,BlockHit? solidHit)
    {
        Inventory inventory=session.Inventory;ItemStack held=inventory.Selected;World world=session.World;var copy=inventory.Clone();
        Point3 cell;BlockType next;ItemId receive;
        if(held.Item==Items.Bucket)
        {
            BlockHit? target=BlockRaycaster.Cast(world,session.Camera.Position,session.Camera.Forward,includeFluids:true);
            if(target is not BlockHit fluid)return false;BlockType block=world.GetBlock(fluid.Cell.X,fluid.Cell.Y,fluid.Cell.Z);
            if(block is not (BlockType.WaterSource or BlockType.LavaSource))return false;
            cell=fluid.Cell;next=BlockType.Air;receive=block==BlockType.WaterSource?Items.WaterBucket:Items.LavaBucket;
        }
        else
        {
            if(held.Item!=Items.WaterBucket && held.Item!=Items.LavaBucket || solidHit is not BlockHit target || !target.HasPlacementFace)return false;
            cell=target.PlacementCell;BlockType before=world.GetBlock(cell.X,cell.Y,cell.Z);
            if(!world.Contains(cell.X,cell.Y,cell.Z) || before is BlockType.WaterSource or BlockType.LavaSource || before!=BlockType.Air && !Lava.Fluid(before) && Blocks.Get(before).Shape!=BlockShape.Cross)return false;
            next=held.Item==Items.WaterBucket?BlockType.WaterSource:BlockType.LavaSource;receive=Items.Bucket;
        }
        if(held.Count==1)copy.SetSlot(copy.SelectedSlot,new(receive,1));
        else {copy.ConsumeSelected();if(!copy.Add(new ItemStack(receive,1)).IsEmpty)return false;}
        if(!world.SetBlock(cell.X,cell.Y,cell.Z,next))return false;
        for(int i=0;i<Inventory.SlotCount;i++)inventory.SetSlot(i,copy[i]);return true;
    }
}
