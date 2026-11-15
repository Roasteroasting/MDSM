using System;
using System.Threading;

namespace MDSM.Worlds.Streaming;

// Stateless after construction, safe for concurrent chunk requests. This is the
// terrain stage only; decoration/structure passes must own deterministic halos.
internal sealed class BetaDensityGenerator
{
    private readonly int _seed;
    private readonly object _cacheGate=new();
    private readonly System.Collections.Generic.Dictionary<ChunkAddress,byte[]> _featureTerrain=new();
    private readonly System.Collections.Generic.Queue<ChunkAddress> _cacheOrder=new();
    internal byte[] TerrainForFeatures(ChunkAddress address,CancellationToken token)
    {
        lock(_cacheGate)if(_featureTerrain.TryGetValue(address,out var cached))return cached;
        byte[] blocks=Generate(address,token);
        lock(_cacheGate)
        {
            if(_featureTerrain.TryGetValue(address,out var cached))return cached;
            while(_featureTerrain.Count>=64)_featureTerrain.Remove(_cacheOrder.Dequeue());
            _featureTerrain.Add(address,blocks);_cacheOrder.Enqueue(address);return blocks;
        }
    }
    internal const int Version=1,SeaLevel=63;
    private readonly LegacyOctaves _low,_high,_selector,_elevation,_relief;
    internal BetaDensityGenerator(int seed)
    {_seed=seed;var random=new Random(seed);_low=new(random,16);_high=new(random,16);_selector=new(random,8);_elevation=new(random,10);_relief=new(random,8);}
    internal ChunkSnapshot GenerateComplete(ChunkAddress address,CancellationToken cancellation=default)=>new InfiniteFeatures(this,_seed,address,cancellation).Generate();
    internal double Density(long x,int y,long z)
    {
        if(!Coordinates.Valid(x,z)||y<0||y>128)throw new ArgumentOutOfRangeException(nameof(x));
        // At a four-block sampling interval 171.103/block is 684.412/sample.
        // The high-frequency octave thus saturates at about +/-12,550,824.
        double lower=_low.Sample(x*171.103,y*85.5515,z*171.103)/512;
        double upper=_high.Sample(x*171.103,y*85.5515,z*171.103)/512;
        double selector=(_selector.Sample(x*2.1387875,y*1.06939375,z*2.1387875)/10+1)/2;
        double terrain=selector<=0?lower:selector>=1?upper:LegacyPerlin.Lerp(lower,upper,selector);
        double elevation=_elevation.Sample(x/96.0,0,z/96.0)/1024;
        double relief=12+Math.Abs(_relief.Sample(x/80.0,0,z/80.0)/256)*12;
        double result=terrain+(70+elevation*10-y)*relief/16;
        // Top clearance is a separate shaping term, not a density clamp. Far
        // Lands values may overwhelm it, including near the upper world edge.
        if(y>112)result-=(y-112)*8;
        if(!double.IsFinite(result))throw new ArithmeticException("Non-finite density.");return result;
    }
    internal byte[] Generate(ChunkAddress address,CancellationToken cancellation=default)
    {
        Coordinates.Validate(address);long ox=address.BlockX,oz=address.BlockZ;
        var samples=new double[5*17*5];
        int S(int x,int y,int z)=>(y*5+z)*5+x;
        for(int y=0;y<=16;y++)for(int z=0;z<=4;z++)for(int x=0;x<=4;x++)
        {cancellation.ThrowIfCancellationRequested();samples[S(x,y,z)]=Density(ox+x*4,y*8,oz+z*4);}
        var blocks=new byte[16*128*16];
        for(int y=0;y<128;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            int sx=x/4,sy=y/8,sz=z/4;double fx=x%4/4.0,fy=y%8/8.0,fz=z%4/4.0;
            double Plane(int dy)=>LegacyPerlin.Lerp(LegacyPerlin.Lerp(samples[S(sx,sy+dy,sz)],samples[S(sx+1,sy+dy,sz)],fx),
                LegacyPerlin.Lerp(samples[S(sx,sy+dy,sz+1)],samples[S(sx+1,sy+dy,sz+1)],fx),fz);
            double density=LegacyPerlin.Lerp(Plane(0),Plane(1),fy);
            blocks[Coordinates.Index(x,y,z)]=(byte)(y==0?BlockType.Bedrock:density>0?BlockType.Stone:y<SeaLevel?BlockType.WaterSource:BlockType.Air);
        }
        // Paint exposed solid surfaces without filling overhangs/caves.
        for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            int soil=0;bool exposed=true,solidAbove=false;
            for(int y=127;y>0;y--)
            {
                int index=Coordinates.Index(x,y,z);BlockType block=(BlockType)blocks[index];
                if(block!=BlockType.Stone)
                {solidAbove=false;soil=0;if(block!=BlockType.Air)exposed=false;continue;}
                if(!solidAbove)
                {
                    soil=3;blocks[index]=(byte)(exposed&&y>=SeaLevel?BlockType.Grass:BlockType.Dirt);
                }
                else if(soil>0){blocks[index]=(byte)BlockType.Dirt;soil--;}
                solidAbove=true;exposed=false;
            }
        }
        return blocks;
    }
}
