using System;
using System.IO;

namespace MDSM.Worlds;

public enum WorldSize { Small, Normal, Large }

public static class WorldSizes
{
    public static int Width(this WorldSize size) => size switch
    {
        WorldSize.Small => 128,
        WorldSize.Normal => 256,
        WorldSize.Large => 512,
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };

    public static int BlockCount(this WorldSize size) => size.Width() * World.Height * size.Width();

    public static WorldSize FromDimensions(int width, int height, int depth)
    {
        if (height == World.Height && width == depth)
            return width switch
            {
                128 => WorldSize.Small,
                256 => WorldSize.Normal,
                512 => WorldSize.Large,
                _ => throw new InvalidDataException("Unsupported world dimensions.")
            };
        throw new InvalidDataException("Worlds must be square, 64 blocks high, and Small, Normal, or Large.");
    }
}
