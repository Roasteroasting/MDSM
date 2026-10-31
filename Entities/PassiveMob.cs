using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Entities;

public sealed class PassiveMob
{
    public MobSpecies Species { get; }
    public MobDefinition Definition { get; }
    private float HalfWidth => Definition.HalfWidth;
    private float Height => Definition.Height;
    public Vector3 Position { get; private set; }
    public float Yaw { get; private set; }
    public int Health { get; private set; }
    public bool IsDead => Health == 0;
    public bool Grounded { get; private set; }
    public bool Walking { get; private set; }
    public float WalkPhase { get; private set; }
    public float HurtSeconds { get; private set; }
    public bool Sheared { get; private set; }
    public double WoolSeconds { get; private set; }
    public BoundingBox Bounds => BoundsAt(Species, Position);
    private readonly Random _random;
    private Vector3 _velocity;
    private float _decisionSeconds;
    private float _shoreHopSeconds;
    private bool _wandering;
    // Tiny local voxel cache, not a simulation throttle: all AI/physics still run at 120 Hz.
    // Conservative world revision invalidation preserves immediate edits/fluid changes.
    private readonly bool[] _solidCells = new bool[216];
    private int _cacheRevision = -1, _minX, _minY, _minZ, _maxX, _maxY, _maxZ, _cacheWidth, _cacheDepth;

    internal PassiveMob(MobState state, int aiSeed)
    {
        Species = state.Species;
        Definition = MobDefinitions.Get(Species);
        Position = state.Position; Yaw = state.Yaw; Health = state.Health;
        Sheared=state.Sheared;WoolSeconds=state.WoolSeconds;
        _random = new Random(aiSeed);
        _decisionSeconds = 0.5f + _random.NextSingle() * 2;
    }

    public MobState CaptureState() => new(Species, Position, Yaw, Health,Sheared,WoolSeconds);
    internal bool Shear()
    {
        if(Species!=MobSpecies.Sheep || Sheared || IsDead)return false;
        Sheared=true;WoolSeconds=180+_random.NextDouble()*180;return true;
    }
    public static BoundingBox BoundsAt(MobSpecies species, Vector3 feet)
    {
        MobDefinition definition = MobDefinitions.Get(species);
        return new(feet + new Vector3(-definition.HalfWidth, 0, -definition.HalfWidth),
            feet + new Vector3(definition.HalfWidth, definition.Height, definition.HalfWidth));
    }

    internal void Damage(int amount, Vector3 away)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (IsDead || amount == 0) return;
        Health -= Math.Min(Health, amount);
        HurtSeconds = 0.25f;
        away.Y = 0;
        if (away.LengthSquared() > 0.000001f) away.Normalize();
        _velocity = away * 2.5f + Vector3.Up * 4;
        // Neither the idle/wander decision nor facing changes in response to damage.
    }

    internal void Update(World world, float elapsed)
    {
        if (IsDead || !float.IsFinite(elapsed) || elapsed <= 0) return;
        float dt = Math.Min(elapsed, 0.1f);
        if(Sheared){WoolSeconds=Math.Max(0,WoolSeconds-elapsed);if(WoolSeconds<=0)Sheared=false;}
        HurtSeconds = Math.Max(0, HurtSeconds - dt);
        _shoreHopSeconds = Math.Max(0, _shoreHopSeconds - dt);
        if ((_decisionSeconds -= dt) <= 0)
        {
            _wandering = !_wandering;
            _decisionSeconds = 1 + _random.NextSingle() * (_wandering ? 4 : 3);
            if (_wandering) Yaw = _random.NextSingle() * MathHelper.TwoPi - MathHelper.Pi;
        }
        Vector3 before = Position;
        Vector3 walking = _wandering ? new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw)) * Definition.WalkSpeed : Vector3.Zero;
        bool swimming = Water.Intersects(world, Bounds);
        if (swimming)
        {
            walking *= 0.45f;
            bool submerged = Water.Intersects(world, new BoundingBox(Position + new Vector3(-0.1f, Height * 0.55f, -0.1f),
                Position + new Vector3(0.1f, Height * 0.65f, 0.1f)));
            _velocity.Y = submerged ? Math.Max(1.5f, _velocity.Y - 4 * dt) : Math.Max(-2, _velocity.Y * MathF.Exp(-3 * dt) - 4 * dt);
        }
        else _velocity.Y = Math.Max(-40, _velocity.Y - 18 * dt);
        Vector3 displacement = (walking + _velocity) * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / 0.15f));
        Vector3 step = displacement / steps;
        Grounded = false;
        bool blocked = false;
        for (int i = 0; i < steps; i++)
        {
            blocked |= MoveAxis(world, step.X, 0);
            blocked |= MoveAxis(world, step.Z, 2);
            if (MoveAxis(world, step.Y, 1))
            {
                if (step.Y < 0) Grounded = true;
                step.Y = 0; _velocity.Y = 0;
            }
        }
        if (blocked && _wandering)
        {
            // A small swimming hop lets wandering pigs leave a one-block shore instead of getting trapped.
            if (swimming) { _velocity.Y = Math.Max(_velocity.Y, 4.5f); _shoreHopSeconds = 0.7f; }
            else if (_shoreHopSeconds == 0) { _wandering = false; _decisionSeconds = 0.5f; }
        }
        float decay = MathF.Exp(-8 * dt);
        _velocity.X *= decay; _velocity.Z *= decay;
        if (Position.Y < -16) { Position = new Vector3(Position.X, -16, Position.Z); _velocity.Y = 0; }
        float distance = new Vector2(Position.X - before.X, Position.Z - before.Z).Length();
        Walking = distance > 0.00001f;
        if (Walking) WalkPhase = (WalkPhase + distance * 7) % MathHelper.TwoPi;
    }

    private bool MoveAxis(World world, float amount, int axis)
    {
        if (amount == 0) return false;
        Vector3 next = Position;
        if (axis == 0) next.X = Math.Clamp(next.X + amount, HalfWidth, world.Width - HalfWidth);
        else if (axis == 2) next.Z = Math.Clamp(next.Z + amount, HalfWidth, world.Depth - HalfWidth);
        else next.Y += amount;
        BoundingBox bounds = BoundsAt(Species, next);
        CacheCollisionCells(world, bounds);
        bool blocked = axis == 0 ? next.X != Position.X + amount : axis == 2 && next.Z != Position.Z + amount;
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {
            if (!_solidCells[((y - _minY) * _cacheDepth + z - _minZ) * _cacheWidth + x - _minX]
                || !World.Overlaps(bounds, World.Bounds(new Point3(x, y, z)))) continue;
            blocked = true;
            if (axis == 0) next.X = amount > 0 ? Math.Min(next.X, x - HalfWidth) : Math.Max(next.X, x + 1 + HalfWidth);
            else if (axis == 2) next.Z = amount > 0 ? Math.Min(next.Z, z - HalfWidth) : Math.Max(next.Z, z + 1 + HalfWidth);
            else next.Y = amount > 0 ? Math.Min(next.Y, y - Height) : Math.Max(next.Y, y + 1);
        }
        Position = next;
        return blocked;
    }

    private void CacheCollisionCells(World world, BoundingBox bounds)
    {
        int minX = (int)MathF.Floor(bounds.Min.X), minY = (int)MathF.Floor(bounds.Min.Y), minZ = (int)MathF.Floor(bounds.Min.Z);
        int maxX = (int)MathF.Floor(bounds.Max.X), maxY = (int)MathF.Floor(bounds.Max.Y), maxZ = (int)MathF.Floor(bounds.Max.Z);
        if (_cacheRevision == world.Revision && minX >= _minX && minY >= _minY && minZ >= _minZ
            && maxX <= _maxX && maxY <= _maxY && maxZ <= _maxZ) return;
        _minX = (int)MathF.Floor(bounds.Min.X - .25f); _maxX = (int)MathF.Floor(bounds.Max.X + .25f);
        _minY = (int)MathF.Floor(bounds.Min.Y - .25f); _maxY = (int)MathF.Floor(bounds.Max.Y + .25f);
        _minZ = (int)MathF.Floor(bounds.Min.Z - .25f); _maxZ = (int)MathF.Floor(bounds.Max.Z + .25f);
        _cacheWidth = _maxX - _minX + 1; _cacheDepth = _maxZ - _minZ + 1;
        int i = 0;
        for (int y = _minY; y <= _maxY; y++) for (int z = _minZ; z <= _maxZ; z++) for (int x = _minX; x <= _maxX; x++)
            _solidCells[i++] = world.IsSolid(x, y, z);
        _cacheRevision = world.Revision;
    }
}
