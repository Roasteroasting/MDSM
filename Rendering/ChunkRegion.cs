using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

// Residency is independent of block storage. The extra rings prefetch and prevent boundary churn.
internal sealed class ChunkRegion
{
    private readonly World _world;
    private readonly bool[] _present;
    private int _x = int.MinValue, _z, _radius;
    public List<int> Indices { get; } = new();
    public List<int> Removed { get; } = new();
    public int Version { get; private set; }
    public int Radius => _radius;
    public int MaximumCount => (2 * (_radius + 2) + 1) * (2 * (_radius + 2) + 1);
    public ChunkRegion(World world) { _world = world; _present = new bool[world.ChunkCount]; }
    public bool Contains(int index) => _present[index];
    public bool Update(Vector3 position, float aspect)
    {
        int x = Math.Clamp((int)MathF.Floor(position.X / Chunk.Size), 0, _world.ChunksX - 1);
        int z = Math.Clamp((int)MathF.Floor(position.Z / Chunk.Size), 0, _world.ChunksZ - 1);
        // A sphere enclosing the far frustum corners also covers all camera elevations/directions.
        float tangent = MathF.Tan(MathHelper.ToRadians(75) / 2);
        int radius = (int)MathF.Ceiling(Camera.FarPlane * MathF.Sqrt(1 + tangent * tangent * (1 + aspect * aspect)) / Chunk.Size) + 2;
        if (x == _x && z == _z && radius == _radius) return false;
        _x = x; _z = z; _radius = radius; Removed.Clear();
        int keep = radius + 2;
        for (int i = Indices.Count - 1; i >= 0; i--)
        {
            int index = Indices[i], dx = index % _world.ChunksX - x, dz = index / _world.ChunksX - z;
            if (dx * dx + dz * dz <= keep * keep) continue;
            _present[index] = false; Removed.Add(index); Indices.RemoveAt(i);
        }
        for (int cz = Math.Max(0, z - radius); cz <= Math.Min(_world.ChunksZ - 1, z + radius); cz++)
        for (int cx = Math.Max(0, x - radius); cx <= Math.Min(_world.ChunksX - 1, x + radius); cx++)
        {
            int dx = cx - x, dz = cz - z, index = cz * _world.ChunksX + cx;
            if (dx * dx + dz * dz > radius * radius || _present[index]) continue;
            _present[index] = true; Indices.Add(index);
        }
        Version++;
        return true;
    }
}
