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
    private readonly System.Collections.Generic.Dictionary<ChunkAddress,InfiniteCaves> _caveCache=new();
    private readonly System.Collections.Generic.Queue<ChunkAddress> _caveOrder=new();
    internal InfiniteCaves CavesForFeatures(ChunkAddress address,Func<long,int,long,BlockType> terrain,CancellationToken token)
    {
        lock(_cacheGate)if(_caveCache.TryGetValue(address,out var cached))return cached;
        var caves=new InfiniteCaves(_seed,address,terrain,token);
        lock(_cacheGate)
        {
            if(_caveCache.TryGetValue(address,out var cached))return cached;
            while(_caveCache.Count>=64)_caveCache.Remove(_caveOrder.Dequeue());
            _caveCache.Add(address,caves);_caveOrder.Enqueue(address);return caves;
        }
    }
    internal byte[] TerrainForFeatures(ChunkAddress address,CancellationToken token)
    {
        lock(_cacheGate)if(_featureTerrain.TryGetValue(address,out var cached))return cached;
        byte[] blocks=Generate(address,token);
        lock(_cacheGate)
        {
            if(_featureTerrain.TryGetValue(address,out var cached))return cached;
            while(_featureTerrain.Count>=(GenerationVersion>=3?128:64))_featureTerrain.Remove(_cacheOrder.Dequeue());
            _featureTerrain.Add(address,blocks);_cacheOrder.Enqueue(address);return blocks;
        }
    }
    internal const int Version=5,SeaLevel=63;
    internal readonly WaterFeaturePlans WaterPlans=new();
    private readonly System.Collections.Generic.Dictionary<(long,long),double[]> _densityColumns=new();
    private readonly System.Collections.Generic.Queue<(long,long)> _densityOrder=new();
    private double[] DensityColumn(long x,long z)
    {
        lock(_cacheGate)if(_densityColumns.TryGetValue((x,z),out var cached))return cached;
        var shape=Shape(x,z);var values=new double[17];for(int y=0;y<=16;y++)values[y]=Density(x,y*8,z,shape);
        lock(_cacheGate)
        {
            if(_densityColumns.TryGetValue((x,z),out var cached))return cached;
            while(_densityColumns.Count>=512)_densityColumns.Remove(_densityOrder.Dequeue());
            _densityColumns.Add((x,z),values);_densityOrder.Enqueue((x,z));return values;
        }
    }
    // Exact highest surface from the same coarse samples/interpolation, without
    // allocating and painting an entire chunk for a remote plan probe.
    internal int PlanSurface(long x,long z)
    {
        if(!Coordinates.Valid(x-4,z-4)||!Coordinates.Valid(x+4,z+4))return 128;
        long bx=Coordinates.FloorDiv(x,4)*4,bz=Coordinates.FloorDiv(z,4)*4;
        double fx=(x-bx)/4.0,fz=(z-bz)/4.0;
        var a=DensityColumn(bx,bz);var b=DensityColumn(bx+4,bz);var c=DensityColumn(bx,bz+4);var d=DensityColumn(bx+4,bz+4);
        double At(int y)=>LegacyPerlin.Lerp(LegacyPerlin.Lerp(a[y/8],b[y/8],fx),LegacyPerlin.Lerp(c[y/8],d[y/8],fx),fz);
        double upper=At(128);if(upper>0)return 127;
        for(int y=120;y>=0;y-=8)
        {
            double lower=At(y);
            if(lower>0)return Math.Clamp((int)Math.Ceiling(y+8*lower/(lower-upper))-1,0,127);
            upper=lower;
        }
        return 0;
    }
    internal int GenerationVersion {get;}
    // The historical attenuation used by Shape belongs to the terrain density.
    // The versioned biome map used for materials and decoration lives in InfiniteFeatures.
    private readonly Climate _climate;
    private readonly LegacyOctaves _low,_high,_selector,_elevation,_relief;
    internal BetaDensityGenerator(int seed,int version=Version)
    {if(version is <1 or >Version)throw new ArgumentOutOfRangeException(nameof(version));GenerationVersion=version;_seed=seed;_climate=new(seed){VariedGeneration=true};var random=new Random(seed);_low=new(random,16);_high=new(random,16);_selector=new(random,8);_elevation=new(random,10);_relief=new(random,8);}
    internal ChunkSnapshot GenerateComplete(ChunkAddress address,CancellationToken cancellation=default,Season? season=null)
        =>new InfiniteFeatures(this,_seed,address,cancellation,season).Generate();
    internal double Density(long x,int y,long z)=>Density(x,y,z,null);
    private double Density(long x,int y,long z,(double Center,double Stretch)? shape)
    {
        if(!Coordinates.Valid(x,z)||y<0||y>128)throw new ArgumentOutOfRangeException(nameof(x));
        // At a four-block sampling interval 171.103/block is 684.412/sample.
        // The high-frequency octave thus saturates at about +/-12,550,824.
        double lower=_low.Sample(x*171.103,y*85.5515,z*171.103)/512;
        double upper=_high.Sample(x*171.103,y*85.5515,z*171.103)/512;
        double selector=(_selector.Sample(x*2.1387875,y*1.06939375,z*2.1387875)/10+1)/2;
        double terrain=selector<=0?lower:selector>=1?upper:LegacyPerlin.Lerp(lower,upper,selector);
        double result;
        if(GenerationVersion==1)
        {
            double elevation=_elevation.Sample(x/96.0,0,z/96.0)/1024;
            double relief=12+Math.Abs(_relief.Sample(x/80.0,0,z/80.0)/256)*12;
            result=terrain+(70+elevation*10-y)*relief/16;
        }
        else
        {
            // Beta's depth shaping is asymmetric, and its vertical gradient is
            // FOUR times stronger below the regional center. Noise still owns
            // all 3D formations; there is no enforced solid heightmap here.
            var (center,stretch)=shape??Shape(x,z);
            double vertical=(y-center)*1.5/stretch;
            if(vertical<0)vertical*=4;
            result=terrain-vertical;
        }
        // Top clearance is a separate shaping term, not a density clamp. Far
        // Lands values may overwhelm it, including near the upper world edge.
        if(y>112)result-=(y-112)*8;
        if(!double.IsFinite(result))throw new ArithmeticException("Non-finite density.");return result;
    }
    private (double Center,double Stretch) Shape(long x,long z)
    {
        double depth=_elevation.Sample(x*.28,0,z*.28)/1024;
        if(depth<0)depth=-depth*.3;
        depth=depth*3-2;
        depth=depth<0?Math.Max(-1,depth/2)/1.4/2:Math.Min(1,depth)/8;
        double region=InfiniteFeatures.Noise(_seed^0x4d4f554e,x/480.0,0,z/480.0);
        double mountain=Math.Clamp((region-.12)/.5,0,1);mountain=mountain*mountain*(3-2*mountain);
        // Smooth the biome influence rather than stepping the relief at a
        // temperature boundary. Deserts suppress mountain relief.
        long bx=Coordinates.FloorDiv(x,128)*128,bz=Coordinates.FloorDiv(z,128)*128;
        double fx=(x-bx)/128.0,fz=(z-bz)/128.0;fx=fx*fx*(3-2*fx);fz=fz*fz*(3-2*fz);
        double B(long a,long b)=>_climate.At(a,b)==Biome.Desert?.3:1;
        mountain*=LegacyPerlin.Lerp(LegacyPerlin.Lerp(B(bx,bz),B(bx+128,bz),fx),LegacyPerlin.Lerp(B(bx,bz+128),B(bx+128,bz+128),fx),fz);
        double stretch=Math.Clamp((_relief.Sample(x*.28,0,z*.28)/256+1)*.5,.35,1);
        stretch=.5+stretch*.2+mountain*.85;
        double center=68+depth*18+mountain*12;
        return (center,stretch);
    }
    internal byte[] Generate(ChunkAddress address,CancellationToken cancellation=default)
    {
        Coordinates.Validate(address);long ox=address.BlockX,oz=address.BlockZ;
        var samples=new double[5*17*5];
        var shapes=new (double Center,double Stretch)[25];
        if(GenerationVersion==2)for(int z=0;z<=4;z++)for(int x=0;x<=4;x++)shapes[z*5+x]=Shape(ox+x*4,oz+z*4);
        int S(int x,int y,int z)=>(y*5+z)*5+x;
        for(int z=0;z<=4;z++)for(int x=0;x<=4;x++)
        {
            cancellation.ThrowIfCancellationRequested();
            var column=GenerationVersion>=3?DensityColumn(ox+x*4,oz+z*4):null;
            for(int y=0;y<=16;y++)samples[S(x,y,z)]=column!=null?column[y]:Density(ox+x*4,y*8,oz+z*4,GenerationVersion==2?shapes[z*5+x]:null);
        }
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
                    soil=3;blocks[index]=(byte)(exposed&&y>=(GenerationVersion>=3?SeaLevel-1:SeaLevel)?BlockType.Grass:BlockType.Dirt);
                }
                else if(soil>0){blocks[index]=(byte)BlockType.Dirt;soil--;}
                solidAbove=true;exposed=false;
            }
        }
        return blocks;
    }
}
