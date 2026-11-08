using System;
using Microsoft.Xna.Framework;

using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Worlds;
using MDSM.Audio;

namespace MDSM.Gameplay;

public sealed class MiningController
{
    public const double BreakCooldownSeconds=.15;
    public double CooldownRemaining { get; private set; }
    public Point3? Target { get; private set; }
    public float Progress { get; private set; }
    private BlockType _block;
    private float _elapsed;
    private float _hitSoundSeconds;
    private float _failedHitSeconds;
    private Point3? _failedHitCell;
    private ItemStack _tool;
    private int _slot=-1;
    private readonly Random _random;
    public MiningController(int? seed=null)=>_random=seed.HasValue?new Random(seed.Value):Random.Shared;

    public void Reset() { Target = null; Progress = _elapsed = _hitSoundSeconds = 0; _block = BlockType.Air; }

    public bool Update(World world, DroppedItems items, BlockHit? selection, bool held, float elapsed,Inventory inventory=null,bool creative=false)
    {
        double dt=float.IsFinite(elapsed) && elapsed>0?elapsed:0;
        _failedHitSeconds=Math.Max(0,_failedHitSeconds-(float)dt);
        double blocked=Math.Min(CooldownRemaining,dt);
        CooldownRemaining=Math.Max(0,CooldownRemaining-dt);
        elapsed=(float)(dt-blocked);
        if(CooldownRemaining>1e-9){Reset();return false;}
        if (!held || selection is not BlockHit hit || !float.IsFinite(hit.Distance) || hit.Distance < 0 || hit.Distance > BlockRaycaster.Reach
            || !world.Contains(hit.Cell.X, hit.Cell.Y, hit.Cell.Z)) { Reset(); return false; }
        BlockType block = world.GetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z);
        ItemStack tool=inventory?.Selected??default;
        if(block==BlockType.Fire)
        {
            bool extinguished=world.RemoveHarvestedBlock(hit.Cell,items,0,true);
            Reset();if(extinguished)CooldownRemaining=BreakCooldownSeconds;return extinguished;
        }
        if (creative && Blocks.IsRenderable(block) && !Lava.Fluid(block))
        {
            BlockContainer contents = world.Containers.Get(hit.Cell);
            world.Containers.Detach(hit.Cell);
            bool changed = world.RemoveHarvestedBlock(hit.Cell,items,0,creative:true);
            if (!changed) world.Containers.Reattach(contents);
            if(changed){world.Sounds?.Block(SoundKind.Break,block,BlockSounds.Center(hit.Cell));world.Particles.Burst(block,BlockSounds.Center(hit.Cell));}
            Reset();if(changed)CooldownRemaining=BreakCooldownSeconds;return changed;
        }
        float seconds = MiningRules.Seconds(block,tool);
        if (!Blocks.IsRenderable(block) || seconds < 0)
        {
            // An unharvestable solid can still be struck. Throttle this feedback
            // independently of the repeatedly reset (invalid) mining progress.
            if(Blocks.IsSolid(block) && (_failedHitCell!=hit.Cell || _failedHitSeconds<=0))
            {world.Sounds?.Block(SoundKind.Hit,block,BlockSounds.Center(hit.Cell));_failedHitCell=hit.Cell;_failedHitSeconds=.22f;}
            Reset();return false;
        }
        if (Target != hit.Cell || _block != block || _tool!=tool || _slot!=(inventory?.SelectedSlot??-1))
        { Reset(); Target = hit.Cell; _block = block;_tool=tool;_slot=inventory?.SelectedSlot??-1; }
        if(seconds>0 && (_hitSoundSeconds-=elapsed)<=0)
        {world.Sounds?.Block(SoundKind.Hit,block,BlockSounds.Center(hit.Cell));_hitSoundSeconds=.22f;}
        if (float.IsFinite(elapsed) && elapsed > 0) _elapsed += elapsed;
        Progress = seconds == 0 ? 1 : Math.Clamp(_elapsed / seconds, 0, 1);
        if (Progress < 1) return false;
        if(block==BlockType.Leaves && !items.CanSpawn(3))return false;
        Span<ItemId> loot=stackalloc ItemId[5];
        int lootCount=MiningRules.Drops(block,tool,_random,loot);
        BlockContainer container=world.Containers.Get(hit.Cell);
        int drops=lootCount;
        if(container!=null)for(int i=0;i<container.SlotCount;i++)if(!container[i].IsEmpty)drops++;
        if (!items.CanSpawn(drops)) return false;
        world.Containers.Detach(hit.Cell);
        bool removed=world.RemoveHarvestedBlock(hit.Cell,items,drops);
        if (!removed) { world.Containers.Reattach(container);Reset(); return false; }
        world.Sounds?.Block(SoundKind.Break,block,BlockSounds.Center(hit.Cell));
        world.Particles.Burst(block,BlockSounds.Center(hit.Cell));
        Vector3 center = new(hit.Cell.X + 0.5f, hit.Cell.Y + 0.5f, hit.Cell.Z + 0.5f);
        for(int i=0;i<lootCount;i++)items.Spawn(loot[i],1,center);
        if(container!=null)for(int i=0;i<container.SlotCount;i++)if(!container[i].IsEmpty)items.TrySpawn(container[i],center,.35f);
        if(inventory!=null && (MiningRules.Wears(tool) || block is BlockType.Leaves or BlockType.DeadBush && tool.Item==Items.Shears))inventory.WearSelected();
        Reset();
        CooldownRemaining=BreakCooldownSeconds;
        return true;
    }
}

public static class BlockPlacement
{
    public static bool TryPlace(World world, Inventory inventory, BlockHit hit, BoundingBox playerBounds,bool creative=false,float yaw=0)
    {
        if(!hit.HasPlacementFace)return false;
        if(Items.TryGetBlock(inventory.Selected.Item,out var block) && World.OrientedBuilding(block))
        {
            Point3 cell=hit.PlacementCell;bool upper=hit.Normal.Y<0 || hit.Normal.Y==0 && hit.Position is Vector3 p && p.Y-MathF.Floor(p.Y)>.5f;
            if(Blocks.Get(block).Shape==BlockShape.Slab && world.GetBlock(hit.Cell.X,hit.Cell.Y,hit.Cell.Z)==block
                && (hit.Normal.Y>0 && (world.Structure(hit.Cell).Facing&4)==0 || hit.Normal.Y<0 && (world.Structure(hit.Cell).Facing&4)!=0))
            {cell=hit.Cell;upper=(world.Structure(cell).Facing&4)==0;}
            byte facing=(byte)(((int)MathF.Round(yaw/MathHelper.PiOver2)+4)%4);
            Point3 support=new(-hit.Normal.X,-hit.Normal.Y,-hit.Normal.Z);
            if(block==BlockType.Sign && support.Y==0)facing=(byte)(support.X<0?1:support.X>0?3:support.Z<0?2:0);
            else if(block==BlockType.Sign)
            {int octant=((int)MathF.Round(yaw/MathHelper.PiOver4)%8+8)%8;facing=(byte)(octant/2+((octant&1)<<3));}
            if(!world.PlaceBuilding(cell,block,facing,upper,playerBounds,support))return false;
            if(!creative)inventory.ConsumeSelected();return true;
        }
        return TryPlace(world,inventory,hit.PlacementCell,playerBounds,creative,new(-hit.Normal.X,-hit.Normal.Y,-hit.Normal.Z));
    }
    public static bool TryPlace(World world, Inventory inventory, Point3 cell, BoundingBox playerBounds,bool creative=false,Point3? support=null)
    {
        ItemStack stack = inventory.Selected;
        if (stack.IsEmpty || !Items.TryGetBlock(stack.Item, out BlockType block)
            || !(block==BlockType.Bed?world.PlaceBed(cell,0,playerBounds):block is BlockType.Door or BlockType.FenceGate or BlockType.Torch?world.PlaceStructure(cell,block,0,support??new(0,-1,0),playerBounds):world.TryPlace(cell, block, playerBounds,support))) return false;
        if (!creative) inventory.ConsumeSelected();
        return true;
    }
}
