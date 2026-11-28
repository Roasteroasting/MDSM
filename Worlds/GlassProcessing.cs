using System;
using Microsoft.Xna.Framework;
using MDSM.Inventories;

namespace MDSM.Worlds;

public enum GlassProduct {Slab,Stairs,Tile,Vase,DoubleSlab}
internal static class GlassProcessing
{
    internal const int Variants=17,DecorativeTile=117,VaseTile=118;
    internal static BlockType Block(GlassProduct product,int color=0)=>(BlockType)(156+(int)product*Variants+color);
    internal static bool IsGlass(BlockType b)=>b is >=BlockType.GlassSlab and <=BlockType.StainedDoubleGlassSlabBlack;
    internal static bool IsVase(BlockType b)=>b is >=BlockType.GlassVase and <=BlockType.StainedGlassVaseBlack;
    internal static bool IsDouble(BlockType b)=>b is >=BlockType.DoubleGlassSlab and <=BlockType.StainedDoubleGlassSlabBlack;
    internal static int ColorIndex(BlockType b)=>IsGlass(b)?((int)b-156)%Variants:BaseGlass(b);
    internal static int BaseGlass(BlockType b)=>b==BlockType.Glass?0:b is >=BlockType.StainedGlassWhite and <=BlockType.StainedGlassBlack?(int)b-(int)BlockType.StainedGlassWhite+1:-1;
    internal static BlockType Double(BlockType slab)=>Block(GlassProduct.DoubleSlab,ColorIndex(slab));
    internal static BlockType Single(BlockType full)=>Block(GlassProduct.Slab,ColorIndex(full));
    internal static void Define(ref BlockDefinition[] definitions)
    {
        Array.Resize(ref definitions,241);
        definitions[153]=new("ELECTRIC FURNACE",true,true,BlockShape.Cube,115,116,116,MiningSeconds:4,FrontTile:112);
        definitions[154]=new("ELECTRIC GLASS KILN",true,true,BlockShape.Cube,115,116,116,MiningSeconds:4,FrontTile:113);
        definitions[155]=new("ELECTRIC COOKER",true,true,BlockShape.Cube,115,116,116,MiningSeconds:3,FrontTile:114);
        foreach(GlassProduct product in Enum.GetValues<GlassProduct>())for(int color=0;color<Variants;color++)
        {
            var block=Block(product,color);string prefix=color==0?"":DyePalette.Name((DyeColor)(color-1))+" STAINED ";
            string name=product switch{GlassProduct.Slab=>"GLASS SLAB",GlassProduct.Stairs=>"GLASS STAIRS",GlassProduct.Tile=>"DECORATIVE GLASS TILE",GlassProduct.Vase=>"GLASS VASE",_=>"DOUBLE GLASS SLAB"};
            var shape=product switch{GlassProduct.Slab=>BlockShape.Slab,GlassProduct.Stairs=>BlockShape.Stairs,GlassProduct.Vase=>BlockShape.Vase,_=>BlockShape.Cube};
            int tile=product==GlassProduct.Tile?DecorativeTile:product==GlassProduct.Vase?VaseTile:color==0?18:DyePalette.GlassTile;
            definitions[(int)block]=new(prefix+name,true,false,shape,tile,Placeable:product!=GlassProduct.DoubleSlab,MiningSeconds:.45f);
        }
    }
    internal static int VaseBoxes(Vector3 origin,Span<BoundingBox> boxes)
    {
        boxes[0]=new(origin+new Vector3(.25f,0,.25f),origin+new Vector3(.75f,.0625f,.75f));
        boxes[1]=new(origin+new Vector3(.25f,.0625f,.25f),origin+new Vector3(.3125f,.625f,.75f));
        boxes[2]=new(origin+new Vector3(.6875f,.0625f,.25f),origin+new Vector3(.75f,.625f,.75f));
        boxes[3]=new(origin+new Vector3(.3125f,.0625f,.25f),origin+new Vector3(.6875f,.625f,.3125f));
        boxes[4]=new(origin+new Vector3(.3125f,.0625f,.6875f),origin+new Vector3(.6875f,.625f,.75f));return 5;
    }
}

public sealed partial class World
{
    internal bool UseVase(Point3 cell,Inventory inventory,bool creative)
    {
        if(!GlassProcessing.IsVase(GetBlock(cell.X,cell.Y,cell.Z)))return false;
        var vase=Containers.Get(cell);var held=inventory.Selected;
        if(vase[0].IsEmpty)
        {
            if(Items.TryGetBlock(held.Item,out var flower)&&Blocks.Flower(flower))
            {vase.SetSlot(0,new(held.Item,1));if(!creative)inventory.ConsumeSelected();}
        }
        else if(held.IsEmpty)
        {
            var copy=inventory.Clone();if(copy.Add(vase[0]).IsEmpty)
            {inventory.Add(vase[0]);vase.SetSlot(0,default);}
        }
        return true;
    }
}
