using System;
using System.IO;

namespace MDSM.Worlds;

public enum WorldSize { Small, Normal, Large, Massive }

public static class WorldSizes
{
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
