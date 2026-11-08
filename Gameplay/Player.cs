using System;
using Microsoft.Xna.Framework;

using MDSM.Worlds;
using MDSM.Inventories;
using MDSM.Core;
using MDSM.Audio;

namespace MDSM.Gameplay;

public sealed class Player
{
    public const float BodyHeight = 1.8f;
    public const float HalfWidth = 0.3f;
    public const int MaximumHealth = 20;
    public const float StandingEyeHeight = 1.62f;
    public const float CrouchingEyeHeight = 1.50f;
    public const float MoveSpeed = 4.3f;
    public const float CreativeFlightBoostMultiplier = 3f;
    public const float Gravity = 28f;
    public const float JumpSpeed = 8.4f;
    public const float TerminalVelocity = 60f;
    public const float StepHeight = .6f;
    public GameSettings Settings { get; set; } = new();
    internal SoundEvents Sounds { get; set; }
    public bool Creative => Settings.GameMode == GameMode.Creative;
    public BreathMeter Breath { get; } = new();
    public BurningStatus Burning {get;}=new();
    private bool _wasCreative, _canStep;
    private float _verticalSpeed;
    private Vector3 _horizontalSpeed;
    private readonly FallTracker _fall = new();
    private double _health;
    private readonly Inventory _equipment;
    private double _invulnerable,_lavaSeconds;
    public double HealthValue=>_health;
    public int Health => (int)Math.Ceiling(_health);
    public bool IsDead => Health == 0;
    public bool Crouching { get; private set; }

    // Position is the center of the player's feet.
    public Vector3 Position { get; private set; }
    public Vector3 EyePosition => Position + new Vector3(0, Crouching ? CrouchingEyeHeight : StandingEyeHeight, 0);
    public bool Grounded { get; private set; }
    public BoundingBox Bounds => BoundsAt(Position);

    public Player(Vector3 spawn, int health = MaximumHealth,Inventory equipment=null)
    {
        if (health < 0 || health > MaximumHealth) throw new ArgumentOutOfRangeException(nameof(health));
        Position = spawn; _health = health;_equipment=equipment; _fall.Reset(spawn.Y);
    }

    public void ApplyDamage(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        TakeDamage(amount,false);
    }
    public bool TakeDamage(double amount,bool invulnerability=true,bool bypassArmor=false)
    {
        if(!double.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
        if(Creative || amount==0 || IsDead || invulnerability && _invulnerable>0)return false;
        if (!bypassArmor) amount *= Settings.DamageMultiplier;
        double reduction=bypassArmor?0:Math.Min(26,_equipment?.ArmorPoints??0)*.03;
        _health=Math.Max(0,_health-amount*(1-reduction));if(!bypassArmor)_equipment?.WearArmor();
        Sounds?.Local(IsDead?SoundKind.PlayerDeath:SoundKind.PlayerHurt);
        if(invulnerability)_invulnerable=.5;if(IsDead)StopMovement();return true;
    }
    internal void RestoreFraction(double health)
    {if(!double.IsFinite(health)||health<0||health>20)throw new ArgumentException("Invalid fractional health.");_health=health;}
    // An explicit command death bypasses damage, difficulty, armor and Creative
    // immunity without changing those ordinary gameplay rules or wearing armor.
    internal bool Kill()
    {
        if (IsDead) return false;
        _health = 0;
        StopMovement();
        Sounds?.Local(SoundKind.PlayerDeath);
        return true;
    }
    public void RestoreHealth(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        _health += Math.Min(MaximumHealth - _health, amount);
    }
    public void StopMovement()
    {
        _verticalSpeed = 0; _horizontalSpeed=Vector3.Zero; Grounded = false; Crouching = false; _canStep=false; _fall.Reset(Position.Y);
    }
    public void StopCrouching() => Crouching = false;

    public void Update(World world, Vector3 movement, bool jump, float elapsedSeconds, bool crouch = false, bool swimUp = false, bool fastFlight = false)
    {
        if (IsDead || !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
            return;

        Crouching = crouch;
        float dt = MathF.Min(elapsedSeconds, 0.1f);
        if (_wasCreative != Creative) { StopMovement(); Breath.Reset(); _wasCreative = Creative; }
        // Doors, settling blocks and restored saves can change collision beneath a
        // stationary body. Resolve that overlap before directional motion, including
        // gravity-free flight. A directional sweep must start outside solid geometry.
        if (!ResolvePenetration(world)) return;
        if (Creative)
        {
            Crouching = false;
            movement.Y = 0; if (movement.LengthSquared() > 1) movement.Normalize();
            float flightSpeed = MoveSpeed * (fastFlight ? CreativeFlightBoostMultiplier : 1);
            Vector3 flight = (movement + Vector3.Up * ((swimUp || jump ? 1 : 0) - (crouch ? 1 : 0))) * flightSpeed * dt;
            int flightSteps = Math.Max(1, (int)MathF.Ceiling(flight.Length() / .2f));
            for (int i = 0; i < flightSteps; i++)
            { MoveAxis(world, flight.X / flightSteps, 0); MoveAxis(world, flight.Z / flightSteps, 2); MoveAxis(world, flight.Y / flightSteps, 1); }
            Grounded = false; _fall.Reset(Position.Y); Breath.Reset(); return;
        }
        _invulnerable=Math.Max(0,_invulnerable-elapsedSeconds);
        Breath.Update(this, world, elapsedSeconds);
        if(Lava.Intersects(world,Bounds))
        { _lavaSeconds+=elapsedSeconds;if(_lavaSeconds>=1){_lavaSeconds%=1;TakeDamage(2);} }
        else _lavaSeconds=0;
        movement.Y = 0;
        if (movement.LengthSquared() > 1)
            movement.Normalize();

        bool swimming = Water.Intersects(world, Bounds);
        bool jumping = jump && Grounded && !swimming;
        bool protectEdge = Crouching && Grounded && !jumping && HasSupport(world, Position);
        if (jumping)
        {
            _verticalSpeed = JumpSpeed;
            Sounds?.Local(SoundKind.Jump);
        }

        if (swimming)
        {
            _fall.Reset(Position.Y);
            _verticalSpeed = swimUp || jump ? 2.8f : MathF.Max(-2, _verticalSpeed * MathF.Exp(-3 * dt) - 4 * dt);
        }
        else { _fall.Observe(Position.Y); _verticalSpeed = MathF.Max(_verticalSpeed - Gravity * dt, -TerminalVelocity); }
        Vector3 target=movement*MoveSpeed*(swimming ? .45f : 1)*(Crouching ? .35f : 1);
        if(movement.LengthSquared()==0)_horizontalSpeed*=MathF.Exp(-(swimming?8:Grounded?24:2)*dt);
        else
        {
            Vector3 change=target-_horizontalSpeed;float acceleration=(swimming?12:Grounded?60:12)*dt;
            _horizontalSpeed+=change.LengthSquared()<=acceleration*acceleration?change:Vector3.Normalize(change)*acceleration;
        }
        Vector3 displacement = _horizontalSpeed*dt + new Vector3(0, _verticalSpeed * dt, 0);
        // Each step is narrower than a block, so fast falls cannot tunnel through terrain.
        int steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / 0.2f));
        Vector3 step = displacement / steps;
        _canStep = Grounded && !jumping && !swimming;
        Grounded = false;
        for (int i = 0; i < steps; i++)
        {
            if(MoveAxis(world, protectEdge ? SupportedMovement(world, step.X, 0) : step.X, 0))_horizontalSpeed.X=0;
            if(MoveAxis(world, protectEdge ? SupportedMovement(world, step.Z, 2) : step.Z, 2))_horizontalSpeed.Z=0;
            if (MoveAxis(world, step.Y, 1))
            {
                if (step.Y < 0)
                    Grounded = true;
                _verticalSpeed = 0;
                step.Y = 0;
            }
            _fall.Observe(Position.Y);
        }

        if (swimming || Water.Intersects(world, Bounds)) _fall.Reset(Position.Y);
        else if (Grounded) TakeDamage(_fall.Land(Position.Y));

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
        else if (axis == 1) next.Y = Creative ? Math.Clamp(next.Y + amount, 0, World.Height + 16) : next.Y + amount;
        else next.Z = Math.Clamp(next.Z + amount, HalfWidth, world.Depth - HalfWidth);

        BoundingBox bounds = BoundsAt(next);
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        bool collided = false;
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)-1); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {
            int count=world.BlockBoxes(new(x,y,z),boxes,true);for(int box=0;box<count;box++)
            {
            BoundingBox obstacle=boxes[box];if(!World.Overlaps(bounds,obstacle))continue;
            collided = true;
            // Clamp the moving axis to the nearest blocking face; retain motion on other axes.
            if (axis == 0) next.X = amount > 0 ? Math.Clamp(obstacle.Min.X - HalfWidth, Position.X, next.X) : Math.Clamp(obstacle.Max.X + HalfWidth, next.X, Position.X);
            else if (axis == 1) next.Y = amount > 0 ? Math.Clamp(obstacle.Min.Y - BodyHeight, Position.Y, next.Y) : Math.Clamp(obstacle.Max.Y, next.Y, Position.Y);
            else next.Z = amount > 0 ? Math.Clamp(obstacle.Min.Z - HalfWidth, Position.Z, next.Z) : Math.Clamp(obstacle.Max.Z + HalfWidth, next.Z, Position.Z);
            bounds = BoundsAt(next);
            }
        }

        if (collided && axis != 1 && _canStep && TryStep(world, amount, axis)) return false;
        Position = next;
        return collided;
    }

    private bool ResolvePenetration(World world)
    {
        // Survival void recovery and mode changes can reach the flight boundary
        // from below. Apply those existing world limits before searching for an exit.
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z)) return false;
        Vector3 bounded = new(Math.Clamp(Position.X, HalfWidth, world.Width - HalfWidth),
            Creative ? Math.Clamp(Position.Y, 0, World.Height + 16) : Position.Y,
            Math.Clamp(Position.Z, HalfWidth, world.Depth - HalfWidth));
        if (bounded != Position) { Position = bounded; StopMovement(); }
        if (!world.IntersectsSolid(Bounds)) return true;
        Vector3 best = Position;
        float shortest = float.PositiveInfinity;
        Span<BoundingBox> boxes = stackalloc BoundingBox[9];
        // Search the six collision-face exits, refreshing the bounds after each
        // displacement. This handles intersecting obstacles without stale bounds or
        // oscillating minimum pushes. Work is local and explicitly bounded.
        for (int direction = 0; direction < 6; direction++)
        {
            int axis = direction / 2;
            bool positive = direction % 2 == 0;
            Vector3 candidate = Position;
            for (int pass = 0; pass < 16; pass++)
            {
                if (candidate.X < HalfWidth || candidate.X > world.Width - HalfWidth
                    || candidate.Z < HalfWidth || candidate.Z > world.Depth - HalfWidth
                    || candidate.Y < 0 || candidate.Y > World.Height + 16) break;
                float distance = Vector3.DistanceSquared(candidate, Position);
                if (distance >= shortest) break;
                BoundingBox bounds = BoundsAt(candidate);
                Vector3 next = candidate;
                bool overlap = false;
                for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
                for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y) - 1); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
                for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
                {
                    int count = world.BlockBoxes(new(x, y, z), boxes, true);
                    for (int i = 0; i < count; i++)
                    {
                        BoundingBox obstacle = boxes[i];
                        if (!World.Overlaps(bounds, obstacle)) continue;
                        overlap = true;
                        if (axis == 0) next.X = positive ? Math.Max(next.X, obstacle.Max.X + HalfWidth) : Math.Min(next.X, obstacle.Min.X - HalfWidth);
                        else if (axis == 1) next.Y = positive ? Math.Max(next.Y, obstacle.Max.Y) : Math.Min(next.Y, obstacle.Min.Y - BodyHeight);
                        else next.Z = positive ? Math.Max(next.Z, obstacle.Max.Z + HalfWidth) : Math.Min(next.Z, obstacle.Min.Z - HalfWidth);
                    }
                }
                if (!overlap) { best = candidate; shortest = distance; break; }
                if (next == candidate) break;
                candidate = next;
            }
        }
        // If no bounded exit exists in an unusually deep solid volume, stay put
        // rather than letting directional collision corrections become noclip.
        if (!float.IsFinite(shortest)) return false;
        Position = best;
        _verticalSpeed = 0; _horizontalSpeed = Vector3.Zero; Grounded = false; _canStep = false;
        _fall.Reset(Position.Y);
        return true;
    }

    private bool TryStep(World world, float amount, int axis)
    {
        Vector3 destination = Position + (axis == 0 ? Vector3.UnitX : Vector3.UnitZ) * amount;
        if (destination.X < HalfWidth || destination.Z < HalfWidth || destination.X > world.Width - HalfWidth || destination.Z > world.Depth - HalfWidth) return false;
        BoundingBox bounds = BoundsAt(destination);
        Span<BoundingBox> boxes = stackalloc BoundingBox[9];
        float rise = 0;
        for (int x = (int)MathF.Floor(bounds.Min.X); x <= (int)MathF.Floor(bounds.Max.X); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(Position.Y) - 1); y <= (int)MathF.Floor(bounds.Max.Y); y++)
        for (int z = (int)MathF.Floor(bounds.Min.Z); z <= (int)MathF.Floor(bounds.Max.Z); z++)
        {
            int count = world.BlockBoxes(new(x, y, z), boxes, true);
            for (int i = 0; i < count; i++) if (World.Overlaps(bounds, boxes[i])) rise = Math.Max(rise, boxes[i].Max.Y - Position.Y);
        }
        if (rise <= 0 || rise > StepHeight + .0001f) return false;
        Vector3 raised = Position + Vector3.Up * rise;
        // Check the short upward sweep and raised destination, including headroom.
        for (float h = Math.Min(.05f, rise); h < rise; h += .05f)
            if (world.IntersectsSolid(BoundsAt(Position + Vector3.Up * h))) return false;
        destination.Y += rise;
        // A high-frequency step may reach the new surface by less than the crouch support inset.
        bool supported = world.IntersectsSolid(new BoundingBox(destination + new Vector3(-HalfWidth,-.05f,-HalfWidth),destination + new Vector3(HalfWidth,0,HalfWidth)));
        if (world.IntersectsSolid(BoundsAt(raised)) || world.IntersectsSolid(BoundsAt(destination)) || !supported) return false;
        Position = destination; _verticalSpeed = 0; _fall.Reset(Position.Y); return true;
    }

    public void Respawn(World world)
    {
        Burning.Reset();
        _health = MaximumHealth;_invulnerable=_lavaSeconds=0; Breath.Reset();
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
