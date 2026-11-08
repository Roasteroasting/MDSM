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
        BlockType.Cobblestone or BlockType.CobblestoneSlab or BlockType.CobblestoneStairs or BlockType.DoubleCobblestoneSlab or BlockType.MossyCobblestone => new(MiningCategory.Mineral,2,ToolTier.Wood),
        BlockType.CoalOre => new(MiningCategory.Mineral,3,ToolTier.Wood),
        BlockType.IronOre => new(MiningCategory.Mineral,3,ToolTier.Stone),
        BlockType.DiamondOre => new(MiningCategory.Mineral,3,ToolTier.Iron),
        BlockType.Furnace => new(MiningCategory.Mineral,3.5f,ToolTier.Wood),
        BlockType.Smoker or BlockType.BlastFurnace => new(MiningCategory.Mineral,3.5f,ToolTier.Wood),
        BlockType.Obsidian => new(MiningCategory.Mineral,50,ToolTier.Diamond),
        BlockType.WoodLog or BlockType.WoodenPlanks or BlockType.CraftingTable or BlockType.Chest or BlockType.Fence or BlockType.FenceGate or BlockType.Door or BlockType.DoorUpper or BlockType.WoodenSlab or BlockType.DoubleWoodenSlab or BlockType.WoodenStairs or BlockType.Sign => new(MiningCategory.Wood,Blocks.Get(block).MiningSeconds/1.5f),
        BlockType.Grass or BlockType.Dirt or BlockType.Sand or BlockType.Gravel or BlockType.FarmlandDry or BlockType.FarmlandWet => new(MiningCategory.Earth,Blocks.Get(block).MiningSeconds/1.5f),
        _ => new(MiningCategory.Fragile,Blocks.Get(block).MiningSeconds/1.5f)
    };
    public static bool Suitable(MiningProfile profile,ItemDefinition tool) => tool.Tool == (profile.Category switch
    {MiningCategory.Mineral=>ToolKind.Pickaxe,MiningCategory.Wood=>ToolKind.Axe,MiningCategory.Earth=>ToolKind.Shovel,_=>ToolKind.None});
    public static float Seconds(BlockType block,ItemStack held)
    {
        if(Blocks.Get(block).MiningSeconds<0)return -1;
        MiningProfile profile=Profile(block);ItemDefinition tool=Items.Get(held.Item);
        if(block==BlockType.Leaves && tool.Tool==ToolKind.Shears)return .10f;
        if(profile.Category==MiningCategory.Mineral && tool.Tool!=ToolKind.Pickaxe)return -1;
        float efficiency=Suitable(profile,tool)?tool.Efficiency:1;
        float penalty=profile.Required>tool.Tier && profile.Category==MiningCategory.Mineral?4:1;
        // Preserve tier ratios and hand hardness; appropriate tools now take 1.6x longer.
        return profile.Hardness*1.5f*penalty/efficiency*(Suitable(profile,tool) && tool.Tier!=ToolTier.None?1.6f:1);
    }
    public static bool Harvests(BlockType block,ItemStack tool) => Profile(block).Category!=MiningCategory.Mineral
        || Items.Get(tool.Item).Tool==ToolKind.Pickaxe && Items.Get(tool.Item).Tier>=Profile(block).Required;
    public static ItemId? Drop(BlockType block,ItemStack held,Random random)
    {
        if(!Harvests(block,held) || block is BlockType.Glass or BlockType.GlassPane or BlockType.TallGrass or BlockType.Leaves || Blocks.Crop(block))return null;
        return block switch
        {
            BlockType.Stone=>Items.FromBlock(BlockType.Cobblestone),BlockType.CoalOre=>Items.Coal,BlockType.DiamondOre=>Items.Diamond,
            BlockType.FarmlandDry or BlockType.FarmlandWet=>Items.FromBlock(BlockType.Dirt),BlockType.DoorUpper=>Items.FromBlock(BlockType.Door),BlockType.BedHead=>Items.FromBlock(BlockType.Bed),
            BlockType.Gravel when random.Next(10)==0=>Items.Flint,_=>Items.FromBlock(block)
        };
    }
    public static bool Wears(ItemStack stack) => Items.Get(stack.Item).Tool is >=ToolKind.Pickaxe and <=ToolKind.Shovel;
    public static int Drops(BlockType block,ItemStack held,Random random,Span<ItemId> output)
    {
        if(block==BlockType.Fire)return 0;
        if(block==BlockType.DeadBush)
        {if(held.Item==Items.Shears){output[0]=Items.FromBlock(block);return 1;}int sticks=random.Next(3);for(int i=0;i<sticks;i++)output[i]=Items.Stick;return sticks;}
        if(block==BlockType.Snow){if(Items.Get(held.Item).Tool!=ToolKind.Shovel)return 0;output[0]=Items.Snowball;return 1;}
        if(block is BlockType.DoubleWoodenSlab or BlockType.DoubleCobblestoneSlab)
        {if(!Harvests(block,held))return 0;output[0]=output[1]=Items.FromBlock(block==BlockType.DoubleWoodenSlab?BlockType.WoodenSlab:BlockType.CobblestoneSlab);return 2;}
        if(block==BlockType.TallGrass || Blocks.DoubleGrass(block)){if(random.NextDouble()>=(Blocks.DoubleGrass(block)?.2:.1))return 0;output[0]=Items.WheatSeeds;return 1;}
        if(Blocks.Crop(block))
        {
            if(block>=BlockType.Carrot1)
            {
                ItemId produce=block>=BlockType.Potato1?Items.Potato:Items.Carrot;
                int quantity=Blocks.MatureCrop(block)?random.Next(2,5):1;for(int i=0;i<quantity;i++)output[i]=produce;return quantity;
            }
            int count=0;if(block==BlockType.Wheat7)output[count++]=Items.Wheat;
            int seeds=block==BlockType.Wheat7?random.Next(1,5):1;for(int i=0;i<seeds;i++)output[count++]=Items.WheatSeeds;return count;
        }
        if(block==BlockType.Leaves)
        {
            if(held.Item==Items.Shears){output[0]=Items.FromBlock(block);return 1;}
            return LeafLoot.Roll(random,output);
        }
        ItemId? drop=Drop(block,held,random);if(!drop.HasValue)return 0;output[0]=drop.Value;return 1;
    }
}
