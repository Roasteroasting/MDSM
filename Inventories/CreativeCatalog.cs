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
        if(Items.TryGetBlock(id,out var block))return BlockGroup(block)*100+id.Value;
        if(d.Tool!=ToolKind.None)return 1000+(int)d.Tier*20+(int)d.Tool;
        if(d.Armor!=ArmorSlot.None)return 2000+(int)((id.Value-304)/4)*10+(int)d.Armor;
        return 3000+id.Value;
    }
    private static int BlockGroup(MDSM.Worlds.BlockType b)=>b switch
    {
        MDSM.Worlds.BlockType.Grass or MDSM.Worlds.BlockType.Dirt or MDSM.Worlds.BlockType.Sand or MDSM.Worlds.BlockType.Gravel=>0,
        MDSM.Worlds.BlockType.Stone or MDSM.Worlds.BlockType.Cobblestone or MDSM.Worlds.BlockType.MossyCobblestone or MDSM.Worlds.BlockType.CobblestoneSlab or MDSM.Worlds.BlockType.CobblestoneStairs or MDSM.Worlds.BlockType.Bedrock or MDSM.Worlds.BlockType.Obsidian=>1,
        MDSM.Worlds.BlockType.WoodLog or MDSM.Worlds.BlockType.WoodenPlanks or MDSM.Worlds.BlockType.WoodenSlab or MDSM.Worlds.BlockType.WoodenStairs or MDSM.Worlds.BlockType.Fence or MDSM.Worlds.BlockType.FenceGate or MDSM.Worlds.BlockType.Door or MDSM.Worlds.BlockType.Sign=>2,
        MDSM.Worlds.BlockType.Glass or MDSM.Worlds.BlockType.GlassPane or MDSM.Worlds.BlockType.WhiteWool=>3,
        MDSM.Worlds.BlockType.Leaves or MDSM.Worlds.BlockType.Sapling or MDSM.Worlds.BlockType.TallGrass or MDSM.Worlds.BlockType.RedFlower or MDSM.Worlds.BlockType.YellowFlower or MDSM.Worlds.BlockType.Cactus or MDSM.Worlds.BlockType.DeadBush=>4,
        MDSM.Worlds.BlockType.CoalOre or MDSM.Worlds.BlockType.IronOre or MDSM.Worlds.BlockType.DiamondOre=>5,
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
