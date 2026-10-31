using System;

namespace MDSM.Worlds;

// Runs after base terrain and only for new worlds. All decisions use world coordinates.
public sealed class WorldDecorator
{
    private const int SpawnClearRadius = 9;
    private readonly int _seed;
    public WorldDecorator(int seed) => _seed = seed;

    internal void Decorate(World world)
    {
        var heights = new int[world.Width * world.Depth];
        var roots = new bool[heights.Length];
        for (int z = 0; z < world.Depth; z++)
        for (int x = 0; x < world.Width; x++)
            heights[z * world.Width + x] = world.SurfaceY(x, z);

        for (int z = 3; z < world.Depth - 3; z++)
        for (int x = 3; x < world.Width - 3; x++)
        {
            if (NearSpawn(world, x, z, SpawnClearRadius)) continue;
            float density = Density(x, z);
            float chance = density < 0.4f ? 0.0004f : 0.002f + (density - 0.4f) * 0.015f;
            if (Unit(Hash(x, z, 1)) >= chance || HasNearbyRoot(world, roots, x, z)) continue;
            int ground = heights[z * world.Width + x];
            int minimum = ground, maximum = ground;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int height = heights[(z + dz) * world.Width + x + dx];
                minimum = Math.Min(minimum, height);
                maximum = Math.Max(maximum, height);
            }
            if (maximum - minimum > 2) continue;
            uint shape = Hash(x, z, 2);
            int trunkHeight = 4 + (int)(shape % 3);
            int radius = 2;
            if (TryTree(world, x, ground, z, trunkHeight, radius, shape))
                roots[z * world.Width + x] = true;
        }

        // Plants are placed last and never overwrite a trunk, leaf, or other plant.
        for (int z = 0; z < world.Depth; z++)
        for (int x = 0; x < world.Width; x++)
        {
            if (NearSpawn(world, x, z, 3)) continue;
            int ground = heights[z * world.Width + x];
            if (!Blocks.SupportsPlant(world.GetBlock(x, ground, z))
                || world.GetBlock(x, ground + 1, z) != BlockType.Air) continue;
            float choice = Unit(Hash(x, z, 3));
            BlockType plant = choice < 0.003f ? BlockType.RedFlower : choice < 0.006f ? BlockType.YellowFlower
                : choice < 0.024f + Density(x, z) * 0.04f ? BlockType.TallGrass : BlockType.Air;
            if (plant != BlockType.Air) world.SetGeneratedBlock(x, ground + 1, z, plant);
        }
    }

    // Checks every affected cell before writing, so rejected candidates leave no partial tree.
    internal static bool TryTree(World world, int x, int ground, int z, int trunkHeight, int radius, uint shape,bool growing=false)
    {
        if (trunkHeight < 4 || trunkHeight > 6 || radius != 2
            || x - radius < 0 || x + radius >= world.Width || z - radius < 0 || z + radius >= world.Depth
            || ground < 0 || ground + trunkHeight + 1 >= World.Height
            || !Blocks.SupportsPlant(world.GetBlock(x,ground,z),growing?BlockType.Sapling:BlockType.TallGrass))
            return false;
        int top = ground + trunkHeight;
        for (int y = ground + 1; y <= top; y++)
            if (world.GetBlock(x, y, z) != BlockType.Air && !(growing && y==ground+1 && world.GetBlock(x,y,z)==BlockType.Sapling)) return false;
        for (int layer = -2; layer <= 1; layer++)
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (!CanopyCell(dx, layer, dz, radius, shape)) continue;
            BlockType existing = world.GetBlock(x + dx, top + layer, z + dz);
            if (existing != BlockType.Air && existing != BlockType.Leaves) return false;
        }

        for (int y = ground + 1; y <= top; y++)
            Write(x,y,z,BlockType.WoodLog);
        for (int layer = -2; layer <= 1; layer++)
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
            if (CanopyCell(dx, layer, dz, radius, shape) && world.GetBlock(x + dx, top + layer, z + dz) == BlockType.Air)
                Write(x + dx, top + layer, z + dz, BlockType.Leaves);
        return true;
        void Write(int px,int py,int pz,BlockType block)
        {
            if(!growing)world.SetGeneratedBlock(px,py,pz,block);
            else if(world.SetBlock(px,py,pz,block) && block==BlockType.Leaves)world.Vegetation.MarkNatural(new Point3(px,py,pz));
        }
    }

    private static bool CanopyCell(int dx, int layer, int dz, int radius, uint shape)
    {
        int width = layer < 0 ? radius : 1;
        if (Math.Abs(dx) > width || Math.Abs(dz) > width) return false;
        int rounding = layer == 1 && (shape & 16) == 0 ? width : width + 1;
        return Math.Abs(dx) + Math.Abs(dz) <= rounding;
    }

    private static bool HasNearbyRoot(World world, bool[] roots, int x, int z)
    {
        for (int dz = -4; dz <= 4; dz++)
        for (int dx = -4; dx <= 4; dx++)
            if (dx * dx + dz * dz < 25 && (uint)(x + dx) < world.Width && (uint)(z + dz) < world.Depth
                && roots[(z + dz) * world.Width + x + dx]) return true;
        return false;
    }

    private static bool NearSpawn(World world, int x, int z, int radius)
    {
        int dx = x - world.Width / 2, dz = z - world.Depth / 2;
        return dx * dx + dz * dz < radius * radius;
    }

    // Slowly varying value noise gives wooded patches and open areas without biomes.
    private float Density(int x, int z)
    {
        float fx = x / 48f, fz = z / 48f;
        int ix = (int)fx, iz = (int)fz;
        fx -= ix; fz -= iz;
        fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
        float a = Unit(Hash(ix, iz, 7)), b = Unit(Hash(ix + 1, iz, 7));
        float c = Unit(Hash(ix, iz + 1, 7)), d = Unit(Hash(ix + 1, iz + 1, 7));
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
    }

    private uint Hash(int x, int z, uint channel)
    {
        unchecked
        {
            uint hash = (uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u ^ (uint)_seed * 0xcb1ab31fu ^ channel * 0x9e3779b9u;
            hash = (hash ^ (hash >> 16)) * 0x7feb352du;
            hash = (hash ^ (hash >> 15)) * 0x846ca68bu;
            return hash ^ (hash >> 16);
        }
    }

    private static float Unit(uint value) => (value >> 8) / 16777216f;
}
