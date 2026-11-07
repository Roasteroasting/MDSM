using System;

using MDSM.Worlds;

namespace MDSM.Inventories;

// Inventory deals in item IDs. Only the current item registry maps them to blocks;
// future non-block items can extend this registry without changing stack operations.
public readonly record struct ItemId(ushort Value);
public enum ToolKind { None, Pickaxe, Axe, Sword, Shovel, Bow, Shears, Hoe }
public enum ToolTier { None, Wood, Stone, Iron, Diamond, Obsidian }
public enum ArmorSlot { None=-1, Helmet, Chestplate, Leggings, Boots }
public enum ArmorMaterial { Leather, Wood, Stone, Iron, Diamond, Obsidian }
public readonly record struct ItemDefinition(string Name, int StackLimit = 64, ToolKind Tool = ToolKind.None,
    ToolTier Tier = ToolTier.None, int Durability = 0, int Sprite = -1, ArmorSlot Armor=ArmorSlot.None,int ArmorPoints=0)
{
    public float Efficiency => Tier switch { ToolTier.Wood => 2, ToolTier.Stone => 4, ToolTier.Iron => 6, ToolTier.Diamond => 8, ToolTier.Obsidian=>10, _ => 1 };
    public int AttackDamage => Tool == ToolKind.Sword ? Tier==ToolTier.Obsidian?9:3 + (int)Tier : Tool is >= ToolKind.Pickaxe and <= ToolKind.Shovel ? Tier==ToolTier.Obsidian?4:1 + (int)Tier / 2 : 0;
}

public static class Items
{
    public static readonly ItemId RawPorkchop = new(256); // Non-block IDs begin above the byte block range.
    public static readonly ItemId Leather = new(257);
    public static readonly ItemId Feather = new(258);
    public static readonly ItemId IronIngot = new(259), Coal = new(260), Flint = new(261), Diamond = new(262),
        Stick = new(263), Arrow = new(264), String = new(265), Charcoal = new(266), Bow = new(283);
    public static readonly ItemId Sapling = new((byte)BlockType.Sapling), Apple = new(284), Shears = new(285), CookedPorkchop = new(286);
    public static readonly ItemId WheatSeeds=new(287),Wheat=new(288),Bread=new(289),RottenFlesh=new(290),Bone=new(291),
        Bucket=new(292),WaterBucket=new(293),LavaBucket=new(294);
    private static readonly ItemDefinition[] Definitions = CreateDefinitions();
    private static readonly ItemId[] SpriteItems = CreateSprites();
    private static readonly ItemId[] Catalog = CreateCatalog();
    public static ReadOnlySpan<ItemId> Obtainable => Catalog;
    private static ItemId[] CreateCatalog()
    {
        var all = new System.Collections.Generic.List<ItemId>();
        for (int id = 1; id < 256 + Definitions.Length; id++)
        { var item = new ItemId((ushort)id); if (IsDefined(item)) all.Add(item); }
        return all.ToArray();
    }
    private static ItemDefinition[] CreateDefinitions()
    {
        var definitions = new ItemDefinition[72];
        string[] names = { "RAW PORKCHOP", "LEATHER", "FEATHER", "IRON INGOT", "COAL", "FLINT", "DIAMOND", "STICK", "ARROW", "STRING", "CHARCOAL" };
        for (int i=0;i<names.Length;i++) definitions[i] = new(names[i], i==0?1:64, Sprite: i);
        int[] uses = {59,131,250,1561};
        for(int tier=1;tier<=4;tier++)for(int kind=1;kind<=4;kind++)
            definitions[Tool((ToolTier)tier,(ToolKind)kind).Value-256] = new($"{(tier==1?"WOODEN":((ToolTier)tier).ToString())} {(ToolKind)kind}".ToUpperInvariant(),1,(ToolKind)kind,(ToolTier)tier,uses[tier-1],11+(tier-1)*4+kind-1);
        definitions[27]=new("BOW",1,ToolKind.Bow,ToolTier.None,384,27);
        definitions[28]=new("APPLE",Sprite:29);
        definitions[29]=new("SHEARS",1,ToolKind.Shears,Durability:238,Sprite:30);
        definitions[30]=new("COOKED PORKCHOP",1,Sprite:31);
        string[] additions={"WHEAT SEEDS","WHEAT","BREAD","ROTTEN FLESH","BONE","BUCKET","WATER BUCKET","LAVA BUCKET"};
        for(int i=0;i<additions.Length;i++)definitions[31+i]=new(additions[i],i>=6?1:64,Sprite:32+i);
        for(int tier=1;tier<=5;tier++)definitions[Tool((ToolTier)tier,ToolKind.Hoe).Value-256]=new($"{(ToolTier)tier} HOE".ToUpperInvariant(),1,ToolKind.Hoe,(ToolTier)tier,tier==5?10000:uses[tier-1],39+tier);
        for(int kind=1;kind<=4;kind++)definitions[Tool(ToolTier.Obsidian,(ToolKind)kind).Value-256]=new($"OBSIDIAN {(ToolKind)kind}".ToUpperInvariant(),1,(ToolKind)kind,ToolTier.Obsidian,10000,44+kind);
        int[,] points={{1,2,2,1},{1,3,3,1},{2,4,3,2},{2,6,5,2},{3,8,6,3},{4,10,8,4}};
        int[,] durability={{55,80,75,50},{80,120,110,75},{150,220,200,140},{180,250,225,165},{380,550,500,350},{2800,4000,3600,2500}};
        for(int material=0;material<6;material++)for(int slot=0;slot<4;slot++)
            definitions[48+material*4+slot]=new($"{(ArmorMaterial)material} {(ArmorSlot)slot}".ToUpperInvariant(),1,Durability:durability[material,slot],Sprite:49+material*4+slot,Armor:(ArmorSlot)slot,ArmorPoints:points[material,slot]);
        return definitions;
    }
    private static ItemId[] CreateSprites() { var all=new ItemId[74];for(int i=0;i<72;i++)all[i]=new((ushort)(256+i));all[72]=Sapling;all[73]=new((byte)BlockType.Bed);return all; }
    public static ItemId Armor(ArmorMaterial material,ArmorSlot slot)=>material is >=ArmorMaterial.Leather and <=ArmorMaterial.Obsidian && slot is >=ArmorSlot.Helmet and <=ArmorSlot.Boots
        ?new((ushort)(304+(int)material*4+(int)slot)):throw new ArgumentException("Invalid armor.");
    public static bool LavaResistant(ItemId item)=>item.Value==(byte)BlockType.Obsidian || Get(item).Tier==ToolTier.Obsidian || item.Value is >=324 and <=327;
    public static ItemId Tool(ToolTier tier,ToolKind kind) => kind==ToolKind.Hoe && tier is >=ToolTier.Wood and <=ToolTier.Obsidian
        ?new((ushort)(tier==ToolTier.Obsidian?303:294+(int)tier))
        :tier==ToolTier.Obsidian && kind is >=ToolKind.Pickaxe and <=ToolKind.Shovel?new((ushort)(298+(int)kind))
        :tier is >= ToolTier.Wood and <= ToolTier.Diamond && kind is >= ToolKind.Pickaxe and <= ToolKind.Shovel
        ? new ItemId((ushort)(267+((int)tier-1)*4+(int)kind-1)) : throw new ArgumentException("Invalid tiered tool.");
    public static ItemDefinition Get(ItemId item) => item.Value >=256 && item.Value <328 ? Definitions[item.Value-256]
        : TryGetBlock(item,out BlockType block) ? new(Blocks.Get(block).Name,StackLimit:block==BlockType.Bed?1:64,Sprite:block==BlockType.Sapling?28:block==BlockType.Bed?73:-1) : default;
    public static int StackLimit(ItemId item) => Get(item).StackLimit;
    public static ReadOnlySpan<ItemId> Sprites => SpriteItems;
    public static bool IsSprite(ItemId item) => item.Value >=256 && item.Value <328 || item==Sapling || item.Value==(byte)BlockType.Bed;
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

    // Only binary compatibility readers may temporarily hold pre-v9 stacked food.
    // It is split into valid stacks before the loaded world is returned to gameplay.
    private ItemStack(ItemId item,int count,bool legacy) { Item=item;Count=count;Durability=0;InstanceId=Guid.Empty; }
    internal static ItemStack ReadLegacyFood(ItemId item,int count) => item==Items.RawPorkchop && count is >=1 and <=64
        ?new ItemStack(item,count,true):throw new ArgumentException("Invalid legacy food stack.");
}
