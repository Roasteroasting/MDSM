using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace MDSM.Worlds.Streaming;

// Immutable coordinate-owned feature plans shared by generation workers. The
// cache stores descriptions, never complete worlds or meshes; misses are pure.
internal sealed class WaterFeaturePlans
{
    private readonly object _gate=new();
    private readonly Dictionary<(long,long,int),object> _plans=new();
    private readonly Queue<(long,long,int)> _order=new();
    private readonly Dictionary<(long,long),int> _heights=new();
    private readonly Queue<(long,long)> _heightOrder=new();
    private readonly Dictionary<ChunkAddress,float[]> _coasts=new();
    private readonly Queue<ChunkAddress> _coastOrder=new();
    internal float CoastDistance(long x,long z,Func<long,int,long,BlockType> raw)
    {
        var address=ChunkAddress.At(x,z);float[] distances;
        lock(_gate)_coasts.TryGetValue(address,out distances);
        if(distances==null)
        {
            var built=BuildCoast(address,raw);
            lock(_gate)
            {
                if(!_coasts.TryGetValue(address,out distances))
                {
                    while(_coasts.Count>=64)_coasts.Remove(_coastOrder.Dequeue());
                    _coasts.Add(address,built);_coastOrder.Enqueue(address);distances=built;
                }
            }
        }
        return distances[Coordinates.Mod(z,16)*16+Coordinates.Mod(x,16)];
    }
    private static float[] BuildCoast(ChunkAddress address,Func<long,int,long,BlockType> raw)
    {
        // Exact squared Euclidean distance transform over a bounded halo. The
        // same nearest shore is considered on either side of a chunk boundary.
        // Test land at the water level, so floating overhangs are not shores.
        const int radius=36,size=16+radius*2;
        float[] field=ArrayPool<float>.Shared.Rent(size*size);
        Span<float> input=stackalloc float[size],output=stackalloc float[size];
        try
        {
            for(int z=0;z<size;z++)
            {
                for(int x=0;x<size;x++)input[x]=Blocks.IsSolid(raw(address.BlockX+x-radius,InfiniteWaterTerrain.Sea,address.BlockZ+z-radius))?0:1000000;
                Transform(input,output);output.CopyTo(field.AsSpan(z*size,size));
            }
            var result=new float[256];
            for(int x=radius;x<radius+16;x++)
            {
                for(int z=0;z<size;z++)input[z]=field[z*size+x];
                Transform(input,output);
                for(int z=radius;z<radius+16;z++)result[(z-radius)*16+x-radius]=Math.Min(radius,MathF.Sqrt(output[z]));
            }
            return result;
        }
        finally{ArrayPool<float>.Shared.Return(field);}
    }
    private static void Transform(ReadOnlySpan<float> input,Span<float> output)
    {
        Span<int> sites=stackalloc int[input.Length];Span<double> boundaries=stackalloc double[input.Length+1];
        int k=0;sites[0]=0;boundaries[0]=double.NegativeInfinity;boundaries[1]=double.PositiveInfinity;
        for(int q=1;q<input.Length;q++)
        {
            double intersection;
            while(true)
            {
                int p=sites[k];intersection=((double)input[q]+q*q-input[p]-p*p)/(2*(q-p));
                if(intersection>boundaries[k])break;k--;
            }
            sites[++k]=q;boundaries[k]=intersection;boundaries[k+1]=double.PositiveInfinity;
        }
        k=0;
        for(int q=0;q<input.Length;q++)
        {while(boundaries[k+1]<q)k++;int delta=q-sites[k];output[q]=delta*delta+input[sites[k]];}
    }
    internal int Height(long x,long z,Func<long,long,int> sample)
    {
        lock(_gate)if(_heights.TryGetValue((x,z),out int h))return h;
        int height=sample(x,z);
        lock(_gate)
        {
            if(_heights.TryGetValue((x,z),out int h))return h;
            while(_heights.Count>=8192)_heights.Remove(_heightOrder.Dequeue());
            _heights.Add((x,z),height);_heightOrder.Enqueue((x,z));return height;
        }
    }
    internal T Get<T>((long,long,int) key,Func<T> build) where T:class
    {
        lock(_gate)if(_plans.TryGetValue(key,out var cached))return (T)cached;
        T plan=build();
        lock(_gate)
        {
            if(_plans.TryGetValue(key,out var cached))return (T)cached;
            while(_plans.Count>=256)_plans.Remove(_order.Dequeue());
            _plans.Add(key,plan);_order.Enqueue(key);return plan;
        }
    }
}

internal enum NaturalWater { None, Sea, Pond, Lake, River }
internal readonly record struct WaterColumn(int Original,int Floor,int Level,NaturalWater Kind)
{
    internal bool Wet=>Level>=0 && Floor<Level;
}

// The single water geometry/material stage for generator v3. Reads immutable
// pre-feature density terrain; never sees structures or another chunk's edits.
internal sealed class InfiniteWaterTerrain(int seed,Func<long,long,int> surface,Func<long,long,int> planSurface,Func<long,int,long,BlockType> raw,
    WaterFeaturePlans shared,CancellationToken cancellation)
{
    internal const int Sea=BetaDensityGenerator.SeaLevel-1;
    private readonly Climate _climate=new(seed){VariedGeneration=true};
    private readonly Dictionary<(long,long),WaterColumn> _columns=new();
    private readonly Dictionary<(long,long),BlockType> _materials=new();
    private readonly Dictionary<(long,long,int),Basin> _basins=new();
    private readonly Dictionary<(long,long,int),River> _rivers=new();
    private readonly record struct Node(long X,long Z,int Height);
    private sealed record Basin(long X,long Z,double Rx,double Rz,int Level,int Depth,bool Valid);
    private sealed record River(double[] X,double[] Z,bool Valid);
    private static double Noise(int seed,double x,double z)=>InfiniteFeatures.Noise(seed,x,0,z);
    private static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    private static double Mix(double a,double b,double t)=>a+(b-a)*t;
    private int PlanHeight(long x,long z)=>shared.Height(x,z,planSurface);
    internal WaterColumn Column(long x,long z)
    {
        if(_columns.TryGetValue((x,z),out var cached))return cached;
        var column=ComputeColumn(x,z);_columns.Add((x,z),column);return column;
    }
    private WaterColumn ComputeColumn(long x,long z)
    {
        int top=surface(x,z);var result=new WaterColumn(top,top,-1,NaturalWater.None);
        // Preserve all high 3D formations, including Far Lands pillars. Excavate
        // only lowland surface caps; no density changes or deep void filling.
        if(top is >=45 and <=88)
        {
            if(top<Sea)
            {
                double coast=Smooth(shared.CoastDistance(x,z,raw)/36.0);
                int extra=(int)Math.Round(coast*(7+3*(Noise(seed^0x53454142,x/64.0,z/64.0)+1)));
                result=new(top,Math.Max(42,top-extra),Sea,NaturalWater.Sea);
            }
            // Major basins first; smaller ponds and river channels remain
            // independent candidates, merged by their actual bed/level only.
            if(!result.Wet||result.Floor>Sea-9)ApplyBasins(512,3);
            if(!result.Wet||result.Floor>Sea-4)ApplyBasins(128,4);
            if(!result.Wet||result.Floor>Sea-3)ApplyRivers();
            if(result.Floor<top && (!Blocks.IsSolid(raw(x,result.Floor,z))||!Blocks.IsSolid(raw(x,result.Floor-1,z))))
                result=new(top,top,top<Sea?Sea:-1,top<Sea?NaturalWater.Sea:NaturalWater.None);
        }
        return result;
        void Merge(int bed,int level,NaturalWater kind)
        {
            // Feature intersections share the lower existing water surface; a
            // low column must never be filled up into a perched lake or river.
            if(result.Level>=0 && result.Level!=level)return;
            if(bed<result.Floor)result=new(top,bed,level,kind);
        }
        void ApplyBasins(int spacing,int kind)
        {
            long gx=Coordinates.FloorDiv(x,spacing),gz=Coordinates.FloorDiv(z,spacing);
            for(long az=gz-1;az<=gz+1;az++)for(long ax=gx-1;ax<=gx+1;ax++)
            {
                var key=(ax,az,kind);
                ulong h=InfiniteFeatures.Hash(seed^(kind==3?0x4c414b45:0x504f4e44),ax,0,az);
                if(h%100>=(kind==3?55UL:65UL))continue;
                long cx=ax*spacing+spacing/4+(long)((h>>8)%(ulong)(spacing/2)),cz=az*spacing+spacing/4+(long)((h>>20)%(ulong)(spacing/2));
                double rx=kind==3?32+(h>>32)%20:7+(h>>32)%6,rz=kind==3?28+(h>>40)%20:7+(h>>40)%5;
                if(Math.Abs(x-cx)>rx*1.3||Math.Abs(z-cz)>rz*1.3)continue;
                if(!_basins.TryGetValue(key,out var basin))
                    basin=RememberBasin(key,cx,cz,rx,rz,kind);
                if(!basin.Valid)continue;
                // Leave an intruding hill as an island/peninsula rather than
                // flattening it down to a distant basin's water surface.
                if(top>basin.Level+5)continue;
                double dx=(x-cx)/rx,dz=(z-cz)/rz;
                double q=1-dx*dx-dz*dz+.14*Noise(seed^0x53484f52,x/(kind==3?24.0:10),z/(kind==3?24.0:10));
                if(q<-.5)continue;
                if(q<=0)
                {
                    if(!result.Wet && (result.Level<0||result.Level==basin.Level))
                    {
                        int bank=(int)Math.Round(Mix(top,basin.Level+1,Smooth((q+.5)/.5)));
                        if(bank<=result.Floor)result=new(top,bank,basin.Level,kind==3?NaturalWater.Lake:NaturalWater.Pond);
                    }
                    continue;
                }
                int depth=1+(int)Math.Round(basin.Depth*Smooth(q));
                Merge(basin.Level-depth,basin.Level,kind==3?NaturalWater.Lake:NaturalWater.Pond);
            }
        }
        void ApplyRivers()
        {
            long gx=Coordinates.FloorDiv(x,192),gz=Coordinates.FloorDiv(z,192);
            for(long az=gz-1;az<=gz+1;az++)for(long ax=gx-1;ax<=gx+1;ax++)for(int axis=0;axis<2;axis++)
            {
                var key=(ax,az,axis);
                if(!_rivers.TryGetValue(key,out var river))
                    river=RememberRiver(key,ax,az,axis);
                if(!river.Valid)continue;
                double distance=double.MaxValue;
                for(int i=1;i<river.X.Length;i++)
                {
                    double dx=river.X[i]-river.X[i-1],dz=river.Z[i]-river.Z[i-1];
                    double t=Math.Clamp(((x-river.X[i-1])*dx+(z-river.Z[i-1])*dz)/(dx*dx+dz*dz),0,1);
                    double px=x-river.X[i-1]-t*dx,pz=z-river.Z[i-1]-t*dz;distance=Math.Min(distance,Math.Sqrt(px*px+pz*pz));
                }
                double width=3.2+1.0*Noise(seed^0x57494454,x/48.0,z/48.0);
                if(distance>width+4||top>Sea+4)continue;
                if(distance<width)Merge(Sea-1-(int)Math.Round(2*Smooth(1-distance/width)),Sea,NaturalWater.River);
                else if(!result.Wet)
                {
                    int bank=(int)Math.Round(Mix(Sea+1,top,Smooth((distance-width)/4)));
                    if(bank<result.Floor)result=new(top,bank,-1,NaturalWater.River);
                }
            }
        }
    }
    // Keep cache-miss factories outside hot column loops: captured loop locals
    // otherwise allocate closure objects even when the plan is already cached.
    private Basin RememberBasin((long,long,int) key,long x,long z,double rx,double rz,int kind)
    {var plan=shared.Get(key,()=>BuildBasin(x,z,rx,rz,kind));_basins.Add(key,plan);return plan;}
    private River RememberRiver((long,long,int) key,long x,long z,int axis)
    {var plan=shared.Get(key,()=>BuildRiver(x,z,axis));_rivers.Add(key,plan);return plan;}
    private Basin BuildBasin(long x,long z,double rx,double rz,int kind)
    {
        cancellation.ThrowIfCancellationRequested();int top=PlanHeight(x,z),min=128,max=0;
        // A sampled containing rim sets the level, not the center height. This
        // rejects perched/flooded plains and keeps the excavation cap shallow.
        for(int i=0;i<24;i++)
        {double a=i*Math.PI/12;int y=PlanHeight(x+(long)Math.Round(Math.Cos(a)*rx*1.2),z+(long)Math.Round(Math.Sin(a)*rz*1.2));min=Math.Min(min,y);max=Math.Max(max,y);}
        int level=Math.Min(top-1,min-1);
        bool valid=top is >=61 and <=83 && min>=60 && max-min<=(kind==3?13:8) && top-level<=5
            && _climate.At(x,z)!=Biome.Desert && (Math.Abs(x)>16||Math.Abs(z)>16);
        // Lowland basins join sea-level water without creating competing levels.
        if(level<Sea)level=Sea;
        if(level>Sea)
        {
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                if(PlanHeight(x+(long)(dx*rx*.55),z+(long)(dz*rz*.55))<Sea)level=Sea;
        }
        return new(x,z,rx,rz,level,kind==3?8:3,valid);
    }
    private River BuildRiver(long ax,long az,int axis)
    {
        cancellation.ThrowIfCancellationRequested();Node a=GetNode(ax,az),b=GetNode(ax+(axis==0?1:0),az+(axis==1?1:0));
        if(a.Height>Sea+3||b.Height>Sea+3)return new(Array.Empty<double>(),Array.Empty<double>(),false);
        double[] bestX=null,bestZ=null;int bestMax=int.MaxValue;
        // Try three gentle bends, favoring the broad lowland contour. Acceptance
        // belongs to the WHOLE link, so hills cannot chop it into trench pieces.
        foreach(int bend in new[]{-32,0,32})
        {
            var xs=new double[17];var zs=new double[17];int max=0,min=128;
            double length=Math.Sqrt((b.X-a.X)*(double)(b.X-a.X)+(b.Z-a.Z)*(double)(b.Z-a.Z));
            for(int i=0;i<17;i++)
            {
                double t=i/16.0,wave=Math.Sin(t*Math.PI)*bend+Math.Sin(t*Math.PI*2)*8;
                xs[i]=Mix(a.X,b.X,t)-(b.Z-a.Z)/length*wave;zs[i]=Mix(a.Z,b.Z,t)+(b.X-a.X)/length*wave;
                int y=PlanHeight((long)Math.Round(xs[i]),(long)Math.Round(zs[i]));max=Math.Max(max,y);min=Math.Min(min,y);
            }
            if(max<bestMax && max-min<=16){bestMax=max;bestX=xs;bestZ=zs;}
        }
        bool valid=bestMax<=Sea+4;
        if(valid)
        {
            // Check the winning corridor at block-scale intervals, including
            // its width. Reject the entire link if a hidden ridge would split
            // it; do not clip individual high columns into disconnected pools.
            for(int i=1;i<bestX.Length&&valid;i++)
            {
                double dx=bestX[i]-bestX[i-1],dz=bestZ[i]-bestZ[i-1],length=Math.Sqrt(dx*dx+dz*dz);
                int steps=(int)Math.Ceiling(length/3);
                for(int step=0;step<=steps&&valid;step++)for(int side=-1;side<=1;side++)
                {
                    double t=step/(double)steps;
                    long x=(long)Math.Round(Mix(bestX[i-1],bestX[i],t)-dz/length*side*4);
                    long z=(long)Math.Round(Mix(bestZ[i-1],bestZ[i],t)+dx/length*side*4);
                    if(PlanHeight(x,z)>Sea+4){valid=false;break;}
                }
            }
        }
        return new(bestX??Array.Empty<double>(),bestZ??Array.Empty<double>(),valid);
        Node GetNode(long nx,long nz)
        {
            var plan=shared.Get((nx,nz,5),()=>
            {
                ulong h=InfiniteFeatures.Hash(seed^0x52495652,nx,0,nz);long x=nx*192+48+(long)((h>>8)%96),z=nz*192+48+(long)((h>>24)%96);
                int y=PlanHeight(x,z);long bx=x,bz=z;
                for(int i=0;i<4;i++)
                {long px=x+(i%2==0?24:-24),pz=z+(i<2?24:-24);int py=PlanHeight(px,pz);if(py<y){y=py;bx=px;bz=pz;}}
                return new Basin(bx,bz,0,0,y,0,true);
            });return new(plan.X,plan.Z,plan.Level);
        }
    }
    internal BlockType At(long x,int y,long z,BlockType original,int top=-1)
    {
        if(y<40)return original;
        if(top<0)top=surface(x,z);
        // Water only edits a bounded surface cap. Cave wetness probes below
        // that cap must not initialize expensive remote surface feature plans.
        if(y<top-16)return original;
        if(top is <45 or >88)
        {
            if(_climate.At(x,z)!=Biome.Desert)return original;
            if(raw(x,top,z) is not (BlockType.Grass or BlockType.Dirt))return original;
            if(y>=top-2 && original is (BlockType.Grass or BlockType.Dirt))return BlockType.Sand;
            return y<top-2 && original is (BlockType.Stone or BlockType.Dirt)
                ?SandstoneSurface.Layer(seed,x,z,top-3-y)?BlockType.Sandstone:BlockType.Stone:original;
        }
        WaterColumn c=Column(x,z);
        if(y>c.Floor && y<=Math.Max(c.Original,c.Level))return y<=c.Level?BlockType.WaterSource:BlockType.Air;
        if(y>c.Floor||y<c.Floor-5||!Blocks.IsSolid(original))return original;
        if(!_materials.TryGetValue((x,z),out var material))
        {material=Material(x,z,c);_materials.Add((x,z),material);}
        if(y<c.Floor-2)return material==BlockType.Sand && original is (BlockType.Stone or BlockType.Dirt)
            ?SandstoneSurface.Layer(seed,x,z,c.Floor-3-y)?BlockType.Sandstone:BlockType.Stone:original;
        if(y==c.Floor)return material;
        return material==BlockType.Stone?BlockType.Stone:material==BlockType.Sand?BlockType.Sand:BlockType.Dirt;
    }
    private BlockType Material(long x,long z,WaterColumn c)
    {
        int slope=0,nearLevel=-1;
        for(int i=0;i<4;i++)
        {
            int dx=i==0?1:i==1?-1:0,dz=i==2?1:i==3?-1:0;
            var n=Column(x+dx*3,z+dz*3);slope=Math.Max(slope,Math.Abs(n.Floor-c.Floor));
            if(n.Wet)nearLevel=Math.Max(nearLevel,n.Level);
        }
        bool desert=_climate.At(x,z)==Biome.Desert;
        if(!c.Wet && c.Kind==NaturalWater.None && nearLevel<0)
        {
            if(desert)return BlockType.Sand;
            BlockType surfaceBlock=raw(x,c.Floor,z);
            return surfaceBlock==BlockType.Dirt?BlockType.Grass:surfaceBlock;
        }
        if(slope>=6)return BlockType.Stone;
        double sediment=Noise(seed^0x53454449,x/36.0,z/36.0);
        if(c.Wet)
        {
            int depth=c.Level-c.Floor;
            if(depth<=4 && slope<=3 && (c.Kind!=NaturalWater.River||sediment>.25))return BlockType.Sand;
            return sediment>.05?BlockType.Gravel:BlockType.Dirt;
        }
        if(desert)return BlockType.Sand;
        double beach=Noise(seed^0x42454143,x/40.0,z/40.0);
        if(nearLevel>=0 && c.Floor>=nearLevel && c.Floor<=nearLevel+1+(beach>0?1:0) && slope<=3)return BlockType.Sand;
        return BlockType.Grass;
    }
}
