using MDSM.Worlds;

namespace MDSM.Inventories;

public static class CreativeCatalog
{
    private static readonly ItemId[] Sorted=Sort();
    public static System.ReadOnlySpan<ItemId> All=>Sorted;
    private static ItemId[] Sort()
    {
        var items=Items.Obtainable.ToArray();
        System.Array.Sort(items,(a,b)=>Key(a).CompareTo(Key(b)));
        return items;
    }
    private static int Key(ItemId id)
    {
        ItemDefinition d=Items.Get(id);
        if(Items.TryGetBlock(id,out var block))return BlockGroup(block)*256+id.Value;
        if(d.Tool!=ToolKind.None)return 2000+(int)d.Tier*20+(int)d.Tool;
        if(d.Armor!=ArmorSlot.None)return 3000+(int)((id.Value-304)/4)*10+(int)d.Armor;
        return 4000+id.Value;
    }
    private static int BlockGroup(BlockType b)=>b switch
    {
        BlockType.Grass or BlockType.Dirt or BlockType.Sand or BlockType.Gravel or BlockType.Clay=>0,
        BlockType.Stone or BlockType.Cobblestone or BlockType.MossyCobblestone or BlockType.CobblestoneSlab or BlockType.CobblestoneStairs
            or BlockType.Bedrock or BlockType.Obsidian or BlockType.BrickBlock or BlockType.CopperBlock=>1,
        BlockType.WoodLog or BlockType.WoodenPlanks or BlockType.WoodenSlab or BlockType.WoodenStairs or BlockType.Fence or BlockType.FenceGate
            or BlockType.Door or BlockType.Sign=>2,
        _ when Blocks.GlassCube(b) || Blocks.GlassPane(b) || b==BlockType.WhiteWool=>3,
        _ when Blocks.Flower(b) || b is BlockType.Leaves or BlockType.Sapling or BlockType.TallGrass or BlockType.Cactus or BlockType.DeadBush=>4,
        BlockType.CoalOre or BlockType.IronOre or BlockType.DiamondOre or BlockType.CopperOre=>5,
        _=>6
    };
    public static bool Matches(ItemId item,string search)=>Items.Name(item).Contains(search.Replace('_',' '),System.StringComparison.OrdinalIgnoreCase)
        || item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)==search;
    // Fresh equipment metadata is created on each grant, never copied from a cached icon.
    public static int Grant(Inventory inventory, ItemId item, bool fullStack)
    {
        if (!Items.IsDefined(item)) return 0;
        int count = fullStack ? Items.StackLimit(item) : 1;
        ItemStack remaining = inventory.Add(new ItemStack(item, count));
        return count - remaining.Count;
    }
}
