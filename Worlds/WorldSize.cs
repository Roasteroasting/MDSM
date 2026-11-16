using System;
using System.IO;

namespace MDSM.Worlds;

public enum WorldSize { Small, Normal, Large, Massive, Infinite }

public static class WorldSizes
{
    public static string Dimensions(this WorldSize size)=>size switch
    {
        WorldSize.Infinite=>"Infinite",
        WorldSize.Large=>"Legacy (1024x1024)",
        _=>$"{size} / {size.Width()} X 128 X {size.Width()}"
    };
    public const WorldSize DefaultCreation=WorldSize.Infinite;
    public static System.Collections.Generic.IReadOnlyList<WorldSize> Creatable {get;}=Array.AsReadOnly(new[]{WorldSize.Infinite,WorldSize.Large});
    internal static WorldSize NextCreation(WorldSize current,int direction)
    {
        for(int i=0;i<Creatable.Count;i++)if(Creatable[i]==current)return Creatable[(i+direction+Creatable.Count)%Creatable.Count];
        return DefaultCreation;
    }
    public static System.Collections.Generic.IReadOnlyList<WorldSize> Finite {get;}=Array.AsReadOnly(new[]{WorldSize.Small,WorldSize.Normal,WorldSize.Large,WorldSize.Massive});
    public static int Width(this WorldSize size) => size switch
    {
        WorldSize.Small => 256,
        WorldSize.Normal => 512,
        WorldSize.Large => 1024,
        WorldSize.Massive => 2048,
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };

    public static int BlockCount(this WorldSize size) => checked(size.Width() * World.Height * size.Width());

    public static WorldSize FromDimensions(int width, int height, int depth)
    {
        if (width == depth && (height == World.Height || (height == 64 && width is 256 or 512)))
            return width switch
            {
                256 => WorldSize.Small,
                512 => WorldSize.Normal,
                1024 when height == World.Height => WorldSize.Large,
                2048 when height == World.Height => WorldSize.Massive,
                _ => throw new InvalidDataException("Unsupported world dimensions.")
            };
        throw new InvalidDataException("Supported worlds are 256/512/1024/2048 blocks square and 128 high. Legacy 256/512 x 64 worlds expand on load; Legacy Small (128 x 64) is obsolete.");
    }
}
