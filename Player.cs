using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public sealed class Player
{
    public const float BodyHeight = 1.8f;
    public const float HalfWidth = 0.3f;
    public const int MaximumHealth = 20;
    public const float StandingEyeHeight = 1.62f;
    public const float CrouchingEyeHeight = 1.50f;
    private const float MoveSpeed = 5f;
    private const float Gravity = 24f;
    private const float JumpSpeed = 8f;
    private float _verticalSpeed;
    private readonly FallTracker _fall = new();
    public int Health { get; private set; }
    public bool IsDead => Health == 0;
    public bool Crouching { get; private set; }

    // Position is the center of the player's feet.
    public Vector3 Position { get; private set; }
    public Vector3 EyePosition => Position + new Vector3(0, Crouching ? CrouchingEyeHeight : StandingEyeHeight, 0);
    public bool Grounded { get; private set; }
    public BoundingBox Bounds => BoundsAt(Position);

    public Player(Vector3 spawn, int health = MaximumHealth)
    {
        if (health < 0 || health > MaximumHealth) throw new ArgumentOutOfRangeException(nameof(health));
        Position = spawn; Health = health; _fall.Reset(spawn.Y);
    }

    public void ApplyDamage(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Health -= Math.Min(Health, amount);
        if (IsDead) StopMovement();
    }
    public void RestoreHealth(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Health += Math.Min(MaximumHealth - Health, amount);
    }
    public void StopMovement()
    {
        _verticalSpeed = 0; Grounded = false; Crouching = false; _fall.Reset(Position.Y);
    }
    public void StopCrouching() => Crouching = false;

    public void Update(World world, Vector3 movement, bool jump, float elapsedSeconds, bool crouch = false)
    {
        if (IsDead || !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
            return;

        Crouching = crouch;
        float dt = MathF.Min(elapsedSeconds, 0.1f);
        movement.Y = 0;
        if (movement.LengthSquared() > 1)
            movement.Normalize();

        bool jumping = jump && Grounded;
        bool protectEdge = Crouching && Grounded && !jumping && HasSupport(world, Position);
        if (jumping)
            _verticalSpeed = JumpSpeed;

        _fall.Observe(Position.Y);
        _verticalSpeed = MathF.Max(_verticalSpeed - Gravity * dt, -40f);
        Vector3 displacement = movement * MoveSpeed * (Crouching ? 0.35f : 1) * dt + new Vector3(0, _verticalSpeed * dt, 0);
        // Each step is narrower than a block, so fast falls cannot tunnel through terrain.
        int steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / 0.2f));
        Vector3 step = displacement / steps;
        Grounded = false;
        for (int i = 0; i < steps; i++)
        {
            MoveAxis(world, protectEdge ? SupportedMovement(world, step.X, 0) : step.X, 0);
            MoveAxis(world, protectEdge ? SupportedMovement(world, step.Z, 2) : step.Z, 2);
            if (MoveAxis(world, step.Y, 1))
            {
                if (step.Y < 0)
                    Grounded = true;
                _verticalSpeed = 0;
                step.Y = 0;
            }
            _fall.Observe(Position.Y);
        }

        if (Grounded) ApplyDamage(_fall.Land(Position.Y));

        if (!IsDead && Position.Y < -16)
            MoveToSpawn(world); // Existing void recovery grants no healing or new damage type.
    }

    private static bool HasSupport(World world, Vector3 feet) => world.IntersectsSolid(new BoundingBox(
        feet + new Vector3(-HalfWidth + 0.001f, -0.05f, -HalfWidth + 0.001f),
        feet + new Vector3(HalfWidth - 0.001f, 0, HalfWidth - 0.001f)));

    private float SupportedMovement(World world, float amount, int axis)
    {
        if (amount == 0) return 0;
        Vector3 direction = axis == 0 ? Vector3.UnitX : Vector3.UnitZ;
        if (HasSupport(world, Position + direction * amount)) return amount;
        float low = 0, high = 1;
        for (int i = 0; i < 12; i++)
        {
            float middle = (low + high) * 0.5f;
            if (HasSupport(world, Position + direction * (amount * middle))) low = middle;
            else high = middle;
        }
        return amount * low;
    }

    private bool MoveAxis(World world, float amount, int axis)
    {
        if (amount == 0)
            return false;

        Vector3 next = Position;
        if (axis == 0) next.X = Math.Clamp(next.X + amount, HalfWidth, world.Width - HalfWidth);
        else if (axis == 1) next.Y += amount;
        else next.Z = Math.Clamp(next.Z + amount, HalfWidth, world.Depth - HalfWidth);

        BoundingBox bounds = BoundsAt(next);
        bool collided = false;
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {
            if (!world.IsSolid(x, y, z) || !World.Overlaps(bounds, World.Bounds(new Point3(x, y, z))))
                continue;

            collided = true;
            // Clamp the moving axis to the nearest blocking face; retain motion on other axes.
            if (axis == 0) next.X = amount > 0 ? MathF.Min(next.X, x - HalfWidth) : MathF.Max(next.X, x + 1 + HalfWidth);
            else if (axis == 1) next.Y = amount > 0 ? MathF.Min(next.Y, y - BodyHeight) : MathF.Max(next.Y, y + 1);
            else next.Z = amount > 0 ? MathF.Min(next.Z, z - HalfWidth) : MathF.Max(next.Z, z + 1 + HalfWidth);
        }

        Position = next;
        return collided;
    }

    public void Respawn(World world)
    {
        Health = MaximumHealth;
        MoveToSpawn(world);
    }

    private void MoveToSpawn(World world)
    {
        Vector3 spawn = world.Spawn;
        // Edits can obstruct the original spawn. Find clear space above the terrain.
        while (spawn.Y < World.Height && world.IntersectsSolid(BoundsAt(spawn)))
            spawn.Y++;
        Position = spawn;
        StopMovement();
    }

    private static BoundingBox BoundsAt(Vector3 feet) => new(
        feet + new Vector3(-HalfWidth, 0, -HalfWidth),
        feet + new Vector3(HalfWidth, BodyHeight, HalfWidth));
}
