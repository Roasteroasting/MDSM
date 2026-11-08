using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Worlds;
using MDSM.Audio;

namespace MDSM.Entities;

public sealed class PassiveMobPopulation
{
    public const int MaximumMobs = 4096; // Shared safety bound; initial populations remain area-based.
    public const double MinimumRespawnSeconds = 30, MaximumRespawnSeconds = 60, RetrySeconds = 5;
    public const int MaximumSpawnAttemptsPerUpdate = 32;
    public const float WheatFollowRange = 10, WheatStopDistance = 2;
    private readonly List<PassiveMob> _mobs = new();
    private readonly World _world;
    private readonly Random _random;
    private readonly Random _respawnRandom;
    private readonly int[] _living = new int[MobDefinitions.SpeciesCount];
    private readonly int[] _targets = new int[MobDefinitions.SpeciesCount];
    private readonly List<MobRespawnState> _pending = new();
    private readonly Dictionary<Point3,List<PassiveMob>> _breedingCells=new();
    private bool _breedingActive;
    public IReadOnlyList<PassiveMob> Entities => _mobs;
    public IReadOnlyList<MobRespawnState> PendingRespawns => _pending;
    public int LastSpawnAttempts { get; private set; }
    public int Target(MobSpecies species) => _targets[(int)species];
    public static int InitialBudget(World world) => world.Width * world.Depth / 2048; // 32 / 128 / 512 / 2048 combined.

    public PassiveMobPopulation(World world, ReadOnlySpan<MobState> saved = default, MobPopulationState population = null)
    {
        _world = world; _random = new Random(unchecked(world.Seed ^ 0x504947));
        _respawnRandom = new Random(unchecked(world.Seed ^ 0x5253504E));
        foreach (MobState state in saved) AddSaved(state);
        RestorePopulation(population);
        foreach(var mob in _mobs)if(mob.LoveSeconds>0)_breedingActive=true;
    }
    public PassiveMobPopulation(World world, ReadOnlySpan<PigState> pigs, ReadOnlySpan<MobState> others, MobPopulationState population = null) : this(world)
    {
        if (pigs.Length + others.Length > MaximumMobs) throw new InvalidDataException("Too many passive mobs.");
        foreach (PigState pig in pigs) AddSaved(pig.ToMobState());
        foreach (MobState state in others)
        {
            if (state.Species == MobSpecies.Pig) throw new InvalidDataException("Pigs must use the pig state section.");
            AddSaved(state);
        }
        RestorePopulation(population);
        foreach(var mob in _mobs)if(mob.LoveSeconds>0)_breedingActive=true;
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
    internal bool TrySummon(MobSpecies species, Vector3 feet)
    {
        if (species is < MobSpecies.Pig or > MobSpecies.Chicken || _mobs.Count + _pending.Count >= MaximumMobs) return false;
        // Like bred animals, explicit additions do not change natural population targets
        // or schedule replacements when killed. This flag already persists in saves.
        AddSaved(new(species, feet, 0, MobDefinitions.Get(species).MaximumHealth, Bred: true));
        return true;
    }
    private void AddSaved(MobState state)
    {
        if (_mobs.Count >= MaximumMobs) throw new InvalidDataException("Too many passive mobs.");
        Validate(state, _world.Size);
        _mobs.Add(new PassiveMob(state, _random.Next()));
        if(!state.Bred)_living[(int)state.Species]++;
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
            || world.IntersectsSolid(bounds) || Water.Intersects(world, bounds) || Lava.Intersects(world,bounds)) return false;
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            if (world.GetBlock(x + dx, y, z + dz) != BlockType.Grass) return false;
        return true;
    }
    public void Update(float elapsed, Vector3? playerPosition = null, BoundingBox? playerBounds = null, DroppedItems items = null, ItemId heldItem = default)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0) return;
        foreach (PassiveMob mob in _mobs)
        {
            float playerDistanceSquared=playerPosition.HasValue?Vector3.DistanceSquared(mob.Position,playerPosition.Value):float.PositiveInfinity;
            Vector3? direction=null;
            if(heldItem==Items.Wheat && playerDistanceSquared<=WheatFollowRange*WheatFollowRange)
            {
                Vector3 toward=playerPosition.Value-mob.Position;toward.Y=0;
                if(toward.LengthSquared()>WheatStopDistance*WheatStopDistance)
                {
                    float distance=toward.Length(),step=mob.Definition.WalkSpeed*Math.Min(elapsed,.1f);
                    toward*=Math.Min(1,(distance-WheatStopDistance)/step)/distance;
                }
                else toward=Vector3.Zero;
                direction=toward; // Overrides wandering, including standing still near the player.
            }
            else if(mob.LoveSeconds>0)
            {
                PassiveMob partner=Partner(mob);if(partner!=null)direction=partner.Position-mob.Position;
            }
            if(!playerPosition.HasValue || playerDistanceSquared<=32*32)mob.Update(_world,elapsed,direction);
            else mob.TickLife(elapsed);
            mob.LayEgg(elapsed,items);
        }
        if(_breedingActive && (_breedTick+=elapsed)>=.5)
        {
            _breedTick%=.5;foreach(var list in _breedingCells.Values)list.Clear();_breedingActive=false;
            foreach(var mob in _mobs)if(mob.LoveSeconds>0)
            {_breedingActive=true;Point3 key=BreedingCell(mob.Position);if(!_breedingCells.TryGetValue(key,out var list))_breedingCells[key]=list=new();list.Add(mob);}
            Breed();
        }
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
        if (_living[(int)species] >= _targets[(int)species] || _mobs.Count>=MaximumMobs) return false;
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
        =>Hit(mob,_random.Next(1,3),attacker,items);
    public bool Hit(PassiveMob mob,int damage,Vector3 attacker,DroppedItems items,bool knockback=true)
    {
        if (mob == null || mob.IsDead || !_mobs.Contains(mob) || damage<=0) return false;
        if(damage>=mob.Health)
        {
            int count=mob.Baby || mob.Species==MobSpecies.Sheep && mob.Sheared?0:_random.Next(1,mob.Definition.MaximumDrop+1);
            int limit=Items.StackLimit(mob.Definition.Drop),entities=(count+limit-1)/limit;
            if(!items.CanSpawn(entities))return false;
            while(count>0){int take=Math.Min(count,limit);items.TrySpawn(new ItemStack(mob.Definition.Drop,take),mob.Position+Vector3.Up*.25f,.35f);count-=take;}
        }
        mob.Damage(damage, mob.Position - attacker,knockback);
        _world.Sounds?.Mob(mob.IsDead?SoundKind.MobDeath:SoundKind.MobHurt,mob);
        if (mob.IsDead && _mobs.Remove(mob))
        {
            if(!mob.Bred){_living[(int)mob.Species]--;
            _pending.Add(new MobRespawnState(mob.Species, MinimumRespawnSeconds + _respawnRandom.NextDouble() * (MaximumRespawnSeconds - MinimumRespawnSeconds)));}
        }
        return true;
    }
    public bool TryShear(PassiveMob mob,Inventory inventory,DroppedItems drops)
    {
        if(mob==null || !_mobs.Contains(mob) || mob.IsDead || mob.Baby || mob.Species!=MobSpecies.Sheep || mob.Sheared
            || inventory.Selected.Item!=Items.Shears || !drops.CanSpawn(1))return false;
        int count=_random.Next(1,5);
        if(!drops.TrySpawn(new ItemStack(Items.FromBlock(BlockType.WhiteWool),count),mob.Position+Vector3.Up*.4f,.7f))return false;
        mob.Shear();inventory.WearSelected();_world.Sounds?.At(SoundKind.Shear,mob.Position);return true;
    }
    public MobState[] CaptureAll()
    {
        var result = new MobState[_mobs.Count]; for (int i = 0; i < result.Length; i++) result[i] = _mobs[i].CaptureState(); return result;
    }
    public PigState[] CapturePigs()
    {
        int count = 0; foreach (PassiveMob mob in _mobs) if (mob.Species == MobSpecies.Pig) count++;
        var result = new PigState[count]; int i = 0;
        foreach (PassiveMob mob in _mobs) if (mob.Species == MobSpecies.Pig) result[i++] = new PigState(mob.Position, mob.Yaw, mob.Health,mob.BabySeconds,mob.LoveSeconds,mob.BreedCooldown,mob.Bred,mob.Burning.Capture());
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
        if (!MobDefinitions.IsDefined(state.Species) || (int)state.Species>=4) throw new InvalidDataException("Invalid passive mob species.");
        MobDefinition d = MobDefinitions.Get(state.Species); Vector3 p = state.Position;
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
            || p.X < d.HalfWidth || p.X > size.Width() - d.HalfWidth || p.Z < d.HalfWidth || p.Z > size.Width() - d.HalfWidth
            || p.Y < -16 || p.Y > World.Height + 16 || !float.IsFinite(state.Yaw) || MathF.Abs(state.Yaw) > MathHelper.Pi
            || state.Health < 1 || state.Health > d.MaximumHealth
            || !double.IsFinite(state.WoolSeconds) || state.WoolSeconds<0 || state.WoolSeconds>360
            || (state.Sheared ? state.Species!=MobSpecies.Sheep || state.WoolSeconds<=0 : state.WoolSeconds!=0)
            || !double.IsFinite(state.EggSeconds) || state.EggSeconds<0 || state.EggSeconds>330) throw new InvalidDataException("Save contains an invalid passive mob.");
        if(!double.IsFinite(state.BabySeconds)||state.BabySeconds<0||state.BabySeconds>2700||!double.IsFinite(state.LoveSeconds)||state.LoveSeconds<0||state.LoveSeconds>60
            ||!double.IsFinite(state.BreedCooldown)||state.BreedCooldown<0||state.BreedCooldown>300 || state.BabySeconds>0 && (state.LoveSeconds>0||state.Sheared))throw new InvalidDataException("Invalid animal life state.");
    }
    private PassiveMob Partner(PassiveMob mob)
    {
        Point3 key=BreedingCell(mob.Position);
        for(int y=key.Y-1;y<=key.Y+1;y++)for(int z=key.Z-1;z<=key.Z+1;z++)for(int x=key.X-1;x<=key.X+1;x++)
        if(_breedingCells.TryGetValue(new(x,y,z),out var list))foreach(PassiveMob candidate in list)if(candidate!=mob && !candidate.IsDead && candidate.Species==mob.Species && !candidate.Baby && candidate.LoveSeconds>0 && Vector3.DistanceSquared(candidate.Position,mob.Position)<=64)return candidate;
        return null;
    }
    private static Point3 BreedingCell(Vector3 p)=>new((int)p.X/8,(int)p.Y/8,(int)p.Z/8);
    private double _breedTick;
    private void Breed()
    {
        int cap=0;foreach(int target in _targets)cap+=target;cap=Math.Min(MaximumMobs,Math.Max(32,cap*2));
        if(_mobs.Count+_pending.Count>=cap)return;
        for(int i=0;i<_mobs.Count;i++)
        {
            PassiveMob a=_mobs[i];if(a.Baby||a.LoveSeconds<=0)continue;PassiveMob b=Partner(a);
            if(b==null || Vector3.DistanceSquared(a.Position,b.Position)>2.25f)continue;
            Vector3 p=(a.Position+b.Position)*.5f;BoundingBox box=PassiveMob.BoundsAt(a.Species,p);
            if(_world.IntersectsSolid(box)||Water.Intersects(_world,box)||Lava.Intersects(_world,box))continue;
            a.Parent();b.Parent();_mobs.Add(new PassiveMob(new(a.Species,p,0,a.Definition.MaximumHealth,BabySeconds:900+_random.NextDouble()*1800,Bred:true),_random.Next()));
            if(_mobs.Count+_pending.Count>=cap)break;
        }
    }
    public bool Feed(PassiveMob mob,Inventory inventory)
    {
        if(mob==null||!_mobs.Contains(mob)||mob.Baby||mob.IsDead||mob.LoveSeconds>0||mob.BreedCooldown>0||inventory.Selected.Item!=Items.Wheat)return false;
        mob.Feed();_breedingActive=true;inventory.ConsumeSelected();_world.Sounds?.At(SoundKind.Feed,mob.Position);return true;
    }
}
