using System;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Entities;

public sealed partial class PassiveMobPopulation
{
    internal LocatedMob[] CaptureChunk(ChunkAddress a)=>_mobs.Where(m=>ChunkGameplay.Owns(a,m.AbsolutePosition)).Select(m=>new LocatedMob(m.CaptureState() with{Position=Vector3.Zero},m.AbsolutePosition)).ToArray();
    internal MobRespawnState[] CapturePending(ChunkAddress a)=>_pending.Where(p=>p.Home.HasValue&&ChunkGameplay.Owns(a,p.Home.Value)).ToArray();
    internal void AttachChunk(LocatedMob[] states,MobRespawnState[] pending)
    {
        foreach(var s in states){AddSaved(s.State with{Position=_world.LocalPosition(s.Position)});if(!s.State.Bred)_targets[(int)s.State.Species]++;}
        foreach(var p in pending){if(!p.Home.HasValue||!double.IsFinite(p.RemainingSeconds)||p.RemainingSeconds<0||p.RemainingSeconds>MaximumRespawnSeconds||p.Species is <MobSpecies.Pig or >MobSpecies.Chicken)throw new System.IO.InvalidDataException("Invalid streamed respawn.");_pending.Add(p);_targets[(int)p.Species]++;}
    }
    internal void DetachChunk(ChunkAddress a)
    {
        for(int i=_mobs.Count-1;i>=0;i--){var m=_mobs[i];if(!ChunkGameplay.Owns(a,m.AbsolutePosition))continue;if(!m.Bred){_living[(int)m.Species]--;_targets[(int)m.Species]--;}_ids.Remove(m.Id);_mobs.RemoveAt(i);}
        for(int i=_pending.Count-1;i>=0;i--)if(_pending[i].Home is Point3 p&&ChunkGameplay.Owns(a,p)){_targets[(int)_pending[i].Species]--;_pending.RemoveAt(i);}
        _breedingCells.Clear();
    }
}
public sealed partial class HostilePopulation
{
    internal LocatedMob[] CaptureChunk(ChunkAddress a)=>_mobs.Where(m=>ChunkGameplay.Owns(a,m.AbsolutePosition)).Select(m=>new LocatedMob(m.CaptureState() with{Position=Vector3.Zero},m.AbsolutePosition)).ToArray();
    internal void AttachChunk(LocatedMob[] states){foreach(var s in states){var state=s.State with{Position=_world.LocalPosition(s.Position)};Validate(state,_world);Add(state);}}
    internal void DetachChunk(ChunkAddress a)
    {for(int i=_mobs.Count-1;i>=0;i--)if(ChunkGameplay.Owns(a,_mobs[i].AbsolutePosition)){_brains.Remove(_mobs[i]);_mobs.RemoveAt(i);}}
    internal void Reframe(Vector3 delta)
    {foreach(var b in _brains.Values){b.LastSeen+=delta;b.CurrentAim+=delta;b.OldAim+=delta;}foreach(var m in _mobs)m.FrameChanged();}
}
public sealed partial class DroppedItems
{
    internal LocatedDrop[] CaptureChunk(ChunkAddress a)=>_items.Where(i=>ChunkGameplay.Owns(a,i.AbsolutePosition)).Select(i=>new LocatedDrop(i.Capture() with{Position=Vector3.Zero},i.AbsolutePosition)).ToArray();
    internal void AttachChunk(LocatedDrop[] states)
    {foreach(var s in states){var state=s.State with{Position=_world.LocalPosition(s.Position)};Validate(state,_world.Size);if(!CanSpawn())throw new System.IO.InvalidDataException("Too many restored items.");_items.Add(Make(state));}}
    internal void DetachChunk(ChunkAddress a){_items.RemoveAll(i=>ChunkGameplay.Owns(a,i.AbsolutePosition));_mergeHeads.Clear();}
}
public sealed partial class Projectiles
{
    internal LocatedArrow[] CaptureChunk(ChunkAddress a)=>_arrows.Where(p=>ChunkGameplay.Owns(a,p.Global??_world.AbsolutePosition(p.Position))).Select(p=>new LocatedArrow(p with{Position=Vector3.Zero,Global=null},p.Global??_world.AbsolutePosition(p.Position))).ToArray();
    internal void AttachChunk(LocatedArrow[] states)
    {foreach(var s in states){if(!float.IsFinite(s.State.Velocity.LengthSquared())||!float.IsFinite(s.State.Age)||s.State.Age<0)throw new System.IO.InvalidDataException("Invalid streamed projectile.");_arrows.Add(s.State with{Position=_world.LocalPosition(s.Position),Global=s.Position});}}
    internal void DetachChunk(ChunkAddress a)=>_arrows.RemoveAll(p=>ChunkGameplay.Owns(a,p.Global??_world.AbsolutePosition(p.Position)));
    internal void PrepareReframe(){for(int i=0;i<_arrows.Count;i++)_arrows[i]=_arrows[i] with{Global=_arrows[i].Global??_world.AbsolutePosition(_arrows[i].Position)};}
    internal void FinishReframe(){for(int i=0;i<_arrows.Count;i++)_arrows[i]=_arrows[i] with{Position=_world.LocalPosition(_arrows[i].Global.Value)};}
}
