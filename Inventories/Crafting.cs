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
    private static readonly ShapedRecipe[] Definitions =
    {
        new(2, 2, new[] { Planks, Planks, Planks, Planks }, new ItemStack(Items.FromBlock(BlockType.CraftingTable), 1))
    };
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
        if (recipe == null || !interaction.TryReceive(recipe.Output)) return false;
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
