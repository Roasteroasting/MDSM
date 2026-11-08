using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Audio;
using MDSM.Gameplay;

namespace MDSM.Entities;

public sealed class PassiveMob
{
    private const float Gravity = 18, JumpSpeed = 6.5f;
    public MobSpecies Species { get; }
    public MobDefinition Definition { get; }
    private float HalfWidth => !Baby?Definition.HalfWidth:Species switch{MobSpecies.Cow=>.32f,MobSpecies.Chicken=>.2f,_=>.3f};
    private float Height => !Baby?Definition.Height:Species switch{MobSpecies.Pig=>.8f,MobSpecies.Cow=>1.2f,MobSpecies.Sheep=>.95f,_=>.65f};
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
    public double BabySeconds { get; private set; }
    public double LoveSeconds { get; private set; }
    public double BreedCooldown { get; private set; }
    public bool Bred { get; }
    public BurningStatus Burning {get;}=new();
    public double EggSeconds {get;private set;}
    private bool _eggReady;
    private double _eggRetry;
    public bool Baby=>BabySeconds>0;
    public BoundingBox Bounds => new(Position+new Vector3(-HalfWidth,0,-HalfWidth),Position+new Vector3(HalfWidth,Height,HalfWidth));
    public BoundingBox RenderBounds
    {
        get
        {
            float radius=Baby?Species==MobSpecies.Cow?.65f:Species==MobSpecies.Chicken?.4f:.6f:Species switch{MobSpecies.Cow=>1f,MobSpecies.Pig=>.85f,MobSpecies.Sheep=>.8f,MobSpecies.Chicken=>.6f,_=>1f};
            return new(Position+new Vector3(-radius,0,-radius),Position+new Vector3(radius,Height,radius));
        }
    }
    private readonly Random _random;
    private Vector3 _velocity;
    private float _decisionSeconds;
    private float _shoreHopSeconds;
    private bool _wandering;
    private bool _wanderHopping;
    private float _obstacleChoiceSeconds;
    private float _idleSoundSeconds=3,_stepSoundDistance;
    private uint _soundSeed;
    // Tiny local voxel cache, not a simulation throttle: all AI/physics still run at 120 Hz.
    // Conservative world revision invalidation preserves immediate edits/fluid changes.
    private readonly bool[] _solidCells = new bool[216];
    private int _cacheRevision = -1, _minX, _minY, _minZ, _maxX, _maxY, _maxZ, _cacheWidth, _cacheDepth;

    internal PassiveMob(MobState state, int aiSeed)
    {
        Species = state.Species;
        Definition = MobDefinitions.Get(Species);
        Position = state.Position; Yaw = state.Yaw; Health = state.Health;
        _soundSeed=unchecked((uint)(Position.X*17+Position.Z*31)+(uint)Species*997+1);_idleSoundSeconds+=_soundSeed%9;
        Sheared=state.Sheared;WoolSeconds=state.WoolSeconds;
        BabySeconds=state.BabySeconds;LoveSeconds=state.LoveSeconds;BreedCooldown=state.BreedCooldown;Bred=state.Bred;
        EggSeconds=state.EggSeconds;_eggReady=EggSeconds==0;
        Burning.Restore(state.Burning);
        _random = new Random(aiSeed);
        _decisionSeconds = 0.5f + _random.NextSingle() * 2;
    }

    public MobState CaptureState() => new(Species, Position, Yaw, Health,Sheared,WoolSeconds,BabySeconds,LoveSeconds,BreedCooldown,Bred,EggSeconds,Burning.Capture());
    internal void LayEgg(float elapsed,DroppedItems items)
    {
        if(Species!=MobSpecies.Chicken || Baby || IsDead || !float.IsFinite(elapsed)||elapsed<=0)return;
        EggSeconds=Math.Max(0,EggSeconds-elapsed);
        if(EggSeconds>0)return;
        if(!_eggReady)
        {
            if(_random.NextDouble()>=.5){EggSeconds=270+_random.NextDouble()*60;return;}
            _eggReady=true;
        }
        _eggRetry-=elapsed;if(_eggRetry>0)return;_eggRetry=5;
        if(items?.TrySpawn(new(MDSM.Inventories.Items.Egg,1),Position+Vector3.Up*.25f,.5f)==true)
        {EggSeconds=270+_random.NextDouble()*60;_eggReady=false;}
    }
    internal void Feed(){LoveSeconds=60;}
    internal void Parent(){LoveSeconds=0;BreedCooldown=300;}
    internal void Lunge(Vector3 direction)
    {if(!Grounded)return;direction.Y=0;if(direction.LengthSquared()>.001f)direction.Normalize();_velocity=direction*2+Vector3.Up*5;}
    internal void Face(Vector3 direction){if(direction.X*direction.X+direction.Z*direction.Z>.001f)Yaw=MathF.Atan2(-direction.X,-direction.Z);}
    internal void TickLife(float elapsed)
    {
        if(!float.IsFinite(elapsed)||elapsed<=0)return;
        BabySeconds=Math.Max(0,BabySeconds-elapsed);LoveSeconds=Math.Max(0,LoveSeconds-elapsed);BreedCooldown=Math.Max(0,BreedCooldown-elapsed);
        if(Sheared){WoolSeconds=Math.Max(0,WoolSeconds-elapsed);if(WoolSeconds<=0)Sheared=false;}
        HurtSeconds=Math.Max(0,HurtSeconds-elapsed);
    }
    internal bool Shear()
    {
        if(Species!=MobSpecies.Sheep || Sheared || IsDead || Baby)return false;
        Sheared=true;WoolSeconds=180+_random.NextDouble()*180;return true;
    }
    public static BoundingBox BoundsAt(MobSpecies species, Vector3 feet)
    {
        MobDefinition definition = MobDefinitions.Get(species);
        return new(feet + new Vector3(-definition.HalfWidth, 0, -definition.HalfWidth),
            feet + new Vector3(definition.HalfWidth, definition.Height, definition.HalfWidth));
    }

    internal void Damage(int amount, Vector3 away,bool knockback=true)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (IsDead || amount == 0) return;
        Health -= Math.Min(Health, amount);
        HurtSeconds = 0.25f;
        away.Y = 0;
        if (away.LengthSquared() > 0.000001f) away.Normalize();
        if(knockback)_velocity = away * 2.5f + Vector3.Up * 4;
        // Neither the idle/wander decision nor facing changes in response to damage.
    }
    internal void Knockback(Vector3 attacker)
    {
        if(IsDead)return;Vector3 away=Position-attacker;away.Y=0;
        if(away.LengthSquared()>.00001f)away.Normalize();_velocity=away*2.5f+Vector3.Up*4;
    }

    internal void Update(World world, float elapsed,Vector3? steering=null,bool life=true)
    {
        if (IsDead || !float.IsFinite(elapsed) || elapsed <= 0) return;
        float dt = Math.Min(elapsed, 0.1f);
        if(world.Sounds?.Nearby(Position,28)==true && (_idleSoundSeconds-=dt)<=0)
        {_soundSeed=_soundSeed*1664525+1013904223;_idleSoundSeconds=6+_soundSeed%900/100f;world.Sounds.Mob(SoundKind.MobIdle,this);}
        if(life)TickLife(elapsed);
        _shoreHopSeconds = Math.Max(0, _shoreHopSeconds - dt);
        _obstacleChoiceSeconds = Math.Max(0, _obstacleChoiceSeconds - dt);
        if ((_decisionSeconds -= dt) <= 0)
        {
            _wandering = !_wandering;
            _decisionSeconds = 1 + _random.NextSingle() * (_wandering ? 4 : 3);
            if (_wandering) Yaw = _random.NextSingle() * MathHelper.TwoPi - MathHelper.Pi;
        }
        Vector3 before = Position;
        Vector3 walking = _wandering ? new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw)) * Definition.WalkSpeed : Vector3.Zero;
        if(steering.HasValue)
        {
            Vector3 desired=steering.Value;desired.Y=0;if(desired.LengthSquared()>1)desired.Normalize();walking=desired*Definition.WalkSpeed;
            if(desired.LengthSquared()>.001f)Yaw=MathF.Atan2(-desired.X,-desired.Z);
        }
        bool swimming = Water.Intersects(world, Bounds);
        if (swimming)
        {
            walking *= 0.45f;
            bool submerged = Water.Intersects(world, new BoundingBox(Position + new Vector3(-0.1f, Height * 0.55f, -0.1f),
                Position + new Vector3(0.1f, Height * 0.65f, 0.1f)));
            _velocity.Y = submerged ? Math.Max(1.5f, _velocity.Y - 4 * dt) : Math.Max(-2, _velocity.Y * MathF.Exp(-3 * dt) - 4 * dt);
        }
        else _velocity.Y = Math.Max(-40, _velocity.Y - Gravity * dt);
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
        if (Grounded || steering.HasValue || swimming) _wanderHopping = false;
        if(blocked && steering.HasValue && Grounded)_velocity.Y=Species==MobSpecies.Spider?5:JumpSpeed;
        if(blocked && Species==MobSpecies.Spider && steering.HasValue)_velocity.Y=3;
        if (blocked && _wandering && !steering.HasValue)
        {
            // A small swimming hop lets wandering pigs leave a one-block shore instead of getting trapped.
            if (swimming) { _velocity.Y = Math.Max(_velocity.Y, 4.5f); _shoreHopSeconds = 0.7f; }
            else if (_shoreHopSeconds == 0 && Species < MobSpecies.Zombie)
            {
                // Decide once at a grounded encounter, not once per blocked frame.
                // Keep the chosen heading throughout the airborne part of a hop.
                if (Grounded && !_wanderHopping && _obstacleChoiceSeconds == 0)
                {
                    _obstacleChoiceSeconds = .5f; // Bounded retries if enclosed or caught at a corner.
                    if (CanWanderHop(world, walking) && _random.Next(2) == 0)
                    { _velocity.Y = JumpSpeed; _wanderHopping = true; _decisionSeconds = Math.Max(_decisionSeconds, .9f); }
                    else
                    {
                        Yaw = MathHelper.WrapAngle(Yaw + MathHelper.Pi + (_random.NextSingle() - .5f) * MathHelper.PiOver2);
                        _decisionSeconds = Math.Max(_decisionSeconds, 1);
                    }
                }
            }
            else if (_shoreHopSeconds == 0) { _wandering = false; _decisionSeconds = 0.5f; }
        }
        float decay = MathF.Exp(-8 * dt);
        _velocity.X *= decay; _velocity.Z *= decay;
        if (Position.Y < -16) { Position = new Vector3(Position.X, -16, Position.Z); _velocity.Y = 0; }
        float distance = new Vector2(Position.X - before.X, Position.Z - before.Z).Length();
        Walking = distance > 0.00001f;
        if (Walking) WalkPhase = (WalkPhase + distance * 7) % MathHelper.TwoPi;
        if(Grounded && Walking && world.Sounds?.Nearby(Position,16)==true)
        {
            _stepSoundDistance+=distance;float stride=Species==MobSpecies.Chicken?.65f:1.7f;
            if(_stepSoundDistance>=stride){_stepSoundDistance%=stride;world.Sounds.Block(SoundKind.Step,BlockSounds.Surface(world,Position),Position,.35f);}
        }
    }

    private bool CanWanderHop(World world, Vector3 walking)
    {
        walking.Y = 0;
        if (walking.LengthSquared() < .0001f) return false;
        walking.Normalize();
        Vector3 ahead = Position + walking * .25f;
        BoundingBox probe = new(ahead + new Vector3(-HalfWidth,0,-HalfWidth), ahead + new Vector3(HalfWidth,Height,HalfWidth));
        bool obstacleFound = false;
        Span<BoundingBox> boxes = stackalloc BoundingBox[9];
        for (int x=Math.Max(0,(int)MathF.Floor(probe.Min.X));x<=Math.Min(world.Width-1,(int)MathF.Floor(probe.Max.X));x++)
        for (int y=Math.Max(0,(int)MathF.Floor(probe.Min.Y)-1);y<=Math.Min(World.Height-1,(int)MathF.Floor(probe.Max.Y));y++)
        for (int z=Math.Max(0,(int)MathF.Floor(probe.Min.Z));z<=Math.Min(world.Depth-1,(int)MathF.Floor(probe.Max.Z));z++)
        {
            int count = world.BlockBoxes(new(x,y,z), boxes, true);
            for (int i=0;i<count;i++)
                if (World.Overlaps(probe,boxes[i]))
                {
                    obstacleFound = true;
                    if (boxes[i].Max.Y > Position.Y + 1.001f) return false;
                }
        }
        if (!obstacleFound) return false; // Finite boundaries are not jumpable obstacles.
        float rise = JumpSpeed * JumpSpeed / (2 * Gravity);
        BoundingBox overhead = Bounds; overhead.Max.Y += rise;
        if (world.IntersectsSolid(overhead)) return false;
        probe.Min.Y += rise; probe.Max.Y += rise;
        return !world.IntersectsSolid(probe);
    }

    private bool MoveAxis(World world, float amount, int axis)
    {
        if (amount == 0) return false;
        Vector3 next = Position;
        if (axis == 0) next.X = Math.Clamp(next.X + amount, HalfWidth, world.Width - HalfWidth);
        else if (axis == 2) next.Z = Math.Clamp(next.Z + amount, HalfWidth, world.Depth - HalfWidth);
        else next.Y += amount;
        BoundingBox bounds = new(next+new Vector3(-HalfWidth,0,-HalfWidth),next+new Vector3(HalfWidth,Height,HalfWidth));
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        CacheCollisionCells(world, bounds);
        bool blocked = axis == 0 ? next.X != Position.X + amount : axis == 2 && next.Z != Position.Z + amount;
        for (int x = Math.Max(0, (int)MathF.Floor(bounds.Min.X)); x <= Math.Min(world.Width - 1, (int)MathF.Floor(bounds.Max.X)); x++)
        for (int y = Math.Max(0, (int)MathF.Floor(bounds.Min.Y)-1); y <= Math.Min(World.Height - 1, (int)MathF.Floor(bounds.Max.Y)); y++)
        for (int z = Math.Max(0, (int)MathF.Floor(bounds.Min.Z)); z <= Math.Min(world.Depth - 1, (int)MathF.Floor(bounds.Max.Z)); z++)
        {
            if (!_solidCells[((y - _minY) * _cacheDepth + z - _minZ) * _cacheWidth + x - _minX]) continue;
            int count=world.BlockBoxes(new(x,y,z),boxes,true);for(int box=0;box<count;box++)
            {
            BoundingBox obstacle=boxes[box];if(!World.Overlaps(bounds,obstacle))continue;
            blocked = true;
            if (axis == 0) next.X = amount > 0 ? Math.Min(next.X, obstacle.Min.X - HalfWidth) : Math.Max(next.X, obstacle.Max.X + HalfWidth);
            else if (axis == 2) next.Z = amount > 0 ? Math.Min(next.Z, obstacle.Min.Z - HalfWidth) : Math.Max(next.Z, obstacle.Max.Z + HalfWidth);
            else next.Y = amount > 0 ? Math.Min(next.Y, obstacle.Min.Y - Height) : Math.Max(next.Y, obstacle.Max.Y);
            }
        }
        Position = next;
        return blocked;
    }

    private void CacheCollisionCells(World world, BoundingBox bounds)
    {
        int minX = (int)MathF.Floor(bounds.Min.X), minY = (int)MathF.Floor(bounds.Min.Y)-1, minZ = (int)MathF.Floor(bounds.Min.Z);
        int maxX = (int)MathF.Floor(bounds.Max.X), maxY = (int)MathF.Floor(bounds.Max.Y), maxZ = (int)MathF.Floor(bounds.Max.Z);
        if (_cacheRevision == world.Revision && minX >= _minX && minY >= _minY && minZ >= _minZ
            && maxX <= _maxX && maxY <= _maxY && maxZ <= _maxZ) return;
        _minX = (int)MathF.Floor(bounds.Min.X - .25f); _maxX = (int)MathF.Floor(bounds.Max.X + .25f);
        _minY = (int)MathF.Floor(bounds.Min.Y)-1; _maxY = (int)MathF.Floor(bounds.Max.Y + .25f);
        _minZ = (int)MathF.Floor(bounds.Min.Z - .25f); _maxZ = (int)MathF.Floor(bounds.Max.Z + .25f);
        _cacheWidth = _maxX - _minX + 1; _cacheDepth = _maxZ - _minZ + 1;
        int i = 0;
        for (int y = _minY; y <= _maxY; y++) for (int z = _minZ; z <= _maxZ; z++) for (int x = _minX; x <= _maxX; x++)
            _solidCells[i++] = world.IsSolid(x, y, z);
        _cacheRevision = world.Revision;
    }
}
