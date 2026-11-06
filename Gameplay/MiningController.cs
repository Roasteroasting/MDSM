using System;
using Microsoft.Xna.Framework;

using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public sealed class MiningController
{
    public const double BreakCooldownSeconds=.15;
    public double CooldownRemaining { get; private set; }
    public Point3? Target { get; private set; }
    public float Progress { get; private set; }
    private BlockType _block;
    private float _elapsed;
    private ItemStack _tool;
    private int _slot=-1;
    private readonly Random _random;
    public MiningController(int? seed=null)=>_random=seed.HasValue?new Random(seed.Value):Random.Shared;

    public void Reset() { Target = null; Progress = _elapsed = 0; _block = BlockType.Air; }

    public bool Update(World world, DroppedItems items, BlockHit? selection, bool held, float elapsed,Inventory inventory=null,bool creative=false)
    {
        double dt=float.IsFinite(elapsed) && elapsed>0?elapsed:0;
        double blocked=Math.Min(CooldownRemaining,dt);
        CooldownRemaining=Math.Max(0,CooldownRemaining-dt);
        elapsed=(float)(dt-blocked);
        if(CooldownRemaining>1e-9){Reset();return false;}
        if (!held || selection is not BlockHit hit || !float.IsFinite(hit.Distance) || hit.Distance < 0 || hit.Distance > BlockRaycaster.Reach
            || !world.Contains(hit.Cell.X, hit.Cell.Y, hit.Cell.Z)) { Reset(); return false; }
        BlockType block = world.GetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z);
        ItemStack tool=inventory?.Selected??default;
        if (creative && Blocks.IsRenderable(block) && !Lava.Fluid(block))
        {
            BlockContainer contents = world.Containers.Get(hit.Cell);
            world.Containers.Detach(hit.Cell);
            world.SuppressHarvest = true;
            bool changed;
            try { changed = world.SetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z, BlockType.Air); }
            finally { world.SuppressHarvest = false; }
            if (!changed) world.Containers.Reattach(contents);
            Reset();if(changed)CooldownRemaining=BreakCooldownSeconds;return changed;
        }
        float seconds = MiningRules.Seconds(block,tool);
        if (!Blocks.IsRenderable(block) || seconds < 0) { Reset(); return false; }
        if (Target != hit.Cell || _block != block || _tool!=tool || _slot!=(inventory?.SelectedSlot??-1))
        { Reset(); Target = hit.Cell; _block = block;_tool=tool;_slot=inventory?.SelectedSlot??-1; }
        if (float.IsFinite(elapsed) && elapsed > 0) _elapsed += elapsed;
        Progress = seconds == 0 ? 1 : Math.Clamp(_elapsed / seconds, 0, 1);
        if (Progress < 1) return false;
        BlockType above = world.GetBlock(hit.Cell.X, hit.Cell.Y + 1, hit.Cell.Z);
        bool removesPlant = !Blocks.DoubleGrass(block) && Blocks.Get(above).NeedsGrass;
        if(block==BlockType.Leaves && !items.CanSpawn(3))return false;
        Span<ItemId> loot=stackalloc ItemId[5];
        int lootCount=MiningRules.Drops(block,tool,_random,loot);
        BlockContainer container=world.Containers.Get(hit.Cell);
        Span<ItemId> plantLoot=stackalloc ItemId[5];int plantCount=removesPlant?MiningRules.Drops(above,default,_random,plantLoot):0;
        int drops=lootCount+plantCount;
        if(container!=null)for(int i=0;i<container.SlotCount;i++)if(!container[i].IsEmpty)drops++;
        if (!items.CanSpawn(drops)) return false;
        world.Containers.Detach(hit.Cell);
        world.SuppressHarvest=true;bool removed;
        try{removed=world.SetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z, BlockType.Air);}finally{world.SuppressHarvest=false;}
        if (!removed) { world.Containers.Reattach(container);Reset(); return false; }
        Vector3 center = new(hit.Cell.X + 0.5f, hit.Cell.Y + 0.5f, hit.Cell.Z + 0.5f);
        for(int i=0;i<lootCount;i++)items.Spawn(loot[i],1,center);
        if(container!=null)for(int i=0;i<container.SlotCount;i++)if(!container[i].IsEmpty)items.TrySpawn(container[i],center,.35f);
        for(int i=0;i<plantCount;i++)items.Spawn(plantLoot[i],1,center+Vector3.Up);
        if(inventory!=null && (MiningRules.Wears(tool) || block==BlockType.Leaves && tool.Item==Items.Shears))inventory.WearSelected();
        Reset();
        CooldownRemaining=BreakCooldownSeconds;
        return true;
    }
}

public static class BlockPlacement
{
    public static bool TryPlace(World world, Inventory inventory, BlockHit hit, BoundingBox playerBounds,bool creative=false)
        =>hit.HasPlacementFace && TryPlace(world,inventory,hit.PlacementCell,playerBounds,creative,new(-hit.Normal.X,-hit.Normal.Y,-hit.Normal.Z));
    public static bool TryPlace(World world, Inventory inventory, Point3 cell, BoundingBox playerBounds,bool creative=false,Point3? support=null)
    {
        ItemStack stack = inventory.Selected;
        if (stack.IsEmpty || !Items.TryGetBlock(stack.Item, out BlockType block)
            || !(block is BlockType.Door or BlockType.FenceGate or BlockType.Torch?world.PlaceStructure(cell,block,0,support??new(0,-1,0),playerBounds):world.TryPlace(cell, block, playerBounds,support))) return false;
        if (!creative) inventory.ConsumeSelected();
        return true;
    }
}
