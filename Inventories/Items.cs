using System;

using MDSM.Worlds;

namespace MDSM.Inventories;

// Inventory deals in item IDs. Only the current item registry maps them to blocks;
// future non-block items can extend this registry without changing stack operations.
public readonly record struct ItemId(ushort Value);

public static class Items
{
    public static bool IsDefined(ItemId item) => TryGetBlock(item, out _);
    public static ItemId FromBlock(BlockType block) => Blocks.IsPlaceable(block)
        ? new ItemId((byte)block) : throw new ArgumentException("This block has no item.", nameof(block));
    public static bool TryGetBlock(ItemId item, out BlockType block)
    {
        block = item.Value <= byte.MaxValue ? (BlockType)item.Value : BlockType.Air;
        return item.Value != 0 && Blocks.IsPlaceable(block);
    }
    public static string Name(ItemId item) => TryGetBlock(item, out BlockType block) ? Blocks.Get(block).Name : "";
}

public readonly record struct ItemStack
{
    public const int Maximum = 64;
    public ItemId Item { get; }
    public int Count { get; }
    public bool IsEmpty => Count == 0;
    public ItemStack(ItemId item, int count)
    {
        if (!Items.IsDefined(item) || count < 1 || count > Maximum)
            throw new ArgumentException("An item stack must contain 1-64 valid items.");
        Item = item;
        Count = count;
    }
    public ItemStack WithCount(int count) => count == 0 ? default : new ItemStack(Item, count);
}
