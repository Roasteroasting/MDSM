using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Entities;

public class PassiveMobPopulation
{
    public const int MaximumMobs = 256; // Shared save/runtime safety bound, not a per-species limit.
    public const double MinimumRespawnSeconds = 30, MaximumRespawnSeconds = 60, RetrySeconds = 5;
    public const int MaximumSpawnAttemptsPerUpdate = 32;
    private readonly List<PassiveMob> _mobs = new();
    private readonly World _world;
    private readonly Random _random;
    private readonly Random _respawnRandom;
    private readonly int[] _living = new int[MobDefinitions.SpeciesCount];
    private readonly int[] _targets = new int[MobDefinitions.SpeciesCount];
    private readonly List<MobRespawnState> _pending = new();
    public IReadOnlyList<PassiveMob> Entities => _mobs;
    public IReadOnlyList<MobRespawnState> PendingRespawns => _pending;
    public int LastSpawnAttempts { get; private set; }
    public int Target(MobSpecies species) => _targets[(int)species];
    public static int InitialBudget(World world) => world.Width * world.Depth / 2048; // 8 / 32 / 128 total.

    public PassiveMobPopulation(World world, ReadOnlySpan<MobState> saved = default, MobPopulationState population = null)
    {
        _world = world; _random = new Random(unchecked(world.Seed ^ 0x504947));
        _respawnRandom = new Random(unchecked(world.Seed ^ 0x5253504E));
        foreach (MobState state in saved) AddSaved(state);
        RestorePopulation(population);
    }
    public PassiveMobPopulation(World world, ReadOnlySpan<PigState> pigs, ReadOnlySpan<MobState> others, MobPopulationState population = null) : this(world)
    {
        if (pigs.Length + others.Length > MaximumMobs) throw new InvalidDataException("Too many passive mobs.");
        foreach (PigState pig in pigs) AddSaved(new MobState(MobSpecies.Pig, pig.Position, pig.Yaw, pig.Health));
        foreach (MobState state in others)
        {
            if (state.Species == MobSpecies.Pig) throw new InvalidDataException("Pigs must use the pig state section.");
            AddSaved(state);
        }
        RestorePopulation(population);
    }
    private void RestorePopulation(MobPopulationState state)
    {
        if (state == null) _living.CopyTo(_targets, 0); // One-time migration: survivors become the baseline.
        else
        {
            state.Validate(_living); state.Targets.CopyTo(_targets, 0); _pending.AddRange(state.Pending);
        }
    }
    public MobPopulationState CapturePopulation() => new((int[])_targets.Clone(), _pending.ToArray());
    private void AddSaved(MobState state)
    {
        if (_mobs.Count >= MaximumMobs) throw new InvalidDataException("Too many passive mobs.");
        Validate(state, _world.Size);
        _mobs.Add(state.Species == MobSpecies.Pig
            ? new Pig(new PigState(state.Position, state.Yaw, state.Health), _random.Next()) : new PassiveMob(state, _random.Next()));
        _living[(int)state.Species]++;
    }

    public static MobState[] Generate(World world) => Generate(world, null);
    // Pig-only fixtures/readers use exactly the same placement checks and seeded sequence.
    internal static MobState[] Generate(World world, MobSpecies? onlySpecies)
    {
        int target = InitialBudget(world);
        var result = new List<MobState>(target);
        var random = new Random(unchecked(world.Seed ^ 0x53504157));
        var mixture = new Random(unchecked(world.Seed ^ 0x4D495854));
        var counts = new int[MobDefinitions.SpeciesCount];
        Vector3 spawn = world.Spawn;
        for (int attempt = 0; attempt < target * 200 && result.Count < target; attempt++)
        {
            // Mild balancing leaves random local variation while preventing a missing species.
            int least = Math.Min(Math.Min(counts[0], counts[1]), Math.Min(counts[2], counts[3]));
            MobSpecies species;
            do { species = onlySpecies ?? (MobSpecies)mixture.Next(MobDefinitions.SpeciesCount); }
            while (!onlySpecies.HasValue && counts[(int)species] > least + 1);
            MobDefinition definition = MobDefinitions.Get(species);
            int x = random.Next(2, world.Width - 2), z = random.Next(2, world.Depth - 2);
            if (!Suitable(world, species, x, z, out Vector3 feet)
                || Vector2.DistanceSquared(new Vector2(feet.X, feet.Z), new Vector2(spawn.X, spawn.Z)) < 144) continue;
            bool suitable = true;
            foreach (MobState other in result) if (Vector3.DistanceSquared(other.Position, feet) < 16) suitable = false;
            if (!suitable) continue;
            result.Add(new MobState(species, feet, random.NextSingle() * MathHelper.TwoPi - MathHelper.Pi, definition.MaximumHealth));
            counts[(int)species]++;
        }
        return result.ToArray();
    }
    private static bool Suitable(World world, MobSpecies species, int x, int z, out Vector3 feet)
    {
        int y = world.SurfaceY(x, z); feet = new Vector3(x + 0.5f, y + 1, z + 0.5f);
        MobDefinition definition = MobDefinitions.Get(species);
        BoundingBox bounds = PassiveMob.BoundsAt(species, feet);
        if (world.GetBlock(x, y, z) != BlockType.Grass || feet.Y + definition.Height > World.Height
            || world.IntersectsSolid(bounds) || Water.Intersects(world, bounds)) return false;
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            if (world.GetBlock(x + dx, y, z + dz) != BlockType.Grass) return false;
        return true;
    }
    public void Update(float elapsed, Vector3? playerPosition = null, BoundingBox? playerBounds = null, DroppedItems items = null)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0) return;
        foreach (PassiveMob mob in _mobs) mob.Update(_world, elapsed);
        LastSpawnAttempts = 0;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            MobRespawnState next = _pending[i];
            double remaining = Math.Max(0, next.RemainingSeconds - elapsed);
            _pending[i] = next with { RemainingSeconds = remaining };
            if (remaining > 1e-9 || LastSpawnAttempts >= MaximumSpawnAttemptsPerUpdate) continue;
            if (TryReplace(next.Species, playerPosition ?? _world.Spawn, playerBounds, items)) _pending.RemoveAt(i);
            else _pending[i] = next with { RemainingSeconds = RetrySeconds };
        }
    }
    private bool TryReplace(MobSpecies species, Vector3 player, BoundingBox? playerBounds, DroppedItems items)
    {
        if (_living[(int)species] >= _targets[(int)species]) return false;
        int attempts = 0;
        while (LastSpawnAttempts < MaximumSpawnAttemptsPerUpdate)
        {
            LastSpawnAttempts++; attempts++;
            int x = _respawnRandom.Next(2, _world.Width - 2), z = _respawnRandom.Next(2, _world.Depth - 2);
            if (!Suitable(_world, species, x, z, out Vector3 feet)) continue;
            float distance = attempts <= MaximumSpawnAttemptsPerUpdate / 2 ? 24 : 12;
            if (Vector2.DistanceSquared(new Vector2(feet.X, feet.Z), new Vector2(player.X, player.Z)) < distance * distance) continue;
            BoundingBox bounds = PassiveMob.BoundsAt(species, feet);
            if (playerBounds.HasValue && World.Overlaps(bounds, playerBounds.Value)) continue;
            bool occupied = false;
            foreach (PassiveMob other in _mobs) if (World.Overlaps(bounds, other.Bounds)) { occupied = true; break; }
            if (!occupied && items != null)
                for (int i = 0; i < items.Entities.Count; i++)
                {
                    Vector3 center = items.Entities[i].Position;
                    if (World.Overlaps(bounds, new BoundingBox(center - new Vector3(DroppedItem.Radius), center + new Vector3(DroppedItem.Radius))))
                    { occupied = true; break; }
                }
            if (occupied) continue;
            AddSaved(new MobState(species, feet, _respawnRandom.NextSingle() * MathHelper.TwoPi - MathHelper.Pi, MobDefinitions.Get(species).MaximumHealth));
            return true;
        }
        return false;
    }
    public bool Punch(PassiveMob mob, Vector3 attacker, DroppedItems items)
    {
        if (mob == null || mob.IsDead || !_mobs.Contains(mob)) return false;
        int damage = _random.Next(1, 3);
        if (damage >= mob.Health && !items.TrySpawn(new ItemStack(mob.Definition.Drop, _random.Next(1, mob.Definition.MaximumDrop + 1)),
            mob.Position + Vector3.Up * 0.25f, 0.35f)) return false;
        mob.Damage(damage, mob.Position - attacker);
        if (mob.IsDead && _mobs.Remove(mob))
        {
            _living[(int)mob.Species]--;
            _pending.Add(new MobRespawnState(mob.Species, MinimumRespawnSeconds + _respawnRandom.NextDouble() * (MaximumRespawnSeconds - MinimumRespawnSeconds)));
        }
        return true;
    }
    public MobState[] CaptureAll()
    {
        var result = new MobState[_mobs.Count]; for (int i = 0; i < result.Length; i++) result[i] = _mobs[i].CaptureState(); return result;
    }
    public PigState[] CapturePigs()
    {
        int count = 0; foreach (PassiveMob mob in _mobs) if (mob.Species == MobSpecies.Pig) count++;
        var result = new PigState[count]; int i = 0;
        foreach (PassiveMob mob in _mobs) if (mob.Species == MobSpecies.Pig) result[i++] = new PigState(mob.Position, mob.Yaw, mob.Health);
        return result;
    }
    public MobState[] CaptureOthers()
    {
        int count = 0; foreach (PassiveMob mob in _mobs) if (mob.Species != MobSpecies.Pig) count++;
        var result = new MobState[count]; int i = 0;
        foreach (PassiveMob mob in _mobs) if (mob.Species != MobSpecies.Pig) result[i++] = mob.CaptureState();
        return result;
    }
    public static void Validate(MobState state, WorldSize size)
    {
        if (!MobDefinitions.IsDefined(state.Species)) throw new InvalidDataException("Invalid mob species.");
        MobDefinition d = MobDefinitions.Get(state.Species); Vector3 p = state.Position;
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
            || p.X < d.HalfWidth || p.X > size.Width() - d.HalfWidth || p.Z < d.HalfWidth || p.Z > size.Width() - d.HalfWidth
            || p.Y < -16 || p.Y > World.Height + 16 || !float.IsFinite(state.Yaw) || MathF.Abs(state.Yaw) > MathHelper.Pi
            || state.Health < 1 || state.Health > d.MaximumHealth) throw new InvalidDataException("Save contains an invalid passive mob.");
    }
}
