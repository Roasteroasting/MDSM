using System;
using System.Linq;
using System.Collections.Generic;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds
{
    public sealed partial class VegetationSimulation
    {
        internal ChunkVegetation CaptureOwned(ChunkAddress a)=>new(_natural.Where(p=>ChunkGameplay.Owns(a,p)).ToArray(),Timers(_decay,a),Timers(_saplings,a));
        private CellTimer[] Timers(Dictionary<Point3,double> entries,ChunkAddress a)=>entries.Where(p=>ChunkGameplay.Owns(a,p.Key)).Select(p=>new CellTimer(p.Key,Math.Max(0,p.Value-_time))).ToArray();
        internal void RestoreChunk(ChunkVegetation state)
        {
            foreach(var p in state.Leaves)if(world.GetBlock(p.X,p.Y,p.Z)==BlockType.Leaves)_natural.Add(p);
            foreach(var t in state.Decay){if(!double.IsFinite(t.Remaining)||t.Remaining<0||t.Remaining>MaximumDecay)throw new System.IO.InvalidDataException("Invalid decay timer.");Schedule(_decay,_leafQueue,t.Cell,t.Remaining);}
            foreach(var t in state.Saplings){if(!double.IsFinite(t.Remaining)||t.Remaining<0||t.Remaining>MaximumGrowth)throw new System.IO.InvalidDataException("Invalid growth timer.");Schedule(_saplings,_growthQueue,t.Cell,t.Remaining);}
        }
        internal void DetachChunk(ChunkAddress a)
        {
            _natural.RemoveWhere(p=>ChunkGameplay.Owns(a,p));foreach(var p in _decay.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray())_decay.Remove(p);foreach(var p in _saplings.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray())_saplings.Remove(p);
            _leafQueue.Clear();foreach(var p in _decay)_leafQueue.Enqueue(p.Key,p.Value);_growthQueue.Clear();foreach(var p in _saplings)_growthQueue.Enqueue(p.Key,p.Value);
        }
    }
    public sealed partial class FluidSimulation
    {
        internal Point3[] CaptureChunk(ChunkAddress a)=>_queue.UnorderedItems.Where(p=>ChunkGameplay.Owns(a,p.Element))
            .OrderBy(p=>p.Priority).Select(p=>p.Element).ToArray();
        internal void RestoreChunk(Point3[] cells){foreach(var p in cells)Enqueue(p);}
        internal void DetachChunk(ChunkAddress a)
        {
            if (_queued.RemoveWhere(p=>ChunkGameplay.Owns(a,p))>0)
            {
                var keep=_queue.UnorderedItems.Where(p=>_queued.Contains(p.Element)).ToArray();
                _queue.Clear();
                _queue.EnqueueRange(keep);
            }
        }
        internal void InitializeChunk(Chunk chunk)
        {
            bool wet = (_lava ? chunk.LavaCount : chunk.WaterCount) != 0;
            if (wet && _world.IsSimulated(new(chunk.OriginX,0,chunk.OriginZ)))
            {
                for (int y=0; y<World.Height; y++)
                for (int z=0; z<Chunk.Size; z++)
                for (int x=0; x<Chunk.Size; x++)
                    Wake(new(chunk.OriginX+x,y,chunk.OriginZ+z),chunk.GetBlock(x,y,z));
            }
            if (!_world.IsInfinite) return;
            // A newly published dry chunk can expose the face of an old wet
            // chunk. Inspect only that face, even when the new chunk is dry.
            foreach (Point3 direction in Horizontal)
            {
                long x=chunk.OriginX+direction.X*Chunk.Size, z=chunk.OriginZ+direction.Z*Chunk.Size;
                if (!_world.TryGetChunkAt(x,z,out var neighbor) || (_lava ? neighbor.LavaCount : neighbor.WaterCount)==0
                    || !_world.IsSimulated(new(x,0,z))) continue;
                for (int y=0; y<World.Height; y++)
                for (int along=0; along<Chunk.Size; along++)
                {
                    int nx=direction.X==0 ? along : direction.X>0 ? 0 : Chunk.Size-1;
                    int nz=direction.Z==0 ? along : direction.Z>0 ? 0 : Chunk.Size-1;
                    Wake(new(x+nx,y,z+nz),neighbor.GetBlock(nx,y,nz));
                }
            }
        }
        private void Wake(Point3 cell,BlockType block)
        {
            if (!IsFluid(block)) return;
            if (!IsSource(block) || CanDisplace(cell+Down)) { Enqueue(cell); return; }
            foreach (Point3 direction in Horizontal)
                if (CanDisplace(cell+direction)) { Enqueue(cell); return; }
            if (_lava && Water.IsWater(At(cell+Up))) Enqueue(cell);
        }
    }
    public sealed partial class FarmingSimulation
    {
        internal CropState[] CaptureChunk(ChunkAddress a)=>_crops.Where(p=>ChunkGameplay.Owns(a,p.Key)).Select(p=>new CropState(p.Key,RemainingAt(p.Value,_time),p.Value.Interval)).ToArray();
        internal Point3[] CaptureHydration(ChunkAddress a)=>_hydration.Where(p=>ChunkGameplay.Owns(a,p)&&Blocks.Soil(world.GetBlock(p.X,p.Y,p.Z))).ToArray();
        internal void DetachChunk(ChunkAddress a)
        {
            foreach(var p in _crops.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray())_crops.Remove(p);
            _growth.Clear();foreach(var p in _crops)
                if(p.Value.Wet && !Blocks.MatureCrop(world.GetBlock(p.Key.X,p.Key.Y,p.Key.Z)))_growth.Enqueue((p.Key,p.Value.Version),p.Value.Due);
            var keep=_hydration.Where(p=>!ChunkGameplay.Owns(a,p)).ToArray();_hydration.Clear();_queued.Clear();foreach(var p in keep)Queue(p);
        }
    }
    public sealed partial class FireSimulation
    {
        internal FireState[] CaptureChunk(ChunkAddress a)=>_flames.Where(p=>ChunkGameplay.Owns(a,p.Key)).Select(p=>new FireState(p.Key,Math.Max(0,p.Value.Expires-_time),Math.Max(0,p.Value.Due-_time),Attachment(p.Key))).ToArray();
        internal void DetachChunk(ChunkAddress a)
        {
            foreach(var p in _flames.Keys.Where(p=>ChunkGameplay.Owns(a,p)).ToArray())_flames.Remove(p);
            _ticks.Clear();foreach(var p in _flames)_ticks.Enqueue((p.Key,p.Value.Version),p.Value.Due);
            var keep=_lava.Where(p=>!ChunkGameplay.Owns(a,p)).ToArray();_lava.Clear();_lavaQueued.Clear();foreach(var p in keep){_lava.Enqueue(p);_lavaQueued.Add(p);}
        }
    }
    public sealed partial class FallingBlockSimulation
    {
        internal FallingBlockState[] CaptureActive(ChunkAddress a)=>_active.Where(p=>ChunkAddress.At(p.X,p.Z)==a).Select(p=>p.Capture()).ToArray();
        internal Point3[] CaptureQueued(ChunkAddress a)=>_columns.Where(p=>ChunkGameplay.Owns(a,p)).ToArray();
        internal void RestoreChunk(Point3[] columns,FallingBlockState[] falling)
        {foreach(var p in columns)Enqueue(p);foreach(var s in falling){if(!Blocks.Falls(s.Block)||!double.IsFinite(s.Y)||s.Y<0||s.Y>127||!double.IsFinite(s.DownwardSpeed)||s.DownwardSpeed<0||s.DownwardSpeed>TerminalSpeed)throw new System.IO.InvalidDataException("Invalid falling block.");Add(s);}}
        internal void DetachChunk(ChunkAddress a)
        {
            var columns=_columns.Where(p=>!ChunkGameplay.Owns(a,p)).ToArray();_columns.Clear();_queued.Clear();foreach(var p in columns)Enqueue(p);
            _active.RemoveAll(p=>ChunkAddress.At(p.X,p.Z)==a);_fallingColumns.Clear();
            for(int i=0;i<_active.Count;i++){var p=_active[i];p.Index=i;var key=new Point3(p.X,0,p.Z);if(!_fallingColumns.TryGetValue(key,out var list))_fallingColumns[key]=list=new();list.Add(p);}
            foreach(var list in _fallingColumns.Values)list.Sort((p,q)=>p.Y.CompareTo(q.Y));
        }
    }
}
