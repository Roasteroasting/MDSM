using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// A local terrain decoration, run after the unchanged height generator and before vegetation.
public sealed class LakeGenerator
{
    private readonly int _seed;
    public LakeGenerator(int seed) => _seed = seed;
    public static int MaximumLakes(WorldSize size) => size switch { WorldSize.Small => 2, WorldSize.Normal => 4, _ => 12 };
    public static int MaximumRadius(WorldSize size) => size == WorldSize.Large ? 10 : size == WorldSize.Normal ? 7 : 6;

    public int Generate(World world)
    {
        var random = new Random(unchecked(_seed ^ 0x4C414B45));
        var centers = new List<(int X, int Z, int Radius)>();
        int target = MaximumLakes(world.Size);
        for (int attempt = 0; attempt < target * 300 && centers.Count < target; attempt++)
        {
            // Most are small; only occasional Large-world basins use the upper size range.
            int upperRadius = world.Size == WorldSize.Large ? (random.Next(4) == 0 ? 10 : 6) : MaximumRadius(world.Size);
            int radius = random.Next(3, upperRadius + 1);
            int extent = radius + 4;
            int cx = random.Next(extent + 1, world.Width - extent - 1), cz = random.Next(extent + 1, world.Depth - extent - 1);
            if (Vector2.Distance(new Vector2(cx, cz), new Vector2(world.Width / 2f, world.Depth / 2f)) < radius + 24) continue;
            bool overlap = false;
            foreach (var center in centers)
                if (Vector2.Distance(new Vector2(cx, cz), new Vector2(center.X, center.Z)) < radius + center.Radius + 10) overlap = true;
            if (overlap) continue;
            float stretch = 0.7f + random.NextSingle() * 0.3f, phase = random.NextSingle() * MathHelper.TwoPi;
            int diameter = extent * 2 + 1;
            var shape = new bool[diameter * diameter];
            int minimum = World.Height, maximum = 0;
            for (int z = -extent; z <= extent; z++)
            for (int x = -extent; x <= extent; x++)
            {
                float angle = MathF.Atan2(z, x);
                float edge = radius * (0.88f + 0.08f * MathF.Sin(angle * 3 + phase) + 0.04f * MathF.Cos(angle * 5 - phase));
                bool wet = MathF.Sqrt(x * x + z * z / (stretch * stretch)) <= edge;
                shape[(z + extent) * diameter + x + extent] = wet;
                if (!wet) continue;
                int y = world.SurfaceY(cx + x, cz + z);
                minimum = Math.Min(minimum, y); maximum = Math.Max(maximum, y);
            }
            // Prefer gently sloped terrain; never carve a cliff, touch bedrock, or overflow a bank.
            if (minimum < 8 || maximum - minimum > 2) continue;
            int waterY = minimum;
            bool Wet(int x, int z) => Math.Abs(x) <= extent && Math.Abs(z) <= extent && shape[(z + extent) * diameter + x + extent];
            for (int z = -extent; z <= extent; z++)
            for (int x = -extent; x <= extent; x++)
            {
                int wx = cx + x, wz = cz + z, top = world.SurfaceY(wx, wz);
                uint variation = Hash(wx, wz);
                if (Wet(x, z))
                {
                    bool shore = !Wet(x - 1, z) || !Wet(x + 1, z) || !Wet(x, z - 1) || !Wet(x, z + 1);
                    int depth = shore ? 1 : MathF.Sqrt(x * x + z * z) < radius * 0.45f ? 3 : 2;
                    int bed = waterY - depth;
                    world.SetGeneratedBlock(wx, bed, wz, variation % 7 == 0 ? BlockType.Sand : variation % 3 == 0 ? BlockType.Dirt : BlockType.Gravel);
                    for (int y = bed + 1; y <= top; y++)
                        world.SetGeneratedBlock(wx, y, wz, y <= waterY ? BlockType.WaterSource : BlockType.Air);
                }
                else
                {
                    bool shore = Wet(x - 1, z) || Wet(x + 1, z) || Wet(x, z - 1) || Wet(x, z + 1);
                    if (!shore) continue;
                    int bank = Math.Max(waterY, Math.Min(top, waterY + 1));
                    for (int y = bank + 1; y <= top; y++) world.SetGeneratedBlock(wx, y, wz, BlockType.Air);
                    // Irregular patches rather than a uniform sand ring; untouched grass supports plants.
                    world.SetGeneratedBlock(wx, bank, wz, variation % 5 < 2 ? BlockType.Sand
                        : variation % 5 == 2 ? BlockType.Gravel : variation % 5 == 3 ? BlockType.Dirt : BlockType.Grass);
                }
            }
            centers.Add((cx, cz, radius));
        }
        return centers.Count;
    }

    private uint Hash(int x, int z)
    {
        uint value = unchecked((uint)(_seed ^ x * 374761393 ^ z * 668265263));
        value = (value ^ (value >> 13)) * 1274126177u;
        return value ^ (value >> 16);
    }
}
