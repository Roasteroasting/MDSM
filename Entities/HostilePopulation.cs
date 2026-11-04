using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;
using MDSM.Core;

namespace MDSM.Entities;

public readonly record struct HostileSpawnSearch(int CandidateColumns, int FloorChecks, int Spawned,
    int OutsideWorld, int Distance, int NoFloor, int Blocked, int Fluid, int Torch, int Occupied);

public sealed class HostilePopulation
{
    public const int Maximum = 36, MaximumSpawnAttempts = 128;
    public const float MinimumSpawnDistance = 24, SpawnRadius = 64, ActiveRadius = 64, DespawnRadius = 128;
    public const double MinimumSpawnSeconds = 2, MaximumSpawnSeconds = 4;
    private enum SpawnRejection { None, OutsideWorld, Distance, NoFloor, Blocked, Fluid, Torch, Occupied }
    private readonly int[] _rejections = new int[8];
    private sealed class Brain { public double Attack,Look,Memory,AimClock,Jump;public bool Visible;public Vector3 LastSeen,OldAim,CurrentAim; }
    private readonly World _world;private readonly List<PassiveMob> _mobs=new();private readonly Dictionary<PassiveMob,Brain> _brains=new();
    private readonly Random _random;private double _spawn=5;
    public IReadOnlyList<PassiveMob> Entities=>_mobs;
    public int ActiveCount { get; private set; }
    public HostileSpawnSearch LastSpawnSearch { get; private set; }
    public double SpawnSecondsRemaining => Math.Max(0, _spawn);
    public void ApplySettings(GameSettings settings)
    {
        if (settings.Difficulty == Difficulty.Peaceful) { _mobs.Clear(); _brains.Clear(); ActiveCount=0; _spawn = 5; }
        else if (settings.GameMode == GameMode.Creative)
            foreach (Brain brain in _brains.Values) { brain.Memory = 0; brain.Visible = false; brain.Attack = 0; }
    }
    public int LastSpawnAttempts { get; private set; }
    public HostilePopulation(World world,MobState[] saved=null)
    {
        _world=world;_random=new(unchecked(world.Seed^0x484f5354));if(saved==null)return;if(saved.Length>Maximum)throw new InvalidDataException("Too many hostile mobs.");foreach(var state in saved){Validate(state,world);Add(state);}
    }
    private void Add(MobState state){var mob=new PassiveMob(state,_random.Next());_mobs.Add(mob);_brains[mob]=new(){LastSeen=mob.Position,OldAim=mob.Position,CurrentAim=mob.Position};}
    public bool Suitable(MobSpecies species,Vector3 feet,Vector3 player)
        => Reject(species, feet, player) == SpawnRejection.None;
    private SpawnRejection Reject(MobSpecies species, Vector3 feet, Vector3 player)
    {
        if (feet.Y<2 || feet.Y>=50 || feet.X<2 || feet.Z<2 || feet.X>=_world.Width-2 || feet.Z>=_world.Depth-2) return SpawnRejection.OutsideWorld;
        float distance=Vector3.DistanceSquared(feet,player);
        if (distance<MinimumSpawnDistance*MinimumSpawnDistance || distance>SpawnRadius*SpawnRadius) return SpawnRejection.Distance;
        if (!_world.IsSolid((int)feet.X,(int)feet.Y-1,(int)feet.Z)) return SpawnRejection.NoFloor;
        var bounds=PassiveMob.BoundsAt(species,feet);
        if (_world.IntersectsSolid(bounds)) return SpawnRejection.Blocked;
        if (Water.Intersects(_world,bounds) || Lava.Intersects(_world,bounds)) return SpawnRejection.Fluid;
        if (_world.Torches.Protected(feet)) return SpawnRejection.Torch;
        return SpawnRejection.None;
    }
    public void Update(float elapsed,Player player,Projectiles arrows,PassiveMobPopulation passive=null)
    {
        LastSpawnAttempts=0;
        if (player.Settings.Difficulty == Difficulty.Peaceful) { ApplySettings(player.Settings); return; }
        if(!float.IsFinite(elapsed)||elapsed<=0)return;
        ActiveCount=0;
        for(int i=_mobs.Count-1;i>=0;i--)
        {
            PassiveMob mob=_mobs[i];float distance=Vector3.DistanceSquared(mob.Position,player.Position);
            if(distance>DespawnRadius*DespawnRadius){_brains.Remove(mob);_mobs.RemoveAt(i);continue;}
            if(distance>ActiveRadius*ActiveRadius){mob.TickLife(elapsed);continue;}
            ActiveCount++;
            Brain brain=_brains[mob];brain.Attack=Math.Max(0,brain.Attack-elapsed);brain.Memory=Math.Max(0,brain.Memory-elapsed);brain.AimClock+=elapsed;brain.Jump=Math.Max(0,brain.Jump-elapsed);
            if (player.Creative)
            { brain.Memory = 0; brain.Visible = false; mob.Update(_world, elapsed, Vector3.Zero); continue; }
            if(brain.AimClock>=.5){brain.AimClock%=.5;brain.OldAim=brain.CurrentAim;brain.CurrentAim=player.Position+Vector3.Up;}
            if((brain.Look-=elapsed)<=0)
            {
                brain.Look=.2;Vector3 eye=mob.Position+Vector3.Up*(mob.Definition.Height*.8f),delta=player.EyePosition-eye;
                float length=delta.Length();brain.Visible=(BlockRaycaster.Cast(_world,eye,delta,length,true)?.Distance??float.MaxValue)>=length;
                if(brain.Visible){if(brain.Memory<=0)brain.OldAim=brain.CurrentAim=player.Position+Vector3.Up;brain.Memory=3;brain.LastSeen=player.Position;}
            }
            Vector3 direction=Vector3.Zero;
            if(brain.Memory>0)
            {
                direction=brain.LastSeen-mob.Position;direction.Y=0;if(direction.LengthSquared()>.001f)direction.Normalize();
                if(mob.Species==MobSpecies.Skeleton){if(distance<36)direction=-direction;else if(distance<144)direction=Vector3.Zero;}
            }
            mob.Update(_world,elapsed,direction);
            if(mob.Species==MobSpecies.Skeleton && brain.Memory>0)mob.Face(brain.LastSeen-mob.Position);
            if(mob.Species==MobSpecies.Spider && brain.Memory>0 && distance<36 && brain.Jump==0){mob.Lunge(direction);brain.Jump=2;}
            if(!brain.Visible || brain.Attack>0 || player.IsDead)continue;
            Vector3 attackEye=mob.Position+Vector3.Up*(mob.Definition.Height*.8f),sight=player.EyePosition-attackEye;
            if((BlockRaycaster.Cast(_world,attackEye,sight,sight.Length(),true)?.Distance??float.MaxValue)<sight.Length())continue;
            if(mob.Species==MobSpecies.Skeleton && distance>9 && distance<400)
            {
                Vector3 origin=mob.Position+Vector3.Up*1.4f,target=brain.OldAim+new Vector3(_random.NextSingle()-.5f,(_random.NextSingle()-.5f)*.5f,_random.NextSingle()-.5f);
                Vector3 delta=target-origin;float range=delta.Length();if(range>.01f){Vector3 velocity=delta/range*18;velocity.Y+=Projectiles.Gravity*.5f*range/18;arrows.ShootHostile(origin,velocity);brain.Attack=2.5;}
            }
            else if(distance<(mob.Species==MobSpecies.Spider?3:2.5f))
            {if(player.TakeDamage(mob.Species==MobSpecies.Zombie?3:mob.Species==MobSpecies.Spider?2:1))brain.Attack=1.2;}
        }
        _spawn-=elapsed;if(_spawn>1e-9 || _mobs.Count>=Maximum)return;
        _spawn=MinimumSpawnSeconds+_random.NextDouble()*(MaximumSpawnSeconds-MinimumSpawnSeconds);
        Array.Clear(_rejections); int floorChecks=0, spawned=0;
        for(int attempt=0;attempt<MaximumSpawnAttempts;attempt++)
        {
            LastSpawnAttempts++;MobSpecies species=(MobSpecies)_random.Next(4,7);
            // Uniform annulus sampling avoids wasting most attempts outside the useful AI range.
            float angle=_random.NextSingle()*MathHelper.TwoPi;
            float radius=MathF.Sqrt(MinimumSpawnDistance*MinimumSpawnDistance+_random.NextSingle()*(SpawnRadius*SpawnRadius-MinimumSpawnDistance*MinimumSpawnDistance));
            int x=(int)MathF.Floor(player.Position.X+MathF.Cos(angle)*radius),z=(int)MathF.Floor(player.Position.Z+MathF.Sin(angle)*radius);
            SpawnRejection rejection=FindFloor(species,x,z,player.Position,passive,ref floorChecks,out Vector3 p);
            if(rejection!=SpawnRejection.None){_rejections[(int)rejection]++;continue;}
            Add(new(species,p,_random.NextSingle()*MathHelper.TwoPi-MathHelper.Pi,MobDefinitions.Get(species).MaximumHealth));
            ActiveCount++;spawned=1;break;
        }
        // Retain the last search, rather than clearing diagnostics on every intervening frame.
        LastSpawnSearch=new(LastSpawnAttempts,floorChecks,spawned,_rejections[1],_rejections[2],_rejections[3],_rejections[4],_rejections[5],_rejections[6],_rejections[7]);
    }
    private SpawnRejection FindFloor(MobSpecies species,int x,int z,Vector3 player,PassiveMobPopulation passive,ref int checks,out Vector3 feet)
    {
        feet=default;
        if(x<2 || z<2 || x>=_world.Width-2 || z>=_world.Depth-2)return SpawnRejection.OutsideWorld;
        int preferred=Math.Clamp((int)MathF.Floor(player.Y)+_random.Next(-8,9),3,49);
        SpawnRejection last=SpawnRejection.NoFloor;
        // At most 47 floor samples per column, nearest the player's elevation first.
        // No surface-height query: cave floors are below the world's surface cache.
        for(int delta=0;delta<=46;delta++)for(int side=0;side<(delta==0?1:2);side++)
        {
            int y=preferred+(side==0?-delta:delta);if(y<3 || y>49)continue;
            checks++;
            if(!_world.IsSolid(x,y-1,z) || _world.IsSolid(x,y,z))continue;
            Vector3 candidate=new(x+.5f,y,z+.5f);SpawnRejection reason=Reject(species,candidate,player);
            if(reason!=SpawnRejection.None){last=reason;continue;}
            var bounds=PassiveMob.BoundsAt(species,candidate);bool occupied=false;
            foreach(var mob in _mobs)if(World.Overlaps(bounds,mob.Bounds)){occupied=true;break;}
            if(!occupied && passive!=null)foreach(var mob in passive.Entities)if(World.Overlaps(bounds,mob.Bounds)){occupied=true;break;}
            if(occupied){last=SpawnRejection.Occupied;continue;}
            feet=candidate;return SpawnRejection.None;
        }
        return last;
    }
    public bool Hit(PassiveMob mob,int damage,Vector3 attacker,DroppedItems drops)
    {
        if(mob==null||!_mobs.Contains(mob)||mob.IsDead||damage<=0)return false;
        if(damage>=mob.Health)
        {
            if(!drops.CanSpawn(3))return false;Vector3 position=mob.Position+Vector3.Up*.3f;
            drops.Spawn(mob.Definition.Drop,_random.Next(1,5),position);
            if(mob.Species==MobSpecies.Skeleton)
            {
                if(_random.NextDouble()<.25)drops.Spawn(Items.Arrow,_random.Next(1,5),position);
                if(_random.NextDouble()<.05)drops.TrySpawn(new(Items.Bow,1,1+(int)(_random.NextDouble()*_random.NextDouble()*383)),position,.35f);
            }
        }
        mob.Damage(damage,mob.Position-attacker);if(mob.IsDead){_mobs.Remove(mob);_brains.Remove(mob);}return true;
    }
    public MobState[] Capture(){var states=new MobState[_mobs.Count];for(int i=0;i<states.Length;i++)states[i]=_mobs[i].CaptureState();return states;}
    public static void Validate(MobState state,World world)
    {
        if(state.Species is <MobSpecies.Zombie or >MobSpecies.Spider)throw new InvalidDataException("Invalid hostile species.");
        float half=MobDefinitions.Get(state.Species).HalfWidth;
        if(!float.IsFinite(state.Position.LengthSquared()) || !float.IsFinite(state.Yaw)||Math.Abs(state.Yaw)>MathHelper.Pi
            || state.Health<1||state.Health>MobDefinitions.Get(state.Species).MaximumHealth || state.Position.X<half || state.Position.Z<half || state.Position.X>world.Width-half || state.Position.Z>world.Depth-half || state.Position.Y< -16 || state.Position.Y>World.Height+16
            || state.BabySeconds!=0||state.Sheared||state.WoolSeconds!=0||state.LoveSeconds!=0||state.BreedCooldown!=0||state.Bred)throw new InvalidDataException("Invalid hostile mob.");
    }
}
