using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Reconstructible, packed skylight/block-light. Only queried chunks are cached;
// a fourteen-cell halo makes each result independent of cache/residency order.
// No full-world light array, initial scan, or time-of-day invalidation is needed.
public sealed class BlockLighting
{
    // Covers the normal 1080p mesh region plus a margin, so preparing its distant
    // edge does not evict the light pages needed by nearby spawn/interaction queries.
    public const int Maximum = 14, MaximumCachedChunks = 1536;
    private const int Halo=15, Side=Chunk.Size+2*Halo, Slice=Side*Side, Volume=Slice*World.Height;
    internal const int PageSide=18;
    private readonly World _world;
    private readonly Dictionary<int,LinkedListNode<Page>> _pages=new();
    private readonly LinkedList<Page> _lru=new();
    private byte[] _light,_cost,_queued;
    private byte[] _direct;
    private Chunk[] _columns;
    private int[] _columnOffsets;
    private static readonly byte[] Costs=CreateCosts();
    private int[] _queue;
    private int _read,_write,_count;
    public int CachedChunks=>_pages.Count;
    public long CachedBytes=>(long)_pages.Count*PageSide*PageSide*World.Height;
    public int BuiltPages { get; private set; }
    internal sealed class Page(int x,int z)
    {
        internal readonly int X=x,Z=z;
        internal readonly byte[] Data=new byte[PageSide*PageSide*World.Height];
        internal bool Dirty;
        internal byte At(int x,int y,int z)=>y<0?(byte)0:y>=World.Height?(byte)0xe0:Data[(y*PageSide+z-Z+1)*PageSide+x-X+1];
    }
    public BlockLighting(World world)=>_world=world;
    // Glass and thin open geometry transmit daylight; leaves/water attenuate it.
    public static int Transmission(BlockType block)=>Costs[(byte)block];
    private static byte[] CreateCosts()
    {
        var costs=new byte[256];for(int i=0;i<costs.Length;i++)
        {BlockType b=(BlockType)i;costs[i]=(byte)(Blocks.IsOpaque(b)?16:b==BlockType.Leaves || Water.IsWater(b)?1:0);}return costs;
    }
    public int Emission(Point3 p,BlockType block)=>block==BlockType.Torch || Lava.IsLava(block)?14
        :Smelting.Machine(block) && _world.Containers.Entries.TryGetValue(p,out var entry) && entry.Burn>0?13:0;
    public byte Packed(Point3 p)
    {
        if(p.Y>=World.Height)return 0xe0;
        if(!_world.Contains(p.X,p.Y,p.Z))return p.Y<0?(byte)0:(byte)0xe0;
        return GetPage(p.X/16,p.Z/16).At(p.X,p.Y,p.Z);
    }
    public int Effective(Point3 p)
    { byte packed=Packed(p);return Math.Max(packed&15,Math.Max(0,(packed>>4)-(int)MathF.Ceiling(14-_world.Time.Daylight-.00001f))); }
    public int Effective(Vector3 p)=>Effective(new Point3((int)MathF.Floor(p.X),(int)MathF.Floor(p.Y),(int)MathF.Floor(p.Z)));
    internal Page GetPage(int cx,int cz)
    {
        int key=cz*_world.ChunksX+cx;
        if(_pages.TryGetValue(key,out var node))
        { _lru.Remove(node);_lru.AddLast(node);if(node.Value.Dirty)Build(node.Value);return node.Value; }
        Page page;
        if(_pages.Count>=MaximumCachedChunks)
        {
            var oldest=_lru.First;_pages.Remove(oldest.Value.Z/16*_world.ChunksX+oldest.Value.X/16);_lru.RemoveFirst();
        }
        page=new(cx*16,cz*16);Build(page);_pages.Add(key,_lru.AddLast(page));return page;
    }
    internal void Changed(Point3 p,BlockType previous,BlockType next)
    { if(Transmission(previous)!=Transmission(next) || Emission(p,previous)!=Emission(p,next))Invalidate(p); }
    internal void Invalidate(Point3 p)
    {
        if(BuiltPages==0)return; // No lit geometry/cache exists yet, including during generation/loading.
        int minX=Math.Max(0,(p.X-Halo)/16),maxX=Math.Min(_world.ChunksX-1,(p.X+Halo)/16);
        int minZ=Math.Max(0,(p.Z-Halo)/16),maxZ=Math.Min(_world.ChunksZ-1,(p.Z+Halo)/16);
        for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)
        {
            if(_pages.TryGetValue(z*_world.ChunksX+x,out var node))node.Value.Dirty=true;
            _world.GetChunk(x,z).MarkDirty();_world.GetChunk(x,z).MarkWaterDirty();
        }
    }
    private void Build(Page page)
    {
        _light??=new byte[Volume];_cost??=new byte[Volume];_queued??=new byte[Volume];_queue??=new int[Volume];
        _direct??=new byte[Slice];_columns??=new Chunk[Slice];_columnOffsets??=new int[Slice];
        Array.Clear(_light);Array.Clear(_queued);_read=_write=_count=0;
        int originX=page.X-Halo,originZ=page.Z-Halo;
        for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
        {
            int wx=originX+x,wz=originZ+z,column=z*Side+x;
            _columns[column]=(uint)wx<_world.Width && (uint)wz<_world.Depth?_world.GetChunk(wx/16,wz/16):null;
            _columnOffsets[column]=(wz&15)*16+(wx&15);_direct[column]=14;
        }
        // Walk contiguous block rows instead of repeatedly striding through a chunk's
        // entire height. Prefetched column references avoid world/dictionary queries.
        for(int y=World.Height-1;y>=0;y--)
        {
            int layer=y*Slice,chunkLayer=y*256;
            for(int column=0;column<Slice;column++)
            {
                int index=layer+column;Chunk chunk=_columns[column];
                BlockType block=chunk==null?BlockType.Air:(BlockType)chunk.Blocks[chunkLayer+_columnOffsets[column]];
                int cost=Costs[(byte)block],sky=_direct[column];_cost[index]=(byte)cost;
                sky=sky==14 && cost==0?14:Math.Max(0,sky-Math.Max(1,cost));
                _direct[column]=(byte)sky;
                int emit=block==BlockType.Torch || Lava.IsLava(block)?14:Smelting.Machine(block)?Emission(new(originX+column%Side,y,originZ+column/Side),block):0;
                _light[index]=(byte)((sky<<4)|emit);
                if(emit>1)Enqueue(index);
            }
        }
        // Direct sky is already correct vertically. Only horizontal shadow frontiers
        // need seeds; the shared propagation queue then also travels vertically.
        for(int y=0;y<World.Height;y++)for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
        {
            int i=y*Slice+z*Side+x,sky=_light[i]>>4;if(sky<=1)continue;
            if(x>0 && SkyCanReach(sky,i-1) || x<Side-1 && SkyCanReach(sky,i+1) || z>0 && SkyCanReach(sky,i-Side) || z<Side-1 && SkyCanReach(sky,i+Side))Enqueue(i);
        }
        while(_count>0)
        {
            int i=_queue[_read];_read=(_read+1)%Volume;_count--;_queued[i]=0;
            int x=i%Side,z=i/Side%Side,y=i/Slice;
            if(x>0)Spread(i,i-1);if(x<Side-1)Spread(i,i+1);if(z>0)Spread(i,i-Side);if(z<Side-1)Spread(i,i+Side);
            if(y>0)Spread(i,i-Slice);if(y<World.Height-1)Spread(i,i+Slice);
        }
        for(int y=0;y<World.Height;y++)for(int z=0;z<PageSide;z++)
            _light.AsSpan(y*Slice+(z+Halo-1)*Side+Halo-1,PageSide).CopyTo(page.Data.AsSpan((y*PageSide+z)*PageSide));
        page.Dirty=false;BuiltPages++;
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
