using System;
using System.Collections.Generic;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

public sealed partial class World
{
    public long OriginX {get;private set;}
    public long OriginZ {get;private set;}
    internal WorldPosition InfiniteSpawn {get;set;}=new(.5,100,.5);
    public Microsoft.Xna.Framework.Vector3 Center(Point3 p)=>LocalPosition(p)+new Microsoft.Xna.Framework.Vector3(.5f);
    public int LocalMinX=>IsInfinite?(int)Math.Max(int.MinValue+16,-Coordinates.MaximumBlock-OriginX):0;
    public int LocalMinZ=>IsInfinite?(int)Math.Max(int.MinValue+16,-Coordinates.MaximumBlock-OriginZ):0;
    public int LocalMaxX=>IsInfinite?(int)Math.Min(int.MaxValue-16,Coordinates.MaximumBlock-OriginX):Width;
    public int LocalMaxZ=>IsInfinite?(int)Math.Min(int.MaxValue-16,Coordinates.MaximumBlock-OriginZ):Depth;
    public WorldPosition AbsolutePosition(Microsoft.Xna.Framework.Vector3 local)=>new(OriginX+(double)local.X,local.Y,OriginZ+(double)local.Z);
    public Microsoft.Xna.Framework.Vector3 LocalPosition(WorldPosition absolute)=>new((float)(absolute.X-OriginX),(float)absolute.Y,(float)(absolute.Z-OriginZ));
    public Microsoft.Xna.Framework.Vector3 LocalPosition(Point3 cell)=>new(cell.X-OriginX,cell.Y,cell.Z-OriginZ);
    public Point3 GlobalCell(int localX,int y,int localZ)=>new(OriginX+localX,y,OriginZ+localZ);
    public Point3 GlobalCell(Microsoft.Xna.Framework.Vector3 local)=>GlobalCell((int)MathF.Floor(local.X),(int)MathF.Floor(local.Y),(int)MathF.Floor(local.Z));
    internal void SetOrigin(long x,long z)
    {if(!IsInfinite && (x!=0||z!=0))throw new InvalidOperationException("Finite coordinates have no moving frame.");OriginX=x;OriginZ=z;Climate.OriginX=x;Climate.OriginZ=z;}
    // Integer actor coordinates are local to the floating frame; Point3/Int64
    // APIs are absolute. Finite worlds have origin zero, preserving their API.
    public BlockType GetBlock(int x,int y,int z)=>GetBlock(OriginX+x,y,OriginZ+z);
    public bool SetBlock(int x,int y,int z,BlockType block)=>SetBlock(OriginX+x,y,OriginZ+z,block);
    public bool Contains(int x,int y,int z)=>Contains(OriginX+x,y,OriginZ+z);
    public int SurfaceY(int x,int z)=>SurfaceY(OriginX+x,OriginZ+z);
    public bool IsSolid(int x,int y,int z)=>IsSolid(OriginX+x,y,OriginZ+z);
    public Chunk GetChunk(int x,int z)=>GetChunk(Streaming.Coordinates.FloorDiv(OriginX,16)+x,Streaming.Coordinates.FloorDiv(OriginZ,16)+z);
    internal bool TryGetLocalChunk(int x,int z,out Chunk chunk)=>TryGetChunkAt(OriginX+(long)x*16,OriginZ+(long)z*16,out chunk);
    private readonly Dictionary<ChunkAddress,Chunk> _resident=new();
    private Chunk[] _residentChunks=Array.Empty<Chunk>();
    internal StreamingChunks ChunkLifecycle {get;private set;}
    internal int ChunkSlotCapacity=>IsInfinite?ChunkLifecycle?.MaximumResident??9:ChunkCount;
    private Chunk[] _slots;
    internal Chunk ChunkAtSlot(int index)=>IsInfinite?_slots?[index]:_chunks[index];
    internal Microsoft.Xna.Framework.BoundingBox RelativeBounds(Chunk chunk)
    {var p=new Microsoft.Xna.Framework.Vector3(chunk.OriginX-OriginX,0,chunk.OriginZ-OriginZ);return new(p,p+new Microsoft.Xna.Framework.Vector3(16,128,16));}
    public Microsoft.Xna.Framework.BoundingBox LocalBounds(Point3 cell)
    {var p=LocalPosition(cell);return new(p,p+Microsoft.Xna.Framework.Vector3.One);}
    internal static World OpenStreamed(int seed,string regions,int loaded=12,int render=10,int simulation=6,int generatorVersion=BetaDensityGenerator.Version,SeasonsMode seasons=SeasonsMode.Off)
    {
        var world=new World(seed,WorldSize.Infinite,false,seasons);
        world.Climate.VariedGeneration=true;
        world.Climate.NoiseBiomes=generatorVersion>=5;
        world.ChunkLifecycle=new StreamingChunks(seed,regions,loaded,render,simulation,generatorVersion){Seasons=world.Seasons};
        world._slots=new Chunk[world.ChunkSlotCapacity];
        world.ChunkLifecycle.Published=world.Publish;
        world.ChunkLifecycle.Retiring=world.Retire;
        world.ChunkLifecycle.Capturing=r=>MDSM.Persistence.StreamingJson.Encode(world.CaptureGameplay(r.Address));
        world.ChunkLifecycle.PreparingRetirement=world.PrepareRetirement;
        world.ChunkLifecycle.Resuming=r=>{world.RestoreGameplay(r.Address,MDSM.Persistence.StreamingJson.Decode<ChunkGameplay>(r.FrozenMetadata),true);world.Electricity.Publish(world._resident[r.Address]);};
        return world;
    }
    public bool IsLoaded(Point3 cell)=>!IsInfinite?Contains(cell.X,cell.Y,cell.Z):TryGetChunkAt(cell.X,cell.Z,out _);
    internal bool TryGetChunkAt(long x,long z,out Chunk chunk)
    {
        if(IsInfinite)return _resident.TryGetValue(ChunkAddress.At(x,z),out chunk);
        if(x<0 || x>=Width || z<0 || z>=Depth){chunk=null;return false;}
        chunk=GetChunk(x/16,z/16);return true;
    }
    private void Publish(StreamingChunks.Resident resident)
    {
        ChunkGameplay state=resident.Metadata==null?null:MDSM.Persistence.StreamingJson.Decode<ChunkGameplay>(resident.Metadata);
        if(state!=null)ChunkGameplay.Validate(resident.Address,state);
        var chunk=new Chunk(resident.Address.X,resident.Address.Z,resident.Blocks);
        chunk.Slot=Array.FindIndex(_slots,c=>c==null);if(chunk.Slot<0)throw new InvalidOperationException("Chunk residency exceeded its bound.");_slots[chunk.Slot]=chunk;
        chunk.RestoreBlocks(resident.Blocks);_resident.Add(resident.Address,chunk);RefreshResidents();
        Shelters.Invalidate(resident.Address);
        try{if(state!=null)RestoreGameplay(resident.Address,state,true);Fluids.InitializeChunk(chunk);LavaFlow.InitializeChunk(chunk);}
        catch{DetachGameplay(resident.Address,true);_resident.Remove(resident.Address);_slots[chunk.Slot]=null;RefreshResidents();throw;}
        Electricity.Publish(chunk);
        Revision++;
        Lighting.Invalidate(new(chunk.OriginX,64,chunk.OriginZ));
    }
    private void Retire(StreamingChunks.Resident resident)
    {if(_resident.Remove(resident.Address,out var chunk))_slots[chunk.Slot]=null;RefreshResidents();Shelters.Invalidate(resident.Address);Revision++;Lighting.Invalidate(new(resident.Address.BlockX,64,resident.Address.BlockZ));}
    private void RefreshResidents()
    {_residentChunks=new Chunk[_resident.Count];_resident.Values.CopyTo(_residentChunks,0);}
    internal void UpdateStreaming(ChunkAddress center){ChunkLifecycle.Update(center);RefreshSimulationWindow();}
    internal void MarkStreamedDirty(long chunkX,long chunkZ)=>ChunkLifecycle?.Dirty(new(chunkX,chunkZ));
}
