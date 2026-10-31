using System;

using MDSM.Worlds;

namespace MDSM.Inventories;

// Inventory deals in item IDs. Only the current item registry maps them to blocks;
// future non-block items can extend this registry without changing stack operations.
public readonly record struct ItemId(ushort Value);
public enum ToolKind { None, Pickaxe, Axe, Sword, Shovel, Bow }
public enum ToolTier { None, Wood, Stone, Iron, Diamond }
public readonly record struct ItemDefinition(string Name, int StackLimit = 64, ToolKind Tool = ToolKind.None,
    ToolTier Tier = ToolTier.None, int Durability = 0, int Sprite = -1)
{
    public float Efficiency => Tier switch { ToolTier.Wood => 2, ToolTier.Stone => 4, ToolTier.Iron => 6, ToolTier.Diamond => 8, _ => 1 };
    public int AttackDamage => Tool == ToolKind.Sword ? 3 + (int)Tier : Tool is not (ToolKind.None or ToolKind.Bow) ? 1 + (int)Tier / 2 : 0;
}

public static class Items
{
    public static readonly ItemId RawPorkchop = new(256); // Non-block IDs begin above the byte block range.
    public static readonly ItemId Leather = new(257);
    public static readonly ItemId Feather = new(258);
    public static readonly ItemId IronIngot = new(259), Coal = new(260), Flint = new(261), Diamond = new(262),
        Stick = new(263), Arrow = new(264), String = new(265), Charcoal = new(266), Bow = new(283);
    private static readonly ItemDefinition[] Definitions = CreateDefinitions();
    private static readonly ItemId[] SpriteItems = CreateSprites();
    private static ItemDefinition[] CreateDefinitions()
    {
        var definitions = new ItemDefinition[28];
        string[] names = { "RAW PORKCHOP", "LEATHER", "FEATHER", "IRON INGOT", "COAL", "FLINT", "DIAMOND", "STICK", "ARROW", "STRING", "CHARCOAL" };
        for (int i=0;i<names.Length;i++) definitions[i] = new(names[i], Sprite: i-3);
        int[] uses = {59,131,250,1561};
        for(int tier=1;tier<=4;tier++)for(int kind=1;kind<=4;kind++)
            definitions[Tool((ToolTier)tier,(ToolKind)kind).Value-256] = new($"{(tier==1?"WOODEN":((ToolTier)tier).ToString())} {(ToolKind)kind}".ToUpperInvariant(),1,(ToolKind)kind,(ToolTier)tier,uses[tier-1],8+(tier-1)*4+kind-1);
        definitions[27]=new("BOW",1,ToolKind.Bow,ToolTier.None,384,24);
        return definitions;
    }
    private static ItemId[] CreateSprites() { var all=new ItemId[28];for(int i=0;i<all.Length;i++)all[i]=new((ushort)(256+i));return all; }
    public static ItemId Tool(ToolTier tier,ToolKind kind) => tier is >= ToolTier.Wood and <= ToolTier.Diamond && kind is >= ToolKind.Pickaxe and <= ToolKind.Shovel
        ? new ItemId((ushort)(267+((int)tier-1)*4+(int)kind-1)) : throw new ArgumentException("Invalid tiered tool.");
    public static ItemDefinition Get(ItemId item) => item.Value >=256 && item.Value <284 ? Definitions[item.Value-256]
        : TryGetBlock(item,out BlockType block) ? new(Blocks.Get(block).Name) : default;
    public static int StackLimit(ItemId item) => Get(item).StackLimit;
    public static ReadOnlySpan<ItemId> Sprites => SpriteItems;
    public static bool IsSprite(ItemId item) => item.Value >=256 && item.Value <284;
    public static bool IsDefined(ItemId item) => IsSprite(item) || TryGetBlock(item, out _);
    public static ItemId FromBlock(BlockType block) => Blocks.IsPlaceable(block)
        ? new ItemId((byte)block) : throw new ArgumentException("This block has no item.", nameof(block));
    public static bool TryGetBlock(ItemId item, out BlockType block)
    {
        block = item.Value <= byte.MaxValue ? (BlockType)item.Value : BlockType.Air;
        return item.Value != 0 && Blocks.IsPlaceable(block);
    }
    public static string Name(ItemId item) => IsDefined(item) ? Get(item).Name : "";
}

public readonly record struct ItemStack
{
    public const int Maximum = 64;
    public ItemId Item { get; }
    public int Count { get; }
    public int Durability { get; }
    public Guid InstanceId { get; }
    public int Limit => IsEmpty ? Maximum : Items.StackLimit(Item);
    public bool IsEmpty => Count == 0;
    public ItemStack(ItemId item, int count, int durability = -1, Guid instanceId = default)
    {
        if (!Items.IsDefined(item) || count < 1 || count > Items.StackLimit(item))
            throw new ArgumentException("Invalid item stack quantity.");
        int maximumDurability = Items.Get(item).Durability;
        if (maximumDurability > 0)
        {
            if(durability == -1)durability=maximumDurability;
            if(durability<1 || durability>maximumDurability)throw new ArgumentException("Invalid equipment durability.");
            InstanceId=instanceId==Guid.Empty?Guid.NewGuid():instanceId;
        }
        else
        {
            if(durability>0 || instanceId!=Guid.Empty)throw new ArgumentException("Ordinary items cannot carry equipment metadata.");
            durability=0;InstanceId=Guid.Empty;
        }
        Item = item;
        Count = count;
        Durability=durability;
    }
    public ItemStack WithCount(int count) => count == 0 ? default : new ItemStack(Item, count, Durability, InstanceId);
    public ItemStack Wear() => Durability <= 0 ? this : Durability == 1 ? default : new(Item,1,Durability-1,InstanceId);
    public ItemStack Fresh() => Durability>0 ? new(Item,1) : this;
    public bool Compatible(ItemStack other) => !IsEmpty && !other.IsEmpty && Item==other.Item && Limit>1;
}
