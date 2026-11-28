using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

// Reconstructible, packed skylight/block-light. Only queried chunks are cached;
// a fourteen-cell halo makes each result independent of cache/residency order.
// No full-world light array, initial scan, or time-of-day invalidation is needed.
public sealed class BlockLighting
{
    // Covers the normal 1080p mesh region plus a margin, so preparing its distant
    // edge does not evict the light pages needed by nearby spawn/interaction queries.
    public const int Maximum = 14, MaximumCachedChunks = 1536;
    private const int Halo=15,MaximumSide=74,MaximumVolume=MaximumSide*MaximumSide*World.Height;
    private int Side=Chunk.Size+2*Halo;
    private int Slice=>Side*Side;
    private int Volume=>Slice*World.Height;
    internal const int PageSide=18;
    private readonly World _world;
    private readonly Dictionary<ChunkAddress,LinkedListNode<Page>> _pages=new();
    private readonly LinkedList<Page> _lru=new();
    private byte[] _light,_cost,_queued,_base;
    private byte[] _direct;
    private Chunk[] _columns;
    private int[] _columnOffsets;
    private static readonly byte[] Costs=CreateCosts();
    private int[] _queue;
    private int _read,_write,_count;
    private int _fieldStage,_fieldY,_fieldZ; private long _fieldOriginX,_fieldOriginZ;
    private bool _warming;
    private bool _workerScratch;
    internal void UseWorkerScratch()=>_workerScratch=true;
    internal void ReleaseWorkerScratch()
    {
        if(!_workerScratch)return;
        if(_light!=null)ArrayPool<byte>.Shared.Return(_light);
        if(_cost!=null)ArrayPool<byte>.Shared.Return(_cost);
        if(_queued!=null)ArrayPool<byte>.Shared.Return(_queued);
        if(_direct!=null)ArrayPool<byte>.Shared.Return(_direct);
        if(_queue!=null)ArrayPool<int>.Shared.Return(_queue);
        if(_columnOffsets!=null)ArrayPool<int>.Shared.Return(_columnOffsets);
        if(_columns!=null)ArrayPool<Chunk>.Shared.Return(_columns,true);
        _light=_cost=_queued=_direct=null;_queue=_columnOffsets=null;_columns=null;_warming=false;
    }
    private long _warmX,_warmZ,_warmMinX,_warmMaxX,_warmMinZ,_warmMaxZ;
    private readonly int[] _warmRevisions=new int[9];
    private readonly Chunk[] _warmSources=new Chunk[9];
    // Coalesce edits by chunk. A recalculation already covers the entire affected
    // light cone, so patch every cached page in that cone instead of rebuilding
    // the same overlapping neighborhood once for each of nine adjacent meshes.
    private readonly Dictionary<int,Rectangle> _edits=new();
    public int CachedChunks=>_pages.Count;
    public long CachedBytes=>(long)_pages.Count*PageSide*PageSide*World.Height;
    public int BuiltPages { get; private set; }
    public int IncrementalUpdates {get;private set;}
    internal sealed class Page(long x,long z)
    {
        internal long X=x,Z=z;
        internal readonly byte[] Data=new byte[PageSide*PageSide*World.Height];
        internal bool Dirty;
        internal byte At(long x,int y,long z)=>y<0?(byte)0:y>=World.Height?(byte)0xe0:Data[checked((int)((y*PageSide+z-Z+1)*PageSide+x-X+1))];
    }
    public BlockLighting(World world)=>_world=world;
    // Glass and thin open geometry transmit daylight; leaves/water attenuate it.
    public static int Transmission(BlockType block)=>Costs[(byte)block];
    private static byte[] CreateCosts()
    {
        var costs=new byte[256];for(int i=0;i<costs.Length;i++)
        {BlockType b=(BlockType)i;costs[i]=(byte)(Blocks.IsOpaque(b)?16:b==BlockType.Leaves || Water.IsWater(b)?1:0);}return costs;
    }
    public int Emission(Point3 p,BlockType block)=>block is BlockType.Torch or BlockType.Fire || Lava.IsLava(block)?14
        :(Smelting.Machine(block)||block==BlockType.CoalGenerator) && _world.Containers.Entries.TryGetValue(p,out var entry) && entry.Burn>0?13:0;
    public byte Packed(Point3 p)
    {
        if(p.Y>=World.Height)return 0xe0;
        if(!_world.Contains(p.X,p.Y,p.Z))return p.Y<0?(byte)0:(byte)0xe0;
        return GetPage(Coordinates.FloorDiv(p.X,16),Coordinates.FloorDiv(p.Z,16)).At(p.X,p.Y,p.Z);
    }
    public int Effective(Point3 p)
    { byte packed=Packed(p);return Math.Max(packed&15,Math.Max(0,(packed>>4)-(Maximum-_world.Time.SunlightLevel))); }
    public int Effective(Vector3 p)=>Effective(_world.GlobalCell(p));
    internal int ForRendering(Vector3 position)
    {
        if (!_world.IsInfinite) return Effective(position);
        Point3 p=_world.GlobalCell(position);
        if (p.Y>=World.Height) return _world.Time.SunlightLevel;
        if (p.Y<0) return 0;
        // The renderer publishes fresh pages from its workers. Read the last
        // result while a changed page is dirty to avoid rebuilding it in Draw.
        if (_pages.TryGetValue(ChunkAddress.At(p.X,p.Z),out var cached))
        {
            byte light=cached.Value.At(p.X,p.Y,p.Z);
            return Math.Max(light&15,Math.Max(0,(light>>4)-(Maximum-_world.Time.SunlightLevel)));
        }
        return p.Y>_world.SurfaceY(p.X,p.Z) ? _world.Time.SunlightLevel : 0;
    }
    internal bool CopyReadyPage(long cx,long cz,BlockLighting target)
    {
        if (!_pages.TryGetValue(new(cx,cz),out var entry) || entry.Value.Dirty) return false;
        target.SetSnapshot(entry.Value.Data);
        return true;
    }
    internal bool TryEffective(Point3 p,out int level)
    {
        if(p.Y>=World.Height){level=_world.Time.SunlightLevel;return true;}
        if(p.Y<0){level=0;return true;}
        if(_pages.TryGetValue(ChunkAddress.At(p.X,p.Z),out var cached)&&!cached.Value.Dirty)
        {byte light=cached.Value.At(p.X,p.Y,p.Z);level=Math.Max(light&15,Math.Max(0,(light>>4)-(Maximum-_world.Time.SunlightLevel)));return true;}
        level=0;return false;
    }
    internal Page GetPage(long cx,long cz)
    {
        if(_snapshotPage!=null && cx==0 && cz==0)return _snapshotPage;
        RefreshEdits();
        var key=new ChunkAddress(cx,cz);
        if(_pages.TryGetValue(key,out var node))
        { _lru.Remove(node);_lru.AddLast(node);if(node.Value.Dirty)Build(node.Value);return node.Value; }
        Page page=RentPage(cx,cz);Build(page);return page;
    }
    private Page _snapshotPage;
    internal void SetSnapshot(byte[] packed){_snapshotPage=new Page(0,0);packed.CopyTo(_snapshotPage.Data,0);}
    internal void AcceptPage(long cx,long cz,ReadOnlySpan<byte> packed)
    {var page=RentPage(cx,cz);packed.CopyTo(page.Data);page.Dirty=false;BuiltPages++;}
    private Page RentPage(long cx,long cz)
    {
        var key=new ChunkAddress(cx,cz);
        if(_pages.TryGetValue(key,out var existing))return existing.Value;
        if(_pages.Count>=MaximumCachedChunks)
        {
            var oldest=_lru.First;_pages.Remove(ChunkAddress.At(oldest.Value.X,oldest.Value.Z));_lru.RemoveFirst();
            Page recycled=oldest.Value;recycled.X=cx*16;recycled.Z=cz*16;recycled.Dirty=true;
            _pages.Add(key,oldest);_lru.AddLast(oldest);return recycled;
        }
        var page=new Page(cx*16,cz*16){Dirty=true};_pages.Add(key,_lru.AddLast(page));return page;
    }
    // Cold prefetch work is resumable. Foreground queries still get an exact
    // synchronous result; cached queries do not disturb the pending scratch field.
    internal bool PreparePage(long cx,long cz,double milliseconds)
    {
        RefreshEdits();
        var key=new ChunkAddress(cx,cz);
        if(_pages.TryGetValue(key,out var cached) && !cached.Value.Dirty)return true;
        if(!_warming || _warmX!=cx || _warmZ!=cz || !WarmUnchanged())
        {
            _warmX=cx;_warmZ=cz;
            _warmMinX=_world.IsInfinite?cx-1:Math.Max(0,cx-1);_warmMaxX=_world.IsInfinite?cx+1:Math.Min(_world.ChunksX-1,cx+1);
            _warmMinZ=_world.IsInfinite?cz-1:Math.Max(0,cz-1);_warmMaxZ=_world.IsInfinite?cz+1:Math.Min(_world.ChunksZ-1,cz+1);
            int i=0;for(long z=_warmMinZ;z<=_warmMaxZ;z++)for(long x=_warmMinX;x<=_warmMaxX;x++)
            {_world.TryGetChunkAt(x*16,z*16,out var source);_warmSources[i]=source;_warmRevisions[i++]=source?.BlockRevision??-1;}
            BeginField(cx*16-Halo,cz*16-Halo,Chunk.Size+2*Halo);_warming=true;
        }
        long deadline=Stopwatch.GetTimestamp()+(long)(Math.Max(.05,milliseconds)*Stopwatch.Frequency/1000);
        if(!AdvanceField(deadline))return false;
        _warming=false;CopyField(RentPage(cx,cz));return true;
    }
    private bool WarmUnchanged()
    {
        int i=0;for(long z=_warmMinZ;z<=_warmMaxZ;z++)for(long x=_warmMinX;x<=_warmMaxX;x++)
        {_world.TryGetChunkAt(x*16,z*16,out var source);if(_warmSources[i]!=source||_warmRevisions[i++]!=(source?.BlockRevision??-1))return false;}
        return true;
    }
    internal void Changed(Point3 p,BlockType previous,BlockType next)
    { if(Transmission(previous)!=Transmission(next) || Emission(p,previous)!=Emission(p,next))Invalidate(p); }
    internal void Invalidate(Point3 p)
    {
        if(_world.IsInfinite)
        {
            long cx=Coordinates.FloorDiv(p.X,16),cz=Coordinates.FloorDiv(p.Z,16);
            for(long z=cz-1;z<=cz+1;z++)for(long x=cx-1;x<=cx+1;x++)
            {
                if(_pages.TryGetValue(new(x,z),out var node))node.Value.Dirty=true;
                if(_world.TryGetChunkAt(x*16,z*16,out var chunk)){chunk.MarkDirty();chunk.MarkWaterDirty();}
            }
            // Publishing an unrelated distant chunk must not throw away the
            // partially calculated page currently needed by the mesh builder.
            if(_warming && cx>=_warmMinX&&cx<=_warmMaxX&&cz>=_warmMinZ&&cz<=_warmMaxZ)_warming=false;
            return;
        }
        if(BuiltPages==0)return; // No lit geometry/cache exists yet, including during generation/loading.
        int minX=checked((int)Math.Max(0,(p.X-Halo)/16)),maxX=checked((int)Math.Min(_world.ChunksX-1,(p.X+Halo)/16));
        int minZ=checked((int)Math.Max(0,(p.Z-Halo)/16)),maxZ=checked((int)Math.Min(_world.ChunksZ-1,(p.Z+Halo)/16));
        bool cached=false;
        for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)
        {
            if(_pages.ContainsKey(new(x,z)))cached=true;
            else {_world.GetChunk(x,z).MarkDirty();_world.GetChunk(x,z).MarkWaterDirty();}
        }
        // Remote simulation edits retain dirty revisions but do not calculate
        // invisible light fields. A later cold query reconstructs them exactly.
        if(!cached)return;
        int key=checked((int)(p.Z/16*_world.ChunksX+p.X/16));
        Rectangle changed=new(checked((int)p.X),checked((int)p.Z),1,1);
        if(_edits.TryGetValue(key,out var previous))changed=Rectangle.Union(changed,previous);
        _edits[key]=changed;
        if(_edits.Count>64)
        {
            foreach(var page in _lru)
            {page.Dirty=true;_world.GetChunk(page.X/16,page.Z/16).MarkDirty();_world.GetChunk(page.X/16,page.Z/16).MarkWaterDirty();}
            _edits.Clear(); // Bounded fallback for bulk gameplay edits.
        }
    }
    private void Build(Page page)
    {
        BuildField(page.X-Halo,page.Z-Halo,Chunk.Size+2*Halo);
        CopyField(page);
    }
    private void CopyField(Page page)
    {
        for(int y=0;y<World.Height;y++)for(int z=0;z<PageSide;z++)
            _light.AsSpan(y*Slice+(z+Halo-1)*Side+Halo-1,PageSide).CopyTo(page.Data.AsSpan((y*PageSide+z)*PageSide));
        page.Dirty=false;
    }
    internal void FlushChanges()=>RefreshEdits();
    private void RefreshEdits()
    {
        if(_edits.Count>0)_warming=false;
        foreach(var edit in _edits)
        {
            int cx=edit.Key%_world.ChunksX,cz=edit.Key/_world.ChunksX;
            Rectangle affected=edit.Value;affected.Inflate(14,14);
            // The changed cone needs its own halo: unrelated lights just outside
            // that cone must still contribute when its cached cells are replaced.
            int originX=edit.Value.Left-15,originZ=edit.Value.Top-15;
            if(!TryIncremental(edit.Value,originX,originZ))
            {
                originX=edit.Value.Left-28;originZ=edit.Value.Top-28;
                BuildField(originX,originZ,Math.Max(edit.Value.Width,edit.Value.Height)+56);
            }
            for(int z=Math.Max(0,cz-1);z<=Math.Min(_world.ChunksZ-1,cz+1);z++)
            for(int x=Math.Max(0,cx-1);x<=Math.Min(_world.ChunksX-1,cx+1);x++)
            {
                if(!_pages.TryGetValue(new(x,z),out var node))continue;
                Page page=node.Value;
                Rectangle area=Rectangle.Intersect(affected,new(checked((int)page.X)-1,checked((int)page.Z)-1,PageSide,PageSide));
                bool different=false;
                for(int y=0;y<World.Height;y++)for(int row=area.Top;row<area.Bottom;row++)
                {
                    var value=_light.AsSpan(y*Slice+(row-originZ)*Side+area.Left-originX,area.Width);
                    var previous=page.Data.AsSpan(checked((int)((y*PageSide+row-page.Z+1)*PageSide+area.Left-page.X+1)),area.Width);
                    if(!value.SequenceEqual(previous)){different=true;value.CopyTo(previous);}
                }
                if(different){_world.GetChunk(x,z).MarkDirty();_world.GetChunk(x,z).MarkWaterDirty();}
            }
        }
        _edits.Clear();
    }
    private bool TryIncremental(Rectangle changed,int originX,int originZ)
    {
        Side=Math.Max(changed.Width,changed.Height)+30;
        _base??=new byte[MaximumVolume];
        Array.Clear(_queued,0,Volume);_read=_write=_count=0;
        for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
        {
            int wx=originX+x,wz=originZ+z;Page page=null;Chunk chunk=null;
            if((uint)wx<_world.Width && (uint)wz<_world.Depth)
            {
                if(!_pages.TryGetValue(ChunkAddress.At(wx,wz),out var node)||node.Value.Dirty)return false;
                page=node.Value;chunk=_world.GetChunk(wx/16,wz/16);
            }
            int sky=14,column=z*Side+x,offset=(wz&15)*16+(wx&15);
            for(int y=World.Height-1;y>=0;y--)
            {
                BlockType block=chunk==null?(_world.IsInfinite?BlockType.Bedrock:BlockType.Air):(BlockType)chunk.Blocks[y*256+offset];
                int index=y*Slice+column,cost=Costs[(byte)block];_cost[index]=(byte)cost;
                sky=sky==14 && cost==0?14:Math.Max(0,sky-Math.Max(1,cost));
                int emit=block is BlockType.Torch or BlockType.Fire || Lava.IsLava(block)?14:Smelting.Machine(block)||block==BlockType.CoalGenerator?Emission(new(wx,y,wz),block):0;
                _base[index]=(byte)((sky<<4)|emit);_light[index]=page==null?(byte)0xe0:page.At(wx,y,wz);
            }
        }
        // Re-evaluate the edited vertical columns (direct sun can change down to
        // bedrock). Only actual changes enqueue neighbors. Positive attenuation
        // also removes unsupported cycles after light sources are destroyed.
        for(int z=changed.Top;z<changed.Bottom;z++)for(int x=changed.Left;x<changed.Right;x++)
        for(int y=0;y<World.Height;y++)Enqueue(y*Slice+(z-originZ)*Side+x-originX);
        int processed=0;
        while(_count>0)
        {
            if(++processed>Volume*32)return false;
            int i=_queue[_read];_read=(_read+1)%Volume;_count--;_queued[i]=0;
            int x=i%Side,z=i/Side%Side,y=i/Slice;
            int sky=_base[i]>>4,block=_base[i]&15,c=Math.Max(1,(int)_cost[i]);
            if(x>0)Consider(i-1);if(x<Side-1)Consider(i+1);if(z>0)Consider(i-Side);if(z<Side-1)Consider(i+Side);
            if(y>0)Consider(i-Slice);if(y<World.Height-1)Consider(i+Slice);
            byte value=(byte)((sky<<4)|block);if(value==_light[i])continue;_light[i]=value;
            if(x>1)Enqueue(i-1);if(x<Side-2)Enqueue(i+1);if(z>1)Enqueue(i-Side);if(z<Side-2)Enqueue(i+Side);
            if(y>0)Enqueue(i-Slice);if(y<World.Height-1)Enqueue(i+Slice);
            void Consider(int neighbor){sky=Math.Max(sky,(_light[neighbor]>>4)-c);block=Math.Max(block,(_light[neighbor]&15)-c);}
        }
        IncrementalUpdates++;return true;
    }
    private void BuildField(long originX,long originZ,int side)
    {
        _warming=false;BeginField(originX,originZ,side);AdvanceField(long.MaxValue);
    }
    private void BeginField(long originX,long originZ,int side)
    {
        _fieldOriginX=originX;_fieldOriginZ=originZ;
        Side=side;
        _light??=Bytes(MaximumVolume);_cost??=Bytes(MaximumVolume);_queued??=Bytes(MaximumVolume);_queue??=Ints(MaximumVolume);
        _direct??=Bytes(MaximumSide*MaximumSide);_columns??=_workerScratch?ArrayPool<Chunk>.Shared.Rent(MaximumSide*MaximumSide):new Chunk[MaximumSide*MaximumSide];_columnOffsets??=Ints(MaximumSide*MaximumSide);
        Array.Clear(_light,0,Volume);Array.Clear(_queued,0,Volume);_read=_write=_count=0;
        for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
        {
            long wx=originX+x,wz=originZ+z;int column=z*Side+x;
            _columns[column]=_world.TryGetChunkAt(wx,wz,out var resident)?resident:null;
            _columnOffsets[column]=(int)((wz&15)*16+(wx&15));_direct[column]=14;
        }
        _fieldStage=1;_fieldY=World.Height-1;_fieldZ=0;
    }
    private byte[] Bytes(int length)=>_workerScratch?ArrayPool<byte>.Shared.Rent(length):new byte[length];
    private int[] Ints(int length)=>_workerScratch?ArrayPool<int>.Shared.Rent(length):new int[length];
    private bool AdvanceField(long deadline)
    {
        // Walk contiguous block rows instead of repeatedly striding through a chunk's
        // entire height. Prefetched column references avoid world/dictionary queries.
        while(_fieldStage==1)
        {
            int y=_fieldY;
            int layer=y*Slice,chunkLayer=y*256;
            for(int column=0;column<Slice;column++)
            {
                int index=layer+column;Chunk chunk=_columns[column];
                BlockType block=chunk==null?(_world.IsInfinite?BlockType.Bedrock:BlockType.Air):(BlockType)chunk.Blocks[chunkLayer+_columnOffsets[column]];
                int cost=Costs[(byte)block],sky=_direct[column];_cost[index]=(byte)cost;
                sky=sky==14 && cost==0?14:Math.Max(0,sky-Math.Max(1,cost));
                _direct[column]=(byte)sky;
                int emit=block is BlockType.Torch or BlockType.Fire || Lava.IsLava(block)?14:Smelting.Machine(block)||block==BlockType.CoalGenerator?Emission(new(_fieldOriginX+column%Side,y,_fieldOriginZ+column/Side),block):0;
                _light[index]=(byte)((sky<<4)|emit);
                if(emit>1)Enqueue(index);
            }
            if(--_fieldY<0){_fieldStage=2;_fieldY=_fieldZ=0;}
            if(Stopwatch.GetTimestamp()>=deadline)return false;
        }
        // Direct sky is already correct vertically. Only horizontal shadow frontiers
        // need seeds; the shared propagation queue then also travels vertically.
        while(_fieldStage==2)
        {
            int y=_fieldY,z=_fieldZ;
            for(int x=0;x<Side;x++)
            {
            int i=y*Slice+z*Side+x,sky=_light[i]>>4;if(sky<=1)continue;
            if(x>0 && SkyCanReach(sky,i-1) || x<Side-1 && SkyCanReach(sky,i+1) || z>0 && SkyCanReach(sky,i-Side) || z<Side-1 && SkyCanReach(sky,i+Side))Enqueue(i);
            }
            if(++_fieldZ==Side){_fieldZ=0;if(++_fieldY==World.Height)_fieldStage=3;}
            if((_fieldZ&7)==0 && Stopwatch.GetTimestamp()>=deadline)return false;
        }
        int batch=0;
        while(_count>0)
        {
            int i=_queue[_read];_read=(_read+1)%Volume;_count--;_queued[i]=0;
            int x=i%Side,z=i/Side%Side,y=i/Slice;
            if(x>0)Spread(i,i-1);if(x<Side-1)Spread(i,i+1);if(z>0)Spread(i,i-Side);if(z<Side-1)Spread(i,i+Side);
            if(y>0)Spread(i,i-Slice);if(y<World.Height-1)Spread(i,i+Slice);
            if((++batch&255)==0 && Stopwatch.GetTimestamp()>=deadline)return false;
        }
        _fieldStage=0;BuiltPages++;return true;
    }
    private bool SkyCanReach(int sky,int to)=>sky-Math.Max(1,(int)_cost[to])>(_light[to]>>4);
    private void Spread(int from,int to)
    {
        int c=Math.Max(1,(int)_cost[to]),sky=Math.Max(_light[to]>>4,(_light[from]>>4)-c),block=Math.Max(_light[to]&15,(_light[from]&15)-c);
        byte value=(byte)((sky<<4)|block);if(value==_light[to])return;_light[to]=value;Enqueue(to);
    }
    private void Enqueue(int index)
    {if(_queued[index]!=0)return;_queued[index]=1;_queue[_write]=index;_write=(_write+1)%Volume;_count++;}
    public static float Visibility(float level,float brightness)
    {float minimum=.025f+.12f*Math.Clamp(brightness,0,1);return minimum+(1-minimum)*MathF.Pow(Math.Clamp(level/14,0,1),1.7f);}
}
