using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public enum DyeColor : byte
{
    White, Orange, Magenta, LightBlue, Yellow, Lime, Pink, Gray,
    LightGray, Cyan, Purple, Blue, Brown, Green, Red, Black
}

internal static class DyePalette
{
    internal const int Count=16;
    internal const int GlassTile=99;
    private static readonly string[] Names=
    {
        "WHITE","ORANGE","MAGENTA","LIGHT BLUE","YELLOW","LIME","PINK","GRAY",
        "LIGHT GRAY","CYAN","PURPLE","BLUE","BROWN","GREEN","RED","BLACK"
    };
    private static readonly Color[] Colors=
    {
        new(236,235,222),new(243,142,52),new(205,80,179),new(111,183,228),
        new(247,210,63),new(156,206,58),new(244,152,177),new(104,110,117),
        new(177,182,183),new(58,172,170),new(135,78,198),new(71,99,218),
        new(145,91,54),new(76,146,65),new(214,64,61),new(56,57,66)
    };
    internal static string Name(DyeColor color)=>Names[(int)color];
    internal static BlockType Glass(DyeColor color,bool pane=false)
        =>(BlockType)((int)(pane?BlockType.StainedPaneWhite:BlockType.StainedGlassWhite)+(int)color);
    internal static Color Tint(BlockType block)
        =>block is >=BlockType.StainedGlassWhite and <=BlockType.StainedPaneBlack
            ? Colors[((int)block-(int)BlockType.StainedGlassWhite)%Count]:GlassProcessing.IsGlass(block)&&GlassProcessing.ColorIndex(block)>0?Colors[GlassProcessing.ColorIndex(block)-1]:Microsoft.Xna.Framework.Color.White;
}
