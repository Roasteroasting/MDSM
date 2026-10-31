using System;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public enum MiningCategory { Fragile, Earth, Wood, Mineral }
public readonly record struct MiningProfile(MiningCategory Category, float Hardness, ToolTier Required = ToolTier.None);
public static class MiningRules
{
    public static MiningProfile Profile(BlockType block) => block switch
    {
        BlockType.Stone => new(MiningCategory.Mineral,1.5f,ToolTier.Wood),
        BlockType.Cobblestone => new(MiningCategory.Mineral,2,ToolTier.Wood),
        BlockType.CoalOre => new(MiningCategory.Mineral,3,ToolTier.Wood),
        BlockType.IronOre => new(MiningCategory.Mineral,3,ToolTier.Stone),
        BlockType.DiamondOre => new(MiningCategory.Mineral,3,ToolTier.Iron),
        BlockType.Furnace => new(MiningCategory.Mineral,3.5f,ToolTier.Wood),
        BlockType.WoodLog or BlockType.WoodenPlanks or BlockType.CraftingTable or BlockType.Chest => new(MiningCategory.Wood,Blocks.Get(block).MiningSeconds/1.5f),
        BlockType.Grass or BlockType.Dirt or BlockType.Sand or BlockType.Gravel => new(MiningCategory.Earth,Blocks.Get(block).MiningSeconds/1.5f),
        _ => new(MiningCategory.Fragile,Blocks.Get(block).MiningSeconds/1.5f)
    };
    public static bool Suitable(MiningProfile profile,ItemDefinition tool) => tool.Tool == (profile.Category switch
    {MiningCategory.Mineral=>ToolKind.Pickaxe,MiningCategory.Wood=>ToolKind.Axe,MiningCategory.Earth=>ToolKind.Shovel,_=>ToolKind.None});
    public static float Seconds(BlockType block,ItemStack held)
    {
        if(Blocks.Get(block).MiningSeconds<0)return -1;
        MiningProfile profile=Profile(block);ItemDefinition tool=Items.Get(held.Item);
        if(profile.Category==MiningCategory.Mineral && tool.Tool!=ToolKind.Pickaxe)return -1;
        float efficiency=Suitable(profile,tool)?tool.Efficiency:1;
        float penalty=profile.Required>tool.Tier && profile.Category==MiningCategory.Mineral?4:1;
        return profile.Hardness*1.5f*penalty/efficiency;
    }
    public static bool Harvests(BlockType block,ItemStack tool) => Profile(block).Category!=MiningCategory.Mineral
        || Items.Get(tool.Item).Tool==ToolKind.Pickaxe && Items.Get(tool.Item).Tier>=Profile(block).Required;
    public static ItemId? Drop(BlockType block,ItemStack held,Random random)
    {
        if(!Harvests(block,held) || block==BlockType.Glass)return null;
        return block switch
        {
            BlockType.Stone=>Items.FromBlock(BlockType.Cobblestone),BlockType.CoalOre=>Items.Coal,BlockType.DiamondOre=>Items.Diamond,
            BlockType.Gravel when random.Next(10)==0=>Items.Flint,_=>Items.FromBlock(block)
        };
    }
    public static bool Wears(ItemStack stack) => Items.Get(stack.Item).Tool is >=ToolKind.Pickaxe and <=ToolKind.Shovel;
}
