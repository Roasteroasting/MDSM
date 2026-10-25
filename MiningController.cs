using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public sealed class MiningController
{
    public Point3? Target { get; private set; }
    public float Progress { get; private set; }
    private BlockType _block;
    private float _elapsed;

    public void Reset() { Target = null; Progress = _elapsed = 0; _block = BlockType.Air; }

    public bool Update(World world, DroppedItems items, BlockHit? selection, bool held, float elapsed)
    {
        if (!held || selection is not BlockHit hit || !float.IsFinite(hit.Distance) || hit.Distance < 0 || hit.Distance > BlockRaycaster.Reach
            || !world.Contains(hit.Cell.X, hit.Cell.Y, hit.Cell.Z)) { Reset(); return false; }
        BlockType block = world.GetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z);
        float seconds = Blocks.Get(block).MiningSeconds;
        if (!Blocks.IsRenderable(block) || seconds < 0) { Reset(); return false; }
        if (Target != hit.Cell || _block != block) { Reset(); Target = hit.Cell; _block = block; }
        if (float.IsFinite(elapsed) && elapsed > 0) _elapsed += elapsed;
        Progress = seconds == 0 ? 1 : Math.Clamp(_elapsed / seconds, 0, 1);
        if (Progress < 1) return false;
        BlockType above = world.GetBlock(hit.Cell.X, hit.Cell.Y + 1, hit.Cell.Z);
        bool removesPlant = Blocks.Get(above).NeedsGrass;
        if (!items.CanSpawn(removesPlant ? 2 : 1)) return false;
        if (!world.SetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z, BlockType.Air)) { Reset(); return false; }
        Vector3 center = new(hit.Cell.X + 0.5f, hit.Cell.Y + 0.5f, hit.Cell.Z + 0.5f);
        items.Spawn(Items.FromBlock(block), 1, center);
        if (removesPlant) items.Spawn(Items.FromBlock(above), 1, center + Vector3.Up);
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
