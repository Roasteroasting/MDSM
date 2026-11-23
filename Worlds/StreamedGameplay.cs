using System;
using System.Collections.Generic;
using System.Linq;
using MDSM.Entities;
using MDSM.Persistence;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

public sealed partial class World
{
    private readonly Dictionary<ChunkAddress,ChunkGameplay> _sleeping=new();
    private Chunk[] _simulationChunks;
    private ChunkAddress? _simulationCenter;
    internal Projectiles LiveProjectiles {get;set;}
    internal bool IsSimulated(Point3 p)
    {
        if(!IsInfinite)return true;
        var a=ChunkAddress.At(p.X,p.Z);var center=ChunkLifecycle?.Center;
        return center.HasValue && Math.Abs(a.X-center.Value.X)<=ChunkLifecycle.SimulationDistance && Math.Abs(a.Z-center.Value.Z)<=ChunkLifecycle.SimulationDistance && IsLoaded(p);
    }
    internal ChunkGameplay CaptureGameplay(ChunkAddress a)
    {
        var state=new ChunkGameplay
        {
            Vegetation=Vegetation.CaptureOwned(a),Crops=Farming.CaptureChunk(a),
            Hydration=Farming.CaptureHydration(a),Fire=Fire.CaptureChunk(a),
            Water=Fluids.CaptureChunk(a),Lava=LavaFlow.CaptureChunk(a),Gravity=Gravity.CaptureQueued(a),Falling=Gravity.CaptureActive(a),
            Passive=LiveMobs?.CaptureChunk(a)??Array.Empty<LocatedMob>(),Hostile=LiveHostiles?.CaptureChunk(a)??Array.Empty<LocatedMob>(),Drops=LiveDrops?.CaptureChunk(a)??Array.Empty<LocatedDrop>(),
            Arrows=LiveProjectiles?.CaptureChunk(a)??Array.Empty<LocatedArrow>(),Respawns=LiveMobs?.CapturePending(a)??Array.Empty<MobRespawnState>()
        };
        if(_sleeping.TryGetValue(a,out var frozen))state=frozen with
        {
            Passive=frozen.Passive.Concat(state.Passive).ToArray(),Hostile=frozen.Hostile.Concat(state.Hostile).ToArray(),Drops=frozen.Drops.Concat(state.Drops).ToArray(),Arrows=frozen.Arrows.Concat(state.Arrows).ToArray(),
            Respawns=frozen.Respawns.Concat(state.Respawns).ToArray(),Water=frozen.Water.Concat(state.Water).Distinct().ToArray(),Lava=frozen.Lava.Concat(state.Lava).Distinct().ToArray(),Gravity=frozen.Gravity.Concat(state.Gravity).Distinct().ToArray(),Falling=frozen.Falling.Concat(state.Falling).ToArray(),
            Vegetation=new(frozen.Vegetation.Leaves.Concat(state.Vegetation.Leaves).Distinct().ToArray(),frozen.Vegetation.Decay.Concat(state.Vegetation.Decay).DistinctBy(p=>p.Cell).ToArray(),frozen.Vegetation.Saplings.Concat(state.Vegetation.Saplings).DistinctBy(p=>p.Cell).ToArray()),
            Crops=frozen.Crops.Concat(state.Crops).DistinctBy(p=>p.Cell).ToArray(),Hydration=frozen.Hydration.Concat(state.Hydration).Distinct().ToArray(),Fire=frozen.Fire.Concat(state.Fire).DistinctBy(p=>p.Cell).ToArray()
        };
        return state with{Structures=Structures.Values.Where(s=>ChunkGameplay.Owns(a,s.Cell)).ToArray(),Signs=Signs.Where(s=>ChunkGameplay.Owns(a,s.Key)).Select(s=>new SignState(s.Key,s.Value)).ToArray(),Containers=Containers.Entries.Values.Where(c=>ChunkGameplay.Owns(a,c.Cell)).Select(c=>c.Capture()).ToArray()};
    }
    internal void RestoreGameplay(ChunkAddress a,ChunkGameplay state,bool statics)
    {
        if(SavedDifficulty==MDSM.Core.Difficulty.Peaceful)state=state with{Hostile=Array.Empty<LocatedMob>()};
        ChunkGameplay.Validate(a,state);
        if(statics)
        {
            foreach(var s in state.Structures)
            {
                var block=GetBlock(s.Cell.X,s.Cell.Y,s.Cell.Z);
                if(!Contains(s.Cell.X,s.Cell.Y,s.Cell.Z)||s.Facing>15||block==BlockType.Air
                    || Blocks.HasPlacementOrientation(block)&&!ValidPlacementOrientation(block,s))
                    throw new System.IO.InvalidDataException("Invalid structure.");
                Structures.Add(s.Cell,s);
                if(block==BlockType.Torch)Torches.Add(s.Cell);
            }
            foreach(var s in state.Signs){if(!ValidSignText(s.Text))throw new System.IO.InvalidDataException("Invalid sign.");Signs.Add(s.Cell,s.Text);}
            Containers.Restore(state.Containers);
        }
        if(LiveMobs==null || !IsSimulated(new(a.BlockX,0,a.BlockZ))){_sleeping[a]=state;return;}
        Vegetation.RestoreChunk(state.Vegetation);Farming.Restore(state.Crops);Farming.RestoreHydration(state.Hydration);Fire.Restore(state.Fire);
        Fluids.RestoreChunk(state.Water);LavaFlow.RestoreChunk(state.Lava);Gravity.RestoreChunk(state.Gravity,state.Falling);
        LiveMobs.AttachChunk(state.Passive,state.Respawns);LiveHostiles.AttachChunk(state.Hostile);LiveDrops.AttachChunk(state.Drops);LiveProjectiles.AttachChunk(state.Arrows);
    }
    internal void ClearDormantHostiles()
    {foreach(var a in _sleeping.Keys.ToArray())if(_sleeping[a].Hostile.Length>0)_sleeping[a]=_sleeping[a] with{Hostile=Array.Empty<LocatedMob>()};}
    // A neighbor's fluid/fire/support update can touch a dormant boundary cell.
    // Its frozen timers must not resurrect vegetation/fire that no longer exists.
    private void ReconcileDormantBlock(Point3 cell,BlockType before,BlockType after)
    {
        var a=ChunkAddress.At(cell.X,cell.Z);if(!_sleeping.TryGetValue(a,out var state))return;
        if(Blocks.Crop(before)&&!Blocks.Crop(after))state=state with{Crops=state.Crops.Where(s=>s.Cell!=cell).ToArray()};
        if(before==BlockType.Fire&&after!=BlockType.Fire)state=state with{Fire=state.Fire.Where(s=>s.Cell!=cell).ToArray()};
        if(before==BlockType.Leaves||after==BlockType.Leaves||before==BlockType.Sapling&&after!=BlockType.Sapling)
            state=state with{Vegetation=new(state.Vegetation.Leaves.Where(p=>p!=cell).ToArray(),state.Vegetation.Decay.Where(t=>t.Cell!=cell).ToArray(),state.Vegetation.Saplings.Where(t=>t.Cell!=cell).ToArray())};
        _sleeping[a]=state;
    }
    private void DetachGameplay(ChunkAddress a,bool statics)
    {
        Vegetation.DetachChunk(a);Farming.DetachChunk(a);Fire.DetachChunk(a);Fluids.DetachChunk(a);LavaFlow.DetachChunk(a);Gravity.DetachChunk(a);
        LiveMobs?.DetachChunk(a);LiveHostiles?.DetachChunk(a);LiveDrops?.DetachChunk(a);LiveProjectiles?.DetachChunk(a);
        if(statics)
        {
            foreach(var p in Structures.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray()){Torches.Remove(p);Structures.Remove(p);}
            foreach(var p in Signs.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray())Signs.Remove(p);
            foreach(var p in Containers.Entries.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray())Containers.Detach(p);
            _sleeping.Remove(a);
        }
    }
    internal FireAttachment DormantFireAttachment(Point3 cell)
    {if(_sleeping.TryGetValue(ChunkAddress.At(cell.X,cell.Z),out var state))foreach(var flame in state.Fire)if(flame.Cell==cell)return flame.Attachment;return FireAttachment.Automatic;}
    private byte[] PrepareRetirement(StreamingChunks.Resident resident)
    {var state=CaptureGameplay(resident.Address);DetachGameplay(resident.Address,true);return StreamingJson.Encode(state);}
    internal void RefreshSimulationWindow()
    {
        if(!IsInfinite || LiveMobs==null)return;
        // Publication/retirement replaces the resident array; walking across a
        // chunk boundary changes Center. Nothing else changes this ownership
        // window, so avoid hundreds of dictionary probes on every render tick.
        if(ReferenceEquals(_simulationChunks,_residentChunks)&&_simulationCenter==ChunkLifecycle.Center)return;
        _simulationChunks=_residentChunks;_simulationCenter=ChunkLifecycle.Center;
        foreach(var chunk in Chunks)
        {
            var a=new ChunkAddress(chunk.X,chunk.Z);
            if(ChunkLifecycle.TryGet(a,out var resident) && resident.FrozenMetadata!=null)continue;
            bool active=IsSimulated(new(chunk.OriginX,0,chunk.OriginZ));
            if(active && _sleeping.ContainsKey(a))
            {var state=CaptureGameplay(a);DetachGameplay(a,false);_sleeping.Remove(a);RestoreGameplay(a,state,false);Fluids.InitializeChunk(chunk);LavaFlow.InitializeChunk(chunk);}
            else if(!active && !_sleeping.ContainsKey(a)){var snapshot=CaptureGameplay(a);DetachGameplay(a,false);_sleeping[a]=snapshot;}
        }
    }
}
