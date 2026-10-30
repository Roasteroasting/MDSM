using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Generation-only measurements; not serialized and never used to repopulate saved worlds.
public readonly record struct NaturalLake(Rectangle Bounds, int WaterY, int MaximumDepth, int Area, int OriginalRelief);

// Broad compound basins, separate from the deliberately unchanged small pond/river carver.
internal sealed class MajorLakeGenerator
{
    private const int Blend = 56;
    private readonly World _world;
    private readonly TerrainGenerator _terrain;
    private readonly Random _random;
    private readonly List<Plan> _plans = new();
    public List<NaturalLake> Lakes { get; } = new();
    public MajorLakeGenerator(World world)
    {
        _world = world; _terrain = new TerrainGenerator(world.Seed);
        _random = new Random(unchecked(world.Seed ^ 0x42415349));
    }
    public void Generate()
    {
        if (_world.Size < WorldSize.Large) return;
        int target = TargetCount(_world.Size, _world.Seed);
        for (int slot = 0; slot < target; slot++)
        {
            Plan plan = CreatePlan(_world.Size, _world.Seed, slot);
            int span = plan.Span;
            // Ranked, jittered candidates are cheap; only promising sites get full footprint validation.
            var candidates = new List<(Point Position, float Score)>(256);
            int margin = plan.Extent + 3;
            for (int trial = 0; trial < 256; trial++)
            {
                int x = _random.Next(margin, _world.Width - margin), z = _random.Next(margin, _world.Depth - margin);
                var bounds = new Rectangle(x - plan.Extent, z - plan.Extent, plan.Width, plan.Width);
                if (bounds.Intersects(new Rectangle(_world.Width / 2 - 30, _world.Depth / 2 - 30, 60, 60))) continue;
                bool spaced = true;
                foreach (Plan other in _plans)
                    if (Vector2.Distance(new Vector2(x, z), new Vector2(other.X, other.Z)) < plan.Radius + other.Radius + 40) { spaced = false; break; }
                if (!spaced) continue;
                int low = 128, high = 0, total = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int y = _world.SurfaceY(x + dx * span / 3, z + dz * span / 3);
                    low = Math.Min(low, y); high = Math.Max(high, y); total += y;
                }
                int center = _world.SurfaceY(x, z);
                if (center > total / 9f + 3 || high - low > 24) continue;
                candidates.Add((new Point(x, z), center + (high - low) * 1.2f + Math.Max(0, center - total / 9f) * 5));
            }
            candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
            foreach (var candidate in candidates)
            {
                if (!TryPlace(plan, candidate.Position)) continue;
                _plans.Add(plan); break;
            }
        }
    }
    internal static int TargetCount(WorldSize size, int seed)
    {
        // Preserve the existing population target, independently of lake dimensions.
        uint population = Hash(size.Width(), size.Width(), seed);
        return size < WorldSize.Large ? 0 : size == WorldSize.Large ? 2 + (int)(population % 2) : 6 + (int)(population % 4);
    }
    internal static Plan CreatePlan(WorldSize size, int seed, int slot)
    {
        // Shape/rarity is stable per lake, independent of placement attempts.
        var random = new Random(unchecked((int)Hash(size.Width(), slot, seed ^ 0x42415349)));
        bool massive = size == WorldSize.Massive, exceptional = random.Next(64) == 0;
        int span = massive ? random.Next(exceptional ? 158 : 100, exceptional ? 171 : 116)
            : random.Next(exceptional ? 105 : 48, exceptional ? 117 : 61);
        int depth = Math.Max(4, (int)MathF.Round(span * .1f) + random.Next(-1, 2));
        return new Plan(span, depth, random);
    }
    // Reserve changed terrain, not a huge bounding rectangle. Ponds/rivers retain ample space.
    public bool Intersects(Rectangle bounds)
    {
        foreach (Plan plan in _plans)
        {
            Rectangle overlap = Rectangle.Intersect(bounds, plan.Bounds);
            for (int z = overlap.Top; z < overlap.Bottom; z++) for (int x = overlap.Left; x < overlap.Right; x++)
                if (plan.Modified[(z - plan.Bounds.Y) * plan.Width + x - plan.Bounds.X]) return true;
        }
        return false;
    }
    private bool TryPlace(Plan plan, Point center)
    {
        plan.X = center.X; plan.Z = center.Y;
        int low = 128, high = 0, count = 0; long sum = 0;
        for (int z = 0; z < plan.Width; z++) for (int x = 0; x < plan.Width; x++)
        {
            int i = z * plan.Width + x; if (plan.Distance[i] <= 0) continue;
            int top = _world.SurfaceY(plan.Bounds.X + x, plan.Bounds.Y + z);
            low = Math.Min(low, top); high = Math.Max(high, top); sum += top; count++;
        }
        if (count == 0 || count < plan.WetBounds.Width * plan.WetBounds.Height / 3 || high - low > 24
            || _world.SurfaceY(center.X, center.Y) > sum / (float)count + 2) return false;
        int waterY = Math.Min(low + 1, TerrainGenerator.MaximumSurface);
        // Only now write blocks. The complete source volume has a solid floor and sealed, graded rim.
        int deepest = 0;
        for (int z = 0; z < plan.Width; z++) for (int x = 0; x < plan.Width; x++)
        {
            int i = z * plan.Width + x, d = plan.Distance[i];
            int wx = plan.Bounds.X + x, wz = plan.Bounds.Y + z, top = _world.SurfaceY(wx, wz);
            uint variation = Hash(wx, wz, _world.Seed);
            if (d > 0)
            {
                // Broad shallows, then a smooth approach to a deep interior, with mild bed variation.
                float t = Math.Clamp((d - 1) / (float)Math.Max(12, plan.DeepestDistance - 1), 0, 1);
                int depth = 1 + (int)MathF.Round((plan.Depth - 1) * MathF.Sin(t * MathHelper.PiOver2));
                deepest = Math.Max(deepest, depth); int bed = waterY - depth;
                _world.SetGeneratedBlock(wx, bed, wz, variation % 9 == 0 ? BlockType.Sand : variation % 3 == 0 ? BlockType.Dirt : BlockType.Gravel);
                for (int y = bed + 1; y <= Math.Max(top, waterY); y++)
                    _world.SetGeneratedBlock(wx, y, wz, y <= waterY ? BlockType.WaterSource : BlockType.Air);
                plan.Modified[i] = true;
            }
            else
            {
                int distance = -d;
                int bank = Math.Clamp(top, waterY - distance + 1, waterY + distance - 1);
                if (bank == top && distance > 2) continue;
                for (int y = bank + 1; y <= top; y++) _world.SetGeneratedBlock(wx, y, wz, BlockType.Air);
                if (bank > top) for (int y = top + 1; y < bank; y++) _world.SetGeneratedBlock(wx, y, wz, BlockType.Dirt);
                BlockType surface = distance <= 2 ? variation % 6 < 2 ? BlockType.Sand : variation % 6 == 2 ? BlockType.Gravel
                    : variation % 6 == 3 ? BlockType.Dirt : BlockType.Grass : BlockType.Grass;
                _world.SetGeneratedBlock(wx, bank, wz, surface);
                if (surface == BlockType.Grass)
                    for (int y = bank - 1; y >= bank - _terrain.DirtDepth(wx, wz); y--) _world.SetGeneratedBlock(wx, y, wz, BlockType.Dirt);
                plan.Modified[i] = true;
            }
        }
        Rectangle wet = plan.WetBounds; wet.Offset(plan.Bounds.Location);
        Lakes.Add(new NaturalLake(wet, waterY, deepest, count, high - low)); return true;
    }
    private static uint Hash(int x, int z, int seed)
    {
        uint h = unchecked((uint)(seed ^ x * 374761393 ^ z * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16);
    }
    internal sealed class Plan
    {
        public int X, Z;
        public int Span { get; }
        public int Extent { get; }
        public int Radius { get; }
        public int Width => Extent * 2 + 1;
        public int Depth { get; }
        public int[] Distance { get; }
        public bool[] Modified { get; }
        public int DeepestDistance { get; private set; }
        public Rectangle WetBounds { get; private set; }
        public Rectangle Bounds => new(X - Extent, Z - Extent, Width, Width);
        public Plan(int span, int depth, Random random)
        {
            Span = span;
            Radius = span / 2 + 12; Extent = Radius + Blend; Depth = depth;
            int n = Width * Width; Distance = new int[n]; Modified = new bool[n];
            var wet = new bool[n]; float rx = span / 2f, rz = rx * (.65f + random.NextSingle() * .2f);
            float angle = random.NextSingle() * MathHelper.TwoPi, c = MathF.Cos(angle), s = MathF.Sin(angle);
            float phase = random.NextSingle() * 6;
            var bays = new Vector3[2];
            for (int i = 0; i < bays.Length; i++)
            {
                float a = random.NextSingle() * MathHelper.TwoPi;
                bays[i] = new Vector3(MathF.Cos(a) * rx, MathF.Sin(a) * rz, rz * (.25f + random.NextSingle() * .15f));
            }
            for (int z = -Radius; z <= Radius; z++) for (int x = -Radius; x <= Radius; x++)
            {
                float u = x * c + z * s, v = z * c - x * s;
                u += MathF.Sin(v / 39 + phase) * 4; v += MathF.Sin(u / 57 - phase) * 4;
                float d = Ellipse(u, v, rx * .82f, rz * .85f);
                d = SmoothMin(d, Ellipse(u + rx * .32f, v - rz * .12f, rx * .68f, rz * .8f), 8);
                d = SmoothMin(d, Ellipse(u - rx * .32f, v + rz * .12f, rx * .68f, rz * .65f), 8);
                foreach (Vector3 bay in bays)
                    d = -SmoothMin(-d, new Vector2(u - bay.X, v - bay.Y).Length() - bay.Z, 6);
                wet[(z + Extent) * Width + x + Extent] = d <= 0;
            }
            // Keep the center-connected basin if a cove cut off a small fragment.
            var queue = new Queue<int>(); int start = Extent * Width + Extent;
            if (!wet[start]) { for (int i = 0; i < n; i++) if (wet[i]) { start = i; break; } }
            Distance[start] = 1; queue.Enqueue(start);
            while (queue.TryDequeue(out int cell))
            {
                Flood(cell - 1); Flood(cell + 1); Flood(cell - Width); Flood(cell + Width);
                void Flood(int next)
                { if (next >= 0 && next < n && wet[next] && Distance[next] == 0) { Distance[next] = 1; queue.Enqueue(next); } }
            }
            for (int i = 0; i < n; i++) wet[i] = Distance[i] != 0;
            Array.Clear(Distance);
            int minX = Width, minZ = Width, maxX = 0, maxZ = 0;
            for (int z = 1; z < Width - 1; z++) for (int x = 1; x < Width - 1; x++)
            {
                int i = z * Width + x;
                if (wet[i]) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z); }
                bool edge = wet[i] && (!wet[i - 1] || !wet[i + 1] || !wet[i - Width] || !wet[i + Width]);
                bool bank = !wet[i] && (wet[i - 1] || wet[i + 1] || wet[i - Width] || wet[i + Width]);
                if (edge || bank) { Distance[i] = edge ? 1 : -1; queue.Enqueue(i); }
            }
            while (queue.TryDequeue(out int cell))
            {
                int x = cell % Width, z = cell / Width;
                Visit(x - 1, z); Visit(x + 1, z); Visit(x, z - 1); Visit(x, z + 1);
                void Visit(int nx, int nz)
                {
                    if ((uint)nx >= Width || (uint)nz >= Width) return;
                    int next = nz * Width + nx;
                    if (Distance[next] != 0 || wet[next] != wet[cell]) return;
                    Distance[next] = Distance[cell] + (wet[next] ? 1 : -1); queue.Enqueue(next);
                }
                DeepestDistance = Math.Max(DeepestDistance, Distance[cell]);
            }
            WetBounds = new Rectangle(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }
        private static float Ellipse(float x, float z, float rx, float rz) => (MathF.Sqrt(x * x / (rx * rx) + z * z / (rz * rz)) - 1) * Math.Min(rx, rz);
        private static float SmoothMin(float a, float b, float k)
        { float h = Math.Max(k - Math.Abs(a - b), 0) / k; return Math.Min(a, b) - h * h * k * .25f; }
    }
}
