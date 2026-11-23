using System;
using System.Collections.Generic;
using MDSM.Worlds;

namespace MDSM.Inventories;

internal static class IndustrialRecipes
{
    internal static void AddTo(List<ShapedRecipe> recipes)
    {
        void Shape(int width,int height,ItemId result,int quantity,CraftingStation station,params ItemId[] cells)
        {
            var ingredients=Array.ConvertAll(cells,item=>item==default ? default : new CraftingIngredient(item));
            recipes.Add(new(width,height,ingredients,new(result,quantity),station:station));
        }
        void Grind(ItemId result,ItemId input)
            =>recipes.Add(new(1,1,new[]{new CraftingIngredient(input)},new(result,1),
                shapeless:true,station:CraftingStation.Mortar));
        void Blend(DyeColor result,DyeColor first,DyeColor second)
            =>recipes.Add(new(2,1,new[]{new CraftingIngredient(Items.Dye(first)),new CraftingIngredient(Items.Dye(second))},
                new(Items.Dye(result),2),shapeless:true,station:CraftingStation.Mortar));

        ItemId bricks=Items.FromBlock(BlockType.BrickBlock), planks=Items.FromBlock(BlockType.WoodenPlanks);
        Shape(2,2,bricks,1,CraftingStation.Standard,Items.Brick,Items.Brick,Items.Brick,Items.Brick);
        Shape(3,3,Items.FromBlock(BlockType.BrickKiln),1,CraftingStation.Standard,
            bricks,bricks,bricks,bricks,default,bricks,bricks,bricks,bricks);
        Shape(3,2,Items.FromBlock(BlockType.MetalworkingBench),1,CraftingStation.Standard,
            Items.IronIngot,Items.FromBlock(BlockType.CraftingTable),Items.IronIngot,planks,planks,planks);
        Shape(3,2,Items.MortarAndPestle,1,CraftingStation.Standard,
            Items.Brick,default,Items.Brick,default,Items.Brick,default);
        Shape(3,2,Items.UnfiredPot,1,CraftingStation.Standard,
            Items.ClayBall,default,Items.ClayBall,default,Items.ClayBall,default);
        var copper=Items.FromBlock(BlockType.CopperBlock);
        var ingots=new ItemId[9];
        Array.Fill(ingots,Items.CopperIngot);
        Shape(3,3,copper,1,CraftingStation.Standard,ingots);
        Shape(1,1,Items.CopperIngot,9,CraftingStation.Standard,copper);
        Shape(3,1,Items.CopperWire,6,CraftingStation.Metalworking,Items.CopperIngot,Items.CopperIngot,Items.CopperIngot);

        foreach(var (block,color) in new[]
        {
            (BlockType.RedFlower,DyeColor.Red),(BlockType.YellowFlower,DyeColor.Yellow),
            (BlockType.Violet,DyeColor.Purple),(BlockType.Peony,DyeColor.Pink),
            (BlockType.Cornflower,DyeColor.Blue),(BlockType.Daisy,DyeColor.White),
            (BlockType.Marigold,DyeColor.Orange),(BlockType.Cactus,DyeColor.Green)
        }) Grind(Items.Dye(color),Items.FromBlock(block));
        Grind(Items.Dye(DyeColor.Black),Items.Charcoal);
        Blend(DyeColor.Cyan,DyeColor.Blue,DyeColor.Green);
        Blend(DyeColor.Lime,DyeColor.Green,DyeColor.White);
        Blend(DyeColor.LightBlue,DyeColor.Blue,DyeColor.White);
        Blend(DyeColor.Magenta,DyeColor.Purple,DyeColor.Pink);
        Blend(DyeColor.Gray,DyeColor.Black,DyeColor.White);
        Blend(DyeColor.LightGray,DyeColor.Gray,DyeColor.White);
        Blend(DyeColor.Brown,DyeColor.Red,DyeColor.Green);

        foreach(DyeColor color in Enum.GetValues<DyeColor>())
        {
            ItemId stainedGlass=Items.FromBlock(DyePalette.Glass(color));
            ItemId stainedPane=Items.FromBlock(DyePalette.Glass(color,true));
            Stain(BlockType.Glass,stainedGlass);
            Stain(BlockType.GlassPane,stainedPane);
            Shape(3,2,stainedPane,16,CraftingStation.Standard,
                stainedGlass,stainedGlass,stainedGlass,stainedGlass,stainedGlass,stainedGlass);

            void Stain(BlockType clearBlock,ItemId output)
            {
                ItemId glass=Items.FromBlock(clearBlock);
                Shape(3,3,output,8,CraftingStation.Standard,
                    glass,glass,glass,glass,Items.Dye(color),glass,glass,glass,glass);
            }
        }
    }
}
