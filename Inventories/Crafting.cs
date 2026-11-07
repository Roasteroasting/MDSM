using System;
using MDSM.Worlds;

namespace MDSM.Inventories;

public readonly record struct CraftingIngredient(ItemId Item, int Count = 1);

// Patterns are never implicitly mirrored/rotated. Empty cells and all cells outside
// the pattern must be empty; a larger grid may translate the complete pattern.
public sealed class ShapedRecipe
{
    private readonly CraftingIngredient[] _ingredients;
    public int Width { get; }
    public int Height { get; }
    public ItemStack Output { get; }
    public ShapedRecipe(int width, int height, ReadOnlySpan<CraftingIngredient> ingredients, ItemStack output)
    {
        if (width < 1 || width > 3 || height < 1 || height > 3 || ingredients.Length != width * height || output.IsEmpty)
            throw new ArgumentException("Invalid crafting recipe.");
        bool any = false;
        foreach (var ingredient in ingredients)
            if (ingredient != default)
            {
                if (!Items.IsDefined(ingredient.Item) || ingredient.Count < 1 || ingredient.Count > ItemStack.Maximum)
                    throw new ArgumentException("Invalid recipe ingredient.");
                any = true;
            }
        if (!any) throw new ArgumentException("Recipes need ingredients.");
        Width = width; Height = height; _ingredients = ingredients.ToArray(); Output = output;
    }
    internal bool Matches(CraftingGrid grid, int offsetX, int offsetY)
    {
        for (int y = 0; y < grid.Width; y++) for (int x = 0; x < grid.Width; x++)
        {
            int px = x - offsetX, py = y - offsetY;
            CraftingIngredient need = px >= 0 && px < Width && py >= 0 && py < Height ? _ingredients[py * Width + px] : default;
            ItemStack actual = grid[y * grid.Width + x];
            if (need == default ? !actual.IsEmpty : actual.IsEmpty || actual.Item != need.Item || actual.Count < need.Count) return false;
        }
        return true;
    }
    internal void Consume(CraftingGrid grid, int offsetX, int offsetY)
    {
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
        {
            var need = _ingredients[y * Width + x]; if (need == default) continue;
            int slot = (y + offsetY) * grid.Width + x + offsetX;
            grid.SetSlot(slot, grid[slot].WithCount(grid[slot].Count - need.Count));
        }
    }
}

public static class CraftingRecipes
{
    private static readonly CraftingIngredient Planks = new(Items.FromBlock(BlockType.WoodenPlanks));
    private static readonly ShapedRecipe[] Definitions = Create();
    private static ShapedRecipe[] Create()
    {
        var recipes=new System.Collections.Generic.List<ShapedRecipe>();
        ItemId P=Items.FromBlock(BlockType.WoodenPlanks),C=Items.FromBlock(BlockType.Cobblestone),S=Items.Stick,T=Items.String;
        void Add(int w,int h,ItemId output,int count,params ItemId[] cells)
        {
            var pattern=new CraftingIngredient[cells.Length];for(int i=0;i<cells.Length;i++)if(cells[i].Value!=0)pattern[i]=new(cells[i]);
            recipes.Add(new(w,h,pattern,new ItemStack(output,count)));
        }
        Add(2,2,Items.FromBlock(BlockType.CraftingTable),1,P,P,P,P);
        Add(1,1,P,4,Items.FromBlock(BlockType.WoodLog));
        Add(1,2,S,4,P,P);
        Add(3,3,Items.FromBlock(BlockType.Chest),1,P,P,P,P,default,P,P,P,P);
        Add(3,3,Items.FromBlock(BlockType.Furnace),1,C,C,C,C,default,C,C,C,C);
        Add(1,3,Items.Arrow,4,Items.Flint,S,Items.Feather);
        Add(2,2,Items.FromBlock(BlockType.WhiteWool),1,T,T,T,T);
        Add(3,3,Items.Bow,1,default,S,T,S,default,T,default,S,T);
        Add(3,3,Items.Bow,1,T,S,default,T,default,S,T,S,default);
        Add(2,2,Items.Shears,1,Items.IronIngot,default,default,Items.IronIngot);
        Add(2,2,Items.Shears,1,default,Items.IronIngot,Items.IronIngot,default);
        for(int tier=1;tier<=4;tier++)
        {
            ItemId M=tier==1?P:tier==2?C:tier==3?Items.IronIngot:Items.Diamond;
            Add(3,3,Items.Tool((ToolTier)tier,ToolKind.Pickaxe),1,M,M,M,default,S,default,default,S,default);
            Add(2,3,Items.Tool((ToolTier)tier,ToolKind.Axe),1,M,M,M,S,default,S);
            Add(2,3,Items.Tool((ToolTier)tier,ToolKind.Axe),1,M,M,S,M,S,default);
            Add(1,3,Items.Tool((ToolTier)tier,ToolKind.Sword),1,M,M,S);
            Add(1,3,Items.Tool((ToolTier)tier,ToolKind.Shovel),1,M,S,S);
        }
        ItemId O=Items.FromBlock(BlockType.Obsidian),L=Items.FromBlock(BlockType.WoodLog),F=Items.FromBlock(BlockType.Furnace),I=Items.IronIngot;
        Add(3,1,Items.Bread,1,Items.Wheat,Items.Wheat,Items.Wheat);
        Add(3,2,Items.FromBlock(BlockType.Fence),3,P,S,P,P,S,P);
        Add(3,2,Items.FromBlock(BlockType.FenceGate),1,S,P,S,S,P,S);
        Add(2,3,Items.FromBlock(BlockType.Door),3,P,P,P,P,P,P);
        ItemId glass=Items.FromBlock(BlockType.Glass);
        Add(3,2,Items.FromBlock(BlockType.GlassPane),16,glass,glass,glass,glass,glass,glass);
        ItemId wool=Items.FromBlock(BlockType.WhiteWool);
        Add(3,2,Items.FromBlock(BlockType.Bed),1,wool,wool,wool,P,P,P);
        Add(1,2,Items.FromBlock(BlockType.Torch),4,Items.Coal,S);Add(1,2,Items.FromBlock(BlockType.Torch),4,Items.Charcoal,S);
        Add(3,2,Items.Bucket,1,I,default,I,default,I,default);
        Add(3,3,Items.FromBlock(BlockType.Smoker),1,default,L,default,L,F,L,default,L,default);
        Add(3,3,Items.FromBlock(BlockType.BlastFurnace),1,I,I,I,I,F,I,C,C,C);
        void MaterialRecipe(int w,int h,ItemId output,ItemId material,params char[] cells)
        {
            var pattern=new CraftingIngredient[cells.Length];
            for(int j=0;j<cells.Length;j++)if(cells[j]!='.')pattern[j]=new(cells[j]=='S'?S:material,cells[j]=='S'?1:material==O?6:1);
            recipes.Add(new(w,h,pattern,new ItemStack(output,1)));
        }
        for(int tier=1;tier<=5;tier++)
        {
            ItemId M=tier==1?P:tier==2?C:tier==3?I:tier==4?Items.Diamond:O;
            MaterialRecipe(2,3,Items.Tool((ToolTier)tier,ToolKind.Hoe),M,"MM.S.S".ToCharArray());
            MaterialRecipe(2,3,Items.Tool((ToolTier)tier,ToolKind.Hoe),M,"MMS.S.".ToCharArray());
        }
        MaterialRecipe(3,3,Items.Tool(ToolTier.Obsidian,ToolKind.Pickaxe),O,"MMM.S..S.".ToCharArray());
        MaterialRecipe(2,3,Items.Tool(ToolTier.Obsidian,ToolKind.Axe),O,"MMMS.S".ToCharArray());
        MaterialRecipe(2,3,Items.Tool(ToolTier.Obsidian,ToolKind.Axe),O,"MMSMS.".ToCharArray());
        MaterialRecipe(1,3,Items.Tool(ToolTier.Obsidian,ToolKind.Sword),O,"MMS".ToCharArray());
        MaterialRecipe(1,3,Items.Tool(ToolTier.Obsidian,ToolKind.Shovel),O,"MSS".ToCharArray());
        for(int material=0;material<6;material++)
        {
            ItemId M=material==0?Items.Leather:material==1?P:material==2?C:material==3?I:material==4?Items.Diamond:O;
            MaterialRecipe(3,2,Items.Armor((ArmorMaterial)material,ArmorSlot.Helmet),M,"MMMM.M".ToCharArray());
            MaterialRecipe(3,3,Items.Armor((ArmorMaterial)material,ArmorSlot.Chestplate),M,"M.MMMMMMM".ToCharArray());
            MaterialRecipe(3,3,Items.Armor((ArmorMaterial)material,ArmorSlot.Leggings),M,"MMMM.MM.M".ToCharArray());
            MaterialRecipe(3,2,Items.Armor((ArmorMaterial)material,ArmorSlot.Boots),M,"M.MM.M".ToCharArray());
        }
        return recipes.ToArray();
    }
    public static ReadOnlySpan<ShapedRecipe> All => Definitions;
}

public sealed class CraftingGrid
{
    private readonly ItemStack[] _inputs;
    public int Width { get; }
    public int SlotCount => _inputs.Length;
    public ItemStack this[int slot] => _inputs[slot];
    public void SetSlot(int slot, ItemStack stack) => _inputs[slot] = stack;
    public CraftingGrid(int width)
    {
        if (width is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; _inputs = new ItemStack[width * width];
    }
    public void Click(int slot, InventoryInteraction interaction, bool right) => _inputs[slot] = interaction.ClickStack(_inputs[slot], right);
    public ItemStack Preview => PreviewFor(CraftingRecipes.All);
    public ItemStack PreviewFor(ReadOnlySpan<ShapedRecipe> recipes) => Find(recipes, out _, out _)?.Output ?? default;
    public bool TryCraft(InventoryInteraction interaction) => TryCraft(interaction, CraftingRecipes.All);
    public bool TryCraft(InventoryInteraction interaction, ReadOnlySpan<ShapedRecipe> recipes)
    {
        ShapedRecipe recipe = Find(recipes, out int x, out int y);
        if (recipe == null || !interaction.TryReceive(recipe.Output.Fresh())) return false;
        recipe.Consume(this, x, y);
        return true;
    }
    private ShapedRecipe Find(ReadOnlySpan<ShapedRecipe> recipes, out int x, out int y)
    {
        foreach (ShapedRecipe recipe in recipes)
            for (int row = 0; row <= Width - recipe.Height; row++) for (int column = 0; column <= Width - recipe.Width; column++)
                if (recipe.Matches(this, column, row)) { x = column; y = row; return recipe; }
        x = y = 0; return null;
    }
}
