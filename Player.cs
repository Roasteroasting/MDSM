using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public sealed class Player
{
    public const float BodyHeight = 1.8f;
    public const float HalfWidth = 0.3f;
    private const float MoveSpeed = 5f;
    private const float Gravity = 24f;
    private const float JumpSpeed = 8f;
    private float _verticalSpeed;

    // Position is the center of the player's feet.
    public Vector3 Position { get; private set; }
    public Vector3 EyePosition => Position + new Vector3(0, 1.62f, 0);
    public bool Grounded { get; private set; }
    public BoundingBox Bounds => BoundsAt(Position);

    public Player(Vector3 spawn) => Position = spawn;

    public void Update(World world, Vector3 movement, bool jump, float elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
            return;

        float dt = MathF.Min(elapsedSeconds, 0.1f);
        movement.Y = 0;
        if (movement.LengthSquared() > 1)
            movement.Normalize();

        if (jump && Grounded)
            _verticalSpeed = JumpSpeed;

        _verticalSpeed = MathF.Max(_verticalSpeed - Gravity * dt, -40f);
        Vector3 displacement = movement * MoveSpeed * dt + new Vector3(0, _verticalSpeed * dt, 0);
        // Each step is narrower than a block, so fast falls cannot tunnel through terrain.
        int steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / 0.2f));
        Vector3 step = displacement / steps;
        Grounded = false;
        for (int i = 0; i < steps; i++)
        {
            MoveAxis(world, step.X, 0);
            MoveAxis(world, step.Z, 2);
            if (MoveAxis(world, step.Y, 1))
            {
                if (step.Y < 0)
                    Grounded = true;
                _verticalSpeed = 0;
                step.Y = 0;
            }
        }

        if (Position.Y < -16)
            Respawn(world);
    }

    private bool MoveAxis(World world, float amount, int axis)
    {
        if (amount == 0)
            return false;

        Vector3 next = Position;
        if (axis == 0) next.X = Math.Clamp(next.X + amount, HalfWidth, World.Width - HalfWidth);
        else if (axis == 1) next.Y += amount;
        else next.Z = Math.Clamp(next.Z + amount, HalfWidth, World.Depth - HalfWidth);

        BoundingBox bounds = BoundsAt(next);
        bool collided = false;
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(World.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(World.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
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

    private void Respawn(World world)
    {
        Vector3 spawn = world.Spawn;
        // Edits can obstruct the original spawn. Find clear space above the terrain.
        while (spawn.Y < World.Height && world.IntersectsSolid(BoundsAt(spawn)))
            spawn.Y++;
        Position = spawn;
        _verticalSpeed = 0;
        Grounded = false;
    }

    private static BoundingBox BoundsAt(Vector3 feet) => new(
        feet + new Vector3(-HalfWidth, 0, -HalfWidth),
        feet + new Vector3(HalfWidth, BodyHeight, HalfWidth));
}
