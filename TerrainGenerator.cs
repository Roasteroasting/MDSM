using System;

namespace MDSM;

public sealed class TerrainGenerator
{
    private readonly int _seed;

    public TerrainGenerator(int seed) => _seed = seed;

    public int SurfaceY(int x, int z)
    {
        // Broad hills modulated by a slower field leave some areas relatively flat.
        float flatness = 0.65f + Noise(x / 150f + 31, z / 150f - 17) * 0.35f;
        float hills = Noise(x / 96f, z / 96f) * 11f
            + Noise(x / 40f + 7, z / 40f + 13) * 4f
            + Noise(x / 18f - 11, z / 18f + 5) * 1.5f;
        return Math.Clamp((int)MathF.Round(28 + hills * flatness), 16, 44);
    }

    public int DirtDepth(int x, int z) =>
        Math.Clamp(4 + (int)MathF.Round(Noise(x / 30f + 17, z / 30f - 23) * 2), 3, 5);

    internal void Generate(World world)
    {
        foreach (Chunk chunk in world.Chunks)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            int worldX = chunk.OriginX + x, worldZ = chunk.OriginZ + z;
            int top = SurfaceY(worldX, worldZ);
            int dirt = DirtDepth(worldX, worldZ);
            for (int y = 0; y <= top; y++)
                chunk.SetBlock(x, y, z, y == 0 ? BlockType.Bedrock : y == top ? BlockType.Grass
                    : y >= top - dirt ? BlockType.Dirt : BlockType.Stone);
        }
    }

    // Seeded 2D gradient noise with Perlin's quintic interpolation. Hashing lattice
    // coordinates avoids permutation tables and is deterministic across chunk order.
    private float Noise(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        float u = Fade(fx), v = Fade(fz);
        float bottom = Lerp(Gradient(ix, iz, fx, fz), Gradient(ix + 1, iz, fx - 1, fz), u);
        float top = Lerp(Gradient(ix, iz + 1, fx, fz - 1), Gradient(ix + 1, iz + 1, fx - 1, fz - 1), u);
        return Lerp(bottom, top, v);
    }

    private float Gradient(int x, int z, float dx, float dz)
    {
        uint hash;
        unchecked
        {
            hash = (uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u ^ (uint)_seed * 0xcb1ab31fu;
            hash = (hash ^ (hash >> 16)) * 0x7feb352du;
            hash = (hash ^ (hash >> 15)) * 0x846ca68bu;
            hash ^= hash >> 16;
        }
        return (hash & 7) switch
        {
            0 => dx,
            1 => -dx,
            2 => dz,
            3 => -dz,
            4 => (dx + dz) * 0.70710678f,
            5 => (dx - dz) * 0.70710678f,
            6 => (-dx + dz) * 0.70710678f,
            _ => (-dx - dz) * 0.70710678f
        };
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
