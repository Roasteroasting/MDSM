namespace MDSM.Inventories;

public static class CreativeCatalog
{
    // Fresh equipment metadata is created on each grant, never copied from a cached icon.
    public static int Grant(Inventory inventory, ItemId item, bool fullStack)
    {
        if (!Items.IsDefined(item)) return 0;
        int count = fullStack ? Items.StackLimit(item) : 1;
        ItemStack remaining = inventory.Add(new ItemStack(item, count));
        return count - remaining.Count;
    }
}
