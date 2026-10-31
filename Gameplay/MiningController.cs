using System;
using Microsoft.Xna.Framework;

using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public sealed class MiningController
{
    public Point3? Target { get; private set; }
    public float Progress { get; private set; }
    private BlockType _block;
    private float _elapsed;
    private ItemStack _tool;
    private int _slot=-1;
    private readonly Random _random;
    public MiningController(int? seed=null)=>_random=seed.HasValue?new Random(seed.Value):Random.Shared;

    public void Reset() { Target = null; Progress = _elapsed = 0; _block = BlockType.Air; }

    public bool Update(World world, DroppedItems items, BlockHit? selection, bool held, float elapsed,Inventory inventory=null)
    {
        if (!held || selection is not BlockHit hit || !float.IsFinite(hit.Distance) || hit.Distance < 0 || hit.Distance > BlockRaycaster.Reach
            || !world.Contains(hit.Cell.X, hit.Cell.Y, hit.Cell.Z)) { Reset(); return false; }
        BlockType block = world.GetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z);
        ItemStack tool=inventory?.Selected??default;
        float seconds = MiningRules.Seconds(block,tool);
        if (!Blocks.IsRenderable(block) || seconds < 0) { Reset(); return false; }
        if (Target != hit.Cell || _block != block || _tool!=tool || _slot!=(inventory?.SelectedSlot??-1))
        { Reset(); Target = hit.Cell; _block = block;_tool=tool;_slot=inventory?.SelectedSlot??-1; }
        if (float.IsFinite(elapsed) && elapsed > 0) _elapsed += elapsed;
        Progress = seconds == 0 ? 1 : Math.Clamp(_elapsed / seconds, 0, 1);
        if (Progress < 1) return false;
        BlockType above = world.GetBlock(hit.Cell.X, hit.Cell.Y + 1, hit.Cell.Z);
        bool removesPlant = Blocks.Get(above).NeedsGrass;
        if(block==BlockType.Leaves && !items.CanSpawn(3))return false;
        Span<ItemId> loot=stackalloc ItemId[3];
        int lootCount=MiningRules.Drops(block,tool,_random,loot);
        BlockContainer container=world.Containers.Get(hit.Cell);
        bool plantDrop=removesPlant && above!=BlockType.TallGrass;
        int drops=lootCount+(plantDrop?1:0);
        if(container!=null)for(int i=0;i<container.SlotCount;i++)if(!container[i].IsEmpty)drops++;
        if (!items.CanSpawn(drops)) return false;
        world.Containers.Detach(hit.Cell);
        if (!world.SetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z, BlockType.Air)) { world.Containers.Reattach(container);Reset(); return false; }
        Vector3 center = new(hit.Cell.X + 0.5f, hit.Cell.Y + 0.5f, hit.Cell.Z + 0.5f);
        for(int i=0;i<lootCount;i++)items.Spawn(loot[i],1,center);
        if(container!=null)for(int i=0;i<container.SlotCount;i++)if(!container[i].IsEmpty)items.TrySpawn(container[i],center,.35f);
        if (plantDrop) items.Spawn(Items.FromBlock(above), 1, center + Vector3.Up);
        if(inventory!=null && (MiningRules.Wears(tool) || block==BlockType.Leaves && tool.Item==Items.Shears))inventory.WearSelected();
        Reset();
        return true;
    }
}

public static class BlockPlacement
{
    public static bool TryPlace(World world, Inventory inventory, Point3 cell, BoundingBox playerBounds)
    {
        ItemStack stack = inventory.Selected;
        if (stack.IsEmpty || !Items.TryGetBlock(stack.Item, out BlockType block)
            || !world.TryPlace(cell, block, playerBounds)) return false;
        inventory.ConsumeSelected();
        return true;
    }
}
