using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Entities;

public sealed class PigPopulation
{
    public const int MaximumPigs = 256;
    private readonly List<Pig> _pigs = new();
    private readonly World _world;
    private readonly Random _random;
    public IReadOnlyList<Pig> Entities => _pigs;

    public PigPopulation(World world, ReadOnlySpan<PigState> saved = default)
    {
        _world = world;
        _random = new Random(unchecked(world.Seed ^ 0x504947));
        if (saved.Length > MaximumPigs) throw new InvalidDataException("Too many pigs.");
        foreach (PigState state in saved)
        {
            Validate(state, world.Size);
            _pigs.Add(new Pig(state, _random.Next()));
        }
    }

    public static PigState[] Generate(World world)
    {
        int target = world.Width * world.Depth / 2048; // Small 8, Normal 32, Large 128.
        var result = new List<PigState>(target);
        var random = new Random(unchecked(world.Seed ^ 0x53504157));
        Vector3 spawn = world.Spawn;
        for (int attempt = 0; attempt < target * 200 && result.Count < target; attempt++)
        {
            int x = random.Next(2, world.Width - 2), z = random.Next(2, world.Depth - 2);
            int y = world.SurfaceY(x, z);
            Vector3 feet = new(x + 0.5f, y + 1, z + 0.5f);
            if (world.GetBlock(x, y, z) != BlockType.Grass || y + 1 + Pig.Height > World.Height
                || Vector2.DistanceSquared(new Vector2(feet.X, feet.Z), new Vector2(spawn.X, spawn.Z)) < 144
                || world.IntersectsSolid(Pig.BoundsAt(feet))) continue;
            bool suitable = true;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (world.GetBlock(x + dx, y, z + dz) != BlockType.Grass) suitable = false;
            foreach (PigState other in result)
                if (Vector3.DistanceSquared(other.Position, feet) < 16) suitable = false;
            if (suitable) result.Add(new PigState(feet, random.NextSingle() * MathHelper.TwoPi - MathHelper.Pi, Pig.MaximumHealth));
        }
        return result.ToArray();
    }

    public void Update(float elapsed)
    {
        foreach (Pig pig in _pigs) pig.Update(_world, elapsed);
    }

    public bool Punch(Pig pig, Vector3 attacker, DroppedItems items)
    {
        if (pig == null || pig.IsDead || !_pigs.Contains(pig)) return false;
        int damage = _random.Next(1, 3);
        if (damage >= pig.Health)
        {
            // Reserve the drop before the irreversible death/removal, like block mining.
            if (!items.TrySpawn(new ItemStack(Items.RawPorkchop, _random.Next(1, 3)),
                pig.Position + Vector3.Up * 0.25f, 0.35f)) return false;
        }
        pig.Damage(damage, pig.Position - attacker);
        if (pig.IsDead) _pigs.Remove(pig);
        return true;
    }

    public PigState[] Capture()
    {
        var states = new PigState[_pigs.Count];
        for (int i = 0; i < states.Length; i++) states[i] = _pigs[i].Capture();
        return states;
    }

    public static void Validate(PigState state, WorldSize size)
    {
        Vector3 p = state.Position;
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
            || p.X < Pig.HalfWidth || p.X > size.Width() - Pig.HalfWidth
            || p.Z < Pig.HalfWidth || p.Z > size.Width() - Pig.HalfWidth || p.Y < -16 || p.Y > World.Height + 16
            || !float.IsFinite(state.Yaw) || MathF.Abs(state.Yaw) > MathHelper.Pi || state.Health < 1 || state.Health > Pig.MaximumHealth)
            throw new InvalidDataException("Save contains an invalid pig.");
    }
}
