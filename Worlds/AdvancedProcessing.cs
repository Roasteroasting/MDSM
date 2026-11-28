using System;
using MDSM.Inventories;

namespace MDSM.Worlds;

public static class AdvancedProcessing
{
    public const double FurnaceWatts=2000,GlassKilnWatts=2500,CookerWatts=400;
    public const double FurnaceSeconds=2.5,CookerSeconds=2;
    public static bool Machine(BlockType b)=>b is BlockType.ElectricFurnace or BlockType.ElectricGlassKiln or BlockType.ElectricCooker;
    public static double Watts(BlockType b)=>b==BlockType.ElectricFurnace?FurnaceWatts:b==BlockType.ElectricGlassKiln?GlassKilnWatts:CookerWatts;
    public static int Inputs(BlockType b)=>b==BlockType.ElectricFurnace?4:b==BlockType.ElectricGlassKiln?1:3;
    public static int Slots(BlockType b)=>b==BlockType.ElectricFurnace?8:b==BlockType.ElectricGlassKiln?2:6;
    public static int Choices(BlockType b)=>b==BlockType.ElectricFurnace?1:5;
    private static readonly string[] GlassNames={"GLASS SLABS","GLASS STAIRS","GLASS PANES","DECORATIVE TILES","FLOWER VASE"};
    private static readonly string[] FoodNames={"COOKED PORK","BAKED POTATO","ROASTED CARROT","FRIED EGG","VEGETABLE STEW"};
    public static string ProductName(BlockType b,int choice)=>(b==BlockType.ElectricGlassKiln?GlassNames:FoodNames)[choice];
    public static bool Input(BlockType b,ItemId item)
    {
        if(b==BlockType.ElectricFurnace)return Smelting.Result(item,BlockType.Furnace)!=default;
        if(b==BlockType.ElectricGlassKiln)return Items.TryGetBlock(item,out var glass)&&GlassProcessing.BaseGlass(glass)>=0;
        return item==Items.RawPorkchop||item==Items.Potato||item==Items.Carrot||item==Items.Egg||item==Items.CeramicPot;
    }
    public static ProcessingRecipe Recipe(BlockType block,int choice,ItemId input)
    {
        if(block==BlockType.ElectricFurnace)
            return new(input,1,default,default,Smelting.Result(input,BlockType.Furnace),1,FurnaceSeconds);
        if(block==BlockType.ElectricGlassKiln)
        {
            if(!Items.TryGetBlock(input,out var glass))return default;
            int color=GlassProcessing.BaseGlass(glass);if(color<0)return default;
            int amount=choice==1?3:choice==2?3:choice==4?2:1;
            int outputCount=choice==0?2:choice==1?4:choice==2?8:1;
            var output=choice==2?(color==0?BlockType.GlassPane:DyePalette.Glass((DyeColor)(color-1),true))
                :GlassProcessing.Block(choice==0?GlassProduct.Slab:choice==1?GlassProduct.Stairs:choice==3?GlassProduct.Tile:GlassProduct.Vase,color);
            return new(input,amount,default,default,Items.FromBlock(output),outputCount,choice==1?6:choice==4?5:4);
        }
        return choice switch
        {
            0=>new(Items.RawPorkchop,1,default,default,Items.CookedPorkchop,1,CookerSeconds),
            1=>new(Items.Potato,1,default,default,Items.BakedPotato,1,CookerSeconds),
            2=>new(Items.Carrot,1,default,default,Items.RoastedCarrot,1,CookerSeconds),
            3=>new(Items.Egg,1,default,default,Items.FriedEgg,1,CookerSeconds),
            4=>new(Items.Carrot,1,Items.Potato,Items.CeramicPot,Items.VegetableStew,1,4),
            _=>default
        };
    }
}

public readonly record struct ProcessingRecipe(ItemId Input,int Amount,ItemId Second,ItemId Vessel,ItemId Output,int Quantity,double Seconds);
