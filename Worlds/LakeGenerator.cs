using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public readonly record struct WaterFeatureCounts(int Ponds, int Rivers, int Lakes);

// New-world carving only. Every footprint is validated and sealed before any blocks change.
public sealed class LakeGenerator
{
    private readonly int _seed;
    private readonly TerrainGenerator _terrain;
    private MajorLakeGenerator _major;
    public IReadOnlyList<NaturalLake> Lakes => _major?.Lakes ?? (IReadOnlyList<NaturalLake>)Array.Empty<NaturalLake>();
    public WaterFeatureCounts Counts { get; private set; }
    public LakeGenerator(int seed) { _seed = seed; _terrain = new TerrainGenerator(seed); }
    public static int MaximumLakes(WorldSize size) => size.Width() * size.Width() / 16384; // Pond budget, retained API.
    public static int MaximumRadius(WorldSize size) => size >= WorldSize.Large ? 9 : 7;
    public static int RiverBudget(WorldSize size) => size == WorldSize.Massive ? 12 : size == WorldSize.Large ? 4 : 0;
    public static int LakeBudget(WorldSize size) => size == WorldSize.Massive ? 9 : size == WorldSize.Large ? 3 : 0;

    public int Generate(World world)
    {
        var random = new Random(unchecked(_seed ^ 0x4C414B45));
        var occupied = new List<Rectangle>();
        int lakes = 0, rivers = 0, ponds = 0;
        _major = new MajorLakeGenerator(world); _major.Generate(); lakes = _major.Lakes.Count;
        for (int attempt = 0; attempt < RiverBudget(world.Size) * 500 && rivers < RiverBudget(world.Size); attempt++)
            if (TryRiver(world, random, occupied)) rivers++;
        for (int attempt = 0; attempt < MaximumLakes(world.Size) * 300 && ponds < MaximumLakes(world.Size); attempt++)
        {
            int radius = random.Next(3, MaximumRadius(world.Size) + 1);
            int x = random.Next(radius + 8, world.Width - radius - 8), z = random.Next(radius + 8, world.Depth - radius - 8);
            if (TryPond(world, random, occupied, x, z, radius)) ponds++;
        }
        Counts = new WaterFeatureCounts(ponds, rivers, lakes);
        return ponds + rivers + lakes;
    }

    private bool TryPond(World world, Random random, List<Rectangle> occupied, int cx, int cz, int radius)
    {
        int extent = radius + 8;
        var bounds = new Rectangle(cx - extent, cz - extent, extent * 2 + 1, extent * 2 + 1);
        if (!Available(world, bounds, occupied)) return false;
        float stretch = 0.7f + random.NextSingle() * 0.3f, phase = random.NextSingle() * MathHelper.TwoPi;
        var shape = new bool[bounds.Width * bounds.Height];
        for (int z = -radius; z <= radius; z++) for (int x = -radius; x <= radius; x++)
        {
            float angle = MathF.Atan2(z, x);
            float edge = radius * (0.86f + 0.09f * MathF.Sin(angle * 3 + phase) + 0.05f * MathF.Cos(angle * 5 - phase));
            shape[(z + extent) * bounds.Width + x + extent] = MathF.Sqrt(x * x + z * z / (stretch * stretch)) <= edge;
        }
        return Carve(world, bounds, shape, 3, 3, occupied);
    }

    private bool TryRiver(World world, Random random, List<Rectangle> occupied)
    {
        int length = random.Next(90, world.Size == WorldSize.Massive ? 221 : 161);
        int margin = length + 12;
        float x = random.Next(margin, world.Width-margin), z = random.Next(margin, world.Depth-margin);
        float startX = x, startZ = z, heading = random.NextSingle() * MathHelper.TwoPi;
        float phase = random.NextSingle() * MathHelper.TwoPi;
        int elevation = _terrain.SurfaceY((int)x, (int)z);
        var points = new List<Vector3>();
        int minX = (int)x, maxX = minX, minZ = (int)z, maxZ = minZ;
        for (int step = 0; step < length / 3; step++)
        {
            float desired = heading + MathF.Sin(step * 0.15f + phase) * 0.18f;
            float best = float.MaxValue, chosen = desired;
            for (int turn = -2; turn <= 2; turn++)
            {
                float angle = desired + turn * 0.18f;
                int nx = (int)(x + MathF.Cos(angle) * 3), nz = (int)(z + MathF.Sin(angle) * 3);
                int height = _terrain.SurfaceY(nx, nz);
                float score = Math.Abs(height - elevation) * 4 + Math.Abs(turn) * 0.35f
                    + Math.Abs(_terrain.SurfaceY(nx+3,nz) - _terrain.SurfaceY(nx-3,nz)) * 0.25f
                    + Math.Abs(_terrain.SurfaceY(nx,nz+3) - _terrain.SurfaceY(nx,nz-3)) * 0.25f;
                if (score < best) { best = score; chosen = angle; }
            }
            heading = chosen; x += MathF.Cos(heading) * 3; z += MathF.Sin(heading) * 3;
            float radius = 2.7f + (MathF.Sin(step * 0.22f + phase) + 1) * 1.1f;
            points.Add(new Vector3(x, radius, z));
            minX = Math.Min(minX,(int)x-7); maxX = Math.Max(maxX,(int)x+7);
            minZ = Math.Min(minZ,(int)z-7); maxZ = Math.Max(maxZ,(int)z+7);
        }
        if (Vector2.Distance(new Vector2(x,z), new Vector2(startX,startZ)) < length * 0.55f) return false;
        var bounds = new Rectangle(minX-8,minZ-8,maxX-minX+17,maxZ-minZ+17);
        if (!Available(world, bounds, occupied)) return false;
        var shape = new bool[bounds.Width*bounds.Height];
        foreach (Vector3 point in points)
        {
            for (int dz = -6; dz <= 6; dz++) for (int dx = -6; dx <= 6; dx++)
            {
                int wx = (int)point.X+dx, wz = (int)point.Z+dz;
                if (Vector2.DistanceSquared(new Vector2(wx,wz),new Vector2(point.X,point.Z)) <= point.Y*point.Y)
                    shape[(wz-bounds.Y)*bounds.Width+wx-bounds.X] = true;
            }
        }
        return Carve(world, bounds, shape, 3, 4, occupied);
    }

    private bool Available(World world, Rectangle bounds, List<Rectangle> occupied)
    {
        if (bounds.Left < 2 || bounds.Top < 2 || bounds.Right >= world.Width-2 || bounds.Bottom >= world.Depth-2) return false;
        // Reserve a generous dry spawn neighborhood; entire rivers must avoid it.
        if (bounds.Intersects(new Rectangle(world.Width/2-28,world.Depth/2-28,56,56))) return false;
        foreach (Rectangle other in occupied) if (bounds.Intersects(other)) return false;
        if (_major.Intersects(bounds)) return false;
        return true;
    }

    private bool Carve(World world, Rectangle bounds, bool[] wet, int maximumDepth, int maximumSlope, List<Rectangle> occupied)
    {
        int width = bounds.Width, height = bounds.Height;
        bool Wet(int x, int z) => (uint)x < width && (uint)z < height && wet[z*width+x];
        int minimum = World.Height, maximum = 0, count = 0;
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) if (Wet(x,z))
        {
            int top = world.SurfaceY(bounds.X+x,bounds.Y+z);
            minimum = Math.Min(minimum,top); maximum = Math.Max(maximum,top); count++;
        }
        if (count < 12 || minimum < TerrainGenerator.MinimumSurface || maximum-minimum > maximumSlope) return false;
        int waterY = minimum;
        // A complete solid rim at/above water level guarantees a stable source-filled basin.
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            if (!Wet(x,z) && (Wet(x-1,z) || Wet(x+1,z) || Wet(x,z-1) || Wet(x,z+1))
                && world.SurfaceY(bounds.X+x,bounds.Y+z) < waterY) return false;

        var distance = new int[wet.Length]; Array.Fill(distance, -1);
        var queue = new Queue<int>();
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            if (Wet(x,z) && (!Wet(x-1,z) || !Wet(x+1,z) || !Wet(x,z-1) || !Wet(x,z+1)))
            { int index = z*width+x; distance[index] = 1; queue.Enqueue(index); }
        while (queue.TryDequeue(out int cell))
        {
            int x = cell%width, z = cell/width;
            Visit(x-1,z); Visit(x+1,z); Visit(x,z-1); Visit(x,z+1);
            void Visit(int nx,int nz)
            {
                if (!Wet(nx,nz)) return;
                int index = nz*width+nx;
                if (distance[index] >= 0) return;
                distance[index] = distance[cell]+1; queue.Enqueue(index);
            }
        }
        // Blend excavated banks back into the heightmap, avoiding a cliff just beyond the beach.
        for (int z=0;z<height;z++) for (int x=0;x<width;x++)
            if (!Wet(x,z) && (Wet(x-1,z) || Wet(x+1,z) || Wet(x,z-1) || Wet(x,z+1)))
            { int cell=z*width+x; distance[cell]=-2; queue.Enqueue(cell); }
        while (queue.TryDequeue(out int cell))
        {
            if (distance[cell]<=-9) continue;
            int x=cell%width,z=cell/width;
            VisitBank(x-1,z);VisitBank(x+1,z);VisitBank(x,z-1);VisitBank(x,z+1);
            void VisitBank(int nx,int nz)
            {
                if ((uint)nx>=width || (uint)nz>=height) return;
                int index=nz*width+nx;
                if(distance[index]!=-1)return;
                distance[index]=distance[cell]-1;queue.Enqueue(index);
            }
        }
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
        {
            int wx = bounds.X+x, wz = bounds.Y+z, top = world.SurfaceY(wx,wz);
            uint variation = Hash(wx,wz);
            if (Wet(x,z))
            {
                int depth = Math.Min(maximumDepth, 1 + (distance[z*width+x]-1) / (maximumDepth > 3 ? 2 : 1));
                int bed = waterY-depth;
                world.SetGeneratedBlock(wx,bed,wz, variation%7 == 0 ? BlockType.Sand : variation%3 == 0 ? BlockType.Dirt : BlockType.Gravel);
                for (int y = bed+1; y <= top; y++) world.SetGeneratedBlock(wx,y,wz,y <= waterY ? BlockType.WaterSource : BlockType.Air);
            }
            else if (distance[z*width+x]<=-2)
            {
                int step=-distance[z*width+x]-1;
                int bank = Math.Min(top,waterY+step);
                if(step>1 && bank==top) continue;
                for (int y = bank+1; y <= top; y++) world.SetGeneratedBlock(wx,y,wz,BlockType.Air);
                world.SetGeneratedBlock(wx,bank,wz, step>1 ? BlockType.Grass : variation%5 < 2 ? BlockType.Sand
                    : variation%5 == 2 ? BlockType.Gravel : variation%5 == 3 ? BlockType.Dirt : BlockType.Grass);
                if (world.GetBlock(wx,bank,wz) == BlockType.Grass)
                    for (int y = bank-1; y >= bank-_terrain.DirtDepth(wx,wz); y--) world.SetGeneratedBlock(wx,y,wz,BlockType.Dirt);
            }
        }
        occupied.Add(bounds); return true;
    }
    private uint Hash(int x,int z)
    {
        uint value = unchecked((uint)(_seed ^ x * 374761393 ^ z * 668265263));
        value = (value ^ (value >> 13)) * 1274126177u; return value ^ (value >> 16);
    }
}
