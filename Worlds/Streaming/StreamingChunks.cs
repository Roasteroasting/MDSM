using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MDSM.Persistence;

namespace MDSM.Worlds.Streaming;

internal sealed record ChunkSnapshot(ChunkAddress Address,int GeneratorVersion,byte[] Blocks,bool FromDisk=false,byte[] Metadata=null)
{
    internal byte[] Encode()
    {
        if(Blocks.Length!=32768)throw new InvalidDataException("Incorrect chunk length.");
        using var data=new MemoryStream();using var writer=new BinaryWriter(data);
        writer.Write(2);writer.Write(GeneratorVersion);writer.Write(Address.X);writer.Write(Address.Z);writer.Write(Blocks.Length);writer.Write(Blocks);writer.Write(Metadata?.Length??0);if(Metadata!=null)writer.Write(Metadata);return data.ToArray();
    }
    internal static ChunkSnapshot Decode(ChunkAddress expected,byte[] bytes)
    {
        using var reader=new BinaryReader(new MemoryStream(bytes,false));
        int schema=reader.ReadInt32();if(schema is <1 or >2)throw new InvalidDataException("Unsupported chunk snapshot schema.");
        int generator=reader.ReadInt32();var address=new ChunkAddress(reader.ReadInt64(),reader.ReadInt64());int length=reader.ReadInt32();
        if(generator is <1 or >BetaDensityGenerator.Version || address!=expected || length!=32768 || bytes.Length<28+length)throw new InvalidDataException("Invalid chunk identity/generator/length.");
        byte[] blocks=reader.ReadBytes(length);foreach(byte block in blocks)if(!Worlds.Blocks.IsDefined((BlockType)block))throw new InvalidDataException("Invalid chunk block ID.");
        byte[] metadata=null;if(schema==2){int count=reader.ReadInt32();if(count<0 || count>ChunkRegionStore.MaximumPayload-32768 || reader.BaseStream.Length-reader.BaseStream.Position!=count)throw new InvalidDataException("Invalid chunk metadata length.");if(count>0)metadata=reader.ReadBytes(count);}
        if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Unexpected chunk snapshot tail.");
        return new(address,generator,blocks,true,metadata);
    }
}

// Owner-thread coordinator. Workers own immutable snapshots only. No graphics
// operations are allowed here; the renderer consumes revision-tagged snapshots.
internal sealed class StreamingChunks : IAsyncDisposable
{
    internal sealed class Resident(ChunkSnapshot snapshot)
    {
        internal readonly ChunkAddress Address=snapshot.Address;
        internal readonly byte[] Blocks=snapshot.Blocks;
        internal readonly byte[] Metadata=snapshot.Metadata;
        internal byte[] FrozenMetadata;
        internal long Revision,SavedRevision=snapshot.FromDisk?0:-1;
        internal bool Dirty=>Revision!=SavedRevision;
    }
    private sealed record Loading(ChunkAddress Address,Task<ChunkSnapshot> Task,CancellationTokenSource Cancellation);
    private sealed record Saving(Resident Chunk,long Revision,Task Task);
    private readonly Dictionary<ChunkAddress,Resident> _loaded=new();
    private readonly List<Loading> _loading=new();
    private readonly List<Saving> _saving=new();
    private readonly HashSet<ChunkAddress> _failed=new();
    private readonly List<ChunkAddress> _evictions=new();
    private readonly ChunkRegionStore _store;
    private readonly BetaDensityGenerator _generator;
    private int _owner=Environment.CurrentManagedThreadId;
    internal void AdoptOwner(){if(_disposed)throw new ObjectDisposedException(nameof(StreamingChunks));_owner=Environment.CurrentManagedThreadId;}
    private ChunkAddress? _center;
    private ChunkAddress[] _desired=Array.Empty<ChunkAddress>();
    private HashSet<ChunkAddress> _wanted=new();
    private bool _disposed;
    private bool _checkpointBusy;
    internal int LoadedDistance {get;}
    internal int RenderDistance {get;}
    internal int SimulationDistance {get;}
    internal const int MaximumWorkers=2;
    internal const int MaximumSaveWorkers=2;
    internal int LoadingCount=>_loading.Count;
    internal int SavingCount=>_saving.Count;
    internal int MaximumResident=>(2*LoadedDistance+1)*(2*LoadedDistance+1);
    internal int Count=>_loaded.Count;
    internal int InFlight=>_loading.Count+_saving.Count;
    internal Exception Error {get;private set;}
    internal Action<Resident> Published,Retiring;
    internal Func<Resident,byte[]> Capturing;
    internal Func<Resident,byte[]> PreparingRetirement;
    internal Action<Resident> Resuming;
    internal SeasonManager Seasons { get; set; }
    internal ChunkAddress? Center=>_center;
    internal void Dirty(ChunkAddress address){Owner();if(_loaded.TryGetValue(address,out var chunk))chunk.Revision++;}
    internal int GeneratorVersion=>_generator.GenerationVersion;
    internal StreamingChunks(int seed,string directory,int loaded=12,int render=10,int simulation=6,int generatorVersion=BetaDensityGenerator.Version)
    {
        if(loaded is <2 or >32 || render<0 || simulation<0 || render>loaded-2 || simulation>loaded-2)throw new ArgumentOutOfRangeException(nameof(loaded));
        LoadedDistance=loaded;RenderDistance=render;SimulationDistance=simulation;
        _store=new(directory);_generator=new(seed,generatorVersion);
    }
    private void Owner(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Chunk lifecycle must run on its owner thread.");ObjectDisposedException.ThrowIf(_disposed,this);}
    internal bool TryGet(ChunkAddress address,out Resident resident){Owner();return _loaded.TryGetValue(address,out resident);}
    internal bool Ready(long x,long z){Owner();return _loaded.ContainsKey(ChunkAddress.At(x,z));}
    // Missing chunks are explicitly solid to collision, never silently ordinary Air.
    internal bool Solid(long x,int y,long z)
    {
        Owner();if(y<0)return true;if(y>=128)return false;
        if(!_loaded.TryGetValue(ChunkAddress.At(x,z),out var chunk))return true;
        return Worlds.Blocks.IsSolid((BlockType)chunk.Blocks[Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16))]);
    }
    internal bool Set(long x,int y,long z,BlockType block)
    {
        Owner();if((uint)y>=128 || !Worlds.Blocks.IsDefined(block)||!_loaded.TryGetValue(ChunkAddress.At(x,z),out var chunk))return false;
        int index=Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16));if(chunk.Blocks[index]==(byte)block)return false;
        chunk.Blocks[index]=(byte)block;chunk.Revision++;return true;
    }
    internal void RetryFailures(){Owner();_failed.Clear();Error=null;}
    internal void Update(ChunkAddress center)
    {
        Owner();Coordinates.Validate(center);
        if(_checkpointBusy)return;
        if(_center!=center)
        {
            _center=center;var desired=new List<ChunkAddress>();
            for(int z=-LoadedDistance;z<=LoadedDistance;z++)for(int x=-LoadedDistance;x<=LoadedDistance;x++)
            {var at=new ChunkAddress(center.X+x,center.Z+z);if(Coordinates.Valid(at.BlockX,at.BlockZ)&&Coordinates.Valid(at.BlockX+15,at.BlockZ+15))desired.Add(at);}
            _desired=desired.OrderBy(a=>(a.X-center.X)*(a.X-center.X)+(a.Z-center.Z)*(a.Z-center.Z)).ToArray();_wanted=new(_desired);
            _failed.RemoveWhere(a=>!_wanted.Contains(a)&&!_loaded.ContainsKey(a));
            foreach(var job in _loading)if(!_wanted.Contains(job.Address))job.Cancellation.Cancel();
        }
        for(int i=_loading.Count-1;i>=0;i--)
        {
            var job=_loading[i];if(!job.Task.IsCompleted)continue;
            try
            {
                var snapshot=job.Task.GetAwaiter().GetResult();
                if(_wanted.Contains(job.Address)&&!_loaded.ContainsKey(job.Address)&&_loaded.Count<MaximumResident)
                {var resident=new Resident(snapshot);Published?.Invoke(resident);_loaded.Add(job.Address,resident);}
            }
            catch(OperationCanceledException){}
            catch(Exception e){Error=e;_failed.Add(job.Address);}
            job.Cancellation.Dispose();_loading.RemoveAt(i);
        }
        for(int i=_saving.Count-1;i>=0;i--)
        {
            var job=_saving[i];if(!job.Task.IsCompleted)continue;
            try{job.Task.GetAwaiter().GetResult();job.Chunk.SavedRevision=job.Revision;}
            catch(Exception e){Error=e;_failed.Add(job.Chunk.Address);}
            _saving.RemoveAt(i);
        }
        _evictions.Clear();
        int captures=0;
        foreach(var pair in _loaded)
        {
            if(_wanted.Contains(pair.Key))
            {
                if(pair.Value.FrozenMetadata!=null){Resuming?.Invoke(pair.Value);pair.Value.FrozenMetadata=null;pair.Value.Revision++;}
                continue;
            }
            if((PreparingRetirement!=null || Capturing!=null) && pair.Value.FrozenMetadata==null)
            {if(captures>=2)continue;pair.Value.FrozenMetadata=PreparingRetirement?.Invoke(pair.Value)??Capturing(pair.Value);pair.Value.Revision++;captures++;}
            if(pair.Value.Dirty)
            {
                // Writes must not monopolize the generation/read workers. Flight
                // continually retires a strip of chunks; sharing the two slots
                // let that entire strip drain before any new terrain could load.
                if(_saving.Count<MaximumSaveWorkers && !_failed.Contains(pair.Key) && !IsSaving(pair.Value))Save(pair.Value);
            }
            else if(!IsSaving(pair.Value))_evictions.Add(pair.Key);
        }
        foreach(var address in _evictions){Retiring?.Invoke(_loaded[address]);_loaded.Remove(address);}
        foreach(var address in _desired)
        {
            if(_loading.Count>=MaximumWorkers || _loaded.Count+_loading.Count>=MaximumResident)break;
            if(_loaded.ContainsKey(address)||IsLoading(address)||_failed.Contains(address))continue;
            var cancellation=new CancellationTokenSource();var token=cancellation.Token;
            Season? season=Seasons!=null && Seasons.Mode!=SeasonsMode.Off?Seasons.Current:null;
            async Task<ChunkSnapshot> Load()
            {
                byte[] stored=await _store.ReadAsync(address,token).ConfigureAwait(false);
                if(stored!=null)
                {
                    var snapshot=ChunkSnapshot.Decode(address,stored);
                    if(snapshot.GeneratorVersion!=GeneratorVersion)throw new InvalidDataException("Chunk generator does not match world manifest.");
                    return snapshot;
                }
                return await Task.Run(()=>_generator.GenerateComplete(address,token,season),token).ConfigureAwait(false);
            }
            _loading.Add(new(address,Load(),cancellation));
        }
    }
    private bool IsSaving(Resident chunk){foreach(var job in _saving)if(job.Chunk==chunk)return true;return false;}
    private bool IsLoading(ChunkAddress address){foreach(var job in _loading)if(job.Address==address)return true;return false;}
    private void Save(Resident chunk)
    {byte[] blocks=(byte[])chunk.Blocks.Clone();byte[] metadata=chunk.FrozenMetadata??Capturing?.Invoke(chunk)??chunk.Metadata;long revision=chunk.Revision;_saving.Add(new(chunk,revision,_store.WriteAsync(chunk.Address,new ChunkSnapshot(chunk.Address,GeneratorVersion,blocks,Metadata:metadata).Encode())));}
    internal List<ChunkSnapshot> CaptureCheckpoint()
    {
        Owner();_checkpointBusy=true;
        var snapshots=new List<ChunkSnapshot>(_loaded.Count);
        foreach(var r in _loaded.Values)snapshots.Add(new(r.Address,GeneratorVersion,(byte[])r.Blocks.Clone(),Metadata:r.FrozenMetadata??Capturing?.Invoke(r)??r.Metadata));
        return snapshots;
    }
    internal async Task WriteCheckpointAsync(List<ChunkSnapshot> snapshots)
    {foreach(var job in _saving)await job.Task.ConfigureAwait(false);foreach(var batch in snapshots.Chunk(ChunkRegionStore.MaximumWriteBatch))await _store.WriteBatchAsync(batch.Select(s=>(s.Address,s.Encode())).ToArray()).ConfigureAwait(false);}
    internal async Task WaitWritesAsync(){foreach(var job in _saving)await job.Task.ConfigureAwait(false);}
    internal void FinishCheckpoint(bool success)
    {
        Owner();foreach(var job in _saving)if(job.Task.IsCompletedSuccessfully)job.Chunk.SavedRevision=job.Revision;_saving.Clear();
        // Failed prepared checkpoints must be recovered before more eviction
        // writes; retrying Save captures current ownership and replays the plan.
        _checkpointBusy=!success;if(success){Error=null;_failed.Clear();}
    }
    public ValueTask DisposeAsync()=>CloseAsync(false);
    internal async ValueTask CloseAsync(bool checkpointIsCurrent)
    {
        Owner();_disposed=true;
        foreach(var job in _loading)job.Cancellation.Cancel();
        var errors=new List<Exception>();
        var pending=new List<(ChunkAddress Address,byte[] Bytes)>();
        foreach(var chunk in _loaded.Values)if(!checkpointIsCurrent&&(chunk.Dirty || Capturing!=null))
        {
            try{pending.Add((chunk.Address,new ChunkSnapshot(chunk.Address,GeneratorVersion,(byte[])chunk.Blocks.Clone(),Metadata:chunk.FrozenMetadata??Capturing?.Invoke(chunk)??chunk.Metadata).Encode()));}catch(Exception e){errors.Add(e);}
        }
        try
        {
            foreach(var job in _loading)try{await job.Task.ConfigureAwait(false);}catch(OperationCanceledException){}catch(Exception e){errors.Add(e);}finally{job.Cancellation.Dispose();}
            foreach(var job in _saving)try{await job.Task.ConfigureAwait(false);job.Chunk.SavedRevision=job.Revision;}catch(Exception e){errors.Add(e);}
            foreach(var item in pending)try{await _store.WriteAsync(item.Address,item.Bytes).ConfigureAwait(false);}catch(Exception e){errors.Add(e);}
        }
        finally{_loading.Clear();_saving.Clear();}
        if(errors.Count>0)
        {
            // A failed Save-and-Exit must leave the session recoverable. The
            // caller can fix storage and retry instead of losing dirty blocks.
            _disposed=false;Error=new AggregateException("Chunk shutdown could not complete all operations.",errors);throw Error;
        }
        _store.Dispose();_loaded.Clear();
    }
}
